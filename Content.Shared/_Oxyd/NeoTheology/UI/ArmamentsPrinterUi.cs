using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.NeoTheology.UI;

[Serializable, NetSerializable]
public enum ArmamentsPrinterUiKey : byte
{
    Key,
}

/// <summary>
/// One purchasable armament, priced for the shop that is asking (the armaments printer or the
/// Eye of the Protector's own armory). The name is already resolved server-side so the client
/// never has to know the prototype.
/// </summary>
[Serializable, NetSerializable]
public sealed class ArmamentEntry
{
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public int Cost { get; }
    public bool Affordable { get; }

    public ArmamentEntry(string id, string name, string description, int cost, bool affordable)
    {
        Id = id;
        Name = name;
        Description = description;
        Cost = cost;
        Affordable = affordable;
    }
}

[Serializable, NetSerializable]
public sealed class ArmamentsPrinterState : BoundUserInterfaceState
{
    public int Points { get; }
    public int MaxPoints { get; }
    public List<ArmamentEntry> Entries { get; }

    public ArmamentsPrinterState(int points, int maxPoints, List<ArmamentEntry> entries)
    {
        Points = points;
        MaxPoints = maxPoints;
        Entries = entries;
    }
}

/// <summary>
/// The client only forwards which armament was clicked; the server revalidates range, follower
/// status and cost. Shared by every armament shop surface (printer and Eye window alike).
/// </summary>
[Serializable, NetSerializable]
public sealed class PurchaseArmamentMessage : BoundUserInterfaceMessage
{
    public string ArmamentId { get; }

    public PurchaseArmamentMessage(string armamentId)
    {
        ArmamentId = armamentId;
    }
}
