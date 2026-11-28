using System.Linq;
using Content.Server._Oxyd.Computers.Cameras;
using Content.Server._Oxyd.Computers.Programs;
using Content.Server._Oxyd.Computers.ScreenSharing;
using Content.Server._Oxyd.Computers.Servers;
using Content.Server.Atmos.Monitor.Components;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.Cameras;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Packets;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared._Oxyd.Computers.Servers;
using Content.Shared.Access.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Components;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.DeviceLinking.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Lathe;
using Content.Shared.Lock;
using Content.Shared.Power;
using Content.Shared.SurveillanceCamera.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.Network;

/// <summary>
/// Marker component identifying entities spawned solely as the #if DEBUG global network fallback.
/// </summary>
[RegisterComponent]
public sealed partial class DebugFallbackNetworkEntityComponent : Component
{
    [DataField]
    public bool IsServer;
}

public sealed partial class NetworkRouterSystem : EntitySystem
{
    [Dependency] private SharedDeviceGuidSystem _guid = default!;
    [Dependency] private NetworkBandwidthSystem _bandwidth = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DeviceLinkSystem _deviceLink = default!;
    [Dependency] private OxydComputerSystem _computer = default!;
    [Dependency] private OxydCameraSystem _cameras = default!;
    [Dependency] private IPrototypeManager _proto = default!;
#if DEBUG
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private MetaDataSystem _meta = default!;
#endif

    [Dependency] private EntityQuery<OxydComputerComponent> _computerQuery = default!;
    [Dependency] private EntityQuery<ComputerStatsComponent> _statsQuery = default!;
    [Dependency] private EntityQuery<EmbeddedComputerHostComponent> _hostQuery = default!;
    [Dependency] private EntityQuery<ProgramComponent> _programQuery = default!;
    [Dependency] private EntityQuery<NetworkRouterComponent> _routerQuery = default!;
    [Dependency] private EntityQuery<DebugFallbackNetworkEntityComponent> _debugFallbackQuery = default!;
    [Dependency] private EntityQuery<DeviceLinkSinkComponent> _sinkQuery = default!;

    private static readonly ProtoId<SourcePortPrototype> PortComputerCommandSender = "ComputerCommandSender";

#if DEBUG
    private static readonly EntProtoId DebugRouterPrototype = "StationNetworkRouter";

    public const string DebugServerDisplayName = "DEV DEBUG SERVER";
#endif

    private const float RouterLookupRange = 512f;
    private const int MaxDnsEntries = 64;
    private const int MaxBlacklistEntries = 128;
    private const int MaxSnifferPayloadChars = 8192;
    private const int MaxSnifferEntryCount = 64;

#if DEBUG
    private EntityUid _debugRouter;
    private bool _ensuringDebugRouter;
    private bool _flushingEntities;
#endif

    public override void Initialize()
    {
        base.Initialize();

#if DEBUG
        EntityManager.BeforeEntityFlush += OnBeforeEntityFlush;
        EntityManager.AfterEntityFlush += OnAfterEntityFlush;
#endif

        Subs.BuiEvents<RouterProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<RouterActionMessage>(OnRouterUiMessage);
        });

        Subs.BuiEvents<PacketSnifferProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<PacketSnifferActionMessage>(OnSnifferUiMessage);
        });

        Subs.BuiEvents<NetNavigatorProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<NetNavigatorActionMessage>(OnNetNavigatorUiMessage);
        });
    }

#if DEBUG
    public override void Shutdown()
    {
        base.Shutdown();
        EntityManager.BeforeEntityFlush -= OnBeforeEntityFlush;
        EntityManager.AfterEntityFlush -= OnAfterEntityFlush;
    }
#endif

    [SubscribeLocalEvent]
    private void OnComputerSinkPacket(ComputerSinkPacketReceivedEvent args)
    {
        HandleSinkComputerPacket(args.ReceiverUid, args.Packet);
    }

    private void HandleSinkComputerPacket(EntityUid receiverUid, ComputerPacket pkt)
    {
        if (!pkt.PacketType.Equals("vfs_packet_write", StringComparison.OrdinalIgnoreCase))
            return;

        var filePath = pkt.GetString("file_path");
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        var destCompUid = UnwrapComputerEntity(receiverUid);
        var entries = new Dictionary<string, string>();
        var entryCount = pkt.GetInt("entry_count");
        for (var i = 0; i < entryCount; i++)
        {
            var key = pkt.GetString($"ek_{i}");
            if (string.IsNullOrEmpty(key))
                continue;
            entries[key] = pkt.GetString($"ev_{i}") ?? string.Empty;
        }

        var parameters = new Dictionary<string, string>();
        var paramCount = pkt.GetInt("param_count");
        for (var i = 0; i < paramCount; i++)
        {
            var key = pkt.GetString($"pk_{i}");
            if (string.IsNullOrEmpty(key))
                continue;
            parameters[key] = pkt.GetString($"pv_{i}") ?? string.Empty;
        }

        var synthPkt = new NetworkDataPacket
        {
            PacketId = pkt.GetString("packet_id") ?? "SINKPKT",
            Protocol = pkt.GetString("protocol") ?? "SINK_WRITE",
            FilePath = filePath,
            EditableEntries = entries,
            Parameters = parameters
        };

        if (ApplyModifiedPacketToComputerVfs(destCompUid, synthPkt, pkt.InternalMarkTampered || pkt.GetBool("tampered"), out _))
            RefreshAllPacketSniffers();
    }

#if DEBUG
    private void OnBeforeEntityFlush()
    {
        _flushingEntities = true;
        _debugRouter = EntityUid.Invalid;
        _ensuringDebugRouter = false;
    }

    private void OnAfterEntityFlush()
    {
        _flushingEntities = false;
    }
#endif

    [SubscribeLocalEvent]
    private void OnRouterPacketRecorded(Entity<NetworkRouterComponent> ent, ref RouterPacketRecordedEvent args)
    {
        RefreshAllPacketSniffers();
    }

    [SubscribeLocalEvent]
    private void OnRouterMapInit(Entity<NetworkRouterComponent> ent, ref MapInitEvent args)
    {
        EnsureDeviceGuid(ent.Owner);
        RefreshDnsDirectory(ent);
    }

    [SubscribeLocalEvent]
    private void OnRouterPowerChanged(Entity<NetworkRouterComponent> ent, ref PowerChangedEvent args)
    {
#if DEBUG
        if (_flushingEntities)
            return;
#endif
        if (TerminatingOrDeleted(ent.Owner))
            return;

        RefreshAllRouterProgramUis();
    }

#if DEBUG
    [SubscribeLocalEvent]
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _debugRouter = EntityUid.Invalid;
        _ensuringDebugRouter = false;
    }

    /// <summary>
    /// Returns true if any non-debug DataServer entity exists and has an operational network interface.
    /// </summary>
    public bool HasOperationalRealCentralServer()
    {
        var query = EntityQueryEnumerator<DataServerComponent>();
        while (query.MoveNext(out var sUid, out _))
        {
            if (_debugFallbackQuery.HasComp(sUid) || TerminatingOrDeleted(sUid))
                continue;

            var sComp = UnwrapComputerEntity(sUid);
            if (IsRouterOperational(sUid) && HasActiveNetworkCard(sComp))
                return true;
        }

        return false;
    }

    private bool TryEnsureDebugGlobalRouter(out Entity<NetworkRouterComponent> router)
    {
        router = default;
        if (_ensuringDebugRouter || _flushingEntities)
            return false;

        _ensuringDebugRouter = true;
        try
        {
            if (!Exists(_debugRouter) || TerminatingOrDeleted(_debugRouter))
            {
                _debugRouter = Spawn(DebugRouterPrototype, MapCoordinates.Nullspace);
                _meta.SetEntityName(_debugRouter, DebugServerDisplayName);
                _power.SetNeedsPower(_debugRouter, false);
                var marker = EnsureComp<DebugFallbackNetworkEntityComponent>(_debugRouter);
                marker.IsServer = false;
            }

            var compSys = EntityManager.System<OxydComputerSystem>();
            if (TryComp<EmbeddedComputerHostComponent>(_debugRouter, out var host) &&
                compSys.EnsureComputerBoundToHost((_debugRouter, host), out var routerCompUid) &&
                _computerQuery.TryComp(routerCompUid, out var comp))
            {
                _meta.SetEntityName(routerCompUid, DebugServerDisplayName);
                var compMarker = EnsureComp<DebugFallbackNetworkEntityComponent>(routerCompUid);
                compMarker.IsServer = false;
                comp.IsPowered = true;
                Dirty(routerCompUid, comp);
                compSys.RecalculateHardwareStats(routerCompUid, comp);
                if (!comp.IsBooted)
                    compSys.RefreshComputerBootState(routerCompUid, comp);
            }

            EntityManager.System<DataServerSystem>().EnsureDebugRepositoryServer();

            if (!_routerQuery.TryComp(_debugRouter, out var rComp))
                return false;

            router = (_debugRouter, rComp);
            RefreshDnsDirectory(router);
            return true;
        }
        finally
        {
            _ensuringDebugRouter = false;
        }
    }
#endif

#if DEBUG
    private void EnsureDebugServerIfNeeded(EntityUid context)
    {
        if (_flushingEntities ||
            _ensuringDebugRouter ||
            LifeStage(context) != EntityLifeStage.MapInitialized ||
            TerminatingOrDeleted(context) ||
            HasOperationalRealCentralServer())
        {
            return;
        }

        _ensuringDebugRouter = true;
        try
        {
            EntityManager.System<DataServerSystem>().EnsureDebugRepositoryServer();
        }
        finally
        {
            _ensuringDebugRouter = false;
        }
    }
#endif

    public bool IsUsingDebugFallbackRouter(Entity<NetworkRouterComponent> router)
    {
#if DEBUG
        return router.Owner == _debugRouter || _debugFallbackQuery.HasComp(router.Owner);
#else
        return false;
#endif
    }

    /// <summary>
    /// Returns a clear network/server banner label when a device connects through the
    /// #if DEBUG global fallback network (when no central server is found).
    /// </summary>
    public string? GetConnectedNetworkLabel(EntityUid origin)
    {
#if DEBUG
        var compUid = UnwrapComputerEntity(origin);
        if (!HasActiveNetworkCard(compUid))
            return null;

        var router = FindNearestRouter(compUid);
        if (router == null)
            return null;

        if (IsUsingDebugFallbackRouter(router.Value) || !HasOperationalRealCentralServer())
            return DebugServerDisplayName;
#endif
        return null;
    }

    [SubscribeLocalEvent]
    private void OnRouterProgramStartup(Entity<RouterProgramComponent> ent, ref ProgramStartupEvent args)
    {
        UpdateRouterProgramUi(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnRouterProgramActivated(Entity<RouterProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdateRouterProgramUi(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnSnifferStartup(Entity<PacketSnifferProgramComponent> ent, ref ProgramStartupEvent args)
    {
        UpdatePacketSnifferUi(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnSnifferActivated(Entity<PacketSnifferProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdatePacketSnifferUi(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnNetNavigatorStartup(Entity<NetNavigatorProgramComponent> ent, ref ProgramStartupEvent args)
    {
        RunNetNavigatorProbe(ent.Owner, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnNetNavigatorActivated(Entity<NetNavigatorProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        RunNetNavigatorProbe(ent.Owner, ent.Comp);
    }

    private void OnNetNavigatorUiMessage(Entity<NetNavigatorProgramComponent> ent, ref NetNavigatorActionMessage msg)
    {
        if (!_computer.CanExecuteProgramAction(ent.Owner, msg.Actor, out var blockReason))
        {
            ent.Comp.StatusMessage = blockReason;
            UpdateNetNavigatorUi(ent.Owner);
            return;
        }

        var compUid = ResolveComputerFromProgram(ent.Owner);
        switch (msg.Action)
        {
            case NetNavigatorAction.Probe:
                RunNetNavigatorProbe(ent.Owner, ent.Comp);
                return;

            case NetNavigatorAction.ToggleLocalDiscoverable:
                if (_computerQuery.TryComp(compUid, out var cComp))
                {
                    cComp.NetworkDiscoverable = !cComp.NetworkDiscoverable;
                    Dirty(compUid, cComp);
                    ent.Comp.StatusMessage = cComp.NetworkDiscoverable
                        ? "Local device discoverability enabled (visible to network probes)."
                        : "Local device discoverability disabled (hidden from network probes).";
                }
                RunNetNavigatorProbe(ent.Owner, ent.Comp);
                return;
        }
    }

    private void RunNetNavigatorProbe(EntityUid progUid, NetNavigatorProgramComponent comp)
    {
        var compUid = ResolveComputerFromProgram(progUid);
        var devices = QueryDiscoverableDevices(compUid, out var routerGuid, out var error);
        comp.LastDevices = devices;
        comp.LastProbeTime = _timing.CurTime;
        comp.StatusMessage = string.IsNullOrEmpty(error)
            ? $"Probed {devices.Count} discoverable device(s) on router [{routerGuid}]."
            : error;
        UpdateNetNavigatorUi(progUid);
    }

    public void UpdateNetNavigatorUi(EntityUid progUid)
    {
        if (!TryComp<NetNavigatorProgramComponent>(progUid, out var navComp))
            return;

        var compUid = ResolveComputerFromProgram(progUid);
        var hasNic = HasActiveNetworkCard(compUid);
        var isOnline = hasNic && IsNetworkConnected(compUid);
        var router = isOnline ? FindNearestRouter(compUid) : null;
        var localGuid = EnsureDeviceGuid(compUid);
        var localDiscoverable = _computerQuery.TryComp(compUid, out var cComp) && cComp.NetworkDiscoverable;

        if (!isOnline)
        {
            navComp.LastDevices = new List<DiscoverableNetworkDeviceDto>();
            navComp.StatusMessage = !hasNic
                ? "Network offline — no active network card installed."
                : "Network offline — no operational station router reachable.";
        }

        var state = new NetNavigatorUiState(
            router != null ? EnsureDeviceGuid(router.Value.Owner) : "OFFLINE",
            localGuid,
            hasNic,
            isOnline,
            localDiscoverable,
            new List<DiscoverableNetworkDeviceDto>(navComp.LastDevices),
            navComp.StatusMessage,
            navComp.LastProbeTime);

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, state);
    }

    /// <summary>
    /// Queries all reachable devices on the origin computer's active station router that advertise
    /// themselves as discoverable (<see cref="OxydComputerComponent.NetworkDiscoverable"/> or operational Wi-Fi cameras).
    /// Used by both the terminal <c>netprobe</c>/<c>netquery</c> command and the NetNavigator app.
    /// </summary>
    public List<DiscoverableNetworkDeviceDto> QueryDiscoverableDevices(
        EntityUid originCompUid,
        out string routerGuid,
        out string error,
        string? filter = null)
    {
        routerGuid = "OFFLINE";
        error = string.Empty;
        var results = new List<DiscoverableNetworkDeviceDto>();

        var compUid = UnwrapComputerEntity(originCompUid);
        if (!HasActiveNetworkCard(compUid))
        {
            error = "No active network card installed (or network wire severed).";
            return results;
        }

        var router = FindNearestRouter(compUid);
        if (router == null)
        {
            error = "No operational station network router reachable.";
            return results;
        }

        var localGuid = EnsureDeviceGuid(compUid);
        routerGuid = EnsureDeviceGuid(router.Value.Owner);
        if (router.Value.Comp.BlacklistedGuids.Contains(localGuid))
        {
            error = "This device GUID is blacklisted on the station network router.";
            return results;
        }

        RefreshDnsDirectory(router.Value);
        RefreshRouterDevices(router.Value);

        // Build reverse DNS lookup so extra info can display registered DNS aliases cleanly.
        var dnsByGuid = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (alias, targetGuid) in router.Value.Comp.DnsRecords)
        {
            if (!dnsByGuid.TryGetValue(targetGuid, out var list))
            {
                list = new List<string>();
                dnsByGuid[targetGuid] = list;
            }
            if (!list.Contains(alias, StringComparer.OrdinalIgnoreCase))
                list.Add(alias);
        }

        var seenGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleanFilter = (filter ?? string.Empty).Trim();

        bool MatchesFilter(DiscoverableNetworkDeviceDto dto)
        {
            if (string.IsNullOrEmpty(cleanFilter))
                return true;

            return dto.Guid.Contains(cleanFilter, StringComparison.OrdinalIgnoreCase) ||
                   dto.Name.Contains(cleanFilter, StringComparison.OrdinalIgnoreCase) ||
                   dto.DeviceType.Contains(cleanFilter, StringComparison.OrdinalIgnoreCase) ||
                   dto.AuthSummary.Contains(cleanFilter, StringComparison.OrdinalIgnoreCase) ||
                   dto.ExtraInfo.Contains(cleanFilter, StringComparison.OrdinalIgnoreCase);
        }

        // 1) Reachable discoverable OxydComputer devices on the same router
        var compQuery = EntityQueryEnumerator<OxydComputerComponent>();
        while (compQuery.MoveNext(out var targetCompUid, out var targetComp))
        {
            if (TerminatingOrDeleted(targetCompUid))
                continue;

            if (_debugFallbackQuery.TryComp(targetCompUid, out var dbgMarker) && !dbgMarker.IsServer)
                continue;

            if (!targetComp.NetworkDiscoverable)
                continue;

            if (!HasActiveNetworkCard(targetCompUid))
                continue;

            if (!targetComp.IsBooted || targetComp.PowerWireCut || targetComp.NetworkWireCut ||
                (targetComp.RequirePowerToBoot && !targetComp.IsPowered))
            {
                continue;
            }

            var targetGuid = EnsureDeviceGuid(targetCompUid);
            if (router.Value.Comp.BlacklistedGuids.Contains(targetGuid))
                continue;

            var targetRouter = _debugFallbackQuery.HasComp(targetCompUid)
                ? router
                : FindNearestRouter(targetCompUid);
            if (targetRouter == null || targetRouter.Value.Owner != router.Value.Owner)
                continue;

            if (!seenGuids.Add(targetGuid))
                continue;

            var hostUid = targetComp.HostMachine is { Valid: true } hm && Exists(hm) ? hm : (EntityUid?) null;
            var displayName = hostUid != null ? MetaData(hostUid.Value).EntityName : MetaData(targetCompUid).EntityName;
            var deviceType = ClassifyComputerDeviceType(targetCompUid, targetComp, hostUid);
            ResolveDeviceAuthMetadata(targetCompUid, targetComp, hostUid, out var authRequired, out var authSummary);

            var sinkCount = _computer.GetMirroredPorts(targetCompUid).Count(p => !p.IsSource);
            var extras = new List<string>();
            if (targetCompUid == compUid)
                extras.Add("SELF");

            if (hostUid != null && TryComp<DataServerComponent>(hostUid.Value, out var srvComp) && srvComp.ServerProtocols.Count > 0)
                extras.Add($"Services: {string.Join(", ", srvComp.ServerProtocols)}");
            else if (TryComp<DataServerComponent>(targetCompUid, out var directSrv) && directSrv.ServerProtocols.Count > 0)
                extras.Add($"Services: {string.Join(", ", directSrv.ServerProtocols)}");

            if (dnsByGuid.TryGetValue(targetGuid, out var aliases))
            {
                var interestingAliases = aliases
                    .Where(a => !a.Equals(displayName, StringComparison.OrdinalIgnoreCase))
                    .Take(3)
                    .ToList();
                if (interestingAliases.Count > 0)
                    extras.Add($"DNS: {string.Join(", ", interestingAliases)}");
            }

            if (sinkCount > 0)
                extras.Add($"Ports: {sinkCount}");

            if (_computer.IsRemoteControlAllowed(targetCompUid, targetComp))
                extras.Add("SSH");

            var dto = new DiscoverableNetworkDeviceDto(
                targetGuid,
                displayName,
                deviceType,
                authRequired,
                authSummary,
                string.Join(" | ", extras),
                sinkCount,
                isLocal: targetCompUid == compUid);

            if (MatchesFilter(dto))
                results.Add(dto);
        }

        // 2) Reachable operational Wi-Fi Surveillance Cameras connected to this router
        var camQuery = EntityQueryEnumerator<SurveillanceCameraComponent>();
        while (camQuery.MoveNext(out var camUid, out var camComp))
        {
            if (TerminatingOrDeleted(camUid) || !_cameras.IsCameraOperational(camUid, camComp))
                continue;

            var camRouter = _cameras.FindClosestRouterForCamera(camUid);
            if (camRouter == null || camRouter.Value.Owner != router.Value.Owner)
                continue;

            var camGuid = EnsureDeviceGuid(camUid);
            if (router.Value.Comp.BlacklistedGuids.Contains(camGuid) || !seenGuids.Add(camGuid))
                continue;

            var camName = _cameras.GetCameraDisplayName(camUid);
            var zone = _cameras.GetCameraZone(camUid);
            var hasMic = TryComp<SurveillanceCameraLogComponent>(camUid, out var logComp) && logComp.HasMicrophoneUpgrade;
            var camExtras = new List<string> { $"Zone: {zone}" };
            if (hasMic)
                camExtras.Add("Mic");

            var camDto = new DiscoverableNetworkDeviceDto(
                camGuid,
                camName,
                "Camera",
                authRequired: true,
                authSummary: "ID",
                extraInfo: string.Join(" | ", camExtras),
                sinkPortCount: 0,
                isLocal: false);

            if (MatchesFilter(camDto))
                results.Add(camDto);
        }

        // Log a lightweight NET/PROBE discovery frame on the router bus.
        var probePkt = new NetworkDataPacket
        {
            PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            SourceAddress = localGuid,
            TargetAddress = routerGuid,
            Protocol = "NET/PROBE",
            PayloadSummary = string.IsNullOrEmpty(cleanFilter)
                ? $"Discovery query -> {results.Count} device(s)"
                : $"Discovery query '{cleanFilter}' -> {results.Count} device(s)",
            ByteSize = 96,
            Timestamp = _timing.CurTime
        };
        _bandwidth.LogPacketToSpecificRouter(router.Value, probePkt);

        results.Sort((a, b) =>
        {
            var localCmp = b.IsLocal.CompareTo(a.IsLocal);
            if (localCmp != 0)
                return localCmp;

            var typeCmp = string.Compare(a.DeviceType, b.DeviceType, StringComparison.OrdinalIgnoreCase);
            if (typeCmp != 0)
                return typeCmp;

            var nameCmp = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return nameCmp != 0 ? nameCmp : string.Compare(a.Guid, b.Guid, StringComparison.OrdinalIgnoreCase);
        });

        return results;
    }

    private string ClassifyComputerDeviceType(EntityUid compUid, OxydComputerComponent comp, EntityUid? hostUid)
    {
        if (hostUid is { Valid: true } hm)
        {
            if (_routerQuery.HasComp(hm))
                return "Router";
            if (HasComp<DataServerComponent>(hm))
                return "Data Server";
            if (HasComp<ApcComponent>(hm))
                return "APC";
            if (HasComp<AirAlarmComponent>(hm))
                return "Air Alarm";
            if (HasComp<LatheComponent>(hm))
                return "Autolathe";
        }

        if (_routerQuery.HasComp(compUid))
            return "Router";
        if (HasComp<DataServerComponent>(compUid))
            return "Data Server";

        return comp.Template switch
        {
            ComputerTemplate.Pda => "PDA",
            ComputerTemplate.Desktop => "Workstation",
            _ => "Embedded"
        };
    }

    private void ResolveDeviceAuthMetadata(
        EntityUid compUid,
        OxydComputerComponent comp,
        EntityUid? hostUid,
        out bool authRequired,
        out string authSummary)
    {
        if (!_computer.IsRemoteControlAllowed(compUid, comp))
        {
            authRequired = true;
            authSummary = "DISABLED";
            return;
        }

        var methods = new List<string>();
        var hasPassword = !string.IsNullOrEmpty(comp.LockPassword) || !string.IsNullOrEmpty(comp.SshPassword);
        var hasKey = !string.IsNullOrEmpty(comp.LockKeySecret);
        var hasHostIdLock = hostUid is { Valid: true } hm &&
                            (HasComp<LockComponent>(hm) || HasComp<AccessReaderComponent>(hm));
        var hasSoftIdLock = comp.RequiredAccessTags.Count > 0;

        if (hasPassword)
            methods.Add("AUTH");
        if (hasKey)
            methods.Add("KEY");
        if (hasHostIdLock || hasSoftIdLock)
            methods.Add("ID");
        else if (!hasPassword && !hasKey && comp.RemoteLocked)
            methods.Add("ID");

        if (methods.Count > 0)
        {
            authRequired = true;
            authSummary = string.Join(" / ", methods);
            return;
        }

        authRequired = false;
        authSummary = "OPEN";
    }

    private void OnRouterUiMessage(Entity<RouterProgramComponent> ent, ref RouterActionMessage msg)
    {
        if (!_computer.CanExecuteProgramAction(ent.Owner, msg.Actor, out var blockReason))
        {
            ent.Comp.StatusMessage = blockReason;
            UpdateRouterProgramUi(ent.Owner);
            return;
        }

        var compUid = ResolveComputerFromProgram(ent.Owner);
        var router = FindActiveRouter(compUid);
        if (router == null)
        {
            ent.Comp.StatusMessage = "ERROR: No active Network Router found.";
            UpdateRouterProgramUi(ent.Owner);
            return;
        }

        var isRouterHost = _computerQuery.TryComp(compUid, out var comp) && comp.HostMachine == router.Value.Owner;
        if (msg.Action != RouterUiAction.Refresh && !isRouterHost)
        {
            ent.Comp.StatusMessage = "Access denied: Router administration must be performed directly on the Station Network Router.";
            UpdateRouterProgramUi(ent.Owner);
            return;
        }

        switch (msg.Action)
        {
            case RouterUiAction.Refresh:
                RefreshDnsDirectory(router.Value);
                RefreshRouterDevices(router.Value);
                ent.Comp.StatusMessage = "Router state synchronized.";
                break;

            case RouterUiAction.KillConnection:
                CloseConnectionById(router.Value, msg.TargetId);
                ent.Comp.StatusMessage = $"Closed connection {msg.TargetId}.";
                break;

            case RouterUiAction.BlacklistGuid:
                if (router.Value.Comp.BlacklistedGuids.Count >= MaxBlacklistEntries)
                {
                    ent.Comp.StatusMessage = $"Blacklist full (max {MaxBlacklistEntries} entries).";
                    break;
                }
                var blTarget = (msg.TargetId ?? string.Empty).Trim();
                if (blTarget.Length is > 0 and <= 48)
                {
                    BlacklistGuid(router.Value, blTarget);
                    ent.Comp.StatusMessage = $"Blacklisted GUID {blTarget}.";
                }
                break;

            case RouterUiAction.UnblacklistGuid:
                UnblacklistGuid(router.Value, msg.TargetId);
                ent.Comp.StatusMessage = $"Removed GUID {msg.TargetId} from blacklist.";
                break;

            case RouterUiAction.SetDns:
                var dnsKey = (msg.TargetId ?? string.Empty).Trim();
                var dnsVal = (msg.Value ?? string.Empty).Trim().ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(dnsKey) && !string.IsNullOrWhiteSpace(dnsVal) &&
                    dnsKey.Length <= 48 && dnsVal.Length <= 48)
                {
                    if (!router.Value.Comp.DnsRecords.ContainsKey(dnsKey) && router.Value.Comp.DnsRecords.Count >= MaxDnsEntries)
                    {
                        ent.Comp.StatusMessage = $"DNS table full (max {MaxDnsEntries} entries).";
                        break;
                    }
                    router.Value.Comp.DnsRecords[dnsKey] = dnsVal;
                    Dirty(router.Value);
                    ent.Comp.StatusMessage = $"DNS set: {dnsKey} -> {dnsVal}";
                }
                break;

            case RouterUiAction.RemoveDns:
                if (router.Value.Comp.DnsRecords.Remove((msg.TargetId ?? string.Empty).Trim()))
                {
                    Dirty(router.Value);
                    ent.Comp.StatusMessage = $"DNS entry '{msg.TargetId}' removed.";
                }
                break;

            case RouterUiAction.ClearPackets:
                router.Value.Comp.RecordedPackets.Clear();
                Dirty(router.Value);
                ent.Comp.StatusMessage = "Cleared router packet buffer.";
                break;
        }

        UpdateRouterProgramUi(ent.Owner);
    }

    private void OnSnifferUiMessage(Entity<PacketSnifferProgramComponent> ent, ref PacketSnifferActionMessage msg)
    {
        if (!_computer.CanExecuteProgramAction(ent.Owner, msg.Actor, out var blockReason))
        {
            ent.Comp.StatusMessage = blockReason;
            UpdatePacketSnifferUi(ent.Owner);
            return;
        }

        var compUid = ResolveComputerFromProgram(ent.Owner);
        switch (msg.Action)
        {
            case PacketSnifferAction.Refresh:
                ent.Comp.StatusMessage = "Packet capture refreshed.";
                break;

            case PacketSnifferAction.ClearLogs:
                if (TryComp<NetworkBandwidthComponent>(compUid, out var localBw))
                {
                    localBw.StoredPackets.Clear();
                    Dirty(compUid, localBw);
                }
                if (_computerQuery.TryComp(compUid, out var cComp) &&
                    cComp.HostMachine is { } hm &&
                    _routerQuery.TryComp(hm, out var hostRouter))
                {
                    hostRouter.RecordedPackets.Clear();
                    Dirty(hm, hostRouter);
                }
                ent.Comp.StatusMessage = "Local packet capture buffer cleared.";
                break;

            case PacketSnifferAction.SaveLogsToFile:
                if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
                {
                    var router = FindNearestRouter(compUid);
                    var packets = router?.Comp.RecordedPackets ?? new List<NetworkDataPacket>();
                    var lines = new List<string> { "=== PACKET CAPTURE DUMP ===" };
                    foreach (var p in packets)
                        lines.Add(GetPacketReadableSummary(p));

                    var dumpFile = new TextVirtualFile
                    {
                        Name = "capture",
                        Extension = "txt",
                        Directory = "/logs/",
                        TextContent = string.Join("\n", lines)
                    };
                    _fileSystem.TryWriteFile(driveUid.Value, dumpFile, fs);
                    ent.Comp.StatusMessage = "Capture saved to /logs/capture.txt.";
                }
                else
                {
                    ent.Comp.StatusMessage = "Cannot save capture: no hard drive installed.";
                }
                break;

            case PacketSnifferAction.EmitPacket:
                HandleEmitPacketToSink(ent.Owner, compUid, ent.Comp, msg);
                break;

            case PacketSnifferAction.ModifyAndTransmitPacket:
                HandleModifyAndTransmitPacket(ent.Owner, compUid, ent.Comp, msg);
                break;

            case PacketSnifferAction.SelectTargetDevice:
                HandleSnifferSelectTarget(compUid, ent.Comp, msg.TargetGuid);
                break;
        }

        UpdatePacketSnifferUi(ent.Owner);
    }

    private void HandleSnifferSelectTarget(EntityUid compUid, PacketSnifferProgramComponent snifferComp, string rawTarget)
    {
        var trimmed = (rawTarget ?? string.Empty).Trim();
        var localGuid = EnsureDeviceGuid(compUid);

        if (string.IsNullOrEmpty(trimmed))
        {
            snifferComp.SelectedTargetGuid = string.Empty;
            snifferComp.SelectedTargetName = string.Empty;
            snifferComp.StatusMessage = "Target cleared — enter a target device GUID to inspect its ports.";
            return;
        }

        var isLocalAlias = trimmed.Equals("local", StringComparison.OrdinalIgnoreCase) ||
                           trimmed.Equals("self", StringComparison.OrdinalIgnoreCase) ||
                           trimmed.Equals(localGuid, StringComparison.OrdinalIgnoreCase);

        if (isLocalAlias)
        {
            var localName = _computerQuery.TryComp(compUid, out var localOxyd) &&
                            localOxyd.HostMachine is { Valid: true } hm && Exists(hm)
                ? MetaData(hm).EntityName
                : MetaData(compUid).EntityName;
            var portCount = _computer.GetMirroredPorts(compUid).Count(p => !p.IsSource);
            snifferComp.SelectedTargetGuid = localGuid;
            snifferComp.SelectedTargetName = localName;
            snifferComp.StatusMessage = $"Target [{localGuid}] ({localName}, LOCAL): {portCount} sink port(s) available.";
            return;
        }

        if (!HasActiveNetworkCard(compUid))
        {
            snifferComp.SelectedTargetGuid = string.Empty;
            snifferComp.SelectedTargetName = string.Empty;
            snifferComp.StatusMessage = "Cannot query remote target: no active network card installed.";
            return;
        }

        var resolvedLookup = ResolveDns(compUid, trimmed) ?? trimmed.ToUpperInvariant();
        if (!TryFindDeviceByGuid(resolvedLookup, out var targetCompUid))
        {
            snifferComp.SelectedTargetGuid = string.Empty;
            snifferComp.SelectedTargetName = string.Empty;
            snifferComp.StatusMessage = $"Target device [{resolvedLookup}] not found on network.";
            return;
        }

        if (targetCompUid != compUid && !AreEndpointsConnected(compUid, targetCompUid, out _, out var connErr))
        {
            snifferComp.SelectedTargetGuid = string.Empty;
            snifferComp.SelectedTargetName = string.Empty;
            snifferComp.StatusMessage = $"Target device [{resolvedLookup}] unreachable: {connErr}";
            return;
        }

        var resolvedGuid = EnsureDeviceGuid(targetCompUid);
        var targetName = _computerQuery.TryComp(targetCompUid, out var tOxyd) &&
                         tOxyd.HostMachine is { Valid: true } thm && Exists(thm)
            ? MetaData(thm).EntityName
            : MetaData(targetCompUid).EntityName;
        var portsCount = _computer.GetMirroredPorts(targetCompUid).Count(p => !p.IsSource);

        snifferComp.SelectedTargetGuid = resolvedGuid;
        snifferComp.SelectedTargetName = targetName;
        snifferComp.StatusMessage = $"Target [{resolvedGuid}] ({targetName}): {portsCount} sink port(s) available.";
    }

    private void HandleEmitPacketToSink(EntityUid progUid, EntityUid compUid, PacketSnifferProgramComponent snifferComp, PacketSnifferActionMessage msg)
    {
        if (!TryResolveSinkDeliveryTarget(
                compUid,
                msg.TargetGuid,
                msg.SinkPortId,
                msg.IsHostPort,
                out var destCompUid,
                out var sinkOwnerUid,
                out var sinkPortProto,
                out var destGuid,
                out var isLocal,
                out var resolveErr))
        {
            snifferComp.StatusMessage = $"Cannot send packet to sink port: {resolveErr}";
            return;
        }

        var localGuid = EnsureDeviceGuid(compUid);
        var srcGuid = string.IsNullOrWhiteSpace(msg.SourceGuid) ? localGuid : msg.SourceGuid.Trim().ToUpperInvariant();
        if (srcGuid.Length > 48)
            srcGuid = srcGuid[..48];

        var rawProtocol = string.IsNullOrWhiteSpace(msg.Protocol) ? string.Empty : msg.Protocol.Trim();
        var payload = (msg.Payload ?? string.Empty).Trim();
        if (payload.Length > MaxSnifferPayloadChars)
            payload = payload[..MaxSnifferPayloadChars];

        var parsedEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positionalArgs = new List<string>();
        var command = rawProtocol;

        var usePayloadAsCommand = string.IsNullOrEmpty(command) ||
                                  command.Equals("TCP/RAW", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(payload))
        {
            var tokens = payload.Split(new[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < tokens.Length; i++)
            {
                var tok = tokens[i];
                var eqIdx = tok.IndexOf('=');
                if (eqIdx > 0)
                {
                    var k = tok[..eqIdx].Trim();
                    var v = tok[(eqIdx + 1)..].Trim();
                    if (k.StartsWith("__", StringComparison.Ordinal) || k.Equals("tampered", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (usePayloadAsCommand && string.IsNullOrEmpty(command) &&
                        (k.Equals("cmd", StringComparison.OrdinalIgnoreCase) || k.Equals("command", StringComparison.OrdinalIgnoreCase)))
                    {
                        command = v;
                    }
                    else if (!string.IsNullOrEmpty(k))
                    {
                        parsedEntries[k] = v;
                    }
                }
                else if (usePayloadAsCommand && (string.IsNullOrEmpty(command) || command.Equals("TCP/RAW", StringComparison.OrdinalIgnoreCase)))
                {
                    command = tok;
                }
                else
                {
                    positionalArgs.Add(tok);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(command))
            command = "status";
        if (command.Length > 48)
            command = command[..48];

        // Prevent raw EmitPacket from forging internal VFS-overwrite packets (must go through editable capture flow).
        if (command.Equals("vfs_packet_write", StringComparison.OrdinalIgnoreCase))
        {
            snifferComp.StatusMessage = "Blocked: raw 'vfs_packet_write' packets cannot be forged directly; modify a captured editable packet instead.";
            return;
        }

        var positionalArg = positionalArgs.Count > 0 ? string.Join(" ", positionalArgs) : string.Empty;
        var compPkt = new ComputerPacket(command);
        if (!string.IsNullOrEmpty(positionalArg))
            compPkt.Set("Argument", positionalArg);

        foreach (var (k, v) in parsedEntries)
            compPkt.Set(k, v);

        if (msg.ModifiedEntries is { Count: > 0 } entries)
        {
            foreach (var (k, v) in entries.Take(MaxSnifferEntryCount))
            {
                if (string.IsNullOrWhiteSpace(k) || k.StartsWith("__", StringComparison.Ordinal) || k.Equals("tampered", StringComparison.OrdinalIgnoreCase))
                    continue;
                var safeK = k.Length > 128 ? k[..128] : k;
                var safeV = (v ?? string.Empty).Length > 2048 ? v![..2048] : (v ?? string.Empty);
                compPkt.Set(safeK, safeV);
            }
        }

        var editableEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var argDisplay = new List<string>();
        foreach (var (k, v) in compPkt.Data)
        {
            if (k.StartsWith("__", StringComparison.Ordinal) || k.Equals("tampered", StringComparison.OrdinalIgnoreCase))
                continue;
            editableEntries[k] = v;
            argDisplay.Add($"{k}={v}");
        }

        var argsSuffix = argDisplay.Count > 0 ? $" [{string.Join(", ", argDisplay)}]" : string.Empty;
        var summary = $"Packet sent to {sinkPortProto.Id} ({command}){argsSuffix}";
        var pkt = new NetworkDataPacket
        {
            PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            SourceAddress = srcGuid,
            TargetAddress = $"{destGuid}:{sinkPortProto.Id}",
            Protocol = $"DEVLINK:{sinkPortProto.Id}",
            PayloadSummary = summary,
            RawPayload = payload,
            IsPlayerRelevant = true,
            IsEditable = true,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SinkPort"] = sinkPortProto.Id,
                ["Command"] = command
            },
            EditableEntries = editableEntries,
            ByteSize = Math.Max(64, System.Text.Encoding.UTF8.GetByteCount(summary) + 64),
            Timestamp = _timing.CurTime
        };

        if (!isLocal)
        {
            if (!AreEndpointsConnected(compUid, destCompUid, out _, out var connErr))
            {
                snifferComp.StatusMessage = $"Blocked: {connErr}";
                return;
            }

            if (!RecordAndValidatePacket(compUid, destCompUid, pkt))
            {
                snifferComp.StatusMessage = $"Blocked: router denied {command} packet to [{destGuid}]:{sinkPortProto.Id}.";
                return;
            }
        }
        else
        {
            _bandwidth.RecordDataTransmitted(compUid, pkt.ByteSize, isUpload: true);
            _bandwidth.StorePacket(compUid, pkt);
            _bandwidth.LogPacketToRouters(pkt, compUid);
        }

        _deviceLink.EnsureSourcePort(compUid, PortComputerCommandSender);
        _deviceLink.InvokeDirect(compUid, sinkOwnerUid, sinkPortProto, compPkt);
        snifferComp.StatusMessage = $"Sent '{command}' -> [{destGuid}]:{sinkPortProto.Id} ({(isLocal ? "LOCAL" : "REMOTE")}).";
        RefreshAllPacketSniffers();
    }

    private void HandleModifyAndTransmitPacket(EntityUid progUid, EntityUid compUid, PacketSnifferProgramComponent snifferComp, PacketSnifferActionMessage msg)
    {
        var router = FindNearestRouter(compUid);
        NetworkDataPacket? captured = null;

        if (router != null)
        {
            captured = router.Value.Comp.RecordedPackets.LastOrDefault(p =>
                p.PacketId.Equals(msg.PacketId, StringComparison.OrdinalIgnoreCase));
        }

        if (captured == null && TryComp<NetworkBandwidthComponent>(compUid, out var localBw))
        {
            captured = localBw.StoredPackets.LastOrDefault(p =>
                p.PacketId.Equals(msg.PacketId, StringComparison.OrdinalIgnoreCase));
        }

        if (captured == null)
        {
            snifferComp.StatusMessage = $"Packet '{msg.PacketId}' no longer in capture buffer.";
            return;
        }

        if (!captured.IsPlayerRelevant || !captured.IsEditable)
        {
            snifferComp.StatusMessage = $"Packet '{captured.PacketId}' ({captured.Protocol}) is not player-editable.";
            return;
        }

        var targetGuidInput = !string.IsNullOrWhiteSpace(msg.TargetGuid) ? msg.TargetGuid.Trim() : captured.TargetAddress;
        if (!TryResolveSinkDeliveryTarget(
                compUid,
                targetGuidInput,
                msg.SinkPortId,
                msg.IsHostPort,
                out var destCompUid,
                out var sinkOwnerUid,
                out var sinkPortProto,
                out var destGuid,
                out var isLocal,
                out var resolveErr))
        {
            snifferComp.StatusMessage = $"Cannot deliver packet to sink port: {resolveErr}";
            return;
        }

        var rawModified = msg.ModifiedEntries != null
            ? new Dictionary<string, string>(msg.ModifiedEntries)
            : new Dictionary<string, string>(captured.EditableEntries);

        var modifiedEntries = new Dictionary<string, string>();
        foreach (var (k, v) in rawModified.Take(MaxSnifferEntryCount))
        {
            var safeK = (k ?? string.Empty).Length > 128 ? k![..128] : (k ?? string.Empty);
            var safeV = (v ?? string.Empty).Length > 2048 ? v![..2048] : (v ?? string.Empty);
            if (string.IsNullOrEmpty(safeK) || safeK.StartsWith("__", StringComparison.Ordinal) || safeK.Equals("tampered", StringComparison.OrdinalIgnoreCase))
                continue;
            modifiedEntries[safeK] = safeV;
        }

        var isDeviceLinkCommandPacket =
            string.IsNullOrWhiteSpace(captured.FilePath) &&
            (captured.Protocol.StartsWith("DEVLINK:", StringComparison.OrdinalIgnoreCase) ||
             captured.Parameters.ContainsKey("Command"));

        var rawSummary = string.Join("; ", modifiedEntries.Select(kv => $"{kv.Key}={kv.Value}"));
        var modPkt = captured.Clone();
        modPkt.PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        modPkt.SourceAddress = EnsureDeviceGuid(compUid);
        modPkt.TargetAddress = $"{destGuid}:{sinkPortProto.Id}";
        modPkt.EditableEntries = modifiedEntries;
        modPkt.RawPayload = rawSummary;
        modPkt.ByteSize = Math.Max(128, System.Text.Encoding.UTF8.GetByteCount(rawSummary) + 64);
        modPkt.Timestamp = _timing.CurTime;

        ComputerPacket compPkt;
        if (isDeviceLinkCommandPacket)
        {
            var resolvedCmd = modifiedEntries.TryGetValue("Command", out var editedCmd) && !string.IsNullOrWhiteSpace(editedCmd)
                ? editedCmd.Trim()
                : captured.Parameters.TryGetValue("Command", out var origCmd) && !string.IsNullOrWhiteSpace(origCmd)
                    ? origCmd.Trim()
                    : "status";
            if (resolvedCmd.Equals("vfs_packet_write", StringComparison.OrdinalIgnoreCase))
            {
                snifferComp.StatusMessage = "Blocked: cannot forge 'vfs_packet_write' via DeviceLink command packet.";
                return;
            }

            compPkt = new ComputerPacket(resolvedCmd);
            foreach (var (k, v) in modifiedEntries)
            {
                if (k.Equals("Command", StringComparison.OrdinalIgnoreCase) || k.Equals("SinkPort", StringComparison.OrdinalIgnoreCase))
                    continue;
                compPkt.Set(k, v);
            }

            modPkt.Protocol = $"DEVLINK:{sinkPortProto.Id}";
            modPkt.Parameters["SinkPort"] = sinkPortProto.Id;
            modPkt.Parameters["Command"] = resolvedCmd;
            var argsText = compPkt.Data.Count > 0
                ? $" [{string.Join(", ", compPkt.Data.Select(kv => $"{kv.Key}={kv.Value}"))}]"
                : string.Empty;
            modPkt.PayloadSummary = $"Packet sent to {sinkPortProto.Id} ({resolvedCmd}){argsText}";
        }
        else
        {
            modPkt.PayloadSummary = $"[SINK:{sinkPortProto.Id}] {captured.Protocol} ({modifiedEntries.Count} entries) -> {destGuid}";
            compPkt = new ComputerPacket("vfs_packet_write")
            {
                InternalMarkTampered = true
            };
            compPkt.Set("packet_id", modPkt.PacketId);
            compPkt.Set("protocol", modPkt.Protocol);
            compPkt.Set("file_path", captured.FilePath);
            compPkt.Set("source_guid", modPkt.SourceAddress);
            compPkt.Set("target_guid", destGuid);
            compPkt.Set("sink_port", sinkPortProto.Id);
            compPkt.Set("entry_count", modifiedEntries.Count);

            var eIdx = 0;
            foreach (var (k, v) in modifiedEntries)
            {
                compPkt.Set($"ek_{eIdx}", k);
                compPkt.Set($"ev_{eIdx}", v);
                eIdx++;
            }

            compPkt.Set("param_count", captured.Parameters.Count);
            var pIdx = 0;
            foreach (var (k, v) in captured.Parameters)
            {
                compPkt.Set($"pk_{pIdx}", k);
                compPkt.Set($"pv_{pIdx}", v);
                pIdx++;
            }
        }

        if (!isLocal)
        {
            if (!AreEndpointsConnected(compUid, destCompUid, out var xferRouter, out var netErr) || xferRouter == null)
            {
                snifferComp.StatusMessage = $"Blocked: {netErr}";
                return;
            }

            _bandwidth.EnqueueRouterTransfer(xferRouter.Value, new QueuedRouterTransfer
            {
                Protocol = modPkt.Protocol,
                TargetPath = captured.FilePath,
                Description = $"Packet Inject -> [{destGuid}]",
                IsUpload = true,
                SourceCompUid = compUid,
                TargetCompUid = destCompUid,
                InitiatorProgramUid = progUid,
                TotalBytes = modPkt.ByteSize,
                AllowDebugFallback = _debugFallbackQuery.HasComp(destCompUid),
                PacketMetadata = modPkt,
                OnCompleted = _ =>
                {
                    if (!Exists(compUid) || !Exists(sinkOwnerUid))
                        return;

                    _bandwidth.StorePacket(compUid, modPkt);
                    _bandwidth.StorePacket(destCompUid, modPkt);
                    _deviceLink.EnsureSourcePort(compUid, PortComputerCommandSender);
                    _deviceLink.InvokeDirect(compUid, sinkOwnerUid, sinkPortProto, compPkt);
                    if (Exists(progUid) && TryComp<PacketSnifferProgramComponent>(progUid, out var sc))
                    {
                        sc.StatusMessage = $"Delivered {modPkt.Protocol} packet -> [{destGuid}]:{sinkPortProto.Id} (REMOTE, {modifiedEntries.Count} entries).";
                        UpdatePacketSnifferUi(progUid);
                    }
                    RefreshAllPacketSniffers();
                },
                OnAborted = (_, reason) =>
                {
                    if (Exists(progUid) && TryComp<PacketSnifferProgramComponent>(progUid, out var sc))
                    {
                        sc.StatusMessage = $"Packet delivery aborted: {reason}";
                        UpdatePacketSnifferUi(progUid);
                    }
                }
            });

            snifferComp.StatusMessage = $"Queued {modPkt.Protocol} packet -> [{destGuid}]:{sinkPortProto.Id} on router ({modPkt.ByteSize} B)...";
            return;
        }

        _bandwidth.RecordDataTransmitted(compUid, modPkt.ByteSize, isUpload: true);
        _bandwidth.StorePacket(compUid, modPkt);
        _bandwidth.LogPacketToRouters(modPkt, compUid);
        _deviceLink.EnsureSourcePort(compUid, PortComputerCommandSender);
        _deviceLink.InvokeDirect(compUid, sinkOwnerUid, sinkPortProto, compPkt);

        snifferComp.StatusMessage =
            $"Sent {modPkt.Protocol} packet -> [{destGuid}]:{sinkPortProto.Id} (LOCAL, {modifiedEntries.Count} entries).";
        RefreshAllPacketSniffers();
    }

    private bool TryResolveSinkDeliveryTarget(
        EntityUid originCompUid,
        string requestedGuid,
        string requestedSinkPortId,
        bool preferHostPort,
        out EntityUid destCompUid,
        out EntityUid sinkOwnerUid,
        out ProtoId<SinkPortPrototype> sinkPortProto,
        out string destGuid,
        out bool isLocal,
        out string error)
    {
        destCompUid = EntityUid.Invalid;
        sinkOwnerUid = EntityUid.Invalid;
        sinkPortProto = default;
        destGuid = string.Empty;
        isLocal = false;
        error = string.Empty;

        var originGuid = EnsureDeviceGuid(originCompUid);
        var cleanGuid = requestedGuid.Trim().ToUpperInvariant();
        var colonIdx = cleanGuid.IndexOf(':');
        if (colonIdx > 0)
            cleanGuid = cleanGuid[..colonIdx].Trim();
        if (string.IsNullOrEmpty(cleanGuid) || string.Equals(cleanGuid, originGuid, StringComparison.OrdinalIgnoreCase))
        {
            isLocal = true;
            destCompUid = originCompUid;
            destGuid = originGuid;
        }
        else
        {
            if (!HasActiveNetworkCard(originCompUid))
            {
                error = "no active network card installed for remote machine targeting.";
                return false;
            }

            if (!TryFindDeviceByGuid(cleanGuid, out destCompUid))
            {
                error = $"destination device [{cleanGuid}] not found on network.";
                return false;
            }

            destGuid = EnsureDeviceGuid(destCompUid);
            isLocal = destCompUid == originCompUid;
        }

        var ports = _computer.GetMirroredPorts(destCompUid).Where(p => !p.IsSource).ToList();
        if (ports.Count == 0)
        {
            error = $"machine [{destGuid}] has no DeviceLink sink ports.";
            return false;
        }

        MirroredDevicePortInfo? chosen = null;
        if (!string.IsNullOrWhiteSpace(requestedSinkPortId))
        {
            var cleanPort = requestedSinkPortId.Trim();
            chosen = ports.FirstOrDefault(p =>
                         p.PortId.Equals(cleanPort, StringComparison.OrdinalIgnoreCase) && p.IsHostPort == preferHostPort)
                     ?? ports.FirstOrDefault(p => p.PortId.Equals(cleanPort, StringComparison.OrdinalIgnoreCase));

            if (chosen == null)
            {
                error = $"sink port '{cleanPort}' does not exist on [{destGuid}].";
                return false;
            }
        }
        else
        {
            chosen = ports.FirstOrDefault(p => p.IsHostPort == preferHostPort) ?? ports[0];
        }

        var hostUid = _computerQuery.TryComp(destCompUid, out var destOxyd) ? destOxyd.HostMachine : null;
        sinkOwnerUid = chosen.IsHostPort && hostUid is { Valid: true } hm ? hm : destCompUid;
        var targetPort = new ProtoId<SinkPortPrototype>(chosen.PortId);
        sinkPortProto = targetPort;

        if (!_sinkQuery.TryComp(sinkOwnerUid, out var sinkComp) ||
            !Array.Exists(_deviceLink.GetSinkPortIds((sinkOwnerUid, sinkComp)), p => p == targetPort))
        {
            error = $"sink port '{chosen.PortId}' is unavailable on [{destGuid}].";
            return false;
        }

        return true;
    }

    private bool ApplyModifiedPacketToComputerVfs(
        EntityUid destCompUid,
        NetworkDataPacket pkt,
        bool markTampered,
        out string error)
    {
        error = string.Empty;
        if (!_fileSystem.TryFindFileSystem(destCompUid, out var driveUid, out var fs))
        {
            error = "no hard drive on target computer";
            return false;
        }

        var path = pkt.FilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "packet has no associated VFS file path";
            return false;
        }

        if (path.Equals(CrewManifestProgramComponent.CacheFilePath, StringComparison.OrdinalIgnoreCase))
        {
            var data = new Dictionary<string, string>();
            var stationName = pkt.Parameters.TryGetValue("__station", out var st) && !string.IsNullOrWhiteSpace(st)
                ? st
                : "Unknown Station";

            if (_fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid.Value, path, out var existingDb, fs) &&
                existingDb.Data.TryGetValue("__station", out var existingSt) &&
                !string.IsNullOrWhiteSpace(existingSt))
            {
                stationName = existingSt;
            }

            data["__station"] = stationName;
            if (markTampered)
                data["__tampered"] = "1";

            var idx = 0;
            foreach (var (charName, jobTitle) in pkt.EditableEntries)
            {
                if (string.IsNullOrWhiteSpace(charName))
                    continue;

                var jobProto = string.Empty;
                var jobIcon = "JobIconUnknown";
                if (pkt.Parameters.TryGetValue($"meta:{charName}", out var metaRaw))
                {
                    var mCols = metaRaw.Split('\t');
                    if (mCols.Length >= 1)
                        jobProto = mCols[0];
                    if (mCols.Length >= 2)
                        jobIcon = mCols[1];
                }

                data[$"row_{idx++}"] = $"{charName.Trim()}\t{jobTitle.Trim()}\t{jobProto}\t{jobIcon}";
            }

            var updated = new DatabaseVirtualFile
            {
                Name = "crew_manifest",
                Extension = "db",
                Directory = "/records/",
                TableName = "crew_manifest",
                Data = data,
                Size = Math.Max(128, data.Count * 64)
            };

            if (!_fileSystem.TryWriteFile(driveUid.Value, updated, fs))
            {
                error = "target storage full or read-only";
                return false;
            }

            _fileSystem.LogSystemActivity(driveUid.Value, "NET-PKT", $"Updated {path} from {pkt.Protocol} ({pkt.PacketId})");
            EntityManager.System<StationAppsSystem>().RefreshRecordProgramsFromLocalCache(destCompUid, path);
            return true;
        }

        if (path.Equals(WantedListProgramComponent.CacheFilePath, StringComparison.OrdinalIgnoreCase))
        {
            var data = new Dictionary<string, string>();
            if (markTampered)
                data["__tampered"] = "1";

            var idx = 0;
            foreach (var (charName, rawVal) in pkt.EditableEntries)
            {
                if (string.IsNullOrWhiteSpace(charName))
                    continue;

                var parts = rawVal.Split('|', 2);
                var statusStr = parts[0].Trim();
                var reason = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                var jobTitle = "Crew";
                var initiator = "Security";
                if (pkt.Parameters.TryGetValue($"meta:{charName}", out var metaRaw))
                {
                    var mCols = metaRaw.Split('\t');
                    if (mCols.Length >= 1 && !string.IsNullOrWhiteSpace(mCols[0]))
                        jobTitle = mCols[0];
                    if (mCols.Length >= 2)
                        initiator = mCols[1];
                }

                data[$"rec_{idx++}"] = $"{charName.Trim()}\t{jobTitle}\t{statusStr}\t{reason}\t{initiator}";
            }

            var updated = new DatabaseVirtualFile
            {
                Name = "wanted_list",
                Extension = "db",
                Directory = "/records/",
                TableName = "wanted_list",
                Data = data,
                Size = Math.Max(128, data.Count * 64)
            };

            if (!_fileSystem.TryWriteFile(driveUid.Value, updated, fs))
            {
                error = "target storage full or read-only";
                return false;
            }

            _fileSystem.LogSystemActivity(driveUid.Value, "NET-PKT", $"Updated {path} from {pkt.Protocol} ({pkt.PacketId})");
            EntityManager.System<StationAppsSystem>().RefreshRecordProgramsFromLocalCache(destCompUid, path);
            return true;
        }

        if (path.Equals(NewsReaderProgramComponent.CacheFilePath, StringComparison.OrdinalIgnoreCase))
        {
            var data = new Dictionary<string, string>();
            if (markTampered)
                data["__tampered"] = "1";

            var idx = 0;
            foreach (var (title, content) in pkt.EditableEntries)
            {
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                var author = "Anonymous";
                var ticks = _timing.CurTime.Ticks.ToString();
                if (pkt.Parameters.TryGetValue($"meta:{title}", out var metaRaw))
                {
                    var mCols = metaRaw.Split('\t');
                    if (mCols.Length >= 1 && !string.IsNullOrWhiteSpace(mCols[0]))
                        author = mCols[0];
                    if (mCols.Length >= 2 && !string.IsNullOrWhiteSpace(mCols[1]))
                        ticks = mCols[1];
                }

                data[$"art_{idx++}"] = $"{title.Trim()}\t{author}\t{ticks}\t{content.Replace('\t', ' ')}";
            }

            var updated = new DatabaseVirtualFile
            {
                Name = "articles",
                Extension = "db",
                Directory = "/news/",
                TableName = "station_news",
                Data = data,
                Size = Math.Max(128, data.Count * 128)
            };

            if (!_fileSystem.TryWriteFile(driveUid.Value, updated, fs))
            {
                error = "target storage full or read-only";
                return false;
            }

            _fileSystem.LogSystemActivity(driveUid.Value, "NET-PKT", $"Updated {path} from {pkt.Protocol} ({pkt.PacketId})");
            EntityManager.System<StationAppsSystem>().RefreshRecordProgramsFromLocalCache(destCompUid, path);
            return true;
        }

        // Generic .db or .txt file override
        var slashIdx = path.LastIndexOf('/');
        var dir = slashIdx >= 0 ? path[..(slashIdx + 1)] : "/";
        var fileWithExt = slashIdx >= 0 ? path[(slashIdx + 1)..] : path;
        var dotIdx = fileWithExt.LastIndexOf('.');
        var fName = dotIdx > 0 ? fileWithExt[..dotIdx] : fileWithExt;
        var fExt = dotIdx > 0 ? fileWithExt[(dotIdx + 1)..] : "db";

        VirtualFile genericFile;
        if (fExt.Equals("txt", StringComparison.OrdinalIgnoreCase))
        {
            var body = pkt.EditableEntries.TryGetValue("content", out var textBody)
                ? textBody
                : string.Join("\n", pkt.EditableEntries.Select(kv => $"{kv.Key}: {kv.Value}"));
            genericFile = new TextVirtualFile
            {
                Name = fName,
                Extension = fExt,
                Directory = dir,
                TextContent = body,
                Size = Math.Max(64, body.Length)
            };
        }
        else
        {
            var dbData = new Dictionary<string, string>(pkt.EditableEntries);
            if (markTampered)
                dbData["__tampered"] = "1";
            genericFile = new DatabaseVirtualFile
            {
                Name = fName,
                Extension = fExt,
                Directory = dir,
                TableName = fName,
                Data = dbData,
                Size = Math.Max(128, dbData.Count * 64)
            };
        }

        if (!_fileSystem.TryWriteFile(driveUid.Value, genericFile, fs))
        {
            error = "target storage full or read-only";
            return false;
        }

        _fileSystem.LogSystemActivity(driveUid.Value, "NET-PKT", $"Updated {path} from {pkt.Protocol} ({pkt.PacketId})");
        if (path.Equals(StationRecordsProgramComponent.CacheFilePath, StringComparison.OrdinalIgnoreCase) ||
            path.Equals(CriminalRecordsProgramComponent.CacheFilePath, StringComparison.OrdinalIgnoreCase))
        {
            EntityManager.System<RecordsProgramsSystem>().RefreshRecordProgramsFromLocalCache(destCompUid, path);
        }
        return true;
    }

    public void RefreshDnsDirectory(Entity<NetworkRouterComponent> router)
    {
#if DEBUG
        EnsureDebugServerIfNeeded(router.Owner);
#endif
        var serverQuery = EntityQueryEnumerator<DataServerComponent>();
        while (serverQuery.MoveNext(out var sUid, out var sComp))
        {
            var targetComp = UnwrapComputerEntity(sUid);
            if (!HasActiveNetworkCard(targetComp))
                continue;

            var guid = EnsureDeviceGuid(targetComp);
            router.Comp.DnsRecords[MetaData(sUid).EntityName] = guid;
            if (_debugFallbackQuery.HasComp(sUid))
                router.Comp.DnsRecords["DEV_DEBUG_SERVER"] = guid;
            foreach (var proto in sComp.ServerProtocols)
                router.Comp.DnsRecords[proto] = guid;
        }
        Dirty(router);
    }

    public EntityUid UnwrapComputerEntity(EntityUid uid)
    {
        if (_hostQuery.TryComp(uid, out var host) && host.ComputerEntity is { } cUid && Exists(cUid))
            return cUid;
        return uid;
    }

    public EntityUid ResolveComputerFromProgram(EntityUid uid)
    {
        if (_programQuery.TryComp(uid, out var prog) && prog.Computer is { } cUid && Exists(cUid))
            return cUid;
        return UnwrapComputerEntity(uid);
    }

    public bool HasActiveNetworkCard(EntityUid uid)
    {
        var compUid = UnwrapComputerEntity(uid);
        if (_computerQuery.TryComp(compUid, out var comp))
        {
            if (comp.NetworkWireCut || (comp.RequirePowerToBoot && !comp.IsPowered))
                return false;

            return _statsQuery.TryComp(compUid, out var stats) && stats.HasNetworkCard && stats.NetworkBandwidthBps > 0;
        }

        if (TryComp<NetworkBandwidthComponent>(compUid, out var bw))
            return bw.MaxBps > 0;

        return false;
    }

    public string EnsureDeviceGuid(EntityUid uid)
    {
        var target = UnwrapComputerEntity(uid);
        return _guid.GetGuid(target);
    }

    public bool TryFindDeviceByGuid(string guid, out EntityUid foundUid)
    {
        foundUid = EntityUid.Invalid;
        if (string.IsNullOrWhiteSpace(guid))
            return false;

        var clean = guid.Trim().ToUpperInvariant();
        if (_guid.TryGetEntityByGuid(clean, out var res) && res.HasValue)
        {
            foundUid = UnwrapComputerEntity(res.Value);
            return true;
        }

        var compQuery = EntityQueryEnumerator<OxydComputerComponent, DeviceGuidComponent>();
        while (compQuery.MoveNext(out var uid, out _, out var gComp))
        {
            if (gComp.Guid.Equals(clean, StringComparison.OrdinalIgnoreCase))
            {
                foundUid = uid;
                return true;
            }
        }

        return false;
    }

    public Entity<NetworkRouterComponent>? FindNearestRouter(EntityUid origin)
    {
        var originCoords = _transform.GetMapCoordinates(origin);
        Entity<NetworkRouterComponent>? best = null;
        var bestDist = float.MaxValue;

        if (originCoords.MapId != MapId.Nullspace)
        {
            var candidates = _lookup.GetEntitiesInRange<NetworkRouterComponent>(
                originCoords,
                RouterLookupRange,
                LookupFlags.All | LookupFlags.Approximate);

            foreach (var rEnt in candidates)
            {
                if (_debugFallbackQuery.HasComp(rEnt.Owner))
                    continue;

                if (!IsRouterOperational(rEnt.Owner))
                    continue;

                var dist = (_transform.GetMapCoordinates(rEnt.Owner).Position - originCoords.Position).LengthSquared();
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = rEnt;
                }
            }
        }

#if DEBUG
        if (best == null &&
            !_flushingEntities &&
            !_ensuringDebugRouter &&
            LifeStage(origin) == EntityLifeStage.MapInitialized &&
            !TerminatingOrDeleted(origin) &&
            !HasOperationalRealCentralServer() &&
            TryEnsureDebugGlobalRouter(out var debugRouter))
        {
            return debugRouter;
        }
#endif

        return best;
    }

    private Entity<NetworkRouterComponent>? FindActiveRouter(EntityUid origin)
    {
        if (TryComp<OxydComputerComponent>(origin, out var oxyd) && oxyd.HostMachine is { } hm && TryComp<NetworkRouterComponent>(hm, out var hmRouter))
            return (hm, hmRouter);

        return FindNearestRouter(origin);
    }

    public bool IsRouterOperational(EntityUid routerHostOrComp)
    {
        if (TryComp<Content.Server.Power.Components.ApcPowerReceiverComponent>(routerHostOrComp, out var apc) && !apc.Powered)
            return false;

        var routerComp = UnwrapComputerEntity(routerHostOrComp);
        if (_computerQuery.TryComp(routerComp, out var comp))
        {
            if (comp.NetworkWireCut || comp.PowerWireCut)
                return false;

            if (_statsQuery.TryComp(routerComp, out var stats) && !stats.HasNetworkCard)
                return false;
        }

        if (TryComp<NetworkBandwidthComponent>(routerComp, out var bw) && bw.IsSaturated)
            return false;

        return true;
    }

    public bool IsNetworkConnected(EntityUid uid)
    {
        var compUid = UnwrapComputerEntity(uid);
        if (!HasActiveNetworkCard(compUid))
            return false;

        var router = FindNearestRouter(compUid);
        if (router == null)
            return false;

        var guid = EnsureDeviceGuid(compUid);
        if (router.Value.Comp.BlacklistedGuids.Contains(guid))
            return false;

        RegisterDeviceOnRouter(router.Value, guid);
        return true;
    }

    public bool IsGuidBlacklisted(EntityUid contextUid, string guid)
    {
        var router = FindNearestRouter(contextUid);
        return router != null && router.Value.Comp.BlacklistedGuids.Contains(guid.Trim().ToUpperInvariant());
    }

    public string? ResolveDns(EntityUid origin, string serviceName)
    {
        if (!HasActiveNetworkCard(origin))
            return null;

        var router = FindNearestRouter(origin);
        if (router == null)
            return null;

        RefreshDnsDirectory(router.Value);
        if (router.Value.Comp.DnsRecords.TryGetValue(serviceName, out var exact))
            return exact;

        foreach (var (k, v) in router.Value.Comp.DnsRecords)
        {
            if (k.Equals(serviceName, StringComparison.OrdinalIgnoreCase))
                return v;
        }

        return null;
    }

    public void RegisterDeviceOnRouter(Entity<NetworkRouterComponent> router, string guid)
    {
        var clean = guid.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(clean) && !router.Comp.ConnectedDevices.Contains(clean))
        {
            router.Comp.ConnectedDevices.Add(clean);
            Dirty(router);
        }
    }

    public void RefreshRouterDevices(Entity<NetworkRouterComponent> router)
    {
#if DEBUG
        EnsureDebugServerIfNeeded(router.Owner);
#endif

        router.Comp.ConnectedDevices.Clear();
        var compQuery = EntityQueryEnumerator<OxydComputerComponent>();
        while (compQuery.MoveNext(out var uid, out _))
        {
            if (_debugFallbackQuery.TryComp(uid, out var marker) && !marker.IsServer)
                continue;

            if (!HasActiveNetworkCard(uid))
                continue;

            var guid = EnsureDeviceGuid(uid);
            if (!router.Comp.ConnectedDevices.Contains(guid))
                router.Comp.ConnectedDevices.Add(guid);
        }

        var srvQuery = EntityQueryEnumerator<DataServerComponent>();
        while (srvQuery.MoveNext(out var sUid, out _))
        {
            var sComp = UnwrapComputerEntity(sUid);
            if (!HasActiveNetworkCard(sComp))
                continue;

            var guid = EnsureDeviceGuid(sComp);
            if (!router.Comp.ConnectedDevices.Contains(guid))
                router.Comp.ConnectedDevices.Add(guid);
        }

        Dirty(router);
    }

    public bool AreEndpointsConnected(
        EntityUid sourceUid,
        EntityUid targetUid,
        out Entity<NetworkRouterComponent>? sharedRouter,
        out string error)
    {
        sharedRouter = null;
        error = string.Empty;

        var sComp = UnwrapComputerEntity(sourceUid);
        var tComp = UnwrapComputerEntity(targetUid);

        if (!Exists(sComp) || TerminatingOrDeleted(sComp))
        {
            error = "Source computer offline.";
            return false;
        }

        if (!Exists(tComp) || TerminatingOrDeleted(tComp))
        {
            error = "Target device offline.";
            return false;
        }

        if (!HasActiveNetworkCard(sComp))
        {
            error = "No active network card installed on this computer.";
            return false;
        }

        if (!HasActiveNetworkCard(tComp))
        {
            error = "Target device has no active network connection.";
            return false;
        }

        if (_computerQuery.TryComp(tComp, out var tOxyd) &&
            (!tOxyd.IsBooted || tOxyd.PowerWireCut || tOxyd.NetworkWireCut || (tOxyd.RequirePowerToBoot && !tOxyd.IsPowered)))
        {
            error = "Target device is powered off or disconnected.";
            return false;
        }

        var sRouter = FindNearestRouter(sComp);
        var tRouter = _debugFallbackQuery.HasComp(tComp) ? sRouter ?? FindNearestRouter(tComp) : FindNearestRouter(tComp);
        if (sRouter == null)
        {
            error = "No operational station network router reachable from this computer.";
            return false;
        }

        if (tRouter == null)
        {
            error = "Target device is not connected to an operational station network router.";
            return false;
        }

        if (!_debugFallbackQuery.HasComp(sComp) && !_debugFallbackQuery.HasComp(tComp) && sRouter.Value.Owner != tRouter.Value.Owner)
        {
            error = "Target device is on a different network router.";
            return false;
        }

        var sGuid = EnsureDeviceGuid(sComp);
        var tGuid = EnsureDeviceGuid(tComp);
        if (sRouter.Value.Comp.BlacklistedGuids.Contains(sGuid) || sRouter.Value.Comp.BlacklistedGuids.Contains(tGuid))
        {
            error = "Device GUID is blacklisted on the station network router.";
            return false;
        }

        RegisterDeviceOnRouter(sRouter.Value, sGuid);
        RegisterDeviceOnRouter(sRouter.Value, tGuid);
        sharedRouter = sRouter;
        return true;
    }

    public bool RecordAndValidatePacket(EntityUid sender, EntityUid target, NetworkDataPacket packet)
    {
        var sComp = UnwrapComputerEntity(sender);
        var tComp = UnwrapComputerEntity(target);

        if (!AreEndpointsConnected(sComp, tComp, out var router, out _) || router == null)
            return false;

        var transferBps = _bandwidth.GetEffectiveTransferRateBps(sComp, tComp, router.Value.Owner);
        if (transferBps <= 0)
            return false;

        _bandwidth.RecordDataTransmitted(sComp, packet.ByteSize, isUpload: true);
        _bandwidth.RecordDataTransmitted(tComp, packet.ByteSize, isUpload: false);
        _bandwidth.StorePacket(sComp, packet);
        _bandwidth.StorePacket(tComp, packet);
        _bandwidth.LogPacketToSpecificRouter(router.Value, packet);

        RefreshAllPacketSniffers();
        return true;
    }

    public string RegisterActiveConnection(EntityUid origin, string sourceGuid, string targetGuid, string connectionType, string description)
    {
        var router = FindNearestRouter(origin);
        if (router == null)
            return string.Empty;

        var connId = $"{connectionType}-{sourceGuid}-{targetGuid}";
        router.Value.Comp.ActiveConnections.RemoveAll(c => c.ConnectionId == connId);
        router.Value.Comp.ActiveConnections.Add(new ActiveNetworkConnectionRecord
        {
            ConnectionId = connId,
            SourceGuid = sourceGuid,
            TargetGuid = targetGuid,
            ConnectionType = connectionType,
            Description = description
        });
        Dirty(router.Value);
        return connId;
    }

    public void UnregisterActiveConnection(EntityUid origin, string connectionId)
    {
        var router = FindNearestRouter(origin);
        if (router == null)
            return;

        if (router.Value.Comp.ActiveConnections.RemoveAll(c => c.ConnectionId == connectionId) > 0)
            Dirty(router.Value);
    }

    public void CloseConnectionById(Entity<NetworkRouterComponent> router, string connectionId)
    {
        _bandwidth.CancelRouterTransferByConnectionId(router, connectionId, "Transfer terminated by router administrator.");

        var removed = router.Comp.ActiveConnections.Where(c => c.ConnectionId == connectionId).ToList();
        if (router.Comp.ActiveConnections.RemoveAll(c => c.ConnectionId == connectionId) > 0)
            Dirty(router);

        foreach (var rec in removed)
            TerminateSessionsForConnection(rec.SourceGuid, rec.TargetGuid);
    }

    public void BlacklistGuid(Entity<NetworkRouterComponent> router, string guid)
    {
        var clean = guid.Trim().ToUpperInvariant();
        if (router.Comp.BlacklistedGuids.Add(clean))
            Dirty(router);

        _bandwidth.CancelTransfersForGuid(router, clean, "Device blacklisted on station router.");
        TerminateAllSessionsForGuid(clean);
    }

    public void UnblacklistGuid(Entity<NetworkRouterComponent> router, string guid)
    {
        var clean = guid.Trim().ToUpperInvariant();
        if (router.Comp.BlacklistedGuids.Remove(clean))
            Dirty(router);
    }

    public void TerminateAllSessionsForGuid(string guid)
    {
        var screenSys = EntityManager.System<ServerRemoteScreenSystem>();
        if (TryFindDeviceByGuid(guid, out var directTarget))
            screenSys.DetachViewer(EntityUid.Invalid, targetComputerUid: directTarget);

        var termQuery = EntityQueryEnumerator<TerminalProgramComponent, ProgramComponent>();
        while (termQuery.MoveNext(out var progUid, out var term, out var prog))
        {
            var localGuid = prog.Computer is { } c ? EnsureDeviceGuid(c) : string.Empty;
            if (term.ConnectedRemoteGuid == guid || localGuid == guid)
            {
                var prevRemote = term.ConnectedRemoteGuid;
                EntityUid? prevTargetUid = prevRemote != null && TryFindDeviceByGuid(prevRemote, out var rt) ? rt : null;

                term.ConnectedRemoteGuid = null;
                term.IsScreenAttached = false;
                term.History.Add($"[NETWORK] Session terminated by router firewall ({guid} blacklisted).");

                if (prevTargetUid != null)
                    screenSys.DetachViewer(progUid, targetComputerUid: prevTargetUid);

                if (localGuid == guid)
                {
                    DetachComputerViewers(screenSys, progUid, prog.Computer, prevTargetUid);
                }

                if (_ui.HasUi(progUid, ProgramUiKey.Key))
                    EntityManager.System<TerminalSystem>().UpdateTerminalUi(progUid);
            }
        }
    }

    private void TerminateSessionsForConnection(string srcGuid, string dstGuid)
    {
        var screenSys = EntityManager.System<ServerRemoteScreenSystem>();
        var termQuery = EntityQueryEnumerator<TerminalProgramComponent, ProgramComponent>();
        while (termQuery.MoveNext(out var progUid, out var term, out var prog))
        {
            var localGuid = prog.Computer is { } c ? EnsureDeviceGuid(c) : string.Empty;
            if ((localGuid == srcGuid && term.ConnectedRemoteGuid == dstGuid) ||
                (localGuid == dstGuid && term.ConnectedRemoteGuid == srcGuid))
            {
                var prevRemote = term.ConnectedRemoteGuid;
                EntityUid? prevTargetUid = prevRemote != null && TryFindDeviceByGuid(prevRemote, out var rt) ? rt : null;

                term.ConnectedRemoteGuid = null;
                term.IsScreenAttached = false;
                term.History.Add("[NETWORK] Connection forcefully dropped by station router.");

                DetachComputerViewers(screenSys, progUid, prog.Computer, prevTargetUid);

                if (_ui.HasUi(progUid, ProgramUiKey.Key))
                    EntityManager.System<TerminalSystem>().UpdateTerminalUi(progUid);
            }
        }
    }

    private void DetachComputerViewers(ServerRemoteScreenSystem screenSys, EntityUid progUid, EntityUid? compUid, EntityUid? targetCompUid)
    {
        var actors = new HashSet<EntityUid>();
        if (TryComp<UserInterfaceComponent>(progUid, out var pUi))
        {
            foreach (var actorSet in pUi.Actors.Values)
            {
                foreach (var act in actorSet)
                    actors.Add(act);
            }
        }

        if (compUid is { } cUid && TryComp<UserInterfaceComponent>(cUid, out var cUi))
        {
            foreach (var actorSet in cUi.Actors.Values)
            {
                foreach (var act in actorSet)
                    actors.Add(act);
            }
        }

        foreach (var act in actors)
            screenSys.DetachViewer(progUid, act, targetCompUid);
    }

    public void RefreshAllRouterProgramUis()
    {
        var q = EntityQueryEnumerator<RouterProgramComponent>();
        while (q.MoveNext(out var uid, out _))
            UpdateRouterProgramUi(uid);
    }

    public void RefreshAllPacketSniffers()
    {
        var q = EntityQueryEnumerator<PacketSnifferProgramComponent>();
        while (q.MoveNext(out var uid, out _))
            UpdatePacketSnifferUi(uid);
    }

    public void UpdateRouterProgramUi(EntityUid progUid)
    {
        if (!TryComp<RouterProgramComponent>(progUid, out var rProg))
            return;

        var compUid = ResolveComputerFromProgram(progUid);
        if (_computer.IsHostLocked(compUid))
        {
            if (_ui.HasUi(progUid, ProgramUiKey.Key))
            {
                _ui.SetUiState(progUid, ProgramUiKey.Key, new RouterUiState(
                    "LOCKED",
                    false,
                    0,
                    0,
                    new List<RouterConnectedDeviceDto>(),
                    new List<ActiveNetworkConnectionRecord>(),
                    new List<NetworkDataPacket>(),
                    new List<string>(),
                    new Dictionary<string, string>(),
                    "LOCKED — Swipe authorized ID card to unlock.",
                    isLocked: true));
            }
            return;
        }

        var router = FindActiveRouter(compUid);
        var bw = router != null ? CompOrNull<NetworkBandwidthComponent>(UnwrapComputerEntity(router.Value.Owner)) : null;

        var devices = new List<RouterConnectedDeviceDto>();
        if (router != null)
        {
            RefreshRouterDevices(router.Value);
            foreach (var guid in router.Value.Comp.ConnectedDevices)
            {
                if (!TryFindDeviceByGuid(guid, out var dUid))
                    continue;

                var dStats = CompOrNull<ComputerStatsComponent>(dUid);
                devices.Add(new RouterConnectedDeviceDto(
                    MetaData(dUid).EntityName,
                    guid,
                    dStats?.NetworkBandwidthBps ?? 0,
                    dStats?.NetworkUploadBandwidthBps ?? 0,
                    HasActiveNetworkCard(dUid),
                    router.Value.Comp.BlacklistedGuids.Contains(guid)));
            }
        }

        var state = new RouterUiState(
            router != null ? EnsureDeviceGuid(router.Value.Owner) : "NO-ROUTER",
            router != null && IsRouterOperational(router.Value.Owner),
            bw?.MaxBps ?? 0,
            bw?.MaxUploadBps ?? 0,
            devices,
            router?.Comp.ActiveConnections ?? new(),
            router?.Comp.RecordedPackets ?? new(),
            router?.Comp.BlacklistedGuids.ToList() ?? new(),
            router?.Comp.DnsRecords ?? new(),
            rProg.StatusMessage);

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, state);
    }

    public void UpdatePacketSnifferUi(EntityUid progUid)
    {
        if (!TryComp<PacketSnifferProgramComponent>(progUid, out var sProg))
            return;

        var compUid = ResolveComputerFromProgram(progUid);
        var hasNic = HasActiveNetworkCard(compUid);
        var router = hasNic ? FindNearestRouter(compUid) : null;
        var packets = new List<NetworkDataPacket>();

        if (hasNic)
        {
            if (router != null)
                packets.AddRange(router.Value.Comp.RecordedPackets);
            else if (TryComp<NetworkBandwidthComponent>(compUid, out var bw))
                packets.AddRange(bw.StoredPackets);
        }

        var localGuid = EnsureDeviceGuid(compUid);
        var sinkTargets = CollectTargetDeviceSinkPorts(
            compUid,
            localGuid,
            sProg.SelectedTargetGuid,
            hasNic,
            router,
            out var resolvedTargetGuid,
            out var resolvedTargetName);

        var statusMsg = router != null && IsUsingDebugFallbackRouter(router.Value)
            ? $"{sProg.StatusMessage} [Global Net: {MetaData(router.Value.Owner).EntityName}]"
            : sProg.StatusMessage;

        var state = new PacketSnifferUiState(
            router != null ? EnsureDeviceGuid(router.Value.Owner) : localGuid,
            localGuid,
            hasNic,
            packets,
            sinkTargets,
            statusMsg,
            resolvedTargetGuid,
            resolvedTargetName);

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, state);
    }

    private List<SnifferSinkTargetDto> CollectTargetDeviceSinkPorts(
        EntityUid localCompUid,
        string localGuid,
        string selectedTargetGuid,
        bool hasNic,
        Entity<NetworkRouterComponent>? router,
        out string resolvedTargetGuid,
        out string resolvedTargetName)
    {
        var list = new List<SnifferSinkTargetDto>();
        resolvedTargetGuid = string.Empty;
        resolvedTargetName = string.Empty;

        if (string.IsNullOrWhiteSpace(selectedTargetGuid))
            return list;

        var cleanGuid = selectedTargetGuid.Trim().ToUpperInvariant();
        EntityUid targetCompUid;
        bool isLocal;

        if (cleanGuid.Equals(localGuid, StringComparison.OrdinalIgnoreCase))
        {
            targetCompUid = localCompUid;
            isLocal = true;
        }
        else
        {
            if (!hasNic || router == null || !TryFindDeviceByGuid(cleanGuid, out targetCompUid))
                return list;

            if (targetCompUid != localCompUid && !AreEndpointsConnected(localCompUid, targetCompUid, out _, out _))
                return list;

            isLocal = targetCompUid == localCompUid;
        }

        resolvedTargetGuid = EnsureDeviceGuid(targetCompUid);
        resolvedTargetName = _computerQuery.TryComp(targetCompUid, out var comp) &&
                             comp.HostMachine is { Valid: true } hm && Exists(hm)
            ? MetaData(hm).EntityName
            : MetaData(targetCompUid).EntityName;

        foreach (var port in _computer.GetMirroredPorts(targetCompUid))
        {
            if (port.IsSource)
                continue;

            list.Add(new SnifferSinkTargetDto(
                resolvedTargetGuid,
                resolvedTargetName,
                port.PortId,
                port.PortName,
                isLocal,
                port.IsHostPort));
        }

        return list;
    }

    public static string GetPacketReadableSummary(NetworkDataPacket pkt)
    {
        var timeStr = pkt.Timestamp.ToString(@"mm\:ss");
        var flag = pkt.Dropped ? " [DROPPED]" : pkt.IsEncrypted ? " [ENC]" : "";
        return $"[{timeStr}] {pkt.SourceAddress} -> {pkt.TargetAddress} [{pkt.Protocol}] {pkt.ByteSize}B{flag}: {pkt.PayloadSummary}";
    }
}
