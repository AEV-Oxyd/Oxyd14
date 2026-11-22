using Content.Shared._Oxyd.Medical;
using Content.Shared.Atmos;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Medical.Cryogenics;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Temperature.Components;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Eris organ-death model (resuscitate.tmpl / organs.dm): while blood stops
/// circulating oxygen — exsanguination or a destroyed heart — every organ
/// accrues anoxic damage. When the brain organ is destroyed the mob suffers
/// true death: MobState.Dead plus <see cref="OxydTrueDeathComponent"/>, which
/// blocks resuscitation. Freezing the body pauses the accrual (Eris: cryo
/// slows organ death); oxygenated blood prevents it entirely.
/// </summary>
public sealed partial class OxydOrganDeathSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private OxydWoundSystem _wounds = default!;

    private const float TickInterval = 2f;
    private const float AnoxiaDamage = 1f;

    /// <summary>Below this fraction of reference blood volume the body counts
    /// as exsanguinated and organs start taking anoxic damage.</summary>
    private const float MinBloodFraction = 0.5f;

    private float _updateRemaining;

    public override void Update(float frameTime)
    {
        _updateRemaining -= frameTime;
        if (_updateRemaining > 0)
            return;
        _updateRemaining = TickInterval;

        var query = EntityQueryEnumerator<BodyComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out _, out var mobState))
        {
            if (mobState.CurrentState == MobState.Dead || IsFrozen(uid))
                continue;

            var organs = _wounds.GetOrgans(uid);
            if (organs.Count == 0 || IsOxygenated(uid, organs))
                continue;

            var brainDead = false;
            foreach (var (orgUid, organ, surg) in organs)
            {
                OxydWoundSystem.AddOrganDamage(surg, AnoxiaDamage);
                if (organ.Category == OxydOrganCategories.Brain &&
                    (surg.OrganDamage >= OxydOrganSurgeryComponent.OrganMaxDamage || surg.Decayed))
                    brainDead = true;
                Dirty(orgUid, surg);
            }

            if (brainDead)
            {
                _mobState.ChangeMobState(uid, MobState.Dead);
                EnsureComp<OxydTrueDeathComponent>(uid);
            }
        }
    }

    /// <summary>Eris "blood still pumping": enough blood volume remains AND the
    /// heart organ is still viable. Anything less means organs suffocate.</summary>
    private bool IsOxygenated(EntityUid body,
        List<(EntityUid Uid, OrganComponent Organ, OxydOrganSurgeryComponent Surgery)> organs)
    {
        if (!TryComp<BloodstreamComponent>(body, out var blood) ||
            _bloodstream.GetBloodLevel((body, blood)) < MinBloodFraction)
            return false;

        foreach (var (_, organ, surg) in organs)
        {
            if (organ.Category == OxydOrganCategories.Heart &&
                (surg.OrganDamage >= OxydOrganSurgeryComponent.OrganMaxDamage || surg.Decayed))
                return false;
        }
        return true;
    }

    /// <summary>Frozen bodies suspend anoxic progression (cryo pod, stasis,
    /// or below 0°C anywhere up the container chain).</summary>
    private bool IsFrozen(EntityUid uid)
    {
        var parent = uid;
        while (parent.IsValid())
        {
            if (HasComp<InsideCryoPodComponent>(parent) || HasComp<OxydInStasisComponent>(parent))
                return true;
            if (TryComp<TemperatureComponent>(parent, out var temp) && temp.Temperature <= Atmospherics.T0C)
                return true;
            parent = Transform(parent).ParentUid;
        }
        return false;
    }
}
