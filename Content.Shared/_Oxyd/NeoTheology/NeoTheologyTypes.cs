using Content.Shared.Damage;
using Content.Shared._Oxyd.Skills;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.NeoTheology;

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

public enum LitanyEffectKind : byte
{
    Relief,
    SoulHunger,
    Entreaty,
    Rejection,
    RevealAdversaries,
    CruciformSense,
    Revelation,
    InstallUpgrade,
    UninstallUpgrade,
    Reincarnation,
    Commitment,
    Deprivation,
    AcceleratedGrowth,
    HandOfMercy,
    AbsolutionOfWounds,
    WordsOfPurging,
    Epiphany,
    Asacris,
    GraceOfPerseverance,
    UpholdHolyWord,
    Atonement,
    BaptismalRecord,
    DivineIntervention,
    HolyGuidance,
    DivineBlessing,
    Confirmation,
    Adoption,
    Ordination,
    Omission,
    Excommunication,
    OrderArmaments,
    DivineGuidance,
    Manifestation,
    Uproot,
    Penance,
    Convalescence,
    Succour,
    Scrying,
    Sending,
    Initiation,
    Knowledge,
    Bounty,
    Resurrection,
    MakeCruciform,
    ActivateDoor,
    RepairDoor,
    PowerBiogenerator,
    BioreactorSolution,
    BioreactorChamber,
    PoundingWhisper,
    RevelationOfSecrets,
    LispOfVitae,
    CantoOfCourage,
    ChantOfObservance,
    ReclamationOfEndurance,
    Sanctify,
    Crusade,
    EternalBrotherhood,
    CallToBattle,
    SearingRevelation,
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
