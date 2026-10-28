using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Technomancer.Components;

/// <summary>
/// Ship shield generator — CEV-Eris code/modules/shield_generators/shield_generator.dm.
/// Mode flags with power multipliers, integrity/capacity, upkeep cost, event log.
/// Tendrils simplified to anchored-on-power-check (blueprint §6).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydShipShieldComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Running;

    [DataField, AutoNetworkedField]
    public float Integrity = 1500f;

    [DataField, AutoNetworkedField]
    public float Capacity = 5000f;

    [DataField, AutoNetworkedField]
    public int FieldSize = 4;

    [DataField, AutoNetworkedField]
    public int MaxFieldSize = 200;

    [DataField, AutoNetworkedField]
    public float InputCap; // 0 = no limit

    /// Enabled mode flags (see OxydShieldModes).
    [DataField, AutoNetworkedField]
    public HashSet<string> EnabledModes = new();

    [DataField, AutoNetworkedField]
    public List<string> EventLog = new();

    /// Upkeep watts per integrity point regen, approx from Eris upkeep math.
    [DataField]
    public float UpkeepWattsPerUnit = 50f;
}

/// Eris shield mode table (modes.dm) — name/desc/multiplier, hacked-only subset.
public static class OxydShieldModes
{
    public readonly record struct ModeDef(string Id, string Name, string Desc, float Mult, bool HackedOnly);

    public static readonly ModeDef[] All =
    {
        new("hyperkinetic", "Hyperkinetic Projectiles", "Blocks fast moving physical objects: bullets, blunt weapons, meteors.", 1.2f, false),
        new("photonic", "Photonic Dispersion", "Blocks majority of light. Beam weaponry and most of the visible light spectrum.", 1.3f, false),
        new("humanoids", "Humanoid Lifeforms", "Blocks humanoid lifeforms.", 1.5f, false),
        new("silicons", "Silicon Lifeforms", "Blocks silicon lifeforms.", 1.5f, false),
        new("nonhumans", "Non-Human Entities", "Blocks all non-human entities.", 1.5f, false),
        new("atmos", "Atmospheric Containment", "Blocks atmospheric flow across the shield.", 1.3f, false),
        new("hull", "Hull Shield", "Projects the shield along the hull lines.", 1f, false),
        new("modulate", "Modulated Field", "Fine-grain field modulation.", 2f, false),
        new("bypass", "Access Bypass", "Hacked: allows registered access through the field.", 3f, true),
        new("overcharge", "Overcharged Field", "Hacked: runs the field far past spec.", 3f, true),
        new("multiz", "Multi-Deck Coverage", "Extends the field across decks.", 2f, false),
    };
}

[Serializable, NetSerializable]
public enum OxydShieldUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class OxydShieldState : BoundUserInterfaceState
{
    public bool HasConduit;
    public bool Running;
    public float Integrity;
    public float Capacity;
    public float MitigationEm;
    public float MitigationPh;
    public float MitigationHe;
    public float MitigationMax;
    public float UpkeepKw;
    public float TotalUseKw;
    public int FieldSize;
    public int MaxFieldSize;
    public float InputCap;
    public List<string> EnabledModes = new();
    public List<string> EventLog = new();
    public bool Hacked;
}

[Serializable, NetSerializable]
public sealed class OxydShieldToggleMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class OxydShieldModeMessage(string modeId, bool on) : BoundUserInterfaceMessage
{
    public string ModeId = modeId;
    public bool On = on;
}

[Serializable, NetSerializable]
public sealed class OxydShieldFieldSizeMessage(int size) : BoundUserInterfaceMessage
{
    public int Size = size;
}

[Serializable, NetSerializable]
public sealed class OxydShieldInputCapMessage(float kw) : BoundUserInterfaceMessage
{
    public float Kw = kw;
}

/// <summary>
/// Breaker box — CEV-Eris /obj/machinery/power/breakerbox: a toggleable
/// circuit breaker reporting whether the load behind it is fed. MVP semantics:
/// toggles its own power pass-through state + reports node load to RCON.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydBreakerBoxComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    [DataField, AutoNetworkedField]
    public string RConTag = string.Empty;
}

[Serializable, NetSerializable]
public enum OxydBreakerUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class OxydBreakerState : BoundUserInterfaceState
{
    public bool Enabled;
    public float LoadWatts;
}

[Serializable, NetSerializable]
public sealed class OxydBreakerToggleMessage(bool on) : BoundUserInterfaceMessage
{
    public bool On = on;
}
