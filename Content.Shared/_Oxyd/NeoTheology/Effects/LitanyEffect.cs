using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;
using Content.Shared._Oxyd.NeoTheology.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// One declarative litany effect. It is a real <see cref="EntityEffect"/>, so the shared
/// EntityEffects pipeline owns probability, conditions, scale and logging. The litany cast
/// transaction supplies the rich <see cref="LitanyEffectContext"/> through
/// <see cref="ILitanyEffectRaiser"/>.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class LitanyEffect : EntityEffect
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

    /// <inheritdoc/>
    public override void RaiseEvent(EntityUid target, IEntityEffectRaiser raiser, float scale, EntityUid? user)
    {
        if (raiser is not ILitanyEffectRaiser litany)
            return;

        litany.ReportResult(Apply(litany.System, litany.Context));
    }
}

/// <summary>
/// Carries the cast context from <see cref="LitanyEffectSystem"/> into
/// <see cref="LitanyEffect.RaiseEvent"/>. The shared EntityEffects system raises the effect, so
/// the litany system passes itself as the raiser.
/// </summary>
public interface ILitanyEffectRaiser : IEntityEffectRaiser
{
    LitanyEffectSystem System { get; }

    LitanyEffectContext Context { get; }

    /// <summary>Records whether the effect's <see cref="LitanyEffect.Apply"/> accepted the cast.</summary>
    void ReportResult(bool applied);
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
