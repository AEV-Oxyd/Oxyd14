using Robust.Shared.Prototypes;
using Content.Shared._Oxyd.NeoTheology.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// One declarative litany effect, modelled on
/// <see cref="Content.Shared.EntityEffects.EntityEffect"/>: data comes from YAML,
/// behaviour from <see cref="CanApply"/> and <see cref="Apply"/>.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class LitanyEffect
{
    /// <summary>
    /// True for the dead-only flows (Deprivation's extraction; resurrection later). Target
    /// resolution excludes Dead mobs by default (§7.1); these effects need them delivered.
    /// </summary>
    public virtual bool AllowsDeadTarget => false;

    /// <summary>
    /// Validates the effect before any holiness is debited. Must not mutate state.
    /// </summary>
    public abstract bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure);

    /// <summary>
    /// Applies the effect. Only called after every effect in the litany validated.
    /// </summary>
    public abstract bool Apply(LitanyEffectSystem system, LitanyEffectContext context);
}

/// <summary>
/// Runtime context handed to a <see cref="LitanyEffect"/>. <see cref="Targets"/> is the
/// candidate list resolved by the cast transaction at begin time (P4.1); effects that
/// predate targeting ignore it and re-derive their own set.
/// </summary>
public readonly record struct LitanyEffectContext(
    EntityUid User,
    LitanyPrototype Litany,
    IReadOnlyList<EntityUid> Targets,
    IReadOnlyList<string>? SelectedTokens = null,
    string? SelectedText = null,
    ProtoId<NeoTheologyProfilePrototype>? Designation = null,
    ProtoId<NeoTheologyBlueprintPrototype>? SelectedBlueprint = null,
    int CeremonyParticipants = 0);
