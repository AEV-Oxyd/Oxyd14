using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Xenobiology;

/// <summary>Eris xenobiology extract tooling: draws a core out of a
/// refinable biological specimen (slimes in the fork). The cores are
/// scannable/deconstructable research items.</summary>
[RegisterComponent]
public sealed partial class OxydSlimeExtractorComponent : Component
{
    /// <summary>Entity spawned per extraction.</summary>
    [DataField] public EntProtoId CoreProto = "OxydSlimeCore";

    /// <summary>Remaining extractions before the tool empties.</summary>
    [DataField] public int Uses = 3;
}
