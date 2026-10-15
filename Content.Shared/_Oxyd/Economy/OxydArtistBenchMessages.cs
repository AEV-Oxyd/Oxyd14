using Content.Shared.Materials;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Economy;

/// <summary>Eris artist_bench — materials + insight (+ oddity inspiration for artists).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydArtistBenchComponent : Component
{
    [DataField, AutoNetworkedField]
    public float MinInsight = 40f;

    [DataField, AutoNetworkedField]
    public float MaxInsightPerWork = 100f;

    /// <summary>Credits = BasePrice + floor(InsightUsed * PricePerInsight).</summary>
    [DataField, AutoNetworkedField]
    public int BasePrice = 50;

    [DataField, AutoNetworkedField]
    public float PricePerInsight = 2f;

    /// <summary>Eris min_mat — each suitable material must have at least this much.</summary>
    [DataField, AutoNetworkedField]
    public int MinMaterial = 20;

    [DataField, AutoNetworkedField]
    public List<ProtoId<MaterialPrototype>> SuitableMaterials = new()
    {
        "Wood",
        "Steel",
        "Glass",
        "Plasteel",
        "Plastic",
    };

    [DataField]
    public string OdditySlotId = "oddity_slot";

    [DataField, AutoNetworkedField]
    public EntProtoId StatueProto = "OxydArtworkStatue";

    [DataField, AutoNetworkedField]
    public EntProtoId OddityProto = "OxydArtworkOddity";

    [DataField, AutoNetworkedField]
    public EntProtoId ToolmodProto = "OxydArtworkToolmod";

    [DataField, AutoNetworkedField]
    public EntProtoId GunPartProto = "OxydArtworkGunPart";

    [DataField, AutoNetworkedField]
    public EntProtoId ArmorPartProto = "OxydArtworkArmorPart";
}

/// <summary>Eris PERK_ARTIST — choose insight spend; oddity inspiration weights apply.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OxydArtistPerkComponent : Component;

/// <summary>Marks an entity as Oxyd artwork (Guild trade cut).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydArtworkComponent : Component
{
    [DataField, AutoNetworkedField]
    public float InsightInvested;

    [DataField, AutoNetworkedField]
    public string ArtistName = string.Empty;

    [DataField, AutoNetworkedField]
    public string ArtKind = string.Empty;
}

[Serializable, NetSerializable]
public enum OxydArtistBenchUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class OxydArtistBenchUiState : BoundUserInterfaceState
{
    public float Insight;
    public float MinInsight;
    public float MaxInsight;
    public bool IsArtist;
    public bool CanCreate;
    public string? OddityName;
    public Dictionary<string, int> OddityStats = new();
    public Dictionary<string, int> Materials = new();
    public int MinMaterial;
    public string? Error;
    public string? Status;
}

[Serializable, NetSerializable]
public sealed class OxydArtistBenchCreateMessage : BoundUserInterfaceMessage
{
    public float InsightUsed;
}
