using System.Linq;
using Content.Server._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Access;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Lock;
using Content.Shared.PDA;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Computers.Programs;

/// <summary>
/// Server runtime for /bin/security_lock.nte (<see cref="SecurityLockProgramComponent"/>).
/// Allows configuring password locks, key-based passwords (.key files), and ID card access sets
/// on the local computer, and engaging the full-screen software security lock.
/// </summary>
public sealed partial class SecurityLockSystem : EntitySystem
{
    [Dependency] private OxydComputerSystem _computer = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private ComputerCardReaderSystem _cardReaders = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private SharedDeviceGuidSystem _guid = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    [Dependency] private EntityQuery<ProgramComponent> _programQuery = default!;
    [Dependency] private EntityQuery<OxydComputerComponent> _computerQuery = default!;
    [Dependency] private EntityQuery<PdaComponent> _pdaQuery = default!;

    private static readonly ProtoId<AccessLevelPrototype> SystemAdminAccess = "SystemAdmin";

    private const int MaxPasswordLength = 64;
    private const int MaxKeyLabelLength = 36;

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<SecurityLockProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnBuiOpened);
            subs.Event<SecurityLockActionMessage>(OnUiAction);
        });
    }

    [SubscribeLocalEvent]
    private void OnStartup(Entity<SecurityLockProgramComponent> ent, ref ProgramStartupEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnActivated(Entity<SecurityLockProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnDeactivated(Entity<SecurityLockProgramComponent> ent, ref ProgramDeactivatedEvent args)
    {
        RevokeSystemAdminAuth(ent);
    }

    [SubscribeLocalEvent]
    private void OnTerminated(Entity<SecurityLockProgramComponent> ent, ref ProgramTerminatedEvent args)
    {
        RevokeSystemAdminAuth(ent);
    }

    [SubscribeLocalEvent]
    private void OnHardwareChanged(Entity<SecurityLockProgramComponent> ent, ref ComputerHardwareChangedEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnCardReaderChanged(Entity<SecurityLockProgramComponent> ent, ref ComputerCardReaderChangedEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnOsAuthChanged(Entity<SecurityLockProgramComponent> ent, ref ComputerOsAuthChangedEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnCardScanned(Entity<SecurityLockProgramComponent> ent, ref ProgramCardScannedEvent args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computer.IsJailbrokenOs(compUid) &&
            !ent.Comp.IsSystemAdminAuthorized &&
            _computer.HasAnyAccessTag(compUid, args.Card, new[] { SystemAdminAccess }, out var label))
        {
            ent.Comp.IsSystemAdminAuthorized = true;
            ent.Comp.AuthorizedByLabel = label;
            ent.Comp.StatusMessage = $"Authorized ({label}).";
            Dirty(ent);
            LogSecurityEvent(compUid, $"Security Lock authorized by System Admin ({label}).");
        }

        UpdateUi(ent.Owner, compUid, ent.Comp);
    }

    private void RevokeSystemAdminAuth(Entity<SecurityLockProgramComponent> ent)
    {
        if (!ent.Comp.IsSystemAdminAuthorized && string.IsNullOrEmpty(ent.Comp.AuthorizedByLabel))
            return;

        ent.Comp.IsSystemAdminAuthorized = false;
        ent.Comp.AuthorizedByLabel = string.Empty;
        Dirty(ent);
    }

    private void OnBuiOpened(Entity<SecurityLockProgramComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        UpdateUi(ent.Owner, compUid, ent.Comp);
    }

    private void OnUiAction(Entity<SecurityLockProgramComponent> ent, ref SecurityLockActionMessage msg)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computer.CanExecuteProgramAction(ent.Owner, msg.Actor, out var blockReason))
        {
            ent.Comp.StatusMessage = blockReason;
            Dirty(ent);
            UpdateUi(ent.Owner, compUid, ent.Comp);
            return;
        }

        if (!_computerQuery.TryComp(compUid, out var comp))
            return;

        var isJailbroken = _computer.IsJailbrokenOs(compUid, comp);
        if (msg.Action == SecurityLockAction.AuthorizeSystemAdmin)
        {
            if (isJailbroken)
            {
                ent.Comp.StatusMessage = "Authorized.";
            }
            else if (_computer.HasAnyAccessTag(compUid, msg.Actor, new[] { SystemAdminAccess }, out var label, comp: comp))
            {
                ent.Comp.IsSystemAdminAuthorized = true;
                ent.Comp.AuthorizedByLabel = label;
                ent.Comp.StatusMessage = $"Authorized ({label}).";
                LogSecurityEvent(compUid, $"Security Lock authorized by System Admin ({label}).");
            }
            else
            {
                ent.Comp.StatusMessage = "Access denied: System Admin access required.";
            }

            Dirty(ent);
            UpdateUi(ent.Owner, compUid, ent.Comp);
            return;
        }

        if (msg.Action != SecurityLockAction.Refresh &&
            msg.Action != SecurityLockAction.LockNow &&
            !isJailbroken &&
            !ent.Comp.IsSystemAdminAuthorized)
        {
            ent.Comp.StatusMessage = "System Admin authorization required to modify lock settings.";
            Dirty(ent);
            UpdateUi(ent.Owner, compUid, ent.Comp);
            return;
        }

        switch (msg.Action)
        {
            case SecurityLockAction.Refresh:
                ent.Comp.StatusMessage = string.Empty;
                break;

            case SecurityLockAction.LockNow:
                if (!HasAnyConfiguredAuthMethod(comp))
                {
                    ent.Comp.StatusMessage = "Configure a password, key file, or ID access requirement before locking.";
                    break;
                }

                _computer.EngageSoftwareLock(compUid, comp);
                RevokeSystemAdminAuth(ent);
                LogSecurityEvent(compUid, "Computer locked via Security Lock.");
                ent.Comp.StatusMessage = "Computer locked.";
                _computer.UpdateComputerDisplayUi(compUid, comp);
                break;

            case SecurityLockAction.SetPassword:
                var pw = (msg.Argument ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(pw))
                {
                    ent.Comp.StatusMessage = "Enter a non-empty password.";
                    break;
                }

                if (pw.Length > MaxPasswordLength)
                {
                    ent.Comp.StatusMessage = $"Password too long (max {MaxPasswordLength} characters).";
                    break;
                }

                comp.LockPassword = pw;
                comp.SshPassword = pw;
                comp.LockEpoch++;
                Dirty(compUid, comp);
                LogSecurityEvent(compUid, "Updated computer password lock.");
                ent.Comp.StatusMessage = "Password lock updated.";
                _computer.UpdateComputerDisplayUi(compUid, comp);
                break;

            case SecurityLockAction.ClearPassword:
                comp.LockPassword = string.Empty;
                comp.SshPassword = string.Empty;
                Dirty(compUid, comp);
                LogSecurityEvent(compUid, "Cleared password lock.");
                ent.Comp.StatusMessage = "Password lock cleared.";
                _computer.UpdateComputerDisplayUi(compUid, comp);
                break;

            case SecurityLockAction.GenerateKey:
                HandleGenerateKey(compUid, comp, ent.Comp, msg.Argument);
                break;

            case SecurityLockAction.BindKeyFile:
                HandleBindKeyFile(compUid, comp, ent.Comp, msg.Argument, msg.FromDisk);
                break;

            case SecurityLockAction.ExportKeyToDisk:
                HandleExportKeyToDisk(compUid, comp, ent.Comp);
                break;

            case SecurityLockAction.ClearKey:
                comp.LockKeyLabel = string.Empty;
                comp.LockKeyId = string.Empty;
                comp.LockKeySecret = string.Empty;
                Dirty(compUid, comp);
                LogSecurityEvent(compUid, "Cleared key-based password lock.");
                ent.Comp.StatusMessage = "Key-based password removed.";
                _computer.UpdateComputerDisplayUi(compUid, comp);
                break;

            case SecurityLockAction.ToggleAccessTag:
                var tagStr = (msg.Argument ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(tagStr) || !_proto.HasIndex<AccessLevelPrototype>(tagStr))
                {
                    ent.Comp.StatusMessage = $"Unknown access level '{tagStr}'.";
                    break;
                }

                var tag = new ProtoId<AccessLevelPrototype>(tagStr);
                if (comp.RequiredAccessTags.Contains(tag))
                {
                    comp.RequiredAccessTags.Remove(tag);
                    ent.Comp.StatusMessage = $"Removed '{tagStr}' from required ID accesses.";
                }
                else
                {
                    comp.RequiredAccessTags.Add(tag);
                    ent.Comp.StatusMessage = $"Added '{tagStr}' to required ID accesses.";
                }

                Dirty(compUid, comp);
                LogSecurityEvent(compUid, $"Updated ID access lock ({comp.RequiredAccessTags.Count} tag(s)).");
                _computer.UpdateComputerDisplayUi(compUid, comp);
                break;

            case SecurityLockAction.ClearAccessTags:
                comp.RequiredAccessTags.Clear();
                Dirty(compUid, comp);
                LogSecurityEvent(compUid, "Cleared ID access lock requirements.");
                ent.Comp.StatusMessage = "ID access lock requirements cleared.";
                _computer.UpdateComputerDisplayUi(compUid, comp);
                break;

            case SecurityLockAction.ToggleRequireAllAccess:
                comp.LockRequiresAllAccess = !comp.LockRequiresAllAccess;
                Dirty(compUid, comp);
                ent.Comp.StatusMessage = comp.LockRequiresAllAccess
                    ? "ID access match mode: ALL selected accesses required."
                    : "ID access match mode: ANY selected access unlocks.";
                _computer.UpdateComputerDisplayUi(compUid, comp);
                break;

            case SecurityLockAction.ToggleRemoteLock:
                if (isJailbroken)
                {
                    ent.Comp.StatusMessage = "Remote control is disabled on this OS.";
                    break;
                }

                comp.RemoteLocked = !comp.RemoteLocked;
                Dirty(compUid, comp);
                LogSecurityEvent(compUid, comp.RemoteLocked ? "Enabled remote access lock." : "Disabled remote access lock.");
                ent.Comp.StatusMessage = comp.RemoteLocked ? "Remote access lock enabled." : "Remote access lock disabled.";
                _computer.UpdateComputerDisplayUi(compUid, comp);
                break;

            case SecurityLockAction.ClearAllLocks:
                comp.SoftwareLocked = false;
                comp.LockPassword = string.Empty;
                comp.SshPassword = string.Empty;
                comp.LockKeyLabel = string.Empty;
                comp.LockKeyId = string.Empty;
                comp.LockKeySecret = string.Empty;
                comp.RequiredAccessTags.Clear();
                comp.LockStatusMessage = string.Empty;
                Dirty(compUid, comp);
                LogSecurityEvent(compUid, "Cleared all software lock credentials.");
                ent.Comp.StatusMessage = "All lock methods cleared.";
                _computer.UpdateComputerDisplayUi(compUid, comp);
                break;
        }

        Dirty(ent);
        UpdateUi(ent.Owner, compUid, ent.Comp);
    }

    private void HandleGenerateKey(EntityUid compUid, OxydComputerComponent comp, SecurityLockProgramComponent progComp, string rawLabel)
    {
        if (!_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) || driveUid == null || fs == null)
        {
            progComp.StatusMessage = "Cannot generate key file: no hard drive installed.";
            return;
        }

        var label = string.IsNullOrWhiteSpace(rawLabel) ? $"{MetaData(compUid).EntityName} Key" : rawLabel.Trim();
        if (label.Length > MaxKeyLabelLength)
            label = label[..MaxKeyLabelLength];

        var slug = SanitizeKeyFileName(label);
        var keyId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var keySecret = Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();
        var deviceGuid = _guid.GetGuid(compUid);

        var keyFile = new PasswordKeyVirtualFile
        {
            Name = slug,
            Extension = "key",
            Directory = "/keys/",
            KeyLabel = label,
            KeyId = keyId,
            KeySecret = keySecret,
            TargetGuid = deviceGuid
        };
        keyFile.Size = keyFile.CalculateRealSize(_proto);

        if (!_fileSystem.TryWriteFile(driveUid.Value, keyFile, fs))
        {
            progComp.StatusMessage = "Failed to write key file (insufficient hard drive storage).";
            return;
        }

        comp.LockKeyLabel = label;
        comp.LockKeyId = keyId;
        comp.LockKeySecret = keySecret;
        Dirty(compUid, comp);

        LogSecurityEvent(compUid, $"Generated & bound key-based password '{label}' [{keyId}] -> {keyFile.FullPath}.");
        progComp.StatusMessage = $"Generated key {keyFile.FullPath} [{keyId}].";
        _computer.UpdateComputerDisplayUi(compUid, comp);
    }

    private void HandleBindKeyFile(
        EntityUid compUid,
        OxydComputerComponent comp,
        SecurityLockProgramComponent progComp,
        string filePath,
        bool fromDisk)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            progComp.StatusMessage = "No key file selected.";
            return;
        }

        PasswordKeyVirtualFile? keyFile = null;
        if (fromDisk)
        {
            if (TryGetInsertedDisk(compUid, out var diskUid, out var diskFs, out _) && diskFs != null)
                _fileSystem.TryGetFile(diskUid, filePath, out keyFile, diskFs);
        }
        else if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) && driveUid != null && fs != null)
        {
            _fileSystem.TryGetFile(driveUid.Value, filePath, out keyFile, fs);
        }

        if (keyFile == null || string.IsNullOrWhiteSpace(keyFile.KeySecret))
        {
            progComp.StatusMessage = $"Key file '{filePath}' could not be read.";
            return;
        }

        comp.LockKeyLabel = string.IsNullOrWhiteSpace(keyFile.KeyLabel) ? keyFile.Name : keyFile.KeyLabel;
        comp.LockKeyId = string.IsNullOrWhiteSpace(keyFile.KeyId) ? "KEY" : keyFile.KeyId;
        comp.LockKeySecret = keyFile.KeySecret;
        Dirty(compUid, comp);

        LogSecurityEvent(compUid, $"Bound existing key '{comp.LockKeyLabel}' [{comp.LockKeyId}] from {filePath}.");
        progComp.StatusMessage = $"Bound key '{comp.LockKeyLabel}' [{comp.LockKeyId}] as key-based password.";
        _computer.UpdateComputerDisplayUi(compUid, comp);
    }

    private void HandleExportKeyToDisk(EntityUid compUid, OxydComputerComponent comp, SecurityLockProgramComponent progComp)
    {
        if (string.IsNullOrEmpty(comp.LockKeySecret))
        {
            progComp.StatusMessage = "No active key-based password configured to export.";
            return;
        }

        if (!TryGetInsertedDisk(compUid, out var diskUid, out var diskFs, out var diskName) || diskFs == null)
        {
            progComp.StatusMessage = "Insert a data disk to export the active .key file.";
            return;
        }

        var label = string.IsNullOrWhiteSpace(comp.LockKeyLabel) ? "System Key" : comp.LockKeyLabel;
        var keyFile = new PasswordKeyVirtualFile
        {
            Name = SanitizeKeyFileName(label),
            Extension = "key",
            Directory = "/keys/",
            KeyLabel = label,
            KeyId = string.IsNullOrWhiteSpace(comp.LockKeyId) ? "KEY" : comp.LockKeyId,
            KeySecret = comp.LockKeySecret,
            TargetGuid = _guid.GetGuid(compUid)
        };
        keyFile.Size = keyFile.CalculateRealSize(_proto);

        if (!_fileSystem.TryWriteFile(diskUid, keyFile, diskFs))
        {
            progComp.StatusMessage = $"Failed to write {keyFile.FullPath} to '{diskName}' (disk full).";
            return;
        }

        LogSecurityEvent(compUid, $"Exported key [{keyFile.KeyId}] to disk '{diskName}' ({keyFile.FullPath}).");
        progComp.StatusMessage = $"Exported {keyFile.FullPath} [{keyFile.KeyId}] to disk '{diskName}'.";
    }

    public static bool HasAnyConfiguredAuthMethod(OxydComputerComponent comp)
    {
        return !string.IsNullOrEmpty(comp.LockPassword) ||
               !string.IsNullOrEmpty(comp.SshPassword) ||
               !string.IsNullOrEmpty(comp.LockKeySecret) ||
               comp.RequiredAccessTags.Count > 0;
    }

    public static string SanitizeKeyFileName(string rawLabel)
    {
        var chars = rawLabel.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();
        var cleaned = new string(chars).Trim('_');
        while (cleaned.Contains("__"))
            cleaned = cleaned.Replace("__", "_");
        return string.IsNullOrWhiteSpace(cleaned) ? "system_key" : cleaned;
    }

    private bool TryGetInsertedDisk(
        EntityUid compUid,
        out EntityUid diskUid,
        out FileSystemComponent? diskFs,
        out string? diskName)
    {
        diskUid = EntityUid.Invalid;
        diskFs = null;
        diskName = null;

        if (_computerQuery.TryComp(compUid, out var comp) &&
            comp.HostMachine is { Valid: true } hostUid &&
            _pdaQuery.TryComp(hostUid, out var pda))
        {
            var pdaDisk = pda.DiskSlot.Item ?? pda.ContainedDisk;
            if (pdaDisk is { Valid: true } pd && Exists(pd) && TryComp<FileSystemComponent>(pd, out diskFs))
            {
                diskUid = pd;
                diskName = MetaData(pd).EntityName;
                return true;
            }
        }

        if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotDiskModule, out var diskSlot) &&
            diskSlot.Item is { Valid: true } slotDisk &&
            Exists(slotDisk) &&
            TryComp<FileSystemComponent>(slotDisk, out diskFs))
        {
            diskUid = slotDisk;
            diskName = MetaData(slotDisk).EntityName;
            return true;
        }

        return false;
    }

    private void LogSecurityEvent(EntityUid compUid, string message)
    {
        if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) && driveUid != null)
            _fileSystem.LogSystemActivity(driveUid.Value, "SEC-LOCK", message, comp: fs);
    }

    public void UpdateUi(EntityUid progUid, EntityUid compUid, SecurityLockProgramComponent progComp)
    {
        if (!_computerQuery.TryComp(compUid, out var comp))
            return;

        var hostName = comp.HostMachine is { Valid: true } hm && Exists(hm)
            ? MetaData(hm).EntityName
            : MetaData(compUid).EntityName;

        var accessOptions = new List<SecurityLockAccessOptionDto>();
        foreach (var proto in _proto.EnumeratePrototypes<AccessLevelPrototype>()
                     .OrderBy(p => p.GetAccessLevelName(), StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(proto.ID))
                continue;

            var isSel = comp.RequiredAccessTags.Contains(proto.ID);
            accessOptions.Add(new SecurityLockAccessOptionDto(proto.ID, proto.GetAccessLevelName(), isSel));
        }

        var keyFiles = new List<SecurityLockKeyFileDto>();
        if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) && driveUid != null && fs != null)
        {
            foreach (var kf in _fileSystem.GetFilesByType<PasswordKeyVirtualFile>(driveUid.Value, fs)
                         .OrderBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase))
            {
                keyFiles.Add(new SecurityLockKeyFileDto(
                    kf.FullPath,
                    string.IsNullOrWhiteSpace(kf.KeyLabel) ? kf.Name : kf.KeyLabel,
                    kf.KeyId,
                    fromDisk: false,
                    isActiveLockKey: !string.IsNullOrEmpty(comp.LockKeySecret) &&
                                     string.Equals(kf.KeySecret, comp.LockKeySecret, StringComparison.Ordinal)));
            }
        }

        var hasDisk = TryGetInsertedDisk(compUid, out var diskUid, out var diskFs, out var diskName);
        if (hasDisk && diskFs != null)
        {
            foreach (var kf in _fileSystem.GetFilesByType<PasswordKeyVirtualFile>(diskUid, diskFs)
                         .OrderBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase))
            {
                keyFiles.Add(new SecurityLockKeyFileDto(
                    kf.FullPath,
                    string.IsNullOrWhiteSpace(kf.KeyLabel) ? kf.Name : kf.KeyLabel,
                    kf.KeyId,
                    fromDisk: true,
                    isActiveLockKey: !string.IsNullOrEmpty(comp.LockKeySecret) &&
                                     string.Equals(kf.KeySecret, comp.LockKeySecret, StringComparison.Ordinal)));
            }
        }

        var hasHostLock = comp.HostMachine is { Valid: true } lockHost && HasComp<LockComponent>(lockHost);
        var isJailbroken = _computer.IsJailbrokenOs(compUid, comp);
        var requiresSysAdmin = !isJailbroken;
        var state = new SecurityLockUiState(
            _guid.GetGuid(compUid),
            hostName,
            comp.SoftwareLocked,
            !string.IsNullOrEmpty(comp.LockPassword) || !string.IsNullOrEmpty(comp.SshPassword),
            !string.IsNullOrEmpty(comp.LockKeySecret),
            comp.LockKeyLabel,
            comp.LockKeyId,
            comp.LockRequiresAllAccess,
            accessOptions,
            keyFiles,
            hasDisk,
            diskName,
            _cardReaders.HasReaders(compUid) || (comp.HostMachine is { Valid: true } pdaHost && _pdaQuery.HasComp(pdaHost)),
            hasHostLock,
            isJailbroken,
            requiresSysAdmin,
            isJailbroken || progComp.IsSystemAdminAuthorized,
            progComp.AuthorizedByLabel,
            comp.RemoteLocked,
            progComp.StatusMessage);

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, state);
    }
}
