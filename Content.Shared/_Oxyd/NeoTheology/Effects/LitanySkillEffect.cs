using Content.Shared._Oxyd.Skills;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Skill-map carrier for dependency-gated chants. No runtime handler exists yet, so
/// <see cref="LitanyHandlerCatalog"/> keeps these litanies unavailable; the data is
/// kept so the catalog validator and UI can read it.
/// </summary>
public sealed partial class LitanySkillEffect : LitanyEffect
{
    [DataField]
    public Dictionary<ProtoId<SkillPrototype>, int> Amounts = new();

    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        // No runtime handler: unavailable litanies never reach here, available ones
        // must not silently succeed.
        failure = "oxyd-litany-no-effect";
        return false;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        return false;
    }
}
