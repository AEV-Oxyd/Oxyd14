using Content.Shared.Damage;
using Content.Shared._Oxyd.Skills;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.NeoTheology;

/// <summary>Eris <c>CLEARANCE_NONE</c> / <c>COMMON</c> / <c>CLERGY</c>. Holy doors compare this number.</summary>
public enum NeoTheologyClearance : byte
{
    None = 0,
    Common = 1,
    Clergy = 2,
}

public enum LitanyCategory : byte
{
    Common,
    Acolyte,
    Agrolyte,
    Custodian,
    Priest,
    Inquisitor,
    Machinery,
    Group,
    Crusader,
    Offerings,
}

/// <summary>
/// Optional target-geometry override on a litany. Default leaves resolution entirely to
/// <see cref="LitanyTargetMode"/>.
/// </summary>
public enum LitanyTargetShape : byte
{
    Default,
    /// <summary>Resolve targets along the caster's forward ray (Eris Revelation).</summary>
    Cone,
}

public enum LitanyTargetMode : byte
{
    Self,
    AdjacentLiving,
    AdjacentFollower,
    VisibleFollower,
    StationFollower,
    FrontMachine,
    NearbyMachine,
    VisibleArea,
    FrontTile,
    Ceremony,
    None,
    GlobalFollower,
}

public enum LitanyCooldownScope : byte
{
    None,
    Personal,
    Global,
}

public enum LitanyCastOrigin : byte
{
    ManualSpeech,
    Book,
    Ceremony,
}

public enum NeoTheologyDependency : byte
{
    None,
    Purity,
    ThreatClassification,
    Attachments,
    SoulCloning,
    PlantGrowth,
    Addiction,
    CoreModules,
    Pain,
    EyeEconomy,
    Armaments,
    ConstructionCatalog,
    Construction,
    ForgeMaterials,
    BiomatterMaterials,
    Biogenerator,
    Bioreactor,
    Ceremonies,
    RemoteView,
    NtUplink,
}

/// <summary>
/// Server result shared by the UI and integration tests. Reasons are localization
/// identifiers, never arbitrary client-provided text.
/// </summary>
[Serializable, NetSerializable]
public sealed class LitanyActionResult
{
    public bool Success { get; }
    public LocId? Reason { get; }
    public string? RequestId { get; }

    public LitanyActionResult(bool success, LocId? reason = null, string? requestId = null)
    {
        Success = success;
        Reason = reason;
        RequestId = requestId;
    }

    public static LitanyActionResult Ok(string? requestId = null) => new(true, null, requestId);
    public static LitanyActionResult Fail(LocId reason, string? requestId = null) => new(false, reason, requestId);
}
