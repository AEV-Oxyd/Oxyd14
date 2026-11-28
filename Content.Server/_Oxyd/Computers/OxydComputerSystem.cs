using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._Oxyd.Computers.Network;
using Content.Server._Oxyd.Computers.Programs;
using Content.Server._Oxyd.Computers.ScreenSharing;
using Content.Server.Construction.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Packets;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared._Oxyd.Computers.ScreenSharing;
using Content.Shared._Oxyd.Computers.Servers;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Computer;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Components;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.DeviceLinking.Systems;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.DeviceNetwork.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Lock;
using Content.Shared.PDA;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.UserInterface;
using Content.Shared.Wires;
using Robust.Server.GameStates;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers;

/// <summary>
/// Event raised locally on a computer entity whenever its installed hardware parts or capabilities change.
/// </summary>
[ByRefEvent]
public readonly record struct ComputerHardwareChangedEvent(EntityUid ComputerUid);

/// <summary>
/// Event raised locally on a computer and its running programs whenever the OS-level authenticated user session changes.
/// </summary>
[ByRefEvent]
public readonly record struct ComputerOsAuthChangedEvent(EntityUid ComputerUid);

/// <summary>
/// Core server system managing encapsulated NTOS computers, physical hardware slot validation,
/// boot-first-.nte execution, entity-based Program BUI lifecycle, and full DeviceLink port mirroring.
/// </summary>
public sealed partial class OxydComputerSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private SharedDeviceGuidSystem _guid = default!;
    [Dependency] private DeviceLinkSystem _deviceLink = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private PvsOverrideSystem _pvsOverride = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ISharedPlayerManager _player = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private NetworkBandwidthSystem _bandwidth = default!;
    [Dependency] private DeviceNetworkSystem _deviceNet = default!;
    [Dependency] private OsProgramSystem _osPrograms = default!;
    [Dependency] private PdaOsSystem _pdaOs = default!;
    [Dependency] private NetworkCoreOsSystem _netCoreOs = default!;
    [Dependency] private ServerRemoteScreenSystem _remoteScreen = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private LockSystem _lock = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    [Dependency] private EntityQuery<OxydComputerComponent> _computerQuery = default!;
    [Dependency] private EntityQuery<EmbeddedComputerHostComponent> _hostQuery = default!;
    [Dependency] private EntityQuery<ComputerStatsComponent> _statsQuery = default!;
    [Dependency] private EntityQuery<ProgramComponent> _programQuery = default!;
    [Dependency] private EntityQuery<DeviceLinkSourceComponent> _sourceQuery = default!;
    [Dependency] private EntityQuery<DeviceLinkSinkComponent> _sinkQuery = default!;

    private static readonly ProtoId<SourcePortPrototype> PortComputerCommandSender = "ComputerCommandSender";
    private static readonly ProtoId<SinkPortPrototype> PortComputerStatusReceiver = "ComputerStatusReceiver";
    private static readonly ProtoId<SourcePortPrototype> PortMachineStatusSender = "MachineStatusSender";
    private static readonly ProtoId<SinkPortPrototype> PortMachineCommandReceiver = "MachineCommandReceiver";
    private static readonly SoundPathSpecifier OsSwipeSound = new("/Audio/Machines/id_swipe.ogg");

    [SubscribeLocalEvent]
    private void OnHostMapInit(Entity<EmbeddedComputerHostComponent> ent, ref MapInitEvent args)
    {
        EnsureComputerBoundToHost(ent, out _);
    }

    [SubscribeLocalEvent]
    private void OnHostShutdown(Entity<EmbeddedComputerHostComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.ComputerEntity is { } compUid && Exists(compUid))
        {
            var hostCoords = Transform(ent.Owner).Coordinates;
            foreach (var part in GetInstalledHardwareEntities(compUid))
            {
                if (TryComp<ComputerIdCardSlotModuleComponent>(part, out var reader) &&
                    reader.Mode == ComputerCardReaderMode.Slot &&
                    _itemSlots.TryGetSlot(part, reader.CardSlotId, out var slot) &&
                    slot.Item != null &&
                    _itemSlots.TryEject(part, slot, null, out var ejected, excludeUserAudio: true) &&
                    Exists(ejected.Value))
                {
                    Transform(ejected.Value).Coordinates = hostCoords;
                }
            }

            QueueDel(compUid);
        }
    }

    [SubscribeLocalEvent]
    private void OnComputerMapInit(Entity<OxydComputerComponent> ent, ref MapInitEvent args)
    {
        _container.EnsureContainer<Container>(ent.Owner, ModularComputerComponent.ContainerHardware);
        _container.EnsureContainer<Container>(ent.Owner, OxydComputerComponent.ContainerPrograms);
        _guid.GetGuid(ent.Owner);

        EnsureHardwareSlotsAndDefaults(ent.Owner, ent.Comp);
        RecalculateHardwareStats(ent.Owner, ent.Comp);
        RefreshComputerBootState(ent.Owner, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnComputerShutdown(Entity<OxydComputerComponent> ent, ref ComponentShutdown args)
    {
        ShutDownComputer(ent.Owner, ent.Comp, updateUi: false);
    }

    [SubscribeLocalEvent]
    private void OnHostPowerChanged(Entity<EmbeddedComputerHostComponent> ent, ref PowerChangedEvent args)
    {
        if (ent.Comp.ComputerEntity is not { } compUid || !_computerQuery.TryComp(compUid, out var comp))
            return;

        ApplyComputerPowerChange(compUid, comp, args.Powered);
    }

    [SubscribeLocalEvent]
    private void OnComputerPowerChanged(Entity<OxydComputerComponent> ent, ref PowerChangedEvent args)
    {
        ApplyComputerPowerChange(ent.Owner, ent.Comp, args.Powered);
    }

    private bool SyncHostPowerState(EntityUid compUid, OxydComputerComponent comp)
    {
        if (comp.HostMachine is { Valid: true } hostUid &&
            TryComp<Content.Server.Power.Components.ApcPowerReceiverComponent>(hostUid, out var apcRecv))
        {
            var powered = apcRecv.Powered || (!apcRecv.NeedsPower && !apcRecv.PowerDisabled);
            if (comp.IsPowered != powered)
            {
                comp.IsPowered = powered;
                Dirty(compUid, comp);
                return true;
            }
        }

        return false;
    }

    public bool IsComputerUnpowered(EntityUid compUid, OxydComputerComponent? comp = null, ComputerStatsComponent? stats = null)
    {
        if (!_computerQuery.Resolve(compUid, ref comp, false))
            return false;

        SyncHostPowerState(compUid, comp);
        stats ??= _statsQuery.CompOrNull(compUid);
        return comp.RequirePowerToBoot && !comp.IsPowered && (stats == null || stats.BatteryCharge <= 0f);
    }

    private void ApplyComputerPowerChange(EntityUid compUid, OxydComputerComponent comp, bool powered)
    {
        comp.IsPowered = powered;
        Dirty(compUid, comp);
        RecalculateHardwareStats(compUid, comp);

        if (!powered && IsComputerUnpowered(compUid, comp))
        {
            HandlePowerLoss(compUid, comp);
            return;
        }

        RefreshComputerBootState(compUid, comp);
    }

    private void HandlePowerLoss(EntityUid compUid, OxydComputerComponent comp)
    {
        var viewers = GetAllComputerViewers(compUid, comp);

        // Detach any remote screen viewers before shutting down and closing all computer UIs.
        _remoteScreen.DetachViewer(EntityUid.Invalid, targetComputerUid: compUid);

        if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) && driveUid != null)
            _fileSystem.LogSystemActivity(driveUid.Value, "POWER", "Power lost — immediate system shutdown.", comp: fs);

        ShutDownComputer(compUid, comp, updateUi: false, manualShutdown: false);

        comp.BootState = ComputerBootState.NoPower;
        comp.IsBooted = false;
        comp.BootError = "POST ERROR 0x02: Insufficient power to boot computer.";
        Dirty(compUid, comp);

        if (_statsQuery.TryComp(compUid, out var stats))
        {
            stats.IsBooted = false;
            stats.BootError = comp.BootError;
            Dirty(compUid, stats);
        }

        _ui.CloseUis(compUid);
        foreach (var viewer in viewers)
            RevokeViewerPvsOverrides(compUid, viewer, comp);
    }

    [SubscribeLocalEvent]
    private void OnHostActivated(Entity<EmbeddedComputerHostComponent> ent, ref EmbeddedComputerHostActivatedEvent args)
    {
        // Devices with a LockComponent (e.g., APCs and Air Alarms) always open their UI regardless of access,
        // displaying their locked status and gating all actions until unlocked by swiping an authorized ID card.
        if (!HasComp<LockComponent>(ent.Owner) &&
            TryComp<AccessReaderComponent>(ent.Owner, out var access) &&
            !_access.IsAllowed(args.User, ent.Owner, access))
        {
            _popup.PopupEntity(Loc.GetString("apc-component-insufficient-access"), ent.Owner, args.User);
            return;
        }

        if (!EnsureComputerBoundToHost(ent, out var compUid))
            return;

        if (!_computerQuery.TryComp(compUid, out var comp))
            return;

        if (IsComputerUnpowered(compUid, comp))
        {
            _ui.CloseUis(compUid);
            _popup.PopupEntity("No power.", ent.Owner, args.User);
            return;
        }

        var key = GetPrimaryUiKey(compUid, comp);
        if (_ui.IsUiOpen(compUid, key, args.User))
        {
            if (_remoteScreen.IsRemoteScreenAuthorized(args.User, compUid))
            {
                // Keep the computer & child program BUIs open for active remote viewers and bring
                // any locally closed (stashed) window back onto the player's screen.
                UpdateComputerDisplayUi(compUid, comp);
                _remoteScreen.HandleLocalOpenWhileViewing(compUid, key, args.User);
                return;
            }

            CloseComputerForActor(compUid, args.User, key);
            return;
        }

        OpenComputerForActor(compUid, args.User, comp);
    }

    [SubscribeLocalEvent]
    private void OnRetryBootMessage(Entity<OxydComputerComponent> ent, ref ComputerRetryBootMessage args)
    {
        ent.Comp.ManuallyShutDown = false;
        RecalculateHardwareStats(ent.Owner, ent.Comp);
        RefreshComputerBootState(ent.Owner, ent.Comp);
        OpenRunningProgramUisForCurrentViewers(ent.Owner, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnUnlockWithPassword(Entity<OxydComputerComponent> ent, ref ComputerUnlockWithPasswordMessage args)
    {
        if (!ent.Comp.SoftwareLocked)
            return;

        var candidate = (args.Secret ?? string.Empty).Trim();
        var matchesPassword =
            (!string.IsNullOrEmpty(ent.Comp.LockPassword) && string.Equals(ent.Comp.LockPassword, candidate, StringComparison.Ordinal)) ||
            (!string.IsNullOrEmpty(ent.Comp.SshPassword) && string.Equals(ent.Comp.SshPassword, candidate, StringComparison.Ordinal));
        var matchesKey =
            !string.IsNullOrEmpty(ent.Comp.LockKeySecret) &&
            string.Equals(ent.Comp.LockKeySecret, candidate, StringComparison.Ordinal);

        if (matchesPassword || matchesKey)
        {
            UnlockSoftwareLock(ent.Owner, ent.Comp, matchesKey ? "Unlocked via key secret." : "Unlocked via password.");
            return;
        }

        ent.Comp.LockStatusMessage = "Authentication failed: invalid password or key.";
        Dirty(ent);
        UpdateComputerDisplayUi(ent.Owner, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnUnlockWithKeyFile(Entity<OxydComputerComponent> ent, ref ComputerUnlockWithKeyFileMessage args)
    {
        if (!ent.Comp.SoftwareLocked)
            return;

        if (!string.IsNullOrEmpty(ent.Comp.LockKeySecret) &&
            TryFindMatchingKeyFile(ent.Owner, ent.Comp.LockKeySecret, includeLocalHdd: true, out var matchedPath))
        {
            UnlockSoftwareLock(ent.Owner, ent.Comp, $"Unlocked via key file {matchedPath}.");
            return;
        }

        if (ent.Comp.RequiredAccessTags.Count > 0 &&
            TryFindMatchingAccFileForLock(ent.Owner, ent.Comp, out var accPath))
        {
            UnlockSoftwareLock(ent.Owner, ent.Comp, $"Unlocked via access codes {accPath}.");
            return;
        }

        ent.Comp.LockStatusMessage = "No matching .key or authorized .acc file found on inserted disk / drive.";
        Dirty(ent);
        UpdateComputerDisplayUi(ent.Owner, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnUnlockWithId(Entity<OxydComputerComponent> ent, ref ComputerUnlockWithIdMessage args)
    {
        if (!ent.Comp.SoftwareLocked)
            return;

        if (ent.Comp.RequiredAccessTags.Count == 0)
        {
            ent.Comp.LockStatusMessage = "This computer is not configured for ID access unlock.";
            Dirty(ent);
            UpdateComputerDisplayUi(ent.Owner, ent.Comp);
            return;
        }

        var cards = GetInsertedIdCards(ent.Owner);
        if (cards.Count == 0)
        {
            ent.Comp.LockStatusMessage = "No ID card inserted in computer ID slot (or swipe an ID card on a card scanner).";
            Dirty(ent);
            UpdateComputerDisplayUi(ent.Owner, ent.Comp);
            return;
        }

        foreach (var cardUid in cards)
        {
            if (TryUnlockWithCard(ent.Owner, cardUid, out _))
                return;
        }

        ent.Comp.LockStatusMessage = "Access denied: inserted ID card lacks required clearance.";
        Dirty(ent);
        UpdateComputerDisplayUi(ent.Owner, ent.Comp);
    }

    public bool TryUnlockWithCard(EntityUid compUid, EntityUid cardUid, out string message)
    {
        message = string.Empty;
        if (!_computerQuery.TryComp(compUid, out var comp) || !comp.SoftwareLocked)
            return false;

        if (comp.RequiredAccessTags.Count == 0)
        {
            message = "This computer does not accept ID card unlock.";
            return false;
        }

        var cardTags = _access.FindAccessTags(cardUid);
        if (SatisfiesRequiredAccess(comp, cardTags))
        {
            var cardName = MetaData(cardUid).EntityName;
            UnlockSoftwareLock(compUid, comp, $"Unlocked via ID card ({cardName}).");
            message = $"Computer unlocked ({cardName}).";
            return true;
        }

        comp.LockStatusMessage = "Access denied: ID card lacks required access clearance.";
        Dirty(compUid, comp);
        UpdateComputerDisplayUi(compUid, comp);
        message = "Access denied: ID card lacks required clearance.";
        return false;
    }

    public bool TryFindMatchingKeyFile(EntityUid compUid, string expectedSecret, bool includeLocalHdd, out string matchedPath)
    {
        matchedPath = string.Empty;
        if (string.IsNullOrEmpty(expectedSecret))
            return false;

        // Check inserted removable disk (PDA disk slot or modular computer disk bay) first.
        if (_computerQuery.TryComp(compUid, out var comp) &&
            comp.HostMachine is { Valid: true } hostUid &&
            TryComp<PdaComponent>(hostUid, out var pda))
        {
            var pdaDisk = pda.DiskSlot.Item ?? pda.ContainedDisk;
            if (pdaDisk is { Valid: true } pd && Exists(pd) && TryComp<FileSystemComponent>(pd, out var pdaDiskFs))
            {
                foreach (var kf in _fileSystem.GetFilesByType<PasswordKeyVirtualFile>(pd, pdaDiskFs))
                {
                    if (string.Equals(kf.KeySecret, expectedSecret, StringComparison.Ordinal))
                    {
                        matchedPath = kf.FullPath;
                        return true;
                    }
                }
            }
        }

        if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotDiskModule, out var diskSlot) &&
            diskSlot.Item is { Valid: true } diskUid &&
            Exists(diskUid) &&
            TryComp<FileSystemComponent>(diskUid, out var diskFs))
        {
            foreach (var kf in _fileSystem.GetFilesByType<PasswordKeyVirtualFile>(diskUid, diskFs))
            {
                if (string.Equals(kf.KeySecret, expectedSecret, StringComparison.Ordinal))
                {
                    matchedPath = kf.FullPath;
                    return true;
                }
            }
        }

        if (includeLocalHdd &&
            _fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) &&
            driveUid != null && fs != null)
        {
            foreach (var kf in _fileSystem.GetFilesByType<PasswordKeyVirtualFile>(driveUid.Value, fs))
            {
                if (string.Equals(kf.KeySecret, expectedSecret, StringComparison.Ordinal))
                {
                    matchedPath = kf.FullPath;
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryFindMatchingAccFileForLock(EntityUid compUid, OxydComputerComponent comp, out string matchedPath)
    {
        matchedPath = string.Empty;
        var drivesToCheck = new List<(EntityUid Uid, FileSystemComponent Fs)>();

        if (comp.HostMachine is { Valid: true } hostUid && TryComp<PdaComponent>(hostUid, out var pda))
        {
            var pdaDisk = pda.DiskSlot.Item ?? pda.ContainedDisk;
            if (pdaDisk is { Valid: true } pd && Exists(pd) && TryComp<FileSystemComponent>(pd, out var pdaDiskFs))
                drivesToCheck.Add((pd, pdaDiskFs));
        }

        if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotDiskModule, out var diskSlot) &&
            diskSlot.Item is { Valid: true } diskUid &&
            Exists(diskUid) &&
            TryComp<FileSystemComponent>(diskUid, out var diskFs))
        {
            drivesToCheck.Add((diskUid, diskFs));
        }

        if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) && driveUid != null && fs != null)
            drivesToCheck.Add((driveUid.Value, fs));

        foreach (var (dUid, dFs) in drivesToCheck)
        {
            foreach (var acc in _fileSystem.GetFilesByType<AccessCodesVirtualFile>(dUid, dFs))
            {
                if (acc.IsEncrypted)
                    continue;

                if (SatisfiesRequiredAccess(comp, acc.AccessTags))
                {
                    matchedPath = acc.FullPath;
                    return true;
                }
            }
        }

        return false;
    }

    private static readonly ProtoId<Content.Shared.Access.AccessLevelPrototype> SystemAdminAccess = "SystemAdmin";

    /// <summary>
    /// Returns true if the computer's active BootProgram is a jailbroken OS kernel.
    /// </summary>
    public bool IsJailbrokenOs(EntityUid compUid, OxydComputerComponent? comp = null)
    {
        if (!_computerQuery.Resolve(compUid, ref comp, false))
            return false;

        return comp.BootProgram is { Valid: true } bootUid &&
               _programQuery.TryComp(bootUid, out var bootProg) &&
               bootProg.IsJailbrokenOs;
    }

    /// <summary>
    /// Returns true if remote control (SSH / remote screen / remote commands) is permitted on this computer.
    /// Jailbroken OS installations always disable all remote control.
    /// </summary>
    public bool IsRemoteControlAllowed(EntityUid compUid, OxydComputerComponent? comp = null)
    {
        if (!_computerQuery.Resolve(compUid, ref comp, false))
            return false;

        if (!comp.AllowRemoteControl || comp.RemoteControlWireCut || comp.PowerWireCut)
            return false;

        if (IsJailbrokenOs(compUid, comp))
            return false;

        return true;
    }

    /// <summary>
    /// Returns true if the given program prototype ID is marked as an Operating System kernel.
    /// </summary>
    public bool IsOperatingSystemExecutable(string programId)
    {
        if (string.IsNullOrWhiteSpace(programId))
            return false;

        return _proto.TryIndex<EntityPrototype>(programId, out var proto) &&
               proto.TryComp<ProgramComponent>(out var defComp, EntityManager.ComponentFactory) &&
               defComp.IsOperatingSystem;
    }

    /// <summary>
    /// <summary>
    /// Collects all available access tags from the computer's physically inserted ID cards,
    /// active OS-level authenticated session, unencrypted /keys/*.acc files (HDD and inserted disk),
    /// and optionally an explicitly swiped <see cref="IdCardComponent"/> entity.
    /// Never reads worn/unauthenticated ID cards from an actor mob.
    /// </summary>
    public HashSet<ProtoId<Content.Shared.Access.AccessLevelPrototype>> CollectAvailableAccessTags(
        EntityUid compUid,
        EntityUid? actor = null,
        OxydComputerComponent? comp = null)
    {
        var tags = new HashSet<ProtoId<Content.Shared.Access.AccessLevelPrototype>>();
        if (!_computerQuery.Resolve(compUid, ref comp, false))
            return tags;

        foreach (var cardUid in GetInsertedIdCards(compUid))
        {
            foreach (var t in _access.FindAccessTags(cardUid))
                tags.Add(t);
        }

        foreach (var t in comp.OsAuthAccessTags)
            tags.Add(t);

        if (actor is { Valid: true } cardEntity && Exists(cardEntity) && HasComp<IdCardComponent>(cardEntity))
        {
            foreach (var t in _access.FindAccessTags(cardEntity))
                tags.Add(t);
        }

        var drivesToCheck = new List<(EntityUid Uid, FileSystemComponent Fs)>();
        if (comp.HostMachine is { Valid: true } hostUid && TryComp<PdaComponent>(hostUid, out var pda))
        {
            var pdaDisk = pda.DiskSlot.Item ?? pda.ContainedDisk;
            if (pdaDisk is { Valid: true } pd && Exists(pd) && TryComp<FileSystemComponent>(pd, out var pdaDiskFs))
                drivesToCheck.Add((pd, pdaDiskFs));
        }

        if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotDiskModule, out var diskSlot) &&
            diskSlot.Item is { Valid: true } diskUid &&
            Exists(diskUid) &&
            TryComp<FileSystemComponent>(diskUid, out var diskFs))
        {
            drivesToCheck.Add((diskUid, diskFs));
        }

        if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) && driveUid != null && fs != null)
            drivesToCheck.Add((driveUid.Value, fs));

        foreach (var (dUid, dFs) in drivesToCheck)
        {
            foreach (var acc in _fileSystem.GetFilesByType<AccessCodesVirtualFile>(dUid, dFs))
            {
                if (acc.IsEncrypted)
                    continue;

                foreach (var t in acc.AccessTags)
                    tags.Add(t);
            }
        }

        return tags;
    }

    /// <summary>
    /// Checks whether the computer or acting user possesses ANY tag in <paramref name="requiredTags"/>
    /// (also returning a descriptive credential source label).
    /// </summary>
    public bool HasAnyAccessTag(
        EntityUid compUid,
        EntityUid? actor,
        IEnumerable<ProtoId<Content.Shared.Access.AccessLevelPrototype>> requiredTags,
        out string matchedSourceLabel,
        bool includeSystemAdmin = false,
        OxydComputerComponent? comp = null)
    {
        matchedSourceLabel = string.Empty;
        if (!_computerQuery.Resolve(compUid, ref comp, false))
            return false;

        var reqSet = new HashSet<ProtoId<Content.Shared.Access.AccessLevelPrototype>>(requiredTags);
        if (includeSystemAdmin)
            reqSet.Add(SystemAdminAccess);
        if (reqSet.Count == 0)
            return false;

        foreach (var cardUid in GetInsertedIdCards(compUid))
        {
            var cardTags = _access.FindAccessTags(cardUid);
            if (reqSet.Any(cardTags.Contains))
            {
                matchedSourceLabel = TryComp<IdCardComponent>(cardUid, out var idCard) && !string.IsNullOrWhiteSpace(idCard.FullName)
                    ? idCard.FullName
                    : MetaData(cardUid).EntityName;
                return true;
            }
        }

        if (comp.OsAuthAccessTags.Count > 0 && reqSet.Any(comp.OsAuthAccessTags.Contains))
        {
            matchedSourceLabel = !string.IsNullOrWhiteSpace(comp.OsAuthLabel)
                ? $"OS Session ({comp.OsAuthLabel})"
                : "OS Session";
            return true;
        }

        if (actor is { Valid: true } cardEntity && Exists(cardEntity) && HasComp<IdCardComponent>(cardEntity))
        {
            var cardTags = _access.FindAccessTags(cardEntity);
            if (reqSet.Any(cardTags.Contains))
            {
                matchedSourceLabel = TryComp<IdCardComponent>(cardEntity, out var idCard) && !string.IsNullOrWhiteSpace(idCard.FullName)
                    ? idCard.FullName
                    : MetaData(cardEntity).EntityName;
                return true;
            }
        }

        var drivesToCheck = new List<(EntityUid Uid, FileSystemComponent Fs)>();
        if (comp.HostMachine is { Valid: true } hostUid && TryComp<PdaComponent>(hostUid, out var pda))
        {
            var pdaDisk = pda.DiskSlot.Item ?? pda.ContainedDisk;
            if (pdaDisk is { Valid: true } pd && Exists(pd) && TryComp<FileSystemComponent>(pd, out var pdaDiskFs))
                drivesToCheck.Add((pd, pdaDiskFs));
        }

        if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotDiskModule, out var diskSlot) &&
            diskSlot.Item is { Valid: true } diskUid &&
            Exists(diskUid) &&
            TryComp<FileSystemComponent>(diskUid, out var diskFs))
        {
            drivesToCheck.Add((diskUid, diskFs));
        }

        if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) && driveUid != null && fs != null)
            drivesToCheck.Add((driveUid.Value, fs));

        foreach (var (dUid, dFs) in drivesToCheck)
        {
            foreach (var acc in _fileSystem.GetFilesByType<AccessCodesVirtualFile>(dUid, dFs))
            {
                if (acc.IsEncrypted)
                    continue;

                if (reqSet.Any(acc.AccessTags.Contains))
                {
                    matchedSourceLabel = string.IsNullOrWhiteSpace(acc.OwnerName) ? acc.FullPath : acc.OwnerName;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Writes /sys/boot.cfg to select an installed .nte executable as the computer's boot OS
    /// and optionally triggers a clean reboot.
    /// </summary>
    public bool TrySetBootExecutable(
        EntityUid compUid,
        string executablePathOrName,
        out string message,
        bool rebootNow = true,
        OxydComputerComponent? comp = null)
    {
        message = string.Empty;
        if (!_computerQuery.Resolve(compUid, ref comp, false))
        {
            message = "Computer unavailable.";
            return false;
        }

        if (!_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) || driveUid == null || fs == null)
        {
            message = "No hard drive available.";
            return false;
        }

        if (!TryResolveExecutable(fs, executablePathOrName, out var resolvedPath, out var execFile))
        {
            message = $"Executable not found: '{executablePathOrName}'.";
            return false;
        }

        var cfgFile = new TextVirtualFile
        {
            Name = "boot",
            Extension = "cfg",
            Directory = "/sys/",
            TextContent = resolvedPath,
            Size = Math.Max(32, resolvedPath.Length)
        };

        if (!_fileSystem.TryWriteFile(driveUid.Value, cfgFile, fs))
        {
            message = "Failed to write /sys/boot.cfg (drive full or read-only).";
            return false;
        }

        _fileSystem.LogSystemActivity(driveUid.Value, "BOOT-CFG", $"Set boot executable to {resolvedPath} ({execFile.Title})");
        message = $"Boot executable set to '{resolvedPath}'.";

        if (rebootNow)
        {
            comp.ManuallyShutDown = false;
            RefreshComputerBootState(compUid, comp);
        }

        return true;
    }

    /// <summary>
    /// Returns true if the computer has any software lock credential configured
    /// (password, key-based password, or required ID access set).
    /// </summary>
    public static bool HasSoftwareLockConfigured(OxydComputerComponent comp)
    {
        return !string.IsNullOrEmpty(comp.LockPassword) ||
               !string.IsNullOrEmpty(comp.SshPassword) ||
               !string.IsNullOrEmpty(comp.LockKeySecret) ||
               comp.RequiredAccessTags.Count > 0;
    }

    /// <summary>
    /// Authenticates an OS-level user session on this computer from a swiped ID card.
    /// Read/control apps (Cameras, APC, Atmos, Station Records, Comms, Downloader) inherit these access tags
    /// without requiring the card to remain physically inserted in a slot.
    /// </summary>
    public bool AuthenticateOsSession(
        EntityUid compUid,
        EntityUid cardUid,
        EntityUid? user = null,
        OxydComputerComponent? comp = null)
    {
        if (!_computerQuery.Resolve(compUid, ref comp, false) || !Exists(cardUid))
            return false;

        var tags = _access.FindAccessTags(cardUid);
        comp.OsAuthAccessTags.Clear();
        foreach (var t in tags)
            comp.OsAuthAccessTags.Add(t);

        var fullName = string.Empty;
        var jobTitle = string.Empty;
        if (TryComp<IdCardComponent>(cardUid, out var idCard))
        {
            fullName = idCard.FullName ?? string.Empty;
            jobTitle = idCard.LocalizedJobTitle ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(fullName))
            fullName = MetaData(cardUid).EntityName;

        comp.OsAuthUserName = fullName;
        comp.OsAuthJobTitle = jobTitle;
        comp.OsAuthLabel = !string.IsNullOrWhiteSpace(jobTitle) ? $"{fullName} ({jobTitle})" : fullName;
        Dirty(compUid, comp);

        RaiseOsAuthChanged(compUid, comp);
        return true;
    }

    /// <summary>
    /// Authenticates the OS-level session from the actor's held ID/PDA, an inserted slot ID card,
    /// or the actor's worn ID card (when physically in range and the device has an ID reader/slot or is a PDA).
    /// </summary>
    public bool TryAuthenticateOsSessionFromActor(
        EntityUid compUid,
        EntityUid actor,
        out string message,
        OxydComputerComponent? comp = null)
    {
        message = string.Empty;
        if (!_computerQuery.Resolve(compUid, ref comp, false) || !comp.IsBooted)
        {
            message = "Computer is not running.";
            return false;
        }

        var hostUid = comp.HostMachine is { Valid: true } hm && Exists(hm) ? hm : compUid;
        if (comp.Template == ComputerTemplate.Pda)
        {
            message = "PDAs use their inserted ID card.";
            return false;
        }

        var hasReader = _statsQuery.TryComp(compUid, out var stats) && stats.HasIdSlotModule;
        if (!hasReader)
        {
            message = "No ID card reader installed.";
            _popup.PopupEntity(message, hostUid, actor);
            return false;
        }

        var inPhysicalRange = Exists(actor) &&
                              (comp.Template == ComputerTemplate.Pda || _interaction.InRangeUnobstructed(actor, hostUid));

        EntityUid? cardUid = null;

        // 1) Active held item (loose ID card or PDA containing an ID card) when in physical range.
        if (inPhysicalRange && _hands.TryGetActiveItem(actor, out var held))
        {
            if (HasComp<IdCardComponent>(held.Value))
            {
                cardUid = held.Value;
            }
            else if (TryComp<PdaComponent>(held.Value, out var heldPda) &&
                     heldPda.ContainedId is { Valid: true } pdaCard &&
                     Exists(pdaCard))
            {
                cardUid = pdaCard;
            }
        }

        // 2) Card currently inserted in this device's physical ID slot or PDA ID slot.
        if (cardUid == null && TryGetInsertedIdCard(compUid, out var insertedCard))
            cardUid = insertedCard;

        // 3) Actor's worn/carried ID card when physically in range.
        if (cardUid == null && inPhysicalRange && _idCard.TryFindIdCard(actor, out var foundCard))
            cardUid = foundCard.Owner;

        if (cardUid == null || !Exists(cardUid.Value))
        {
            message = inPhysicalRange
                ? "Hold, wear, or insert an ID card to authenticate."
                : "No ID card inserted in remote device.";
            _popup.PopupEntity(message, hostUid, actor);
            return false;
        }

        if (!AuthenticateOsSession(compUid, cardUid.Value, actor, comp))
        {
            message = "Unable to read ID card.";
            return false;
        }

        _audio.PlayPvs(OsSwipeSound, hostUid);
        message = $"Authenticated: {comp.OsAuthLabel}";
        _popup.PopupEntity(message, hostUid, actor);
        return true;
    }

    /// <summary>
    /// Clears any active OS-level user authentication session on this computer.
    /// </summary>
    public void ClearOsSession(EntityUid compUid, OxydComputerComponent? comp = null)
    {
        if (!_computerQuery.Resolve(compUid, ref comp, false))
            return;

        if (string.IsNullOrEmpty(comp.OsAuthLabel) && comp.OsAuthAccessTags.Count == 0)
            return;

        comp.OsAuthUserName = string.Empty;
        comp.OsAuthJobTitle = string.Empty;
        comp.OsAuthLabel = string.Empty;
        comp.OsAuthAccessTags.Clear();
        Dirty(compUid, comp);

        RaiseOsAuthChanged(compUid, comp);
    }

    private void RaiseOsAuthChanged(EntityUid compUid, OxydComputerComponent comp)
    {
        var ev = new ComputerOsAuthChangedEvent(compUid);
        RaiseLocalEvent(compUid, ref ev);

        var notified = new HashSet<EntityUid>();
        if (comp.BootProgram is { Valid: true } bootUid && Exists(bootUid) && notified.Add(bootUid))
            RaiseLocalEvent(bootUid, ref ev);

        foreach (var progUid in comp.RunningPrograms.ToList())
        {
            if (Exists(progUid) && notified.Add(progUid))
                RaiseLocalEvent(progUid, ref ev);
        }
    }

    /// <summary>
    /// Engages the computer's software lock and advances <see cref="OxydComputerComponent.LockEpoch"/>
    /// so any previously opened remote SSH sessions must re-authenticate.
    /// </summary>
    public void EngageSoftwareLock(EntityUid compUid, OxydComputerComponent comp)
    {
        comp.SoftwareLocked = true;
        comp.LockStatusMessage = string.Empty;
        comp.LockEpoch++;
        Dirty(compUid, comp);

        ClearOsSession(compUid, comp);

        foreach (var progUid in comp.RunningPrograms.ToList())
        {
            if (progUid == comp.BootProgram || !Exists(progUid))
                continue;

            var deactEv = new ProgramDeactivatedEvent(compUid, progUid);
            RaiseLocalEvent(progUid, ref deactEv);
        }
    }

    public static bool SatisfiesRequiredAccess(OxydComputerComponent comp, ICollection<ProtoId<Content.Shared.Access.AccessLevelPrototype>> heldTags)
    {
        if (comp.RequiredAccessTags.Count == 0)
            return false;

        if (comp.LockRequiresAllAccess)
            return comp.RequiredAccessTags.All(req => heldTags.Contains(req));

        return comp.RequiredAccessTags.Any(req => heldTags.Contains(req));
    }

    private void UnlockSoftwareLock(EntityUid compUid, OxydComputerComponent comp, string logReason)
    {
        comp.SoftwareLocked = false;
        comp.LockStatusMessage = string.Empty;
        Dirty(compUid, comp);

        if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) && driveUid != null)
            _fileSystem.LogSystemActivity(driveUid.Value, "SEC-LOCK", logReason, comp: fs);

        UpdateComputerDisplayUi(compUid, comp);
    }

    [SubscribeLocalEvent]
    private void OnComputerUiClosed(Entity<OxydComputerComponent> ent, ref BoundUIClosedEvent args)
    {
        var key = GetPrimaryUiKey(ent.Owner, ent.Comp);
        if (!key.Equals(args.UiKey))
            return;

        if (_remoteScreen.IsRemoteScreenAuthorized(args.Actor, ent.Owner))
        {
            // Keep child program BUIs and remote stream alive if user closes a popped-out target window locally;
            // only genuinely tear down when ServerRemoteScreenSystem initiates a detach.
            if (!_remoteScreen.IsDetachingViewer(args.Actor, ent.Owner))
                _remoteScreen.HandleTargetUiClosedWhileViewing(ent.Owner, args.Actor);
            else
                CloseChildProgramUisForActor(ent.Owner, args.Actor, ent.Comp);
            return;
        }

        CloseChildProgramUisForActor(ent.Owner, args.Actor, ent.Comp);
        RevokeViewerPvsOverrides(ent.Owner, args.Actor, ent.Comp);

        if (GetAllComputerViewers(ent.Owner, ent.Comp).Count == 0)
            ClearOsSession(ent.Owner, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnHardwareOrProgramInserted(Entity<OxydComputerComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == OxydComputerComponent.ContainerPrograms || ent.Comp.InitializingHardware)
            return;

        RecalculateHardwareStats(ent.Owner, ent.Comp);
        RefreshComputerBootState(ent.Owner, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnHardwareOrProgramRemoved(Entity<OxydComputerComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == OxydComputerComponent.ContainerPrograms)
            return;

        RecalculateHardwareStats(ent.Owner, ent.Comp);
        RefreshComputerBootState(ent.Owner, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnHostPacketReceived(Entity<EmbeddedComputerHostComponent> ent, ref SignalReceivedEvent<ComputerPacket> args)
    {
        if (ent.Comp.ComputerEntity is { } compUid)
            RelayDeviceLinkToPrograms(compUid, ent.Owner, args.Port, args.Trigger, null, args.Data, null);
    }

    [SubscribeLocalEvent]
    private void OnHostDeviceLinkPayloadReceived(Entity<EmbeddedComputerHostComponent> ent, ref SignalReceivedEvent<ComputerDeviceLinkPayload> args)
    {
        if (ent.Comp.ComputerEntity is { } compUid)
            RelayDeviceLinkToPrograms(compUid, ent.Owner, args.Port, args.Trigger, args.Data, null, null);
    }

    [SubscribeLocalEvent]
    private void OnHostBoolSignalReceived(Entity<EmbeddedComputerHostComponent> ent, ref SignalReceivedEvent args)
    {
        if (ent.Comp.ComputerEntity is { } compUid)
            RelayDeviceLinkToPrograms(compUid, ent.Owner, args.Port, args.Trigger, null, null, true);
    }

    [SubscribeLocalEvent]
    private void OnComputerPacketReceived(Entity<OxydComputerComponent> ent, ref SignalReceivedEvent<ComputerPacket> args)
    {
        RelayDeviceLinkToPrograms(ent.Owner, ent.Owner, args.Port, args.Trigger, null, args.Data, null);
    }

    [SubscribeLocalEvent]
    private void OnComputerDeviceLinkPayloadReceived(Entity<OxydComputerComponent> ent, ref SignalReceivedEvent<ComputerDeviceLinkPayload> args)
    {
        RelayDeviceLinkToPrograms(ent.Owner, ent.Owner, args.Port, args.Trigger, args.Data, null, null);
    }

    [SubscribeLocalEvent]
    private void OnComputerBoolSignalReceived(Entity<OxydComputerComponent> ent, ref SignalReceivedEvent args)
    {
        RelayDeviceLinkToPrograms(ent.Owner, ent.Owner, args.Port, args.Trigger, null, null, true);
    }

    public Enum GetPrimaryUiKey(EntityUid compUid, OxydComputerComponent? comp = null)
    {
        if (_computerQuery.Resolve(compUid, ref comp, false) && comp.Template == ComputerTemplate.Pda && _ui.HasUi(compUid, PdaUiKey.Key))
            return PdaUiKey.Key;

        if (_ui.HasUi(compUid, ComputerUiKey.Key))
            return ComputerUiKey.Key;

        return _ui.HasUi(compUid, PdaUiKey.Key) ? PdaUiKey.Key : ComputerUiKey.Key;
    }

    public bool EnsureComputerBoundToHost(Entity<EmbeddedComputerHostComponent> host, out EntityUid compUid)
    {
        if (host.Comp.ComputerEntity is { } existing && Exists(existing))
        {
            compUid = existing;
            if (_computerQuery.TryComp(existing, out var existingComp) && SyncHostPowerState(existing, existingComp))
            {
                RecalculateHardwareStats(existing, existingComp);
                RefreshComputerBootState(existing, existingComp);
            }
            return true;
        }

        // Guard against re-entrant binding on the host component (e.g. remote APC discovery while booting).
        if (host.Comp.BindingInProgress)
        {
            compUid = default;
            return false;
        }

        host.Comp.BindingInProgress = true;
        try
        {
            var slot = _container.EnsureContainer<ContainerSlot>(host.Owner, EmbeddedComputerHostComponent.ContainerBoard);
            if (slot.ContainedEntity is { } contained && Exists(contained))
            {
                compUid = contained;
            }
            else
            {
                compUid = Spawn(host.Comp.ComputerPrototype, Transform(host.Owner).Coordinates);
                _container.Insert(compUid, slot);
            }

        host.Comp.ComputerEntity = compUid;
        Dirty(host.Owner, host.Comp);

        if (_computerQuery.TryComp(compUid, out var comp))
        {
            comp.HostMachine = host.Owner;
            comp.HostTitle = host.Comp.Title;
            comp.HostSubtitle = host.Comp.Subtitle;
            comp.BorderColor = host.Comp.BorderColor;
            comp.AccentHColor = host.Comp.AccentHColor;
            comp.AccentVColor = host.Comp.AccentVColor;
            comp.DockInViewport = ShouldDockInViewport(compUid, comp);

            if (!string.IsNullOrEmpty(host.Comp.EmbeddedDefaultAppPath))
                comp.EmbeddedDefaultAppPath = host.Comp.EmbeddedDefaultAppPath;

            if (host.Comp.DefaultHardware.Count > 0)
            {
                ClearHardwareParts(compUid, comp);
                comp.DefaultHardware = new List<EntProtoId>(host.Comp.DefaultHardware);
                EnsureHardwareSlotsAndDefaults(compUid, comp);
            }

            if (TryComp<Content.Server.Power.Components.ApcPowerReceiverComponent>(host.Owner, out var apcRecv))
                comp.IsPowered = apcRecv.Powered || (!apcRecv.NeedsPower && !apcRecv.PowerDisabled);

            Dirty(compUid, comp);
            BindHostDeviceLinks(host.Owner, compUid);
            RecalculateHardwareStats(compUid, comp);
            RefreshComputerBootState(compUid, comp);
            SendPacketToHost(compUid, "MachineCommandReceiver", new ComputerPacket("status"));
            if (comp.BootProgram is { } bootProg && Exists(bootProg))
            {
                _osPrograms.UpdateOsUi(bootProg);
                _pdaOs.UpdatePdaOsUi(bootProg);
                _netCoreOs.UpdateCoreUi(bootProg);
            }
        }

        return true;
        }
        finally
        {
            host.Comp.BindingInProgress = false;
        }
    }

    public bool IsHostLocked(EntityUid compUid, OxydComputerComponent? comp = null)
    {
        return _computerQuery.Resolve(compUid, ref comp, false) &&
               comp.HostMachine is { } hm &&
               Exists(hm) &&
               _lock.IsLocked(hm);
    }

    /// <summary>
    /// Central server validation guard for program BUI actions: ensures the program is still running on a booted,
    /// powered computer with intact power bus, and optionally verifies the host machine is not hardware-locked.
    /// </summary>
    public bool CanExecuteProgramAction(
        EntityUid progUid,
        EntityUid? actor,
        out string blockReason,
        bool checkHostLock = true)
    {
        if (!_programQuery.TryComp(progUid, out var prog) || prog.Computer is not { } compUid || !_computerQuery.TryComp(compUid, out var comp))
        {
            blockReason = "Program is not bound to an active computer.";
            return false;
        }

        if (!comp.IsBooted || comp.PowerWireCut || (comp.RequirePowerToBoot && !comp.IsPowered))
        {
            blockReason = "Computer is powered off or not booted.";
            return false;
        }

        if (!comp.RunningPrograms.Contains(progUid))
        {
            blockReason = "Program is no longer running.";
            return false;
        }

        if (comp.SoftwareLocked)
        {
            blockReason = "Access denied: Computer is locked. Authenticate on the lock screen to unlock.";
            return false;
        }

        if (checkHostLock && IsHostLocked(compUid, comp))
        {
            blockReason = "Access denied: Host console is hardware-locked. Swipe authorized ID to unlock.";
            return false;
        }

        blockReason = string.Empty;
        return true;
    }

    /// <summary>
    /// Connects the computer entity to ALL source and sink ports on its represented host machine via DeviceLinkSystem.
    /// </summary>
    public void BindHostDeviceLinks(EntityUid hostUid, EntityUid compUid)
    {
        _deviceLink.EnsureSourcePort(compUid, PortComputerCommandSender);
        _deviceLink.EnsureSinkPort(compUid, PortComputerStatusReceiver);

        if (_sinkQuery.TryComp(compUid, out var compSink))
            _deviceLink.SetInvokeLimit((compUid, compSink), 64);

        var outLinks = new List<DeviceLink>();
        if (_sinkQuery.TryComp(hostUid, out var hostSinks))
        {
            _deviceLink.SetInvokeLimit((hostUid, hostSinks), 64);
            foreach (var sinkPort in hostSinks.Ports)
            {
                outLinks.Add(new DeviceLink(PortComputerCommandSender, sinkPort));
            }
        }
        if (outLinks.Count > 0)
            _deviceLink.SaveLinks(null, compUid, hostUid, outLinks);

        if (_sourceQuery.TryComp(hostUid, out var hostSources))
        {
            var hasDedicatedStatusSource = false;
            foreach (var srcPort in hostSources.Ports)
            {
                if (srcPort == "ApcStatusSource" ||
                    srcPort == "AirAlarmStatusSource" ||
                    srcPort == "PdaFlashlightStatusSource")
                {
                    hasDedicatedStatusSource = true;
                    break;
                }
            }

            var inLinks = new List<DeviceLink>();
            foreach (var srcPort in hostSources.Ports)
            {
                if (hasDedicatedStatusSource && srcPort == PortMachineStatusSender)
                    continue;

                inLinks.Add(new DeviceLink(srcPort, PortComputerStatusReceiver));
            }
            if (inLinks.Count > 0)
                _deviceLink.SaveLinks(null, hostUid, compUid, inLinks);
        }
    }

    private void ClearHardwareParts(EntityUid compUid, OxydComputerComponent comp)
    {
        foreach (var item in GetInstalledHardwareEntities(compUid))
        {
            if (_container.TryGetContainingContainer(compUid, item, out var containing))
                _container.Remove(item, containing, force: true);
            QueueDel(item);
        }

        if (TryComp<ModularComputerComponent>(compUid, out var mod))
        {
            foreach (var item in mod.InstalledParts.ToList())
            {
                if (Exists(item))
                    QueueDel(item);
            }
            mod.InstalledParts.Clear();
            Dirty(compUid, mod);
        }
    }

    private void EnsureHardwareSlotsAndDefaults(EntityUid compUid, OxydComputerComponent comp)
    {
        var hwContainer = _container.EnsureContainer<Container>(compUid, ModularComputerComponent.ContainerHardware);
        if (GetInstalledHardwareEntities(compUid).Count > 0)
            return;

        if (comp.DefaultHardware.Count == 0)
            return;

        comp.InitializingHardware = true;
        try
        {
            var coords = Transform(compUid).Coordinates;
            foreach (var protoId in comp.DefaultHardware)
            {
                if (!_proto.HasIndex<EntityPrototype>(protoId))
                    continue;

                var partUid = Spawn(protoId, coords);
                if (!TryInsertHardwarePartIntoSlot(compUid, partUid))
                    _container.Insert(partUid, hwContainer);
            }
        }
        finally
        {
            comp.InitializingHardware = false;
        }
    }

    public bool TryInsertHardwarePartIntoSlot(EntityUid compUid, EntityUid partUid)
    {
        if (TryComp<ModularComputerComponent>(compUid, out var mod))
        {
            if (TryComp<ComputerCpuComponent>(partUid, out var cpu) && cpu.Size > mod.MaxCpuSize)
                return false;
            if (TryComp<ComputerRamComponent>(partUid, out var ram) && ram.Size > mod.MaxRamSize)
                return false;
            if (TryComp<ComputerHardDriveComponent>(partUid, out var hdd) && hdd.Size > mod.MaxHardDriveSize)
                return false;
            if (TryComp<ComputerNetworkCardComponent>(partUid, out var nic) && nic.Size > mod.MaxNetworkCardSize)
                return false;
            if (TryComp<ComputerBatteryModuleComponent>(partUid, out var bat) && bat.Size > mod.MaxBatterySize)
                return false;
        }

        string? preferredSlot = null;
        if (HasComp<ComputerCpuComponent>(partUid))
            preferredSlot = ModularComputerComponent.SlotCpu;
        else if (HasComp<ComputerHardDriveComponent>(partUid))
            preferredSlot = ModularComputerComponent.SlotStorage;
        else if (HasComp<ComputerNetworkCardComponent>(partUid))
            preferredSlot = ModularComputerComponent.SlotNetwork;
        else if (HasComp<ComputerBatteryModuleComponent>(partUid))
            preferredSlot = ModularComputerComponent.SlotBattery;
        else if (HasComp<ComputerSensorModuleComponent>(partUid) || HasComp<ComputerHealthScannerModuleComponent>(partUid))
            preferredSlot = ModularComputerComponent.SlotSensor;
        else if (HasComp<ComputerDiskSlotModuleComponent>(partUid))
            preferredSlot = ModularComputerComponent.SlotDiskModule;
        else if (HasComp<ComputerPrinterModuleComponent>(partUid))
            preferredSlot = ModularComputerComponent.SlotPrinter;

        if (preferredSlot != null && _itemSlots.TryGetSlot(compUid, preferredSlot, out var slot) && slot.Item == null)
            return _itemSlots.TryInsert(compUid, slot, partUid, null);

        if (HasComp<ComputerRamComponent>(partUid))
        {
            if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotRam, out var ram1) && ram1.Item == null)
                return _itemSlots.TryInsert(compUid, ram1, partUid, null);
            if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotRamSecondary, out var ram2) && ram2.Item == null)
                return _itemSlots.TryInsert(compUid, ram2, partUid, null);
        }

        if (HasComp<ComputerIdCardSlotModuleComponent>(partUid))
        {
            if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotIdModule, out var idSlot1) && idSlot1.Item == null)
                return _itemSlots.TryInsert(compUid, idSlot1, partUid, null);
            if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotIdModuleSecondary, out var idSlot2) && idSlot2.Item == null)
                return _itemSlots.TryInsert(compUid, idSlot2, partUid, null);
        }

        return false;
    }

    public List<EntityUid> GetInstalledHardwareEntities(EntityUid compUid)
    {
        var result = new List<EntityUid>();
        if (!TryComp<ContainerManagerComponent>(compUid, out var containerMgr))
            return result;

        foreach (var container in containerMgr.Containers.Values)
        {
            if (container.ID == OxydComputerComponent.ContainerPrograms)
                continue;

            foreach (var ent in container.ContainedEntities)
            {
                if (Exists(ent) && !TerminatingOrDeleted(ent))
                    result.Add(ent);
            }
        }

        return result;
    }

    public void RecalculateHardwareStats(EntityUid compUid, OxydComputerComponent? comp = null)
    {
        if (!_computerQuery.Resolve(compUid, ref comp, false))
            return;

        var stats = EnsureComp<ComputerStatsComponent>(compUid);
        var parts = GetInstalledHardwareEntities(compUid);

        stats.HasCpu = false;
        stats.CpuFrequencyMhz = 0;
        stats.MaxConcurrentPrograms = 0;
        stats.CpuCapacity = 0f;
        stats.UsedCpuUnits = 0f;
        stats.HasRam = false;
        stats.RamMb = 0;
        stats.HasHardDrive = false;
        stats.PrimaryDriveUid = null;
        stats.StorageBytes = 0;
        stats.UsedStorageBytes = 0;
        stats.HasNetworkCard = false;
        stats.NetworkBandwidthBps = 0;
        stats.NetworkUploadBandwidthBps = 0;
        stats.HasBattery = false;
        stats.BatteryCharge = 0f;
        stats.BatteryMaxCharge = 0f;
        stats.HasSensorModule = false;
        stats.HasHealthScannerModule = false;
        stats.HasPrinterModule = false;
        stats.HasIdSlotModule = false;
        stats.HasDiskSlotModule = false;
        stats.HasDataPort = TryComp<ComputerDataPortComponent>(compUid, out var dp) && dp.Enabled;

        var modComp = CompOrNull<ModularComputerComponent>(compUid);
        if (modComp != null)
            modComp.InstalledParts.Clear();

        var nicBufferLimit = 50;

        foreach (var part in parts)
        {
            modComp?.InstalledParts.Add(part);

            if (TryComp<ComputerCpuComponent>(part, out var cpu))
            {
                stats.HasCpu = true;
                stats.CpuFrequencyMhz = Math.Max(stats.CpuFrequencyMhz, cpu.FrequencyMhz);
                stats.MaxConcurrentPrograms += Math.Max(1, cpu.MaxConcurrentPrograms);
                stats.CpuCapacity += ComputerMetricsHelper.GetCpuCapacity(cpu);
            }

            if (TryComp<ComputerRamComponent>(part, out var ram))
            {
                stats.HasRam = true;
                stats.RamMb += Math.Max(0, ram.SizeMb);
            }

            if (HasComp<ComputerHardDriveComponent>(part) && TryComp<FileSystemComponent>(part, out var fs))
            {
                stats.HasHardDrive = true;
                stats.PrimaryDriveUid ??= part;
                stats.StorageBytes += fs.MaxStorageBytes;
                stats.UsedStorageBytes += fs.UsedStorageBytes;
            }

            if (TryComp<ComputerNetworkCardComponent>(part, out var nic) && !comp.NetworkWireCut)
            {
                stats.HasNetworkCard = true;
                stats.NetworkBandwidthBps += Math.Max(0, nic.BandwidthBps);
                stats.NetworkUploadBandwidthBps += Math.Max(0, nic.MaxUploadBps);
                nicBufferLimit = Math.Max(nicBufferLimit, nic.BufferQueueLimit);
            }

            if (TryComp<ComputerBatteryModuleComponent>(part, out var bat))
            {
                stats.HasBattery = true;
                stats.BatteryCharge += bat.CurrentCharge;
                stats.BatteryMaxCharge += bat.MaxCharge;
            }

            if (HasComp<ComputerSensorModuleComponent>(part))
                stats.HasSensorModule = true;
            if (HasComp<ComputerHealthScannerModuleComponent>(part))
                stats.HasHealthScannerModule = true;
            if (HasComp<ComputerPrinterModuleComponent>(part))
                stats.HasPrinterModule = true;
            if (HasComp<ComputerIdCardSlotModuleComponent>(part))
                stats.HasIdSlotModule = true;
            if (HasComp<ComputerDiskSlotModuleComponent>(part))
                stats.HasDiskSlotModule = true;
        }

        // Sync running resource footprint.
        var usedRam = 0;
        var cpuUnits = 0f;
        foreach (var progUid in comp.RunningPrograms)
        {
            if (!_programQuery.TryComp(progUid, out var prog))
                continue;

            usedRam += ComputerMetricsHelper.GetProgramRamUsageMb(prog);
            cpuUnits += ComputerMetricsHelper.GetProgramCpuUnits(prog);
        }
        stats.UsedRamMb = usedRam;
        stats.UsedCpuUnits = cpuUnits;
        stats.CurrentCpuLoadPercent = stats.CpuCapacity > 0f
            ? MathF.Min(999f, (cpuUnits / stats.CpuCapacity) * 100f)
            : 0f;

        // Sync NetworkBandwidth & DeviceNetwork state based strictly on physical NetworkCard + NetworkWire.
        if (stats.HasNetworkCard && !comp.NetworkWireCut && comp.IsPowered)
        {
            var bw = EnsureComp<NetworkBandwidthComponent>(compUid);
            bw.MaxBps = Math.Max(1024, stats.NetworkBandwidthBps);
            bw.MaxUploadBps = Math.Max(1024, stats.NetworkUploadBandwidthBps);
            bw.BufferQueueLimit = nicBufferLimit;
            Dirty(compUid, bw);

            EnsureComp<DeviceNetworkComponent>(compUid);
        }
        else
        {
            if (TryComp<NetworkBandwidthComponent>(compUid, out var bw))
            {
                bw.MaxBps = 0;
                bw.MaxUploadBps = 0;
                Dirty(compUid, bw);
            }
        }

        if (modComp != null)
            Dirty(compUid, modComp);
        Dirty(compUid, stats);

        var hwEv = new ComputerHardwareChangedEvent(compUid);
        RaiseLocalEvent(compUid, ref hwEv);
        var notified = new HashSet<EntityUid>();
        if (comp.BootProgram is { } bootUid && Exists(bootUid) && notified.Add(bootUid))
            RaiseLocalEvent(bootUid, ref hwEv);
        foreach (var progUid in comp.RunningPrograms.ToList())
        {
            if (Exists(progUid) && notified.Add(progUid))
                RaiseLocalEvent(progUid, ref hwEv);
        }
    }

    public void RefreshComputerBootState(EntityUid compUid, OxydComputerComponent? comp = null)
    {
        if (!_computerQuery.Resolve(compUid, ref comp, false))
            return;

        SyncHostPowerState(compUid, comp);

        if (!_statsQuery.TryComp(compUid, out var stats))
        {
            RecalculateHardwareStats(compUid, comp);
            stats = Comp<ComputerStatsComponent>(compUid);
        }

        if (comp.ManuallyShutDown)
        {
            FailBoot(compUid, comp, ComputerBootState.Off, "System powered off. Click Boot to power on.");
            return;
        }

        if (comp.PowerWireCut)
        {
            FailBoot(compUid, comp, ComputerBootState.WireCut, "POST ERROR 0x01: Motherboard power bus severed.");
            return;
        }

        if (comp.RequirePowerToBoot && !comp.IsPowered && stats.BatteryCharge <= 0f)
        {
            FailBoot(compUid, comp, ComputerBootState.NoPower, "POST ERROR 0x02: Insufficient power to boot computer.");
            return;
        }

        if (!stats.HasCpu)
        {
            FailBoot(compUid, comp, ComputerBootState.NoCpu, "POST ERROR 0x05: No CPU module installed.");
            return;
        }

        if (!stats.HasRam || stats.RamMb <= 0)
        {
            FailBoot(compUid, comp, ComputerBootState.NoRam, "POST ERROR 0x03: No system memory (RAM) installed.");
            return;
        }

        if (comp.StorageWireCut)
        {
            FailBoot(compUid, comp, ComputerBootState.WireCut, "POST ERROR 0x04: Storage controller wire severed.");
            return;
        }

        if (!stats.HasHardDrive || !_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
        {
            FailBoot(compUid, comp, ComputerBootState.NoHardDrive, "BOOT FAILURE: No hard drive installed.");
            return;
        }

        if (!_fileSystem.TryGetFirstExecutable(driveUid.Value, out var firstPath, out var execFile, fs))
        {
            FailBoot(compUid, comp, ComputerBootState.NoBootProgram, "BOOT FAILURE: No bootable .nte executable found on hard drive.");
            return;
        }

        if (!_proto.TryIndex<EntityPrototype>(execFile.ProgramId, out var execProto))
        {
            FailBoot(compUid, comp, ComputerBootState.NoBootProgram, $"BOOT FAILURE: Executable '{firstPath}' references invalid program '{execFile.ProgramId}'.");
            return;
        }

        var bootRam = execFile.RamUsageMb;
        var bootCpuUnits = Math.Max(0.5f, execFile.CpuUsagePercent);
        if (execProto.TryComp<ProgramComponent>(out var defProg, EntityManager.ComponentFactory))
        {
            bootRam = Math.Max(1, defProg.RamUsageMb);
            bootCpuUnits = Math.Max(0.5f, defProg.CpuUsagePercent);
        }

        if (bootRam > stats.RamMb)
        {
            FailBoot(compUid, comp, ComputerBootState.NoRam, $"BOOT FAILURE: '{firstPath}' requires {bootRam} QB RAM ({stats.RamMb} QB installed).");
            return;
        }

        if (bootCpuUnits > stats.CpuCapacity)
        {
            FailBoot(compUid, comp, ComputerBootState.NoCpu, $"BOOT FAILURE: '{firstPath}' requires {bootCpuUnits:0} CPU units ({stats.CpuCapacity:0} available).");
            return;
        }

        // If already booted on the exact same boot file and running, just validate running programs still fit hardware.
        if (comp.IsBooted &&
            comp.BootProgram is { } existingBoot &&
            Exists(existingBoot) &&
            _programQuery.TryComp(existingBoot, out var bootProgComp) &&
            bootProgComp.ExecutablePath == firstPath)
        {
            TrimExcessProgramsToHardware(compUid, comp, stats);
            comp.BootState = ComputerBootState.Running;
            comp.BootError = string.Empty;
            stats.IsBooted = true;
            stats.BootError = string.Empty;
            Dirty(compUid, comp);
            Dirty(compUid, stats);
            UpdateComputerDisplayUi(compUid, comp);
            return;
        }

        // Clean boot of the first .nte program on the hard drive!
        ShutDownComputer(compUid, comp, updateUi: false);

        var progContainer = _container.EnsureContainer<Container>(compUid, OxydComputerComponent.ContainerPrograms);
        var bootUid = Spawn(execFile.ProgramId, Transform(compUid).Coordinates);
        _container.Insert(bootUid, progContainer);

        var pComp = EnsureComp<ProgramComponent>(bootUid);
        pComp.Computer = compUid;
        pComp.Prototype = execFile.ProgramId;
        pComp.IsBootProgram = true;
        pComp.ExecutablePath = firstPath;
        if (!string.IsNullOrEmpty(execFile.Title))
            pComp.Title = execFile.Title;
        Dirty(bootUid, pComp);

        comp.BootProgram = bootUid;
        comp.ActiveProgram = null;
        comp.RunningPrograms.Add(bootUid);
        comp.IsBooted = true;
        comp.BootState = ComputerBootState.Running;
        comp.BootError = string.Empty;

        // Force re-authentication on every clean boot whenever a password/key/ID lock is configured.
        if (HasSoftwareLockConfigured(comp))
            EngageSoftwareLock(compUid, comp);

        stats.IsBooted = true;
        stats.BootError = string.Empty;
        stats.UsedRamMb = ComputerMetricsHelper.GetProgramRamUsageMb(pComp);
        stats.UsedCpuUnits = ComputerMetricsHelper.GetProgramCpuUnits(pComp);
        stats.CurrentCpuLoadPercent = stats.CpuCapacity > 0f
            ? MathF.Min(999f, (stats.UsedCpuUnits / stats.CpuCapacity) * 100f)
            : 0f;
        Dirty(compUid, comp);
        Dirty(compUid, stats);

        _fileSystem.LogSystemActivity(driveUid.Value, "BOOT", $"Booted first executable {firstPath} ({pComp.Title})");

        var startEv = new ProgramStartupEvent(compUid, bootUid);
        RaiseLocalEvent(bootUid, ref startEv);
        var actEv = new ProgramActivatedEvent(compUid, bootUid);
        RaiseLocalEvent(bootUid, ref actEv);

        UpdateComputerDisplayUi(compUid, comp);
        OpenRunningProgramUisForCurrentViewers(compUid, comp);
    }

    private void FailBoot(EntityUid compUid, OxydComputerComponent comp, ComputerBootState failState, string message)
    {
        ShutDownComputer(compUid, comp, updateUi: false);

        comp.BootState = failState;
        comp.IsBooted = false;
        comp.BootError = message;
        Dirty(compUid, comp);

        if (_statsQuery.TryComp(compUid, out var stats))
        {
            stats.IsBooted = false;
            stats.BootError = message;
            Dirty(compUid, stats);
        }

        UpdateComputerDisplayUi(compUid, comp);
    }

    public void ShutDownComputer(EntityUid compUid, OxydComputerComponent? comp = null, bool updateUi = true, bool manualShutdown = false)
    {
        if (!_computerQuery.Resolve(compUid, ref comp, false))
            return;

        foreach (var progUid in comp.RunningPrograms.ToList())
        {
            if (!Exists(progUid))
                continue;

            _ui.CloseUis(progUid);
            var termEv = new ProgramTerminatedEvent(compUid, progUid);
            RaiseLocalEvent(progUid, ref termEv);
            QueueDel(progUid);
        }

        comp.RunningPrograms.Clear();
        comp.BootProgram = null;
        comp.ActiveProgram = null;
        comp.IsBooted = false;
        comp.BootState = ComputerBootState.Off;
        comp.OsAuthUserName = string.Empty;
        comp.OsAuthJobTitle = string.Empty;
        comp.OsAuthLabel = string.Empty;
        comp.OsAuthAccessTags.Clear();
        if (manualShutdown)
        {
            comp.ManuallyShutDown = true;
            comp.BootError = "System powered off. Click Boot to power on.";
        }
        Dirty(compUid, comp);

        if (_statsQuery.TryComp(compUid, out var stats))
        {
            stats.UsedRamMb = 0;
            stats.CurrentCpuLoadPercent = 0f;
            stats.IsBooted = false;
            if (manualShutdown)
                stats.BootError = comp.BootError;
            Dirty(compUid, stats);
        }

        if (updateUi)
            UpdateComputerDisplayUi(compUid, comp);
    }

    public void RebootComputer(EntityUid compUid, OxydComputerComponent? comp = null)
    {
        if (!_computerQuery.Resolve(compUid, ref comp, false))
            return;

        comp.ManuallyShutDown = false;
        ShutDownComputer(compUid, comp, updateUi: false);
        RecalculateHardwareStats(compUid, comp);
        RefreshComputerBootState(compUid, comp);
    }

    /// <summary>
    /// Updates a running program's dynamic runtime CPU workload bonus and recalculates computer CPU load.
    /// </summary>
    public void SetProgramDynamicCpuBonus(EntityUid progUid, float bonus)
    {
        if (!_programQuery.TryComp(progUid, out var prog) || prog.Computer is not { Valid: true } compUid)
            return;

        var clamped = Math.Max(0f, bonus);
        if (MathF.Abs(prog.DynamicCpuUsageBonus - clamped) < 0.01f)
            return;

        prog.DynamicCpuUsageBonus = clamped;
        Dirty(progUid, prog);
        RecalculateHardwareStats(compUid);
    }

    private void TrimExcessProgramsToHardware(EntityUid compUid, OxydComputerComponent comp, ComputerStatsComponent stats)
    {
        var nonBoot = comp.RunningPrograms.Where(p => p != comp.BootProgram).ToList();
        while (nonBoot.Count > 0 && (stats.UsedCpuUnits > stats.CpuCapacity || stats.UsedRamMb > stats.RamMb))
        {
            var victim = nonBoot[^1];
            nonBoot.RemoveAt(nonBoot.Count - 1);
            CloseProgram(compUid, victim, comp);
            RecalculateHardwareStats(compUid, comp);
        }
    }

    /// <summary>
    /// Launches an executable .nte file from the computer's hard drive into a program entity
    /// and opens its BoundUserInterface for all current viewers so the Boot OS can parent its UI.
    /// </summary>
    public bool TryLaunchExecutable(
        EntityUid compUid,
        string executablePathOrName,
        [NotNullWhen(true)] out EntityUid? launchedUid,
        out string error,
        EntityUid? actor = null,
        OxydComputerComponent? comp = null)
    {
        launchedUid = null;
        error = string.Empty;

        if (!_computerQuery.Resolve(compUid, ref comp, false) || !comp.IsBooted)
        {
            error = "Computer is not booted.";
            return false;
        }

        if (!_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
        {
            error = "No hard drive available.";
            return false;
        }

        if (!TryResolveExecutable(fs, executablePathOrName, out var resolvedPath, out var execFile))
        {
            error = $"Executable not found on hard drive: '{executablePathOrName}'.";
            return false;
        }

        // If already running, focus existing process entity!
        foreach (var existing in comp.RunningPrograms)
        {
            if (!_programQuery.TryComp(existing, out var exProg))
                continue;

            if (exProg.ExecutablePath == resolvedPath || exProg.Prototype == execFile.ProgramId)
            {
                SwitchActiveProgram(compUid, existing, comp);
                launchedUid = existing;
                return true;
            }
        }

        if (!_proto.TryIndex<EntityPrototype>(execFile.ProgramId, out var progProto))
        {
            error = $"Invalid program prototype '{execFile.ProgramId}'.";
            return false;
        }

        if (progProto.TryComp<ProgramComponent>(out var defComp, EntityManager.ComponentFactory) &&
            defComp.IsOperatingSystem)
        {
            error = $"'{resolvedPath}' is an operating system kernel and cannot run as a child program. Use 'boot {resolvedPath}' to switch OS.";
            return false;
        }

        var ramNeeded = execFile.RamUsageMb;
        var cpuUnitsNeeded = Math.Max(0.5f, execFile.CpuUsagePercent);
        if (defComp != null)
        {
            ramNeeded = Math.Max(1, defComp.RamUsageMb);
            cpuUnitsNeeded = Math.Max(0.5f, defComp.CpuUsagePercent);
        }

        RecalculateHardwareStats(compUid, comp);
        var stats = Comp<ComputerStatsComponent>(compUid);

        if (stats.UsedCpuUnits + cpuUnitsNeeded > stats.CpuCapacity)
        {
            error = $"Insufficient CPU capacity: needs {cpuUnitsNeeded:0} units, only {Math.Max(0f, stats.CpuCapacity - stats.UsedCpuUnits):0} available (CPU load {stats.CurrentCpuLoadPercent:0}%).";
            return false;
        }

        if (stats.UsedRamMb + ramNeeded > stats.RamMb)
        {
            error = $"Insufficient RAM: needs {ramNeeded} QB, only {Math.Max(0, stats.RamMb - stats.UsedRamMb)} QB free.";
            return false;
        }

        var progContainer = _container.EnsureContainer<Container>(compUid, OxydComputerComponent.ContainerPrograms);
        var progUid = Spawn(execFile.ProgramId, Transform(compUid).Coordinates);
        _container.Insert(progUid, progContainer);

        var pComp = EnsureComp<ProgramComponent>(progUid);
        pComp.Computer = compUid;
        pComp.Prototype = execFile.ProgramId;
        pComp.ExecutablePath = resolvedPath;
        pComp.IsBootProgram = false;
        if (!string.IsNullOrEmpty(execFile.Title))
            pComp.Title = execFile.Title;
        Dirty(progUid, pComp);

        comp.RunningPrograms.Add(progUid);
        Dirty(compUid, comp);
        RecalculateHardwareStats(compUid, comp);

        _fileSystem.LogSystemActivity(driveUid.Value, "EXEC", $"Launched {resolvedPath} (PID {progUid.Id})");

        var startEv = new ProgramStartupEvent(compUid, progUid);
        RaiseLocalEvent(progUid, ref startEv);

        SwitchActiveProgram(compUid, progUid, comp, actor);
        launchedUid = progUid;
        return true;
    }

    public bool SwitchActiveProgram(EntityUid compUid, EntityUid targetProgUid, OxydComputerComponent? comp = null, EntityUid? actor = null)
    {
        if (!_computerQuery.Resolve(compUid, ref comp, false))
            return false;

        if (!comp.RunningPrograms.Contains(targetProgUid))
            return false;

        if (comp.ActiveProgram is { } oldActive && oldActive != targetProgUid && Exists(oldActive))
        {
            var deactEv = new ProgramDeactivatedEvent(compUid, oldActive);
            RaiseLocalEvent(oldActive, ref deactEv);
        }

        comp.ActiveProgram = targetProgUid == comp.BootProgram ? null : targetProgUid;
        Dirty(compUid, comp);

        OpenProgramUiForAllComputerViewers(compUid, targetProgUid, actor);

        var actEv = new ProgramActivatedEvent(compUid, targetProgUid);
        RaiseLocalEvent(targetProgUid, ref actEv);

        // Notify Boot OS program to switch active viewport and show child UI.
        if (comp.BootProgram is { } bootUid && Exists(bootUid) && bootUid != targetProgUid)
        {
            if (TryComp<OsProgramComponent>(bootUid, out var osComp))
            {
                osComp.CurrentView = 3; // Active child application view
                Dirty(bootUid, osComp);
                _osPrograms.UpdateOsUi(bootUid);
            }
            else if (TryComp<PdaOsProgramComponent>(bootUid, out var pdaOsComp))
            {
                pdaOsComp.CurrentView = 2; // Active child application view
                Dirty(bootUid, pdaOsComp);
                _pdaOs.UpdatePdaOsUi(bootUid);
            }
            else if (TryComp<EmbeddedOsProgramComponent>(bootUid, out var embComp))
            {
                embComp.CurrentView = 0; // Active child application view
                Dirty(bootUid, embComp);
                _osPrograms.UpdateOsUi(bootUid);
            }
            else if (TryComp<NetworkCoreOsProgramComponent>(bootUid, out var netCoreComp))
            {
                _netCoreOs.UpdateCoreUi(bootUid);
            }
            else
            {
                _osPrograms.UpdateOsUi(bootUid);
            }
        }

        UpdateComputerDisplayUi(compUid, comp);
        return true;
    }

    public bool CloseProgram(EntityUid compUid, EntityUid targetProgUid, OxydComputerComponent? comp = null)
    {
        if (!_computerQuery.Resolve(compUid, ref comp, false))
            return false;

        if (comp.BootProgram == targetProgUid)
            return false;

        if (!comp.RunningPrograms.Remove(targetProgUid))
            return false;

        if (comp.ActiveProgram == targetProgUid)
        {
            var fallback = comp.RunningPrograms.LastOrDefault(p => p != comp.BootProgram);
            comp.ActiveProgram = fallback != EntityUid.Invalid ? fallback : null;
        }
        Dirty(compUid, comp);

        if (Exists(targetProgUid))
        {
            _ui.CloseUis(targetProgUid);
            var termEv = new ProgramTerminatedEvent(compUid, targetProgUid);
            RaiseLocalEvent(targetProgUid, ref termEv);
            QueueDel(targetProgUid);
        }

        RecalculateHardwareStats(compUid, comp);

        if (comp.BootProgram is { } bootUid && Exists(bootUid))
        {
            if (comp.ActiveProgram == null)
            {
                if (TryComp<OsProgramComponent>(bootUid, out var osComp) && osComp.CurrentView == 3)
                {
                    osComp.CurrentView = 0; // Return to merged Desktop & App Launcher
                    Dirty(bootUid, osComp);
                }
                else if (TryComp<PdaOsProgramComponent>(bootUid, out var pdaOsComp) && pdaOsComp.CurrentView == 2)
                {
                    pdaOsComp.CurrentView = 0; // Return to merged Home/Apps view
                    Dirty(bootUid, pdaOsComp);
                }
                else if (TryComp<EmbeddedOsProgramComponent>(bootUid, out var embComp))
                {
                    var isTwoTabController =
                        string.Equals(comp.EmbeddedDefaultAppPath, "/bin/apc_control.nte", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(comp.EmbeddedDefaultAppPath, "/bin/lathe_control.nte", StringComparison.OrdinalIgnoreCase);
                    embComp.CurrentView = isTwoTabController ? 0 : 1;
                    Dirty(bootUid, embComp);
                }
                else if (TryComp<NetworkCoreOsProgramComponent>(bootUid, out var netCoreComp) && netCoreComp.CurrentTab == 1)
                {
                    netCoreComp.CurrentTab = 0;
                    Dirty(bootUid, netCoreComp);
                }
            }

            _osPrograms.UpdateOsUi(bootUid);
            _pdaOs.UpdatePdaOsUi(bootUid);
            _netCoreOs.UpdateCoreUi(bootUid);
        }

        UpdateComputerDisplayUi(compUid, comp);
        return true;
    }

    private bool TryResolveExecutable(
        FileSystemComponent fs,
        string query,
        [NotNullWhen(true)] out string? fullPath,
        [NotNullWhen(true)] out ProgramVirtualFile? execFile)
    {
        fullPath = null;
        execFile = null;

        if (string.IsNullOrWhiteSpace(query))
            return false;

        var trimmed = query.Trim();
        if (fs.Files.TryGetValue(trimmed, out var direct) && direct is ProgramVirtualFile dProg &&
            dProg.Extension.Equals("nte", StringComparison.OrdinalIgnoreCase))
        {
            fullPath = trimmed;
            execFile = dProg;
            return true;
        }

        var normalizedName = trimmed.EndsWith(".nte", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;

        foreach (var (path, file) in fs.Files)
        {
            if (file is not ProgramVirtualFile prog || !prog.Extension.Equals("nte", StringComparison.OrdinalIgnoreCase))
                continue;

            if (path.Equals($"/bin/{normalizedName}.nte", StringComparison.OrdinalIgnoreCase) ||
                prog.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase) ||
                prog.ProgramId.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                prog.Title.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                fullPath = path;
                execFile = prog;
                return true;
            }
        }

        return false;
    }

    public void OpenComputerForActor(EntityUid compUid, EntityUid actor, OxydComputerComponent? comp = null)
    {
        if (!_computerQuery.Resolve(compUid, ref comp, false))
            return;

        if (!_statsQuery.TryComp(compUid, out var stats))
        {
            RecalculateHardwareStats(compUid, comp);
            stats = Comp<ComputerStatsComponent>(compUid);
        }

        if (IsComputerUnpowered(compUid, comp, stats))
        {
            _ui.CloseUis(compUid);
            return;
        }

        if (!comp.IsBooted && comp.AutoBootWhenPowered)
            RefreshComputerBootState(compUid, comp);

        GrantViewerPvsOverrides(compUid, actor, comp, stats);

        var key = GetPrimaryUiKey(compUid, comp);
        _ui.OpenUi(compUid, key, actor);
        UpdateComputerDisplayUi(compUid, comp);

        foreach (var progUid in comp.RunningPrograms)
        {
            if (Exists(progUid))
                _ui.OpenUi(progUid, ProgramUiKey.Key, actor);
        }
    }

    public void CloseComputerForActor(EntityUid compUid, EntityUid actor, Enum? keyOverride = null)
    {
        if (!_computerQuery.TryComp(compUid, out var comp))
            return;

        var key = keyOverride ?? GetPrimaryUiKey(compUid, comp);
        _ui.CloseUi(compUid, key, actor);
        CloseChildProgramUisForActor(compUid, actor, comp);
    }

    private void CloseChildProgramUisForActor(EntityUid compUid, EntityUid actor, OxydComputerComponent comp)
    {
        foreach (var progUid in comp.RunningPrograms)
        {
            if (Exists(progUid))
                _ui.CloseUi(progUid, ProgramUiKey.Key, actor);
        }
    }

    private void OpenRunningProgramUisForCurrentViewers(EntityUid compUid, OxydComputerComponent comp)
    {
        foreach (var actor in GetAllComputerViewers(compUid, comp))
        {
            if (TryComp<ActorComponent>(actor, out var actorComp))
                GrantViewerPvsOverrides(compUid, actor, comp, CompOrNull<ComputerStatsComponent>(compUid));

            foreach (var progUid in comp.RunningPrograms)
            {
                if (Exists(progUid))
                    _ui.OpenUi(progUid, ProgramUiKey.Key, actor);
            }
        }
    }

    private void OpenProgramUiForAllComputerViewers(EntityUid compUid, EntityUid progUid, EntityUid? extraActor = null)
    {
        if (!_computerQuery.TryComp(compUid, out var comp))
            return;

        var viewers = GetAllComputerViewers(compUid, comp);
        if (extraActor is { } extra && !viewers.Contains(extra))
            viewers.Add(extra);

        foreach (var viewer in viewers)
        {
            if (TryComp<ActorComponent>(viewer, out var actorComp))
                _pvsOverride.AddSessionOverride(progUid, actorComp.PlayerSession);

            _remoteScreen.AuthorizeNewProgramForRemoteViewers(compUid, progUid);
            _ui.OpenUi(progUid, ProgramUiKey.Key, viewer);
        }
    }

    public List<EntityUid> GetAllComputerViewers(EntityUid compUid, OxydComputerComponent? comp = null)
    {
        var result = new List<EntityUid>();
        if (TryComp<UserInterfaceComponent>(compUid, out var ui))
        {
            foreach (var actorSet in ui.Actors.Values)
            {
                foreach (var actor in actorSet)
                {
                    if (Exists(actor) && !result.Contains(actor))
                        result.Add(actor);
                }
            }
        }

        // Include anyone remotely viewing via ServerRemoteScreenSystem.
        var compNet = GetNetEntity(compUid);
        var remQuery = EntityQueryEnumerator<ActiveRemoteScreenViewerComponent>();
        while (remQuery.MoveNext(out var viewerUid, out var rem))
        {
            if (rem.AuthorizedTargets.Contains(compNet) && Exists(viewerUid) && !result.Contains(viewerUid))
                result.Add(viewerUid);
        }

        return result;
    }

    public void GrantViewerPvsOverrides(EntityUid compUid, EntityUid actor, OxydComputerComponent? comp = null, ComputerStatsComponent? stats = null)
    {
        if (!TryComp<ActorComponent>(actor, out var actorComp))
            return;

        var session = actorComp.PlayerSession;
        _pvsOverride.AddSessionOverride(compUid, session);

        if (stats?.PrimaryDriveUid is { } driveUid && Exists(driveUid))
            _pvsOverride.AddSessionOverride(driveUid, session);

        if (_computerQuery.Resolve(compUid, ref comp, false))
        {
            foreach (var progUid in comp.RunningPrograms)
            {
                if (Exists(progUid))
                    _pvsOverride.AddSessionOverride(progUid, session);
            }
        }
    }

    public void RevokeViewerPvsOverrides(EntityUid compUid, EntityUid actor, OxydComputerComponent? comp = null)
    {
        if (!TryComp<ActorComponent>(actor, out var actorComp))
            return;

        var session = actorComp.PlayerSession;
        _pvsOverride.RemoveSessionOverride(compUid, session);

        if (_statsQuery.TryComp(compUid, out var stats) && stats.PrimaryDriveUid is { } driveUid && Exists(driveUid))
            _pvsOverride.RemoveSessionOverride(driveUid, session);

        if (_computerQuery.Resolve(compUid, ref comp, false))
        {
            foreach (var progUid in comp.RunningPrograms)
            {
                if (Exists(progUid))
                    _pvsOverride.RemoveSessionOverride(progUid, session);
            }
        }
    }

    public void UpdateComputerDisplayUi(EntityUid compUid, OxydComputerComponent? comp = null)
    {
        if (!_computerQuery.Resolve(compUid, ref comp, false))
            return;

        var dockInViewport = ShouldDockInViewport(compUid, comp);
        if (comp.DockInViewport != dockInViewport)
        {
            comp.DockInViewport = dockInViewport;
            Dirty(compUid, comp);
        }

        var requiredAccessLabels = new List<string>();
        foreach (var tag in comp.RequiredAccessTags)
        {
            if (_proto.TryIndex(tag, out var accessProto))
                requiredAccessLabels.Add(accessProto.GetAccessLevelName());
            else
                requiredAccessLabels.Add(tag.Id);
        }

        var keyLabelDisplay = !string.IsNullOrEmpty(comp.LockKeySecret)
            ? (!string.IsNullOrWhiteSpace(comp.LockKeyId) ? $"{comp.LockKeyLabel} [{comp.LockKeyId}]" : comp.LockKeyLabel)
            : string.Empty;

        var hostDisplayName = comp.HostMachine is { Valid: true } displayHost && Exists(displayHost)
            ? MetaData(displayHost).EntityName
            : MetaData(compUid).EntityName;
        var isHostLocked = IsHostLocked(compUid, comp);

        var stats = CompOrNull<ComputerStatsComponent>(compUid) ?? EnsureComp<ComputerStatsComponent>(compUid);
        var state = new ComputerDisplayUiState(
            MetaData(compUid).EntityName,
            _guid.GetGuid(compUid),
            comp.HostTitle,
            comp.HostSubtitle,
            comp.BorderColor,
            comp.AccentHColor,
            comp.AccentVColor,
            comp.DefaultScreenSize,
            comp.IsBooted,
            comp.BootError,
            comp.BootProgram is { } bp && Exists(bp) ? GetNetEntity(bp) : null,
            comp.ActiveProgram is { } ap && Exists(ap) ? GetNetEntity(ap) : null,
            comp.HostMachine is { } hm && Exists(hm) ? GetNetEntity(hm) : null,
            stats.CpuFrequencyMhz,
            stats.UsedRamMb,
            stats.RamMb,
            stats.UsedStorageBytes,
            stats.StorageBytes,
            stats.HasCpu,
            stats.HasRam,
            stats.HasHardDrive,
            stats.HasNetworkCard,
            stats.HasDataPort,
            dockInViewport,
            comp.SoftwareLocked,
            !string.IsNullOrEmpty(comp.LockPassword) || !string.IsNullOrEmpty(comp.SshPassword),
            !string.IsNullOrEmpty(comp.LockKeySecret),
            keyLabelDisplay,
            requiredAccessLabels,
            comp.LockRequiresAllAccess,
            comp.LockStatusMessage,
            isHostLocked,
            hostDisplayName);

        UpdateHostScreenVisuals(compUid, comp);

        var key = GetPrimaryUiKey(compUid, comp);
        if (_ui.HasUi(compUid, key))
            _ui.SetUiState(compUid, key, state);
    }

    private void UpdateHostScreenVisuals(EntityUid compUid, OxydComputerComponent comp)
    {
        if (comp.HostMachine is not { } hostUid || !Exists(hostUid))
            return;

        var desiredScreen = string.Empty;
        var fgUid = comp.ActiveProgram ?? comp.BootProgram;
        if (comp.IsBooted &&
            fgUid is { } activeUid &&
            Exists(activeUid) &&
            _programQuery.TryComp(activeUid, out var activeProg) &&
            !string.IsNullOrWhiteSpace(activeProg.ScreenState))
        {
            desiredScreen = activeProg.ScreenState;
        }

        _appearance.SetData(hostUid, ComputerVisuals.ScreenState, desiredScreen);
    }

    /// <summary>
    /// Determines whether this computer's display UI should dock inside the client's <c>ViewportContainer</c>
    /// and pause normal world rendering while open locally.
    /// Enabled for stationary consoles, workstations, data servers, and station routers;
    /// always disabled for handheld PDAs, tablets, loose boards, and small embedded subsystem panels.
    /// </summary>
    private bool ShouldDockInViewport(EntityUid compUid, OxydComputerComponent comp)
    {
        if (comp.Template == ComputerTemplate.Pda)
            return false;

        if (comp.HostMachine is not { } host || !Exists(host))
            return false;

        if (HasComp<ItemComponent>(host) || HasComp<PdaComponent>(host))
            return false;

        if (TryComp<EmbeddedComputerHostComponent>(host, out var hostComp) && hostComp.DockInViewport.HasValue)
            return hostComp.DockInViewport.Value;

        return comp.Template == ComputerTemplate.Desktop
            || HasComp<ComputerComponent>(host)
            || HasComp<DataServerComponent>(host)
            || HasComp<NetworkRouterComponent>(host);
    }

    /// <summary>
    /// Collects all DeviceLink source and sink ports from both the represented host machine
    /// and the encapsulated computer entity so programs have unified port visibility.
    /// </summary>
    public List<MirroredDevicePortInfo> GetMirroredPorts(EntityUid compUid, OxydComputerComponent? comp = null)
    {
        var result = new List<MirroredDevicePortInfo>();
        if (!_computerQuery.Resolve(compUid, ref comp, false))
            return result;

        if (comp.HostMachine is { } hostUid && Exists(hostUid))
            CollectEntityPorts(hostUid, isHost: true, result);

        CollectEntityPorts(compUid, isHost: false, result);
        return result;
    }

    private void CollectEntityPorts(EntityUid ownerUid, bool isHost, List<MirroredDevicePortInfo> output)
    {
        var ownerName = MetaData(ownerUid).EntityName;
        var ownerNet = GetNetEntity(ownerUid);

        if (_sourceQuery.TryComp(ownerUid, out var srcComp))
        {
            foreach (var portId in _deviceLink.GetSourcePortIds((ownerUid, srcComp)))
            {
                var linked = new List<string>();
                foreach (var sinkEnt in _deviceLink.GetLinkedSinks((ownerUid, srcComp), portId))
                {
                    if (!Exists(sinkEnt))
                        continue;

                    foreach (var link in _deviceLink.GetLinks((ownerUid, srcComp), sinkEnt))
                    {
                        if (link.Source == portId)
                            linked.Add($"{MetaData(sinkEnt).EntityName}:{link.Sink}");
                    }
                }

                var portName = _proto.TryIndex<SourcePortPrototype>(portId, out var p) ? Loc.GetString(p.Name) : portId.Id;
                output.Add(new MirroredDevicePortInfo(ownerNet, ownerName, portId.Id, portName, isSource: true, isHostPort: isHost, linked));
            }
        }

        if (_sinkQuery.TryComp(ownerUid, out var sinkComp))
        {
            foreach (var portId in _deviceLink.GetSinkPortIds((ownerUid, sinkComp)))
            {
                var linked = new List<string>();
                foreach (var srcEnt in sinkComp.LinkedSources)
                {
                    if (Exists(srcEnt))
                        linked.Add(MetaData(srcEnt).EntityName);
                }

                var portName = _proto.TryIndex<SinkPortPrototype>(portId, out var p) ? Loc.GetString(p.Name) : portId.Id;
                output.Add(new MirroredDevicePortInfo(ownerNet, ownerName, portId.Id, portName, isSource: false, isHostPort: isHost, linked));
            }
        }
    }

    /// <summary>
    /// Invokes a mirrored DeviceLink port strictly through DeviceLink connectivity.
    /// </summary>
    public bool TryInvokeDeviceLinkPort(
        EntityUid compUid,
        string portId,
        bool isHostPort,
        string command,
        string argument,
        out string error,
        EntityUid? actor = null,
        ComputerPacket? packet = null,
        OxydComputerComponent? comp = null)
    {
        error = string.Empty;
        if (!_computerQuery.Resolve(compUid, ref comp, false))
        {
            error = "Invalid computer entity.";
            return false;
        }

        if (!comp.AllowDeviceLink || comp.DeviceLinkWireCut)
        {
            error = "DeviceLink bus disabled or wire severed.";
            return false;
        }

        if (comp.LockedDeviceLinkPorts.Contains(portId))
        {
            error = $"Port '{portId}' is locked by motherboard security.";
            return false;
        }

        var srcPortProto = new ProtoId<SourcePortPrototype>(portId);
        var sinkPortProto = new ProtoId<SinkPortPrototype>(portId);
        var senderGuid = _guid.GetGuid(compUid);
        var payload = new ComputerDeviceLinkPayload(command, argument, senderGuid);

        if (isHostPort)
        {
            if (comp.HostMachine is not { } hostUid || !Exists(hostUid))
            {
                error = "Computer has no represented host device.";
                return false;
            }

            // If target port is a source port on the host, invoke external DeviceLink subscribers on that source port.
            if (_sourceQuery.TryComp(hostUid, out var hostSrc) && _deviceLink.GetSourcePorts((hostUid, hostSrc)).Contains(srcPortProto))
            {
                if (packet != null)
                {
                    _deviceLink.InvokeDirect(hostUid, srcPortProto, packet);
                    LogDeviceLinkPacketTraffic(compUid, hostUid, portId, isOutgoing: true, packet.PacketType, packet, null);
                }
                else
                {
                    _deviceLink.InvokeDirect(hostUid, srcPortProto, payload);
                    if (string.IsNullOrEmpty(command))
                        _deviceLink.SendSignal(hostUid, srcPortProto, true);
                    LogDeviceLinkPacketTraffic(compUid, hostUid, portId, isOutgoing: true, command, null, payload);
                }
                return true;
            }

            // Verify there is an active DeviceLink connection from ComputerCommandSender -> host sink port!
            if (_sinkQuery.TryComp(hostUid, out var hostSink) && Array.Exists(_deviceLink.GetSinkPortIds((hostUid, hostSink)), p => p == sinkPortProto))
            {
                if (!_sourceQuery.TryComp(compUid, out var compSrc) ||
                    !_deviceLink.GetLinks((compUid, compSrc), hostUid).Any(pair => pair.Source == PortComputerCommandSender && pair.Sink == sinkPortProto))
                {
                    error = $"Computer is not DeviceLinked to host sink port '{portId}'.";
                    return false;
                }

                if (packet != null)
                {
                    if (actor != null)
                        packet.SetReference("Actor", actor.Value);
                    LogDeviceLinkPacketTraffic(compUid, hostUid, portId, isOutgoing: true, packet.PacketType, packet, null);
                    _deviceLink.InvokeDirect(compUid, hostUid, sinkPortProto, packet);
                }
                else
                {
                    LogDeviceLinkPacketTraffic(compUid, hostUid, portId, isOutgoing: true, command, null, payload);
                    _deviceLink.InvokeDirect(compUid, hostUid, sinkPortProto, payload);
                }
                return true;
            }

            error = $"Host device does not expose port '{portId}'.";
            return false;
        }

        // Port on the computer entity itself.
        if (_sourceQuery.TryComp(compUid, out var ownSrc) && _deviceLink.GetSourcePorts((compUid, ownSrc)).Contains(srcPortProto))
        {
            if (packet != null)
            {
                LogDeviceLinkPacketTraffic(compUid, compUid, portId, isOutgoing: true, packet.PacketType, packet, null);
                _deviceLink.InvokePort((compUid, ownSrc), srcPortProto, ref packet);
            }
            else if (!string.IsNullOrEmpty(command))
            {
                LogDeviceLinkPacketTraffic(compUid, compUid, portId, isOutgoing: true, command, null, payload);
                _deviceLink.InvokePort((compUid, ownSrc), srcPortProto, ref payload);
            }
            else
            {
                LogDeviceLinkPacketTraffic(compUid, compUid, portId, isOutgoing: true, "signal", null, payload);
                _deviceLink.SendSignal((compUid, ownSrc), srcPortProto, true);
            }

            return true;
        }

        if (_sinkQuery.TryComp(compUid, out var ownSink) && Array.Exists(_deviceLink.GetSinkPortIds((compUid, ownSink)), p => p == sinkPortProto))
        {
            if (packet != null)
            {
                LogDeviceLinkPacketTraffic(compUid, compUid, portId, isOutgoing: true, packet.PacketType, packet, null);
                var sigPkt = new SignalReceivedEvent<ComputerPacket>(sinkPortProto, packet, actor);
                RaiseLocalEvent(compUid, ref sigPkt);
            }
            else
            {
                LogDeviceLinkPacketTraffic(compUid, compUid, portId, isOutgoing: true, command, null, payload);
                var sigPayload = new SignalReceivedEvent<ComputerDeviceLinkPayload>(sinkPortProto, payload, actor);
                RaiseLocalEvent(compUid, ref sigPayload);
            }

            return true;
        }

        error = $"Computer does not expose port '{portId}'.";
        return false;
    }

    public void OnRemoteControlCut(EntityUid compUid, OxydComputerComponent? comp = null)
    {
        if (!_computerQuery.Resolve(compUid, ref comp, false))
            return;

        if (comp.RemoteControlWireCut)
            _remoteScreen.DetachViewer(EntityUid.Invalid, targetComputerUid: compUid);

        UpdateComputerDisplayUi(compUid, comp);
    }

    public bool SendPacketToHost(
        EntityUid compUid,
        string hostSinkPortId,
        ComputerPacket packet,
        EntityUid? actor = null)
    {
        return TryInvokeDeviceLinkPort(
            compUid,
            hostSinkPortId,
            isHostPort: true,
            command: packet.PacketType,
            argument: string.Empty,
            out _,
            actor: actor,
            packet: packet);
    }

    /// <summary>
    /// Resolves an ID card currently inserted into this computer's host PDA, host ID slot,
    /// or modular computer ID slot hardware.
    /// </summary>
    public bool TryGetInsertedIdCard(EntityUid compUid, out EntityUid idCardUid)
    {
        idCardUid = EntityUid.Invalid;
        var cards = GetInsertedIdCards(compUid);
        if (cards.Count == 0)
            return false;

        idCardUid = cards[0];
        return true;
    }

    public List<EntityUid> GetInsertedIdCards(EntityUid compUid)
    {
        var results = new List<EntityUid>();
        if (!_computerQuery.TryComp(compUid, out var comp))
            return results;

        if (comp.HostMachine is { } hostUid && Exists(hostUid))
        {
            if (TryComp<PdaComponent>(hostUid, out var pda))
            {
                var pdaCard = pda.IdSlot.Item ?? pda.ContainedId;
                if (pdaCard is { Valid: true } pId && Exists(pId) && HasComp<IdCardComponent>(pId) && !results.Contains(pId))
                    results.Add(pId);
            }

            if (TryComp<ItemSlotsComponent>(hostUid, out var hostSlots))
            {
                foreach (var slot in hostSlots.Slots.Values)
                {
                    if (slot.Item is { Valid: true } sId && Exists(sId) && HasComp<IdCardComponent>(sId) && !results.Contains(sId))
                        results.Add(sId);
                }
            }
        }

        // ID cards retained by card slot hardware modules (deterministic slot order: SlotIdModule, SlotIdModuleSecondary, then any others).
        var orderedParts = new List<EntityUid>();
        if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotIdModule, out var idMod1) && idMod1.Item is { Valid: true } p1)
            orderedParts.Add(p1);
        if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotIdModuleSecondary, out var idMod2) && idMod2.Item is { Valid: true } p2)
            orderedParts.Add(p2);
        foreach (var extra in GetInstalledHardwareEntities(compUid).OrderBy(e => e.Id))
        {
            if (!orderedParts.Contains(extra))
                orderedParts.Add(extra);
        }

        foreach (var part in orderedParts)
        {
            if (TryComp<ComputerIdCardSlotModuleComponent>(part, out var reader) &&
                reader.Mode == ComputerCardReaderMode.Slot &&
                _itemSlots.TryGetSlot(part, reader.CardSlotId, out var cardSlot) &&
                cardSlot.Item is { Valid: true } slotCard &&
                Exists(slotCard) &&
                HasComp<IdCardComponent>(slotCard) &&
                !results.Contains(slotCard))
            {
                results.Add(slotCard);
            }
        }

        return results;
    }

    public void LogDeviceLinkPacketTraffic(
        EntityUid sourceCompUid,
        EntityUid targetUid,
        string portId,
        bool isOutgoing,
        string command,
        ComputerPacket? packet = null,
        ComputerDeviceLinkPayload? payload = null)
    {
        if (!Exists(sourceCompUid))
            return;

        var effectiveCmd = !string.IsNullOrWhiteSpace(packet?.PacketType)
            ? packet.PacketType
            : !string.IsNullOrWhiteSpace(command)
                ? command
                : "signal";

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SinkPort"] = portId,
            ["Command"] = effectiveCmd
        };
        var editableEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var argDisplayParts = new List<string>();

        if (packet != null)
        {
            foreach (var (k, v) in packet.Data)
            {
                if (k.StartsWith("__", StringComparison.Ordinal) || k.Equals("tampered", StringComparison.OrdinalIgnoreCase))
                    continue;

                var strVal = v?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(strVal))
                    continue;

                parameters[k] = strVal;
                editableEntries[k] = strVal;

                var clipped = strVal.Length > 72 ? $"{strVal[..69]}..." : strVal;
                argDisplayParts.Add($"{k}={clipped}");
            }
        }
        else if (payload != null && !string.IsNullOrWhiteSpace(payload.Argument))
        {
            parameters["Argument"] = payload.Argument;
            editableEntries["Argument"] = payload.Argument;
            argDisplayParts.Add($"Argument={payload.Argument}");
        }

        var argsText = argDisplayParts.Count > 0 ? $" [{string.Join(", ", argDisplayParts)}]" : string.Empty;
        var summary = isOutgoing
            ? $"Packet sent to {portId} ({effectiveCmd}){argsText}"
            : $"Packet received on {portId} ({effectiveCmd}){argsText}";

        var isStatusPolling =
            effectiveCmd.Equals("status", StringComparison.OrdinalIgnoreCase) ||
            effectiveCmd.Equals("status_request", StringComparison.OrdinalIgnoreCase) ||
            effectiveCmd.EndsWith("Status", StringComparison.OrdinalIgnoreCase);

        // Persist packet traffic in the computer's /sys/system.log so players can inspect device packet flow.
        if (_fileSystem.TryFindFileSystem(sourceCompUid, out var driveUid, out var fs) && driveUid != null)
        {
            var shouldWriteSysLog = !isStatusPolling || !isOutgoing;
            if (shouldWriteSysLog)
            {
                var tag = isOutgoing ? "PKT-OUT" : "PKT-IN";
                var skipDuplicateStatus = isStatusPolling &&
                                          fs.LogEntries is { Count: > 0 } entries &&
                                          entries.Last().Contains(summary, StringComparison.Ordinal);
                if (!skipDuplicateStatus)
                    _fileSystem.LogSystemActivity(driveUid.Value, tag, summary, comp: fs);
            }
        }

        var compGuid = _guid.GetGuid(sourceCompUid);
        var targetGuid = Exists(targetUid) ? _guid.GetGuid(targetUid) : "HOST";
        var rawKv = string.Join(";", parameters.Select(kv => $"{kv.Key}={kv.Value}"));

        var netPkt = new NetworkDataPacket
        {
            SourceAddress = isOutgoing ? compGuid : targetGuid,
            TargetAddress = isOutgoing ? targetGuid : compGuid,
            Protocol = isOutgoing ? $"DEVLINK:{portId}" : $"DEVLINK-RX:{portId}",
            PayloadSummary = summary,
            RawPayload = rawKv,
            ByteSize = Math.Max(64, summary.Length * 2 + 32),
            Timestamp = _timing.CurTime,
            IsPlayerRelevant = !isStatusPolling,
            IsEditable = isOutgoing && !isStatusPolling,
            Parameters = parameters,
            EditableEntries = editableEntries
        };

        _bandwidth.StorePacket(sourceCompUid, netPkt);

        if (_computerQuery.TryComp(sourceCompUid, out var comp) &&
            _statsQuery.TryComp(sourceCompUid, out var stats) &&
            stats.HasNetworkCard &&
            !comp.NetworkWireCut &&
            comp.IsPowered)
        {
            _bandwidth.LogPacketToRouters(netPkt, senderUid: sourceCompUid);
        }
    }

    private void RelayDeviceLinkToPrograms(
        EntityUid compUid,
        EntityUid sourceEntity,
        string port,
        EntityUid? trigger,
        ComputerDeviceLinkPayload? payload,
        ComputerPacket? packet,
        bool? boolSignal)
    {
        if (!_computerQuery.TryComp(compUid, out var comp) || !comp.IsBooted)
            return;

        if (!comp.AllowDeviceLink || comp.DeviceLinkWireCut)
            return;

        if (packet != null || payload != null)
        {
            var sender = trigger is { Valid: true } trig ? trig : sourceEntity;
            LogDeviceLinkPacketTraffic(compUid, sender, port, isOutgoing: false, packet?.PacketType ?? payload?.Command ?? "signal", packet, payload);
        }

        if (packet != null)
            RaiseLocalEvent(new ComputerSinkPacketReceivedEvent(compUid, sourceEntity, port, packet));

        var ev = new ProgramDeviceLinkReceivedEvent(compUid, sourceEntity, port, trigger, payload, packet, boolSignal);
        foreach (var progUid in comp.RunningPrograms.ToList())
        {
            if (Exists(progUid))
                RaiseLocalEvent(progUid, ref ev);
        }
    }

    public bool ExecuteComputerCommand(EntityUid compUid, string commandText, out string output, EntityUid? actor = null)
    {
        output = string.Empty;
        if (!_computerQuery.TryComp(compUid, out var comp))
            return false;

        var parts = commandText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        var cmd = parts[0].ToLowerInvariant();
        if (cmd is "ports" or "who-linked" or "devlink" or "port")
        {
            if (parts.Length == 1 || cmd == "ports" || (parts.Length > 1 && parts[1].ToLowerInvariant() is "list" or "scan"))
            {
                var ports = GetMirroredPorts(compUid, comp);
                if (ports.Count == 0)
                {
                    output = "No DeviceLink ports exposed on computer or host device.";
                    return true;
                }

                var lines = new List<string> { $"DeviceLink Ports ({ports.Count}):" };
                foreach (var p in ports)
                {
                    var scope = p.IsHostPort ? "HOST" : "COMP";
                    var dir = p.IsSource ? "OUT" : "IN ";
                    var links = p.LinkedTargets.Count > 0 ? string.Join(", ", p.LinkedTargets) : "(unlinked)";
                    lines.Add($"  [{scope} {dir}] {p.PortId} - {p.PortName} -> {links}");
                }
                output = string.Join("\n", lines);
                return true;
            }

            var sub = parts[1].ToLowerInvariant();
            if (sub is "trigger" or "send" && parts.Length >= 3)
            {
                var portName = parts[2];
                var portCmd = parts.Length >= 4 ? parts[3] : string.Empty;
                var portArg = parts.Length >= 5 ? string.Join(" ", parts.Skip(4)) : string.Empty;

                var mirrored = GetMirroredPorts(compUid, comp);
                var match = mirrored.FirstOrDefault(p => p.PortId.Equals(portName, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    output = $"devlink: unknown port '{portName}'.";
                    return true;
                }

                if (TryInvokeDeviceLinkPort(compUid, match.PortId, match.IsHostPort, portCmd, portArg, out var err, actor))
                {
                    output = $"Invoked {(match.IsHostPort ? "host" : "computer")} port '{match.PortId}'.";
                }
                else
                {
                    output = $"devlink error: {err}";
                }
                return true;
            }

            if (sub == "rebind")
            {
                if (comp.HostMachine is { } hm && Exists(hm))
                {
                    BindHostDeviceLinks(hm, compUid);
                    output = "Rebound internal DeviceLink bridge between computer and host device.";
                }
                else
                {
                    output = "Computer has no host device to bind.";
                }
                return true;
            }

            output = "Usage: devlink [list | trigger <port> | send <port> <cmd> [arg] | rebind]";
            return true;
        }

        if (cmd is "apc" or "atmos" or "lathe")
        {
            var sinkPort = cmd switch
            {
                "apc" => "ApcCommandSink",
                "atmos" => "AirAlarmCommandSink",
                _ => "AutolatheCommandSink"
            };
            var action = parts.Length > 1 ? parts[1] : "status";
            var arg = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : string.Empty;

            var pkt = new ComputerPacket(action);
            pkt.Set("Argument", arg);
            if (TryInvokeDeviceLinkPort(compUid, sinkPort, isHostPort: true, action, arg, out var err, actor, pkt))
                output = $"Dispatched DeviceLink '{action}' -> {sinkPort}.";
            else
                output = $"DeviceLink error ({sinkPort}): {err}";
            return true;
        }

        var ev = new ExecuteComputerCommandEvent(compUid, commandText);
        RaiseLocalEvent(compUid, ref ev);
        if (ev.Handled)
        {
            output = ev.Output;
            return true;
        }

        return false;
    }
}
