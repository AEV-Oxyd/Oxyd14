using System.Linq;
using Content.Shared._Oxyd.Medical;
using Content.Shared.Damage.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
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
                _popup.PopupEntity(Loc.GetString("oxyd-medical-nsa-overload"), uid, uid);

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
