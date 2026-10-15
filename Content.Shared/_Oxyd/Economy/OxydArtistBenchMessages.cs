using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Economy;

/// <summary>Eris artist_bench — consume Insight to mint priced artwork.</summary>
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

    [DataField, AutoNetworkedField]
    public EntProtoId StatueProto = "OxydArtworkStatue";

    [DataField, AutoNetworkedField]
    public EntProtoId OddityProto = "OxydArtworkOddity";
}

/// <summary>Marks an entity as Oxyd artwork (Guild trade cut).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydArtworkComponent : Component
{
    [DataField, AutoNetworkedField]
    public float InsightInvested;

    [DataField, AutoNetworkedField]
    public string ArtistName = string.Empty;
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
    public bool CanCreate;
    public string? Error;
    public string? Status;
}

[Serializable, NetSerializable]
public sealed class OxydArtistBenchCreateMessage : BoundUserInterfaceMessage
{
    public float InsightUsed;
}
