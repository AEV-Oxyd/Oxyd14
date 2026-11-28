using Content.Shared.DeviceLinking.Payloads;

namespace Content.Shared._Oxyd.Computers.Packets;

/// <summary>
/// Server-side packet structure used for communication across DeviceLinks between computers,
/// programs, and machines. Strictly NOT networked or serialized over the wire.
/// </summary>
public sealed partial class ComputerPacket : ISignalNetworkPayload
{
    public string PacketType = string.Empty;

    /// <summary>
    /// Open data fields: strings, ints, floats, booleans. Player-viewable and editable via terminal.
    /// </summary>
    public Dictionary<string, string> Data = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Protected technical references: EntityUids, server-internal handles.
    /// Inaccessible and uneditable by player terminal commands to prevent entity spoofing.
    /// </summary>
    public Dictionary<string, EntityUid> ProtectedReferences = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Server-internal flag indicating a VFS packet write should mark the updated database as tampered
    /// so auto-sync does not immediately overwrite it. Never exposed in <see cref="Data"/>, logs, or Packet Sniffer.
    /// </summary>
    public bool InternalMarkTampered;

    public ComputerPacket()
    {
    }

    public ComputerPacket(string packetType)
    {
        PacketType = packetType;
    }

    public string? GetString(string key, string? defaultValue = null)
    {
        return Data.TryGetValue(key, out var val) ? val : defaultValue;
    }

    public int GetInt(string key, int defaultValue = 0)
    {
        return Data.TryGetValue(key, out var val) && int.TryParse(val, out var parsed) ? parsed : defaultValue;
    }

    public float GetFloat(string key, float defaultValue = 0f)
    {
        return Data.TryGetValue(key, out var val) && float.TryParse(val, out var parsed) ? parsed : defaultValue;
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        return Data.TryGetValue(key, out var val) && bool.TryParse(val, out var parsed) ? parsed : defaultValue;
    }

    public void Set(string key, string value) => Data[key] = value;
    public void Set(string key, int value) => Data[key] = value.ToString();
    public void Set(string key, float value) => Data[key] = value.ToString();
    public void Set(string key, bool value) => Data[key] = value ? "true" : "false";

    public EntityUid? GetReference(string key)
    {
        return ProtectedReferences.TryGetValue(key, out var val) ? val : null;
    }

    public void SetReference(string key, EntityUid uid) => ProtectedReferences[key] = uid;
}
