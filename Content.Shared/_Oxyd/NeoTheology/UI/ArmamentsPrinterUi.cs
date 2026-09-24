using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.NeoTheology.UI;

[Serializable, NetSerializable]
public enum ArmamentsPrinterUiKey : byte
{
    Key,
}

/// <summary>
/// One purchasable armament, priced for the printer that is asking. The name is already resolved
/// server-side so the client never has to know the prototype.
/// </summary>
[Serializable, NetSerializable]
public sealed class ArmamentsPrinterEntry
{
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public int Cost { get; }
    public bool Affordable { get; }

    public ArmamentsPrinterEntry(string id, string name, string description, int cost, bool affordable)
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
    public List<ArmamentsPrinterEntry> Entries { get; }

    public ArmamentsPrinterState(int points, int maxPoints, List<ArmamentsPrinterEntry> entries)
    {
        Points = points;
        MaxPoints = maxPoints;
        Entries = entries;
    }
}

/// <summary>
/// The client only forwards which armament was clicked; the server revalidates range, follower
/// status and cost.
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
