using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.NeoTheology.UI;

[Serializable, NetSerializable]
public enum EyeOfTheProtectorUiKey : byte
{
    Key,
}

/// <summary>
/// P3.7: read-only snapshot of the Eye's status. The client displays it verbatim — no messages,
/// no buttons; the server re-pushes on every UI open.
/// </summary>
[Serializable, NetSerializable]
public sealed class EyeOfTheProtectorState : BoundUserInterfaceState
{
    public float Observation { get; }
    public int ArmamentsPoints { get; }
    public int MaxArmamentsPoints { get; }
    public TimeSpan MiracleCooldown { get; }

    public EyeOfTheProtectorState(float observation, int armamentsPoints, int maxArmamentsPoints, TimeSpan miracleCooldown)
    {
        Observation = observation;
        ArmamentsPoints = armamentsPoints;
        MaxArmamentsPoints = maxArmamentsPoints;
        MiracleCooldown = miracleCooldown;
    }
}
