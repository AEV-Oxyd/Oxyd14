using Content.Shared.CartridgeLoader;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Ntos;

// ---------------------------------------------------------------------------
// Station Records (live)
// ---------------------------------------------------------------------------

[RegisterComponent, NetworkedComponent]
public sealed partial class OxydRecordsCartridgeComponent : Component;

[Serializable, NetSerializable]
public sealed class OxydRecordsUiState : BoundUserInterfaceState
{
    public string StationName = string.Empty;
    public List<OxydRecordListEntry> Records = new();
    public OxydRecordDetail? Active;
    public string? Error;
}

[Serializable, NetSerializable]
public sealed class OxydRecordListEntry
{
    public uint Id;
    public string Name = string.Empty;
    public string Job = string.Empty;
}

[Serializable, NetSerializable]
public sealed class OxydRecordDetail
{
    public uint Id;
    public string Name = string.Empty;
    public string Job = string.Empty;
    public int Age;
    public string Species = string.Empty;
    public string Gender = string.Empty;
    public string? Fingerprint;
    public string? Dna;
}

[Serializable, NetSerializable]
public sealed class OxydRecordsUiMessageEvent : CartridgeMessageEvent
{
    public OxydRecordsUiAction Action;
    public uint RecordId;

    public OxydRecordsUiMessageEvent(OxydRecordsUiAction action, uint recordId = 0)
    {
        Action = action;
        RecordId = recordId;
    }
}

[Serializable, NetSerializable]
public enum OxydRecordsUiAction : byte
{
    Refresh,
    Select,
    Clear,
}

// ---------------------------------------------------------------------------
// Cargo Bounty board (live)
// ---------------------------------------------------------------------------

[RegisterComponent, NetworkedComponent]
public sealed partial class OxydBountyCartridgeComponent : Component;

[Serializable, NetSerializable]
public sealed class OxydBountyUiState : BoundUserInterfaceState
{
    public string StationName = string.Empty;
    public List<OxydBountyListEntry> Active = new();
    public List<OxydBountyListEntry> History = new();
    public string? Error;
}

[Serializable, NetSerializable]
public sealed class OxydBountyListEntry
{
    public string Id = string.Empty;
    public string Title = string.Empty;
    public string Description = string.Empty;
    public int Reward;
    public string Status = string.Empty;
}

[Serializable, NetSerializable]
public sealed class OxydBountyUiMessageEvent : CartridgeMessageEvent
{
    public OxydBountyUiAction Action;

    public OxydBountyUiMessageEvent(OxydBountyUiAction action)
    {
        Action = action;
    }
}

[Serializable, NetSerializable]
public enum OxydBountyUiAction : byte
{
    Refresh,
}

// ---------------------------------------------------------------------------
// Email client
// ---------------------------------------------------------------------------

[RegisterComponent, NetworkedComponent]
public sealed partial class OxydEmailCartridgeComponent : Component;

[Serializable, NetSerializable]
public sealed class OxydEmailUiState : BoundUserInterfaceState
{
    public bool LoggedIn;
    public string Login = string.Empty;
    public List<OxydEmailListEntry> Inbox = new();
    public OxydEmailDetail? Active;
    public string? Status;
    public string? Error;
}

[Serializable, NetSerializable]
public sealed class OxydEmailListEntry
{
    public int Id;
    public string From = string.Empty;
    public string Subject = string.Empty;
    public bool Read;
}

[Serializable, NetSerializable]
public sealed class OxydEmailDetail
{
    public int Id;
    public string From = string.Empty;
    public string To = string.Empty;
    public string Subject = string.Empty;
    public string Body = string.Empty;
}

[Serializable, NetSerializable]
public sealed class OxydEmailUiMessageEvent : CartridgeMessageEvent
{
    public OxydEmailUiAction Action;
    public string Login = string.Empty;
    public string Password = string.Empty;
    public string To = string.Empty;
    public string Subject = string.Empty;
    public string Body = string.Empty;
    public int MessageId;

    public OxydEmailUiMessageEvent(OxydEmailUiAction action)
    {
        Action = action;
    }
}

[Serializable, NetSerializable]
public enum OxydEmailUiAction : byte
{
    Login,
    Logout,
    Register,
    Refresh,
    Open,
    Send,
    Delete,
}

// ---------------------------------------------------------------------------
// NTNRC chat
// ---------------------------------------------------------------------------

[RegisterComponent, NetworkedComponent]
public sealed partial class OxydNtChatCartridgeComponent : Component;

[Serializable, NetSerializable]
public sealed class OxydNtChatUiState : BoundUserInterfaceState
{
    public string Username = string.Empty;
    public int? ActiveChannelId;
    public string? ActiveChannelTitle;
    public List<OxydChatChannelEntry> Channels = new();
    public List<string> Messages = new();
    public string? Status;
    public string? Error;
}

[Serializable, NetSerializable]
public sealed class OxydChatChannelEntry
{
    public int Id;
    public string Title = string.Empty;
    public int Users;
}

[Serializable, NetSerializable]
public sealed class OxydNtChatUiMessageEvent : CartridgeMessageEvent
{
    public OxydNtChatUiAction Action;
    public string Text = string.Empty;
    public int ChannelId;

    public OxydNtChatUiMessageEvent(OxydNtChatUiAction action)
    {
        Action = action;
    }
}

[Serializable, NetSerializable]
public enum OxydNtChatUiAction : byte
{
    SetName,
    Refresh,
    Join,
    Leave,
    Create,
    Speak,
}

// ---------------------------------------------------------------------------
// Signaller program (needs OxydHwSignaller peripheral)
// ---------------------------------------------------------------------------

[RegisterComponent, NetworkedComponent]
public sealed partial class OxydSignallerCartridgeComponent : Component;

[Serializable, NetSerializable]
public sealed class OxydSignallerUiState : BoundUserInterfaceState
{
    public bool HasHardware;
    public bool HasNetwork;
    public int NetworkRange;
    public string? Status;
    public string? Error;
}

[Serializable, NetSerializable]
public sealed class OxydSignallerUiMessageEvent : CartridgeMessageEvent
{
    public OxydSignallerUiAction Action;

    public OxydSignallerUiMessageEvent(OxydSignallerUiAction action)
    {
        Action = action;
    }
}

[Serializable, NetSerializable]
public enum OxydSignallerUiAction : byte
{
    Refresh,
    Fire,
}

// ---------------------------------------------------------------------------
// Scanner program (needs scanner peripheral)
// ---------------------------------------------------------------------------

[RegisterComponent, NetworkedComponent]
public sealed partial class OxydScannerCartridgeComponent : Component;

[Serializable, NetSerializable]
public sealed class OxydScannerUiState : BoundUserInterfaceState
{
    public string ScannerKind = "none";
    public string Buffer = string.Empty;
    public bool CanPrint;
    public string? Status;
    public string? Error;
}

[Serializable, NetSerializable]
public sealed class OxydScannerUiMessageEvent : CartridgeMessageEvent
{
    public OxydScannerUiAction Action;

    public OxydScannerUiMessageEvent(OxydScannerUiAction action)
    {
        Action = action;
    }
}

[Serializable, NetSerializable]
public enum OxydScannerUiAction : byte
{
    Refresh,
    Clear,
    Print,
}
