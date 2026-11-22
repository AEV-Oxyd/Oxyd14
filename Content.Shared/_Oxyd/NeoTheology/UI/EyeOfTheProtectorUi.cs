using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.NeoTheology.UI;

[Serializable, NetSerializable]
public enum EyeOfTheProtectorUiKey : byte
{
    Key,
}

/// <summary>
/// Snapshot of the Eye's status and its armory rows (Eris <c>eopt.tmpl</c>'s armaments list).
/// Purchases arrive as <see cref="PurchaseArmamentMessage"/> and are revalidated server-side.
/// </summary>
[Serializable, NetSerializable]
public sealed class EyeOfTheProtectorState : BoundUserInterfaceState
{
    public float Observation { get; }
    public int ArmamentsPoints { get; }
    public int MaxArmamentsPoints { get; }
    public TimeSpan MiracleCooldown { get; }
    public List<ArmamentEntry> Armaments { get; }

    public EyeOfTheProtectorState(float observation, int armamentsPoints, int maxArmamentsPoints,
        TimeSpan miracleCooldown, List<ArmamentEntry> armaments)
    {
        Observation = observation;
        ArmamentsPoints = armamentsPoints;
        MaxArmamentsPoints = maxArmamentsPoints;
        MiracleCooldown = miracleCooldown;
        Armaments = armaments;
    }
}
