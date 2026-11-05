using System.Linq;
using Content.Server._Oxyd.Computers.Network;
using Content.Server.Atmos.Monitor.Components;
using Content.Server.PDA;
using Content.Server.Power.Components;
using Content.Shared._Oxyd.Computers.Cartridges;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.ScreenSharing;
using Content.Shared._Oxyd.Computers.Servers;
using Content.Shared.APC;
using Content.Shared.CartridgeLoader;
using Content.Server.Construction.Components;
using Content.Shared.PDA;
using Content.Shared.Strip;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Shared.Player;

namespace Content.Server._Oxyd.Computers.ScreenSharing;

/// <summary>
/// Server-side orchestration for universal device screen-sharing and Picture-in-Picture (PiP)
/// across PDAs, Static Computers, APCs, Atmospheric Alarms, and other BUI devices.
/// </summary>
public sealed partial class ServerRemoteScreenSystem : SharedRemoteScreenSystem
{
    [Dependency] private SharedDeviceGuidSystem _guidSystem = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private PvsOverrideSystem _pvsOverride = default!;
    [Dependency] private ISharedPlayerManager _playerManager = default!;
    [Dependency] private PdaSystem _pdaSystem = default!;
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;

    private sealed class TerminalViewerSession
    {
        public EntityUid ViewerActor;
        public readonly HashSet<EntityUid> ActiveTargets = new();
        public readonly HashSet<EntityUid> ActivePvsEntities = new();
    }

    private readonly Dictionary<EntityUid, TerminalViewerSession> _sessionsByTerminal = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ApcComponent, MapInitEvent>(OnApcMapInit);
        SubscribeLocalEvent<AirAlarmComponent, MapInitEvent>(OnAirAlarmMapInit);
        SubscribeLocalEvent<ComputerComponent, MapInitEvent>(OnComputerMapInit);
        SubscribeLocalEvent<PdaComponent, MapInitEvent>(OnPdaMapInit);
        SubscribeLocalEvent<TerminalCartridgeComponent, ComponentShutdown>(OnTerminalShutdown);
    }

    private void OnApcMapInit(EntityUid uid, ApcComponent comp, MapInitEvent args)
    {
        EnsureDeviceProvisioned(uid);
    }

    private void OnAirAlarmMapInit(EntityUid uid, AirAlarmComponent comp, MapInitEvent args)
    {
        EnsureDeviceProvisioned(uid);
    }

    private void OnComputerMapInit(EntityUid uid, ComputerComponent comp, MapInitEvent args)
    {
        EnsureDeviceProvisioned(uid);
    }

    private void OnPdaMapInit(EntityUid uid, PdaComponent comp, MapInitEvent args)
    {
        EnsureDeviceProvisioned(uid);
    }

    private void OnTerminalShutdown(EntityUid uid, TerminalCartridgeComponent comp, ComponentShutdown args)
    {
        DetachViewer(uid);
    }

    /// <summary>
    /// Ensures that an APC, Air Alarm, Static Computer, or PDA has a network GUID, hardware stats,
    /// bandwidth metrics, and an embedded filesystem so it can be discovered, SSHed into, and screen-shared.
    /// </summary>
    public void EnsureDeviceProvisioned(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid))
            return;

        EnsureComp<DeviceGuidComponent>(uid);

        if (HasComp<PdaComponent>(uid))
        {
            _pdaSystem.EnsurePdaInitialized(uid);
            return;
        }

        var stats = EnsureComp<ComputerStatsComponent>(uid);
        var bw = EnsureComp<NetworkBandwidthComponent>(uid);
        var fs = EnsureComp<FileSystemComponent>(uid);

        if (HasComp<ApcComponent>(uid))
        {
            stats.CpuFrequencyMhz = 25;
            stats.RamMb = 16;
            bw.MaxBps = 64000;
            if (fs.Files.Count == 0)
            {
                WriteDefaultEmbeddedFiles(fs,
                    "NTOS-EMBEDDED-PWR-1.2\nSUBSYSTEM: AREA POWER CONTROLLER (APC)\nDISPLAY: BUI-280x260",
                    "APC power distribution telemetry nominal. Remote breaker control enabled via SSH/GUI.");
            }
        }
        else if (HasComp<AirAlarmComponent>(uid) || HasComp<AtmosMonitorComponent>(uid))
        {
            stats.CpuFrequencyMhz = 40;
            stats.RamMb = 32;
            bw.MaxBps = 64000;
            if (fs.Files.Count == 0)
            {
                WriteDefaultEmbeddedFiles(fs,
                    "NTOS-EMBEDDED-ATMOS-2.0\nSUBSYSTEM: ATMOSPHERIC ALARM CONTROLLER\nDISPLAY: BUI-500x420",
                    "Atmospheric sensor/vent/scrubber control matrix online.");
            }
        }
        else if (HasComp<ComputerComponent>(uid))
        {
            stats.CpuFrequencyMhz = 200;
            stats.RamMb = 256;
            bw.MaxBps = 500000;
            if (fs.Files.Count == 0)
            {
                WriteDefaultEmbeddedFiles(fs,
                    $"NTOS-WORKSTATION-4.2\nSUBSYSTEM: STATIC CONSOLE ({MetaData(uid).EntityName})",
                    "Station console terminal active.");
            }
        }
    }

    private static void WriteDefaultEmbeddedFiles(FileSystemComponent fs, string osContent, string logContent)
    {
        var osFile = new TextVirtualFile
        {
            Name = "os",
            Extension = "sys",
            Directory = "/sys/",
            Size = osContent.Length,
            TextContent = osContent,
            IsReadOnly = true
        };
        fs.Files[osFile.FullPath] = osFile;

        var statusFile = new TextVirtualFile
        {
            Name = "status",
            Extension = "txt",
            Directory = "/notes/",
            Size = logContent.Length,
            TextContent = logContent,
            IsReadOnly = false
        };
        fs.Files[statusFile.FullPath] = statusFile;
    }

    /// <summary>
    /// Returns a short category label for a networked device (used by terminal `scan` and `screen status`).
    /// </summary>
    public string GetDeviceCategory(EntityUid uid)
    {
        if (HasComp<ApcComponent>(uid))
            return "APC";
        if (HasComp<AirAlarmComponent>(uid))
            return "AIR-ALARM";
        if (HasComp<AtmosMonitorComponent>(uid))
            return "ATMOS-MON";
        if (HasComp<PdaComponent>(uid))
            return "PDA";
        if (HasComp<RouterServerComponent>(uid))
            return "ROUTER";
        if (HasComp<DataServerComponent>(uid))
            return "SERVER";
        if (HasComp<ComputerComponent>(uid))
            return "COMPUTER";
        return "DEVICE";
    }

    /// <summary>
    /// Returns true if the target entity has a shareable graphical interface (PDA, CartridgeLoader, or BUI).
    /// </summary>
    public bool CanShareScreen(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid))
            return false;

        if (HasComp<PdaComponent>(uid) || HasComp<CartridgeLoaderComponent>(uid))
            return true;

        return TryGetPrimaryUiKey(uid, out _, out _);
    }

    /// <summary>
    /// Resolves the primary BoundUserInterface key and metadata for a device.
    /// </summary>
    public bool TryGetPrimaryUiKey(EntityUid uid, out Enum? uiKey, out InterfaceData? interfaceData)
    {
        uiKey = null;
        interfaceData = null;

        if (!TryComp<UserInterfaceComponent>(uid, out var uiComp) || uiComp.Interfaces.Count == 0)
            return false;

        if (TryComp<ActivatableUIComponent>(uid, out var actUi) &&
            actUi.Key != null &&
            uiComp.Interfaces.TryGetValue(actUi.Key, out var actData))
        {
            uiKey = actUi.Key;
            interfaceData = actData;
            return true;
        }

        if (uiComp.Interfaces.TryGetValue(ApcUiKey.Key, out var apcData))
        {
            uiKey = ApcUiKey.Key;
            interfaceData = apcData;
            return true;
        }

        foreach (var (key, data) in uiComp.Interfaces)
        {
            if (key is StrippableUiKey)
                continue;

            uiKey = key;
            interfaceData = data;
            return true;
        }

        var first = uiComp.Interfaces.First();
        uiKey = first.Key;
        interfaceData = first.Value;
        return true;
    }

    /// <summary>
    /// Synchronizes the viewer's PVS overrides, remote interaction authorizations, and open remote BUIs
    /// for the entire chain of screens visible from <paramref name="terminalUid"/>.
    /// </summary>
    public void SyncViewerChain(
        EntityUid terminalUid,
        EntityUid? viewerActor,
        HashSet<EntityUid> newTargets,
        HashSet<EntityUid> newPvsEntities)
    {
        if (viewerActor is not { } actor || !Exists(actor) || TerminatingOrDeleted(actor))
            return;

        if (!_sessionsByTerminal.TryGetValue(terminalUid, out var session))
        {
            session = new TerminalViewerSession { ViewerActor = actor };
            _sessionsByTerminal[terminalUid] = session;
        }
        else if (session.ViewerActor != actor)
        {
            DetachViewer(terminalUid);
            session = new TerminalViewerSession { ViewerActor = actor };
            _sessionsByTerminal[terminalUid] = session;
        }

        var hasPlayerSession = _playerManager.TryGetSessionByEntity(actor, out var playerSession);

        // 1. Update PVS overrides for entities in the screen chain (including PDA cartridge programs)
        if (hasPlayerSession && playerSession != null)
        {
            foreach (var oldPvs in session.ActivePvsEntities.ToList())
            {
                if (!newPvsEntities.Contains(oldPvs))
                {
                    session.ActivePvsEntities.Remove(oldPvs);
                    if (!IsPvsNeededByActor(actor, oldPvs) && !TerminatingOrDeleted(oldPvs))
                        _pvsOverride.RemoveSessionOverride(oldPvs, playerSession);
                }
            }

            foreach (var addedPvs in newPvsEntities)
            {
                if (!TerminatingOrDeleted(addedPvs) && session.ActivePvsEntities.Add(addedPvs))
                {
                    _pvsOverride.AddSessionOverride(addedPvs, playerSession);
                }
            }
        }

        // 2. Update active target devices & open their BUIs if needed
        foreach (var oldTarget in session.ActiveTargets.ToList())
        {
            if (!newTargets.Contains(oldTarget))
            {
                session.ActiveTargets.Remove(oldTarget);
            }
        }

        var newlyAddedTargets = new List<EntityUid>();
        foreach (var target in newTargets)
        {
            if (!TerminatingOrDeleted(target) && session.ActiveTargets.Add(target))
            {
                newlyAddedTargets.Add(target);
            }
        }

        // Rebuild the actor's AuthorizedTargets set across all active terminals for this actor
        RebuildActorAuthorizedTargets(actor);

        // Now that AuthorizedTargets is updated, open any newly added non-PDA device BUIs
        foreach (var target in newlyAddedTargets)
        {
            if (HasComp<PdaComponent>(target))
                continue;

            if (TryGetPrimaryUiKey(target, out var uiKey, out _) && uiKey != null)
            {
                _uiSystem.OpenUi(target, uiKey, actor);
                var afterOpenEv = new AfterActivatableUIOpenEvent(actor, target);
                RaiseLocalEvent(target, afterOpenEv);
            }
        }
    }

    public void DetachViewer(EntityUid terminalUid, EntityUid? explicitActor = null)
    {
        if (!_sessionsByTerminal.Remove(terminalUid, out var session))
            return;

        var actor = explicitActor ?? session.ViewerActor;
        if (!Exists(actor) || TerminatingOrDeleted(actor))
            return;

        if (_playerManager.TryGetSessionByEntity(actor, out var playerSession) && playerSession != null)
        {
            foreach (var pvsEnt in session.ActivePvsEntities)
            {
                if (!IsPvsNeededByActor(actor, pvsEnt) && !TerminatingOrDeleted(pvsEnt))
                    _pvsOverride.RemoveSessionOverride(pvsEnt, playerSession);
            }
        }

        RebuildActorAuthorizedTargets(actor);
    }

    public void DetachAllForHostDevice(EntityUid actor, EntityUid hostDeviceUid)
    {
        var toDetach = new List<EntityUid>();
        foreach (var (termUid, session) in _sessionsByTerminal)
        {
            if (session.ViewerActor == actor && Transform(termUid).ParentUid == hostDeviceUid)
                toDetach.Add(termUid);
        }

        foreach (var termUid in toDetach)
        {
            DetachViewer(termUid, actor);
        }
    }

    private bool IsPvsNeededByActor(EntityUid actor, EntityUid entity)
    {
        foreach (var session in _sessionsByTerminal.Values)
        {
            if (session.ViewerActor == actor && session.ActivePvsEntities.Contains(entity))
                return true;
        }
        return false;
    }

    private void RebuildActorAuthorizedTargets(EntityUid actor)
    {
        var allTargets = new HashSet<NetEntity>();
        foreach (var session in _sessionsByTerminal.Values)
        {
            if (session.ViewerActor != actor)
                continue;

            foreach (var target in session.ActiveTargets)
            {
                if (!TerminatingOrDeleted(target))
                    allTargets.Add(GetNetEntity(target));
            }
        }

        if (allTargets.Count == 0)
        {
            RemComp<ActiveRemoteScreenViewerComponent>(actor);
            return;
        }

        var viewerComp = EnsureComp<ActiveRemoteScreenViewerComponent>(actor);
        viewerComp.AuthorizedTargets = allTargets;
        Dirty(actor, viewerComp);
    }

    /// <summary>
    /// Recursively constructs a <see cref="RemoteScreenState"/> for <paramref name="targetUid"/>,
    /// detecting any cycle in <paramref name="visitedGuids"/> and emitting a PiP cycle reference
    /// (`IsCycleReference = true`) instead of recursing infinitely.
    /// </summary>
    public RemoteScreenState? BuildRemoteScreenState(
        EntityUid targetUid,
        HashSet<string> visitedGuids,
        HashSet<EntityUid>? chainTargets = null,
        HashSet<EntityUid>? chainPvsEntities = null,
        int maxDepth = 4)
    {
        if (!Exists(targetUid) || TerminatingOrDeleted(targetUid))
            return null;

        EnsureDeviceProvisioned(targetUid);

        chainTargets?.Add(targetUid);
        chainPvsEntities?.Add(targetUid);

        var targetGuid = _guidSystem.GetGuid(targetUid);
        var deviceName = MetaData(targetUid).EntityName;

        // Cycle detected (e.g., A -> A, A -> B -> A, or A -> B -> C -> A):
        // Emit a lightweight PiP cycle reference so the client renders the ancestor surface viaPipMirrorViewport!
        if (!visitedGuids.Add(targetGuid))
        {
            return new RemoteScreenState(
                GetNetEntity(targetUid),
                targetGuid,
                deviceName,
                isCycleReference: true);
        }

        // Case 1: PDA / Cartridge host device
        if (TryComp<PdaComponent>(targetUid, out var remPda))
        {
            if (remPda.LastState == null)
                _pdaSystem.UpdatePdaUi(targetUid, remPda);

            if (chainPvsEntities != null && TryComp<CartridgeLoaderComponent>(targetUid, out var loader))
            {
                foreach (var prog in _cartridgeLoader.GetAllPrograms(targetUid))
                {
                    if (!TerminatingOrDeleted(prog))
                        chainPvsEntities.Add(prog);
                }
                if (loader.ActiveProgram is { } activeProg && !TerminatingOrDeleted(activeProg))
                    chainPvsEntities.Add(activeProg);
            }

            if (remPda.LastState is { } lastState)
            {
                var remoteCartridgeState = lastState.CartridgeState;
                string? viewingGuid = null;

                // If the remote PDA is itself running a Terminal with an attached screen, resolve the next hop!
                if (remoteCartridgeState is TerminalUiState remTerm &&
                    remTerm.IsScreenAttached &&
                    !string.IsNullOrEmpty(remTerm.SshHost))
                {
                    viewingGuid = remTerm.SshHost;
                    RemoteScreenState? nestedScreen = null;

                    if (visitedGuids.Count <= maxDepth &&
                        _guidSystem.TryGetEntityByGuid(remTerm.SshHost, out var nextTarget) &&
                        nextTarget.HasValue)
                    {
                        nestedScreen = BuildRemoteScreenState(
                            nextTarget.Value,
                            visitedGuids,
                            chainTargets,
                            chainPvsEntities,
                            maxDepth);
                    }

                    remoteCartridgeState = new TerminalUiState(
                        remTerm.Lines,
                        remTerm.Prompt,
                        remTerm.DeviceGuid,
                        remTerm.SshHost,
                        remTerm.IsPasswordPrompt,
                        remTerm.IsSshLocalMode,
                        isScreenAttached: nestedScreen != null,
                        remotePdaState: nestedScreen?.PdaState,
                        remoteScreen: nestedScreen);
                }

                var pdaState = new PdaUpdateState(
                    lastState.Programs,
                    lastState.ActiveUI,
                    lastState.FlashlightEnabled,
                    lastState.HasPen,
                    lastState.HasPai,
                    lastState.PdaOwnerInfo,
                    lastState.StationName,
                    lastState.HasUplink,
                    lastState.CanPlayMusic,
                    lastState.Address,
                    lastState.DeviceGuid,
                    lastState.CpuUsagePercent,
                    lastState.RamUsedMb,
                    lastState.RamTotalMb,
                    lastState.StorageUsedBytes,
                    lastState.StorageTotalBytes,
                    lastState.IsNetworkSaturated,
                    lastState.OpenPrograms,
                    remoteCartridgeState,
                    lastState.HasDisk,
                    lastState.DiskName,
                    lastState.HasCaptainAccess,
                    lastState.SshPassword,
                    lastState.CurrentView);

                return new RemoteScreenState(
                    GetNetEntity(targetUid),
                    targetGuid,
                    deviceName,
                    pdaState: pdaState,
                    viewingTargetGuid: viewingGuid);
            }
        }

        // Case 2: Generic BoundUserInterface device (APC, Air Alarm, Static Computer, etc.)
        if (TryGetPrimaryUiKey(targetUid, out var uiKey, out var iData) &&
            uiKey != null &&
            iData != null)
        {
            _uiSystem.TryGetUiState<BoundUserInterfaceState>(targetUid, uiKey, out var buiState);
            return new RemoteScreenState(
                GetNetEntity(targetUid),
                targetGuid,
                deviceName,
                uiKey: uiKey,
                buiClientType: iData.ClientType,
                buiState: buiState);
        }

        return null;
    }
}
