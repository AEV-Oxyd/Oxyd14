using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Programs;

/// <summary>
/// Security Lock application (/bin/security_lock.nte) allowing users to lock a computer
/// behind a password, a key-based password (.key file), and/or a configurable set of ID access tags.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SecurityLockProgramComponent : Component
{
    [DataField, AutoNetworkedField]
    public string StatusMessage = string.Empty;

    /// <summary>
    /// On non-jailbroken operating systems, SystemAdmin authorization is required
    /// before configuring or toggling software security locks.
    /// Automatically cleared on close/deactivation.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsSystemAdminAuthorized;

    [DataField, AutoNetworkedField]
    public string AuthorizedByLabel = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SecurityLockKeyFileDto
{
    public string FilePath { get; }
    public string Label { get; }
    public string KeyId { get; }
    public bool FromDisk { get; }
    public bool IsActiveLockKey { get; }

    public SecurityLockKeyFileDto(
        string filePath,
        string label,
        string keyId,
        bool fromDisk,
        bool isActiveLockKey)
    {
        FilePath = filePath;
        Label = label;
        KeyId = keyId;
        FromDisk = fromDisk;
        IsActiveLockKey = isActiveLockKey;
    }
}

[Serializable, NetSerializable]
public sealed class SecurityLockAccessOptionDto
{
    public string AccessId { get; }
    public string DisplayName { get; }
    public bool IsSelected { get; }

    public SecurityLockAccessOptionDto(string accessId, string displayName, bool isSelected)
    {
        AccessId = accessId;
        DisplayName = displayName;
        IsSelected = isSelected;
    }
}

[Serializable, NetSerializable]
public sealed class SecurityLockUiState : BoundUserInterfaceState
{
    public string DeviceGuid { get; }
    public string HostName { get; }
    public bool IsSoftwareLocked { get; }
    public bool HasPassword { get; }
    public bool HasKeyLock { get; }
    public string KeyLabel { get; }
    public string KeyId { get; }
    public bool RequireAllAccess { get; }
    public List<SecurityLockAccessOptionDto> AccessOptions { get; }
    public List<SecurityLockKeyFileDto> KeyFiles { get; }
    public bool HasInsertedDisk { get; }
    public string? InsertedDiskName { get; }
    public bool HasCardReader { get; }
    public bool HasHostHardwareLock { get; }
    public bool IsJailbrokenOs { get; }
    public bool RequiresSystemAdminAuth { get; }
    public bool IsSystemAdminAuthorized { get; }
    public string AuthorizedByLabel { get; }
    public bool RemoteLocked { get; }
    public string StatusMessage { get; }

    public SecurityLockUiState(
        string deviceGuid,
        string hostName,
        bool isSoftwareLocked,
        bool hasPassword,
        bool hasKeyLock,
        string keyLabel,
        string keyId,
        bool requireAllAccess,
        List<SecurityLockAccessOptionDto> accessOptions,
        List<SecurityLockKeyFileDto> keyFiles,
        bool hasInsertedDisk,
        string? insertedDiskName,
        bool hasCardReader,
        bool hasHostHardwareLock,
        bool isJailbrokenOs,
        bool requiresSystemAdminAuth,
        bool isSystemAdminAuthorized,
        string authorizedByLabel,
        bool remoteLocked,
        string statusMessage)
    {
        DeviceGuid = deviceGuid;
        HostName = hostName;
        IsSoftwareLocked = isSoftwareLocked;
        HasPassword = hasPassword;
        HasKeyLock = hasKeyLock;
        KeyLabel = keyLabel;
        KeyId = keyId;
        RequireAllAccess = requireAllAccess;
        AccessOptions = accessOptions;
        KeyFiles = keyFiles;
        HasInsertedDisk = hasInsertedDisk;
        InsertedDiskName = insertedDiskName;
        HasCardReader = hasCardReader;
        HasHostHardwareLock = hasHostHardwareLock;
        IsJailbrokenOs = isJailbrokenOs;
        RequiresSystemAdminAuth = requiresSystemAdminAuth;
        IsSystemAdminAuthorized = isSystemAdminAuthorized;
        AuthorizedByLabel = authorizedByLabel;
        RemoteLocked = remoteLocked;
        StatusMessage = statusMessage;
    }
}

[Serializable, NetSerializable]
public enum SecurityLockAction : byte
{
    Refresh = 0,
    LockNow = 1,
    SetPassword = 2,
    ClearPassword = 3,
    GenerateKey = 4,
    BindKeyFile = 5,
    ExportKeyToDisk = 6,
    ClearKey = 7,
    ToggleAccessTag = 8,
    ClearAccessTags = 9,
    ToggleRequireAllAccess = 10,
    ClearAllLocks = 11,
    AuthorizeSystemAdmin = 12,
    ToggleRemoteLock = 13
}

[Serializable, NetSerializable]
public sealed class SecurityLockActionMessage : BoundUserInterfaceMessage
{
    public SecurityLockAction Action { get; }
    public string Argument { get; }
    public bool FromDisk { get; }

    public SecurityLockActionMessage(SecurityLockAction action, string argument = "", bool fromDisk = false)
    {
        Action = action;
        Argument = argument;
        FromDisk = fromDisk;
    }
}
