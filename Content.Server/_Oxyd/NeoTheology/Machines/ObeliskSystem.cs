using System.Linq;
using Content.Server._Oxyd.Framework.ViewCalc;
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>Applies the obelisk aura to the visible targets from each view tick.</summary>
public sealed partial class ObeliskSystem : EntitySystem
{
    [Dependency] private readonly CruciformSystem _cruciform = default!;
    [Dependency] private readonly EyeOfTheProtectorSystem _eye = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PlantTraySystem _tray = default!;
    [Dependency] private readonly SanitySystem _sanity = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ViewCalcSystem _view = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly NeoTheologyMachineSystem _machines = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;

    [SubscribeLocalEvent]
    private void OnInit(Entity<ObeliskComponent> ent, ref ComponentInit args)
    {
        EnsureComp<ViewTickerComponent>(ent).range = ent.Comp.Radius;
    }

    [SubscribeLocalEvent]
    private void OnMobStartup(Entity<MobStateComponent> ent, ref ComponentStartup args)
    {
        EnsureComp<ViewRelevantComponent>(ent);
    }

    [SubscribeLocalEvent]
    private void OnTrayInit(Entity<PlantTrayComponent> ent, ref ComponentInit args)
    {
        EnsureComp<ViewRelevantComponent>(ent);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<ObeliskComponent> ent, ref ComponentShutdown args)
    {
        foreach (var implant in ent.Comp.AffectedCruciforms)
            SetRegeneration(implant, ent, null);
        ent.Comp.AffectedCruciforms.Clear();
    }

    [SubscribeLocalEvent]
    private void OnViewTick(Entity<ObeliskComponent> ent, ref ViewTickEvent args)
    {
        Tick(ent, ent.Comp, args.seen);
    }

    private void SetRegeneration(EntityUid implant, EntityUid source, float? multiplier)
    {
        if (!TryComp<CruciformComponent>(implant, out var state))
            return;

        if (multiplier is { } value)
            state.ObeliskRegeneration[source] = value;
        else
            state.ObeliskRegeneration.Remove(source);

        var strongest = Math.Max(1f, state.ObeliskRegeneration.Values.DefaultIfEmpty(1f).Max());
        if (Math.Abs(state.RegenerationMultiplier - strongest) < 0.001f)
            return;

        state.RegenerationMultiplier = strongest;
        _cruciform.RecomputeProfile(implant, state);
    }

    /// <summary>Runs one pulse. Direct callers request a fresh view; event handlers reuse the supplied view.</summary>
    public void Tick(EntityUid uid, ObeliskComponent? obelisk = null, HashSet<EntityUid>? seen = null)
    {
        if (!Resolve(uid, ref obelisk))
            return;

        var affected = new HashSet<EntityUid>();
        var operational = _machines.IsOperational(uid);
        var origin = _transform.GetMapCoordinates(uid);
        if (operational)
        {
            seen ??= _view.GetEntsInView(origin, obelisk.Radius);
            var eye = _eye.FindEye(uid);
            foreach (var target in seen)
            {
                if (TerminatingOrDeleted(target) ||
                    !origin.InRange(_transform.GetMapCoordinates(target), obelisk.Radius))
                    continue;

                if (eye is { } observer)
                    _eye.ObserveEntity(observer, target);

                if (!_cruciform.TryGetCruciform(target, out var implant, out _))
                    continue;

                affected.Add(implant);
                SetRegeneration(implant, uid, obelisk.RegenMultiplier);
                if (TryComp<SanityComponent>(target, out var sanity))
                    _sanity.ApplySanityDelta((target, sanity), SanitySource.Belief, obelisk.SanityPerSecond);
            }
        }

        foreach (var implant in obelisk.AffectedCruciforms)
        {
            if (!affected.Contains(implant))
                SetRegeneration(implant, uid, null);
        }
        obelisk.AffectedCruciforms = affected;

        if (_timing.CurTime >= obelisk.ForceActiveUntil)
            obelisk.ForceActiveUntil = TimeSpan.Zero;

        obelisk.Active = operational && (affected.Count > 0 || obelisk.ForceActiveUntil > _timing.CurTime);
        Dirty(uid, obelisk);
        if (!obelisk.Active || seen == null)
            return;

        var hit = 0;
        foreach (var target in seen)
        {
            if (TerminatingOrDeleted(target) ||
                !origin.InRange(_transform.GetMapCoordinates(target), obelisk.Radius))
                continue;

            if (hit < obelisk.MaxTargets &&
                TryComp<MobStateComponent>(target, out var mobState) && _mobState.IsAlive(target, mobState) &&
                TryComp<NpcFactionMemberComponent>(target, out var faction) &&
                !HasComp<CruciformBearerComponent>(target) && !HasComp<HumanoidProfileComponent>(target) &&
                _factions.IsMemberOfAny((target, faction), obelisk.HostileFactions) &&
                _damageable.TryChangeDamage(target, obelisk.HostileDamage, origin: uid))
                hit++;

            if (TryComp<PlantTrayComponent>(target, out var tray) && tray.WeedLevel > 0)
                _tray.AdjustWeed((target, tray), -obelisk.WeedRemovalPerSecond);
        }
    }
}
