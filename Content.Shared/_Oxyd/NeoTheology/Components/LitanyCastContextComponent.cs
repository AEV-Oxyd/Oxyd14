using Content.Shared._Oxyd.NeoTheology.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// Runtime-only cast inputs for the litany currently being applied. The cast transaction
/// refreshes this component on the caster before raising the litany's effects through the
/// shared EntityEffects pipeline, and effect/condition handlers read it back via
/// <c>args.User</c> / the condition's <c>sourceEnt</c>. It replaces the mutable
/// <c>_context</c> field the old bypass pipeline kept on <c>LitanyEffectSystem</c>.
/// </summary>
/// <remarks>
/// Not networked and never serialized: the component exists only for the duration of a
/// cast on this side of the wire. <see cref="CastDepth"/> pairs Push/Pop calls so the
/// component survives until the outermost pass ends; a nested cast still re-stamps the
/// fields, so nested casts are unsupported and warned on.
/// </remarks>
[RegisterComponent]
public sealed partial class LitanyCastContextComponent : Component
{
    /// <summary>The litany prototype being applied.</summary>
    public LitanyPrototype Litany = default!;

    /// <summary>The candidates resolved by the cast transaction at begin time (P4.1).</summary>
    public List<EntityUid> Targets = new();

    /// <summary>Book-choice tokens the caster picked (target/designation/blueprint lists).</summary>
    public List<string>? SelectedTokens;

    /// <summary>Typed text the caster submitted (Sending, baptisms).</summary>
    public string? SelectedText;

    /// <summary>Profile designation chosen for promotion rites.</summary>
    public ProtoId<NeoTheologyProfilePrototype>? Designation;

    /// <summary>Construction blueprint chosen for build rites.</summary>
    public ProtoId<NeoTheologyBlueprintPrototype>? SelectedBlueprint;

    /// <summary>Participants recorded for the running ceremony (starter excluded).</summary>
    public int CeremonyParticipants;

    /// <summary>Set by the most recent effect/condition handler; read by the cast transaction.</summary>
    public bool LastApplySucceeded = true;

    /// <summary>True once any handler reports a failed apply during this cast.</summary>
    public bool ApplyFailed;

    /// <summary>First validation failure a condition reported, surfaced to the caster as the reason.</summary>
    public LocId? ValidationFailure;

    /// <summary>Number of unmatched <c>PushCastContext</c> calls; the component is removed at 0.</summary>
    public int CastDepth;
}
