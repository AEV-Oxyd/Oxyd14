using Content.Shared._Oxyd.Medical;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

public sealed partial class LitanyEffectSystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly PainSystem _pain = default!;

    public bool RejectsHolyInfluence(EntityUid uid) => HasComp<AtheistMutationComponent>(uid);

    public EntityUid MedicalTarget(LitanyEffectContext context) => context.Litany.TargetMode == LitanyTargetMode.Self
        ? context.User
        : context.Targets.Count > 0 ? context.Targets[0] : EntityUid.Invalid;

    public bool CanInjectMedicine(EntityUid uid, FixedPoint2 amount) =>
        uid != EntityUid.Invalid && !IsDead(uid) && HasComp<BloodstreamComponent>(uid) &&
        _solutions.TryGetSolution(uid, BloodstreamComponent.DefaultBloodSolutionName, out _, out var blood) &&
        blood.AvailableVolume >= amount;

    public bool InjectMedicine(EntityUid uid, Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> reagents)
    {
        var dose = new Solution();
        foreach (var (reagent, amount) in reagents)
            dose.AddReagent(reagent.Id, amount);

        return CanInjectMedicine(uid, dose.Volume) && _bloodstream.TryAddToBloodstream(uid, dose);
    }

    public void RelievePain(EntityUid uid, string source, float strength, float seconds = 2f) =>
        _pain.SuppressPain(uid, source, strength, seconds);
}
