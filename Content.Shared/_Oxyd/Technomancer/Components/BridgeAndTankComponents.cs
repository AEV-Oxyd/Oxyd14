using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Technomancer.Components;

/// <summary>
/// Pulsar power bridge — CEV-Eris /obj/machinery/power/pulsar_power_bridge.
/// Injects up to PULSAR_100_POWER * effective% into the powernet, disconnect/
/// reconnect with a 5-min cooldown (RECONNECT_CD).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydPulsarBridgeComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Connected = true;

    /// Seconds remaining before reconnect is allowed (RECONNECT_CD = 300).
    [DataField, AutoNetworkedField]
    public float ReconnectCooldown;

    [DataField]
    public float ReconnectCooldownMax = 300f;

    /// Linked console entity (set at spawn or via multitool later — MVP: nearest console).
    [DataField, AutoNetworkedField]
    public EntityUid? Console;

    /// Current injected watts (produced * effective).
    [DataField, AutoNetworkedField]
    public float InjectedWatts;
}

[Serializable, NetSerializable]
public enum OxydPulsarBridgeUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class OxydPulsarBridgeState : BoundUserInterfaceState
{
    public bool Connected;
    public float InjectedWatts;
    public float ReconnectCooldown;
    public bool HasConsole;
}

[Serializable, NetSerializable]
public sealed class OxydPulsarBridgeToggleMessage : BoundUserInterfaceMessage;

/// <summary>
/// Pulsar fuel tank — CEV-Eris /obj/structure/pulsar_fuel_tank (700u, refilled
/// by item tanks, bursts/explodes on overpressure — simplified to capacity).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydPulsarFuelTankComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Fuel = 700;

    [DataField]
    public int Capacity = 700;
}
