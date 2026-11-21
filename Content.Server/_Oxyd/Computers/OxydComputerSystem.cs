using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._Oxyd.Computers.Network;
using Content.Server._Oxyd.Computers.Programs;
using Content.Server._Oxyd.Computers.ScreenSharing;
using Content.Server.Power.EntitySystems;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Packets;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared._Oxyd.Computers.ScreenSharing;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Components;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.DeviceLinking.Systems;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.DeviceNetwork.Systems;
using Content.Shared.Lock;
using Content.Shared.PDA;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.UserInterface;
using Content.Shared.Wires;
using Robust.Server.GameStates;
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
    [Dependency] private ServerRemoteScreenSystem _remoteScreen = default!;

    private EntityQuery<OxydComputerComponent> _computerQuery;
    private EntityQuery<EmbeddedComputerHostComponent> _hostQuery;
    private EntityQuery<ComputerStatsComponent> _statsQuery;
    private EntityQuery<ProgramComponent> _programQuery;
    private EntityQuery<DeviceLinkSourceComponent> _sourceQuery;
    private EntityQuery<DeviceLinkSinkComponent> _sinkQuery;

    private static readonly ProtoId<SourcePortPrototype> PortComputerCommandSender = "ComputerCommandSender";
    private static readonly ProtoId<SinkPortPrototype> PortComputerStatusReceiver = "ComputerStatusReceiver";
    private static readonly ProtoId<SourcePortPrototype> PortMachineStatusSender = "MachineStatusSender";
    private static readonly ProtoId<SinkPortPrototype> PortMachineCommandReceiver = "MachineCommandReceiver";

    public override void Initialize()
    {
        base.Initialize();

        _computerQuery = GetEntityQuery<OxydComputerComponent>();
        _hostQuery = GetEntityQuery<EmbeddedComputerHostComponent>();
        _statsQuery = GetEntityQuery<ComputerStatsComponent>();
        _programQuery = GetEntityQuery<ProgramComponent>();
        _sourceQuery = GetEntityQuery<DeviceLinkSourceComponent>();
        _sinkQuery = GetEntityQuery<DeviceLinkSinkComponent>();

        SubscribeLocalEvent<EmbeddedComputerHostComponent, MapInitEvent>(OnHostMapInit);
        SubscribeLocalEvent<EmbeddedComputerHostComponent, ComponentShutdown>(OnHostShutdown);
        SubscribeLocalEvent<EmbeddedComputerHostComponent, EmbeddedComputerHostActivatedEvent>(OnHostActivated);
        SubscribeLocalEvent<EmbeddedComputerHostComponent, PowerChangedEvent>(OnHostPowerChanged);
        SubscribeLocalEvent<EmbeddedComputerHostComponent, SignalReceivedEvent<ComputerPacket>>(OnHostPacketReceived);
        SubscribeLocalEvent<EmbeddedComputerHostComponent, SignalReceivedEvent<ComputerDeviceLinkPayload>>(OnHostDeviceLinkPayloadReceived);
        SubscribeLocalEvent<EmbeddedComputerHostComponent, SignalReceivedEvent>(OnHostBoolSignalReceived);

        SubscribeLocalEvent<OxydComputerComponent, MapInitEvent>(OnComputerMapInit);
        SubscribeLocalEvent<OxydComputerComponent, ComponentShutdown>(OnComputerShutdown);
        SubscribeLocalEvent<OxydComputerComponent, EntInsertedIntoContainerMessage>(OnHardwareOrProgramInserted);
        SubscribeLocalEvent<OxydComputerComponent, EntRemovedFromContainerMessage>(OnHardwareOrProgramRemoved);
        SubscribeLocalEvent<OxydComputerComponent, SignalReceivedEvent<ComputerPacket>>(OnComputerPacketReceived);
        SubscribeLocalEvent<OxydComputerComponent, SignalReceivedEvent<ComputerDeviceLinkPayload>>(OnComputerDeviceLinkPayloadReceived);
        SubscribeLocalEvent<OxydComputerComponent, SignalReceivedEvent>(OnComputerBoolSignalReceived);
        SubscribeLocalEvent<OxydComputerComponent, BoundUIClosedEvent>(OnComputerUiClosed);
        SubscribeLocalEvent<OxydComputerComponent, ComputerRetryBootMessage>(OnRetryBootMessage);
    }

    private void OnHostMapInit(Entity<EmbeddedComputerHostComponent> ent, ref MapInitEvent args)
    {
        EnsureComputerBoundToHost(ent, out _);
    }

    private void OnHostShutdown(Entity<EmbeddedComputerHostComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.ComputerEntity is { } compUid && Exists(compUid))
            QueueDel(compUid);
    }

    private void OnComputerMapInit(Entity<OxydComputerComponent> ent, ref MapInitEvent args)
    {
        _container.EnsureContainer<Container>(ent.Owner, ModularComputerComponent.ContainerHardware);
        _container.EnsureContainer<Container>(ent.Owner, OxydComputerComponent.ContainerPrograms);
        _guid.GetGuid(ent.Owner);

        EnsureHardwareSlotsAndDefaults(ent.Owner, ent.Comp);
        RecalculateHardwareStats(ent.Owner, ent.Comp);
        RefreshComputerBootState(ent.Owner, ent.Comp);
    }

    private void OnComputerShutdown(Entity<OxydComputerComponent> ent, ref ComponentShutdown args)
    {
        ShutDownComputer(ent.Owner, ent.Comp, updateUi: false);
    }

    private void OnHostPowerChanged(Entity<EmbeddedComputerHostComponent> ent, ref PowerChangedEvent args)
    {
        if (ent.Comp.ComputerEntity is not { } compUid || !_computerQuery.TryComp(compUid, out var comp))
            return;

        comp.IsPowered = args.Powered;
        Dirty(compUid, comp);
        RecalculateHardwareStats(compUid, comp);
        RefreshComputerBootState(compUid, comp);
    }

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

    private void OnRetryBootMessage(Entity<OxydComputerComponent> ent, ref ComputerRetryBootMessage args)
    {
        RecalculateHardwareStats(ent.Owner, ent.Comp);
        RefreshComputerBootState(ent.Owner, ent.Comp);
        OpenRunningProgramUisForCurrentViewers(ent.Owner, ent.Comp);
    }

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
    }

    private void OnHardwareOrProgramInserted(Entity<OxydComputerComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == OxydComputerComponent.ContainerPrograms)
            return;

        RecalculateHardwareStats(ent.Owner, ent.Comp);
        RefreshComputerBootState(ent.Owner, ent.Comp);
    }

    private void OnHardwareOrProgramRemoved(Entity<OxydComputerComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == OxydComputerComponent.ContainerPrograms)
            return;

        RecalculateHardwareStats(ent.Owner, ent.Comp);
        RefreshComputerBootState(ent.Owner, ent.Comp);
    }

    private void OnHostPacketReceived(Entity<EmbeddedComputerHostComponent> ent, ref SignalReceivedEvent<ComputerPacket> args)
    {
        if (ent.Comp.ComputerEntity is { } compUid)
            RelayDeviceLinkToPrograms(compUid, ent.Owner, args.Port, args.Trigger, null, args.Data, null);
    }

    private void OnHostDeviceLinkPayloadReceived(Entity<EmbeddedComputerHostComponent> ent, ref SignalReceivedEvent<ComputerDeviceLinkPayload> args)
    {
        if (ent.Comp.ComputerEntity is { } compUid)
            RelayDeviceLinkToPrograms(compUid, ent.Owner, args.Port, args.Trigger, args.Data, null, null);
    }

    private void OnHostBoolSignalReceived(Entity<EmbeddedComputerHostComponent> ent, ref SignalReceivedEvent args)
    {
        if (ent.Comp.ComputerEntity is { } compUid)
            RelayDeviceLinkToPrograms(compUid, ent.Owner, args.Port, args.Trigger, null, null, true);
    }

    private void OnComputerPacketReceived(Entity<OxydComputerComponent> ent, ref SignalReceivedEvent<ComputerPacket> args)
    {
        RelayDeviceLinkToPrograms(ent.Owner, ent.Owner, args.Port, args.Trigger, null, args.Data, null);
    }

    private void OnComputerDeviceLinkPayloadReceived(Entity<OxydComputerComponent> ent, ref SignalReceivedEvent<ComputerDeviceLinkPayload> args)
    {
        RelayDeviceLinkToPrograms(ent.Owner, ent.Owner, args.Port, args.Trigger, args.Data, null, null);
    }

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
            return true;
        }

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

            if (!string.IsNullOrEmpty(host.Comp.EmbeddedDefaultAppPath))
                comp.EmbeddedDefaultAppPath = host.Comp.EmbeddedDefaultAppPath;

            if (host.Comp.DefaultHardware.Count > 0)
            {
                ClearHardwareParts(compUid, comp);
                comp.DefaultHardware = new List<EntProtoId>(host.Comp.DefaultHardware);
                EnsureHardwareSlotsAndDefaults(compUid, comp);
            }

            if (TryComp<Content.Server.Power.Components.ApcPowerReceiverComponent>(host.Owner, out var apcRecv))
                comp.IsPowered = apcRecv.Powered;

            Dirty(compUid, comp);
            BindHostDeviceLinks(host.Owner, compUid);
            RecalculateHardwareStats(compUid, comp);
            RefreshComputerBootState(compUid, comp);
            SendPacketToHost(compUid, "MachineCommandReceiver", new ComputerPacket("status"));
            if (comp.BootProgram is { } bootProg && Exists(bootProg))
            {
                _osPrograms.UpdateOsUi(bootProg);
                _pdaOs.UpdatePdaOsUi(bootProg);
            }
        }

        return true;
    }

    /// <summary>
    /// Connects the computer entity to ALL source and sink ports on its represented host machine via DeviceLinkSystem.
    /// </summary>
    public void BindHostDeviceLinks(EntityUid hostUid, EntityUid compUid)
    {
        _deviceLink.EnsureSourcePort(compUid, PortComputerCommandSender);
        _deviceLink.EnsureSinkPort(compUid, PortComputerStatusReceiver);

        var outLinks = new List<DeviceLink>();
        if (_sinkQuery.TryComp(hostUid, out var hostSinks))
        {
            foreach (var sinkPort in hostSinks.Ports)
            {
                outLinks.Add(new DeviceLink(PortComputerCommandSender, sinkPort));
            }
        }
        if (outLinks.Count > 0)
            _deviceLink.SaveLinks(null, compUid, hostUid, outLinks);

        if (_sourceQuery.TryComp(hostUid, out var hostSources))
        {
            var inLinks = new List<DeviceLink>();
            foreach (var srcPort in hostSources.Ports)
            {
                inLinks.Add(new DeviceLink(srcPort, PortComputerStatusReceiver));
            }
            if (inLinks.Count > 0)
                _deviceLink.SaveLinks(null, hostUid, compUid, inLinks);
        }
    }

    private void ClearHardwareParts(EntityUid compUid, OxydComputerComponent comp)
    {
        if (_container.TryGetContainer(compUid, ModularComputerComponent.ContainerHardware, out var container))
        {
            foreach (var item in container.ContainedEntities.ToList())
            {
                _container.Remove(item, container, force: true);
                QueueDel(item);
            }
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
        if (hwContainer.ContainedEntities.Count > 0)
            return;

        if (comp.DefaultHardware.Count == 0)
            return;

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

    private bool TryInsertHardwarePartIntoSlot(EntityUid compUid, EntityUid partUid)
    {
        string? preferredSlot = null;
        if (HasComp<ComputerCpuComponent>(partUid))
            preferredSlot = ModularComputerComponent.SlotCpu;
        else if (HasComp<ComputerHardDriveComponent>(partUid))
            preferredSlot = ModularComputerComponent.SlotStorage;
        else if (HasComp<ComputerNetworkCardComponent>(partUid))
            preferredSlot = ModularComputerComponent.SlotNetwork;
        else if (HasComp<ComputerBatteryModuleComponent>(partUid))
            preferredSlot = ModularComputerComponent.SlotBattery;
        else if (HasComp<ComputerSensorModuleComponent>(partUid))
            preferredSlot = ModularComputerComponent.SlotSensor;
        else if (HasComp<ComputerIdCardSlotModuleComponent>(partUid))
            preferredSlot = ModularComputerComponent.SlotIdModule;
        else if (HasComp<ComputerDiskSlotModuleComponent>(partUid))
            preferredSlot = ModularComputerComponent.SlotDiskModule;

        if (preferredSlot != null && _itemSlots.TryGetSlot(compUid, preferredSlot, out var slot) && slot.Item == null)
            return _itemSlots.TryInsert(compUid, slot, partUid, null);

        if (HasComp<ComputerRamComponent>(partUid))
        {
            if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotRam, out var ram1) && ram1.Item == null)
                return _itemSlots.TryInsert(compUid, ram1, partUid, null);
            if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotRamSecondary, out var ram2) && ram2.Item == null)
                return _itemSlots.TryInsert(compUid, ram2, partUid, null);
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
            if (HasComp<ComputerIdCardSlotModuleComponent>(part))
                stats.HasIdSlotModule = true;
            if (HasComp<ComputerDiskSlotModuleComponent>(part))
                stats.HasDiskSlotModule = true;
        }

        // Sync running resource footprint.
        var usedRam = 0;
        var cpuLoad = 0f;
        foreach (var progUid in comp.RunningPrograms)
        {
            if (!_programQuery.TryComp(progUid, out var prog))
                continue;

            usedRam += ComputerMetricsHelper.GetProgramRamUsageMb(prog);
            cpuLoad += ComputerMetricsHelper.GetProgramCpuUsagePercent(prog);
        }
        stats.UsedRamMb = usedRam;
        stats.CurrentCpuLoadPercent = cpuLoad;

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
        if (comp.BootProgram is { } bootUid && Exists(bootUid))
            RaiseLocalEvent(bootUid, ref hwEv);
    }

    public void RefreshComputerBootState(EntityUid compUid, OxydComputerComponent? comp = null)
    {
        if (!_computerQuery.Resolve(compUid, ref comp, false))
            return;

        if (!_statsQuery.TryComp(compUid, out var stats))
        {
            RecalculateHardwareStats(compUid, comp);
            stats = Comp<ComputerStatsComponent>(compUid);
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
        if (execProto.Components.TryGetValue("Program", out var progEntry) && progEntry.Component is ProgramComponent defProg)
            bootRam = Math.Max(1, defProg.RamUsageMb);

        if (bootRam > stats.RamMb || stats.MaxConcurrentPrograms < 1)
        {
            FailBoot(compUid, comp, ComputerBootState.NoRam, $"BOOT FAILURE: '{firstPath}' requires {bootRam} MB RAM ({stats.RamMb} MB installed).");
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

        stats.IsBooted = true;
        stats.BootError = string.Empty;
        stats.UsedRamMb = ComputerMetricsHelper.GetProgramRamUsageMb(pComp);
        stats.CurrentCpuLoadPercent = ComputerMetricsHelper.GetProgramCpuUsagePercent(pComp);
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

    public void ShutDownComputer(EntityUid compUid, OxydComputerComponent? comp = null, bool updateUi = true)
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
        Dirty(compUid, comp);

        if (_statsQuery.TryComp(compUid, out var stats))
        {
            stats.UsedRamMb = 0;
            stats.CurrentCpuLoadPercent = 0f;
            stats.IsBooted = false;
            Dirty(compUid, stats);
        }

        if (updateUi)
            UpdateComputerDisplayUi(compUid, comp);
    }

    public void RebootComputer(EntityUid compUid, OxydComputerComponent? comp = null)
    {
        if (!_computerQuery.Resolve(compUid, ref comp, false))
            return;

        ShutDownComputer(compUid, comp, updateUi: false);
        RecalculateHardwareStats(compUid, comp);
        RefreshComputerBootState(compUid, comp);
    }

    private void TrimExcessProgramsToHardware(EntityUid compUid, OxydComputerComponent comp, ComputerStatsComponent stats)
    {
        var nonBoot = comp.RunningPrograms.Where(p => p != comp.BootProgram).ToList();
        while (nonBoot.Count > 0 && (comp.RunningPrograms.Count > stats.MaxConcurrentPrograms || stats.UsedRamMb > stats.RamMb))
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

        var ramNeeded = execFile.RamUsageMb;
        if (progProto.Components.TryGetValue("Program", out var compReg) && compReg.Component is ProgramComponent defComp)
            ramNeeded = Math.Max(1, defComp.RamUsageMb);

        RecalculateHardwareStats(compUid, comp);
        var stats = Comp<ComputerStatsComponent>(compUid);

        if (comp.RunningPrograms.Count >= stats.MaxConcurrentPrograms)
        {
            error = $"CPU limit reached ({comp.RunningPrograms.Count}/{stats.MaxConcurrentPrograms} active programs).";
            return false;
        }

        if (stats.UsedRamMb + ramNeeded > stats.RamMb)
        {
            error = $"Insufficient RAM: needs {ramNeeded} MB, only {Math.Max(0, stats.RamMb - stats.UsedRamMb)} MB free.";
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
                pdaOsComp.CurrentView = 3; // Active child application view
                Dirty(bootUid, pdaOsComp);
                _pdaOs.UpdatePdaOsUi(bootUid);
            }
            else if (TryComp<EmbeddedOsProgramComponent>(bootUid, out var embComp))
            {
                embComp.CurrentView = 0; // Active child application view
                Dirty(bootUid, embComp);
                _osPrograms.UpdateOsUi(bootUid);
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
                    osComp.CurrentView = 1; // Return to Apps launcher
                    Dirty(bootUid, osComp);
                }
                else if (TryComp<PdaOsProgramComponent>(bootUid, out var pdaOsComp) && pdaOsComp.CurrentView == 3)
                {
                    pdaOsComp.CurrentView = 1; // Return to Apps launcher
                    Dirty(bootUid, pdaOsComp);
                }
                else if (TryComp<EmbeddedOsProgramComponent>(bootUid, out var embComp))
                {
                    embComp.CurrentView = 1;
                    Dirty(bootUid, embComp);
                }
            }

            _osPrograms.UpdateOsUi(bootUid);
            _pdaOs.UpdatePdaOsUi(bootUid);
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
            stats.HasDataPort);

        var key = GetPrimaryUiKey(compUid, comp);
        if (_ui.HasUi(compUid, key))
            _ui.SetUiState(compUid, key, state);
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
                }
                else
                {
                    _deviceLink.InvokeDirect(hostUid, srcPortProto, payload);
                    if (string.IsNullOrEmpty(command))
                        _deviceLink.SendSignal(hostUid, srcPortProto, true);
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
                    _deviceLink.InvokeDirect(compUid, hostUid, sinkPortProto, packet);
                }
                else
                {
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
                _deviceLink.InvokePort(compUid, srcPortProto, ref packet);
            else if (!string.IsNullOrEmpty(command))
                _deviceLink.InvokePort(compUid, srcPortProto, ref payload);
            else
                _deviceLink.SendSignal(compUid, srcPortProto, true);

            return true;
        }

        if (_sinkQuery.TryComp(compUid, out var ownSink) && Array.Exists(_deviceLink.GetSinkPortIds((compUid, ownSink)), p => p == sinkPortProto))
        {
            if (packet != null)
            {
                var sigPkt = new SignalReceivedEvent<ComputerPacket>(sinkPortProto, packet, actor);
                RaiseLocalEvent(compUid, ref sigPkt);
            }
            else
            {
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
        if (!_computerQuery.TryComp(compUid, out var comp))
            return false;

        if (comp.HostMachine is { } hostUid && Exists(hostUid))
        {
            if (TryComp<PdaComponent>(hostUid, out var pda))
            {
                var pdaCard = pda.IdSlot.Item ?? pda.ContainedId;
                if (pdaCard is { Valid: true } pId && Exists(pId) && HasComp<IdCardComponent>(pId))
                {
                    idCardUid = pId;
                    return true;
                }
            }

            if (TryComp<ItemSlotsComponent>(hostUid, out var hostSlots))
            {
                foreach (var slot in hostSlots.Slots.Values)
                {
                    if (slot.Item is { Valid: true } sId && Exists(sId) && HasComp<IdCardComponent>(sId))
                    {
                        idCardUid = sId;
                        return true;
                    }
                }
            }
        }

        if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotIdModule, out var modSlot) &&
            modSlot.Item is { Valid: true } modId &&
            Exists(modId) &&
            HasComp<IdCardComponent>(modId))
        {
            idCardUid = modId;
            return true;
        }

        return false;
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
