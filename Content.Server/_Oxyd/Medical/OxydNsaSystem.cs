using System.Linq;
using Content.Shared._Oxyd.Medical;
using Content.Shared.Bed.Sleep;
using Content.Shared.Damage.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Medical;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Nervous System Accumulation (Eris NSA). Each tick the load is rebuilt from the blood
/// solution: every reagent with an <see cref="Nsa"/> effect contributes Value * quantity.
/// Over the threshold the patient takes toxin damage until the load subsides.
/// </summary>
public sealed partial class OxydNsaSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private VomitSystem _vomit = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;
    [Dependency] private OxydWoundSystem _wounds = default!;

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<OxydNsaComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var now = _timing.CurTime;
            if (now < comp.NextUpdate)
                continue;
            comp.NextUpdate = now + TimeSpan.FromSeconds(comp.UpdateInterval);

            if (_mobs.IsDead(uid))
                continue;

            var contribution = ComputeContribution(uid);
            var wasOver = comp.Current > EffectiveThreshold(comp, uid);
            comp.Current = contribution;
            var isOver = comp.Current > EffectiveThreshold(comp, uid);

            if (isOver && !wasOver)
            {
                _popup.PopupEntity(Loc.GetString("oxyd-medical-nsa-overload"), uid, uid);
                ApplyOverloadEffects(uid, comp);
            }

            if (isOver)
            {
                var excess = comp.Current - EffectiveThreshold(comp, uid);
                _damage.TryChangeDamage(uid, new DamageSpecifier
                {
                    DamageDict = { ["Poison"] = Math.Min(excess / 20f, comp.OverloadToxinPerSecond) * comp.UpdateInterval },
                }, ignoreResistances: true, interruptsDoAfters: false);
            }

            Dirty(uid, comp);
        }
    }

    /// <summary>Eris NSA overload onset: violent nausea, stun, forced sleep and
    /// cardiac strain on top of the ongoing toxin damage.</summary>
    private void ApplyOverloadEffects(EntityUid uid, OxydNsaComponent comp)
    {
        _vomit.Vomit(uid, force: true);
        _stun.TryAddStunDuration(uid, TimeSpan.FromSeconds(comp.OverloadStunSeconds));
        _statusEffects.TryAddStatusEffectDuration(uid, SleepingSystem.StatusEffectForcedSleeping,
            TimeSpan.FromSeconds(comp.OverloadSleepSeconds));

        foreach (var organ in _wounds.GetOrgans(uid))
        {
            if (organ.Organ.Category != comp.HeartOrgan)
                continue;
            OxydWoundSystem.AddOrganDamage(organ.Surgery, comp.OverloadHeartDamage);
            Dirty(organ.Uid, organ.Surgery);
            break;
        }
    }

    private float EffectiveThreshold(OxydNsaComponent comp, EntityUid uid)
    {
        var bonus = comp.ToleranceUntil > _timing.CurTime ? comp.ToleranceBonus : 0f;
        return comp.Threshold + bonus;
    }

    private float ComputeContribution(EntityUid uid)
    {
        if (!_solutions.TryGetSolution(uid, BloodstreamComponent.DefaultBloodSolutionName, out _, out var blood))
            return 0f;

        var total = 0f;
        foreach (var (reagent, quantity) in blood.Contents)
        {
            if (!_prototypes.TryIndex<ReagentPrototype>(reagent.Prototype, out var proto))
                continue;
            if (proto.Metabolisms?.Metabolisms.TryGetValue("Bloodstream", out var entry) != true || entry == null)
                continue;

            foreach (var effect in entry.Effects)
            {
                if (effect is Nsa nsa)
                    total += nsa.Value * quantity.Float();
            }
        }
        return total;
    }

    /// <summary>Applies a temporary NSA tolerance (Eris detox nanites).</summary>
    public void ApplyTolerance(EntityUid uid, float value, float duration)
    {
        var comp = EnsureComp<OxydNsaComponent>(uid);
        comp.ToleranceBonus = Math.Max(comp.ToleranceBonus, value);
        comp.ToleranceUntil = _timing.CurTime + TimeSpan.FromSeconds(duration);
        Dirty(uid, comp);
    }
}
