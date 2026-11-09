using System.Linq;
using System.Text;
using Content.Server._Oxyd.Computers.Network;
using Content.Server._Oxyd.Computers.ScreenSharing;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Monitor.Components;
using Content.Server.Atmos.Monitor.Systems;
using Content.Shared.DeviceLinking.Systems;
using Content.Shared.UserInterface;
using Robust.Server.GameStates;
using Robust.Server.Player;
using Robust.Shared.Player;
using Content.Server.Lathe;
using Content.Server.PDA;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared.Atmos.Monitor.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.APC;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Components;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.PDA;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Power;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Research.Systems;
using Content.Server.Access.Systems;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Access.Systems;
using Content.Server.Instruments;
using Content.Server.PDA.Ringer;
using Content.Shared.Access.Components;
using Content.Shared.Instruments;
using Content.Shared.PDA.Ringer;
using Content.Shared.Store.Components;
using Content.Shared.Store;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers;

public sealed partial class OxydComputerSystem : EntitySystem
{
    [Dependency] private AirAlarmSystem _airAlarm = default!;
    [Dependency] private ApcSystem _apc = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private DeviceLinkSystem _deviceLink = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private LatheSystem _lathe = default!;
    [Dependency] private NetworkBandwidthSystem _bandwidth = default!;
    [Dependency] private NetworkRouterSystem _router = default!;

    [Dependency] private SharedDeviceGuidSystem _guidSystem = default!;
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private IdCardSystem _idCard = default!;
    [Dependency] private InstrumentSystem _instrument = default!;
    [Dependency] private PdaSystem _pda = default!;
    [Dependency] private RingerSystem _ringer = default!;
    [Dependency] private PvsOverrideSystem _pvsOverride = default!;
    [Dependency] private ServerRemoteScreenSystem _remoteScreen = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedMaterialStorageSystem _materialStorage = default!;
    [Dependency] private SharedResearchSystem _research = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private UnpoweredFlashlightSystem _unpoweredFlashlight = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    public const string ComputerCommandPort = "ComputerCommandSender";
    public const string ComputerStatusSinkPort = "ComputerStatusReceiver";
    public const string MachineCommandSinkPort = "MachineCommandReceiver";
    public const string MachineStatusSourcePort = "MachineStatusSender";

    private bool _spawningDefaultHardware;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OxydComputerComponent, ComponentInit>(OnComputerInit);
        SubscribeLocalEvent<OxydComputerComponent, MapInitEvent>(OnComputerMapInit);
        SubscribeLocalEvent<OxydComputerComponent, EntInsertedIntoContainerMessage>(OnHardwareModified);
        SubscribeLocalEvent<OxydComputerComponent, EntRemovedFromContainerMessage>(OnHardwareModified);
        SubscribeLocalEvent<OxydComputerComponent, PowerChangedEvent>(OnComputerPowerChanged);
        SubscribeLocalEvent<OxydComputerComponent, ExecuteComputerCommandEvent>(OnExecuteComputerCommand);
        SubscribeLocalEvent<OxydComputerComponent, SignalReceivedEvent>(OnComputerSignalReceived);
        SubscribeLocalEvent<OxydComputerComponent, SignalReceivedEvent<ComputerDeviceLinkPayload>>(OnComputerPayloadSignalReceived);

        SubscribeLocalEvent<EmbeddedComputerHostComponent, ComponentInit>(OnEmbeddedHostInit);
        SubscribeLocalEvent<EmbeddedComputerHostComponent, MapInitEvent>(OnEmbeddedHostMapInit);
        SubscribeLocalEvent<EmbeddedComputerHostComponent, UserOpenActivatableUIAttemptEvent>(OnEmbeddedHostOpenUiAttempt);

        // OS Cartridge UI
        SubscribeLocalEvent<OsCartridgeComponent, CartridgeUiReadyEvent>(OnOsUiReady);

        // Embedded OS Cartridge UI
        SubscribeLocalEvent<EmbeddedOsCartridgeComponent, CartridgeUiReadyEvent>(OnEmbeddedOsUiReady);
        SubscribeLocalEvent<EmbeddedOsCartridgeComponent, CartridgeMessageEvent>(OnEmbeddedOsUiMessage);
        SubscribeLocalEvent<OsCartridgeComponent, CartridgeActivatedEvent>(OnOsActivated);
        SubscribeLocalEvent<OsCartridgeComponent, CartridgeMessageEvent>(OnOsUiMessage);
        SubscribeLocalEvent<OsCartridgeComponent, OsUiMessageEvent>(OnOsUiMessageDirect);

        // Lathe Control Cartridge UI
        SubscribeLocalEvent<LatheControlCartridgeComponent, CartridgeUiReadyEvent>(OnLatheControlUiReady);
        SubscribeLocalEvent<LatheControlCartridgeComponent, CartridgeMessageEvent>(OnLatheControlUiMessage);

        // APC Control Cartridge UI
        SubscribeLocalEvent<ApcControlCartridgeComponent, CartridgeUiReadyEvent>(OnApcControlUiReady);
        SubscribeLocalEvent<ApcControlCartridgeComponent, CartridgeMessageEvent>(OnApcControlUiMessage);

        // Atmos Control Cartridge UI
        SubscribeLocalEvent<AtmosControlCartridgeComponent, CartridgeUiReadyEvent>(OnAtmosControlUiReady);
        SubscribeLocalEvent<AtmosControlCartridgeComponent, CartridgeMessageEvent>(OnAtmosControlUiMessage);
    }

    private void OnComputerInit(Entity<OxydComputerComponent> ent, ref ComponentInit args)
    {
        if (TryComp<ModularComputerComponent>(ent.Owner, out _))
        {
            _container.EnsureContainer<Container>(ent.Owner, ModularComputerComponent.ExtraDriveContainerId);
        }
    }

    private void OnComputerMapInit(Entity<OxydComputerComponent> ent, ref MapInitEvent args)
    {
        _router.EnsureDeviceGuid(ent.Owner);
        EnsureDefaultDeviceLinkPorts(ent.Owner);
        EnsureDefaultHardwareEntities(ent.Owner);

        if (ent.Comp.AutoLinkLocalPorts)
            AutoDiscoverAndLinkPorts(ent);

        RecalculateHardwareAndBoot(ent);
    }

    public void EnsureDefaultHardwareEntities(EntityUid uid)
    {
        if (!TryComp<ModularComputerComponent>(uid, out _))
            return;

        var container = _container.EnsureContainer<Container>(uid, ModularComputerComponent.ExtraDriveContainerId);
        if (container.ContainedEntities.Count > 0)
            return;

        var initialStats = CompOrNull<ComputerStatsComponent>(uid);
        var initialCpu = initialStats?.CpuFrequencyMhz ?? 400f;
        var initialRam = initialStats != null && initialStats.RamMb > 0 ? initialStats.RamMb : 512;
        var initialStorage = initialStats != null && initialStats.StorageBytes > 0 ? initialStats.StorageBytes : 2097152;
        var initialNetDown = initialStats != null && initialStats.NetworkBandwidthBps > 0 ? initialStats.NetworkBandwidthBps : 128000;
        var initialNetUp = initialStats != null && initialStats.NetworkUploadBandwidthBps > 0 ? initialStats.NetworkUploadBandwidthBps : 64000;

        var prevSpawning = _spawningDefaultHardware;
        _spawningDefaultHardware = true;
        try
        {
            if (_prototypeManager.HasIndex<EntityPrototype>("ComputerPartCpuBasic"))
            {
                var cpuEnt = Spawn("ComputerPartCpuBasic");
                if (TryComp<ComputerCpuComponent>(cpuEnt, out var cpuComp))
                    cpuComp.FrequencyMhz = initialCpu;
                _container.Insert(cpuEnt, container);
            }

            if (_prototypeManager.HasIndex<EntityPrototype>("ComputerPartRamBasic"))
            {
                var ramEnt = Spawn("ComputerPartRamBasic");
                if (TryComp<ComputerRamComponent>(ramEnt, out var ramComp))
                    ramComp.SizeMb = initialRam;
                _container.Insert(ramEnt, container);
            }

            if (_prototypeManager.HasIndex<EntityPrototype>("ComputerPartHardDriveBasic"))
            {
                var hddEnt = Spawn("ComputerPartHardDriveBasic");
                if (TryComp<ComputerStorageDeviceComponent>(hddEnt, out var storageComp))
                    storageComp.CapacityBytes = initialStorage;
                if (TryComp<ComputerHardDriveComponent>(hddEnt, out var hddComp))
                    hddComp.CapacityBytes = initialStorage;
                _container.Insert(hddEnt, container);
            }

            if (_prototypeManager.HasIndex<EntityPrototype>("ComputerPartNetworkCardBasic"))
            {
                var netEnt = Spawn("ComputerPartNetworkCardBasic");
                if (TryComp<ComputerNetworkCardComponent>(netEnt, out var netComp))
                {
                    netComp.BandwidthBps = initialNetDown;
                    netComp.MaxUploadBps = initialNetUp;
                }
                _container.Insert(netEnt, container);
            }

            if (_prototypeManager.HasIndex<EntityPrototype>("ComputerPartBatterySmall"))
            {
                var batEnt = Spawn("ComputerPartBatterySmall");
                _container.Insert(batEnt, container);
            }

            if (_prototypeManager.HasIndex<EntityPrototype>("ComputerPartDiskSlotModule"))
            {
                var diskSlotEnt = Spawn("ComputerPartDiskSlotModule");
                _container.Insert(diskSlotEnt, container);
            }
        }
        finally
        {
            _spawningDefaultHardware = prevSpawning;
        }
    }

    private void OnEmbeddedHostOpenUiAttempt(Entity<EmbeddedComputerHostComponent> ent, ref UserOpenActivatableUIAttemptEvent args)
    {
        if (args.Silent || args.Cancelled)
            return;

        if (ent.Comp.ComputerEntity is not { } compUid || !Exists(compUid))
            return;

        if (TryComp<ActorComponent>(args.User, out var actor))
        {
            _pvsOverride.AddSessionOverride(compUid, actor.PlayerSession);
            foreach (var prog in _cartridgeLoader.GetAllPrograms(compUid))
            {
                if (Exists(prog))
                    _pvsOverride.AddSessionOverride(prog, actor.PlayerSession);
            }
        }

        RefreshInstalledControlPrograms(compUid);
        if (TryComp<CartridgeLoaderComponent>(compUid, out var compLoader))
        {
            if (TryComp<OxydComputerComponent>(compUid, out var comp) && comp.BootProgram is { } boot && Exists(boot))
            {
                var readyEvt = new CartridgeUiReadyEvent((compUid, compLoader));
                RaiseLocalEvent(boot, ref readyEvt);
            }
            if (compLoader.ActiveProgram is { } activeProg && activeProg != (TryComp<OxydComputerComponent>(compUid, out var comp2) ? comp2.BootProgram : null) && Exists(activeProg))
            {
                var readyEvt = new CartridgeUiReadyEvent((compUid, compLoader));
                RaiseLocalEvent(activeProg, ref readyEvt);
            }
        }
        _pda.UpdatePdaUi(compUid);
        if (!_ui.TryOpenUi(compUid, PdaUiKey.Key, args.User))
            _ui.TryOpenUi(compUid, ComputerUiKey.Key, args.User);
        args.Cancel();
    }

    private void OnEmbeddedHostInit(Entity<EmbeddedComputerHostComponent> ent, ref ComponentInit args)
    {
        _container.EnsureContainer<Container>(ent.Owner, ent.Comp.ContainerId);
    }

    private void OnEmbeddedHostMapInit(Entity<EmbeddedComputerHostComponent> ent, ref MapInitEvent args)
    {
        EnsureDefaultMachinePorts(ent.Owner);

        var container = _container.EnsureContainer<Container>(ent.Owner, ent.Comp.ContainerId);
        if (ent.Comp.ComputerEntity == null || !Exists(ent.Comp.ComputerEntity.Value))
        {
            if (!_prototypeManager.HasIndex<EntityPrototype>(ent.Comp.ComputerPrototype))
                return;

            var compUid = Spawn(ent.Comp.ComputerPrototype);
            _container.Insert(compUid, container);
            ent.Comp.ComputerEntity = compUid;

            var oxydComp = EnsureComp<OxydComputerComponent>(compUid);
            oxydComp.HostMachine = ent.Owner;
            oxydComp.Template = ComputerUiTemplate.Embedded;

            if (ent.Comp.DefaultPrograms.Count > 0 && TryComp<CartridgeLoaderComponent>(compUid, out var loader))
            {
                foreach (var progProto in ent.Comp.DefaultPrograms)
                {
                    if (_prototypeManager.HasIndex<EntityPrototype>(progProto) &&
                        !_cartridgeLoader.GetAllPrograms(compUid).Any(p => string.Equals(MetaData(p).EntityPrototype?.ID, progProto.Id)))
                    {
                        _cartridgeLoader.InstallProgram((compUid, loader), progProto, deinstallable: false);
                    }
                }
            }

            EnsureDefaultDeviceLinkPorts(compUid);
            LinkComputerAndMachine(compUid, ent.Owner);
            RecalculateHardwareAndBoot((compUid, oxydComp));
            Dirty(ent);
        }
    }

    public void EnsureDefaultDeviceLinkPorts(EntityUid computerUid)
    {
        _deviceLink.EnsureSourcePort(computerUid, ComputerCommandPort);
        _deviceLink.EnsureSinkPort(computerUid, ComputerStatusSinkPort);
    }

    public void EnsureDefaultMachinePorts(EntityUid machineUid)
    {
        _deviceLink.EnsureSourcePort(machineUid, MachineStatusSourcePort);
        _deviceLink.EnsureSinkPort(machineUid, MachineCommandSinkPort);
    }

    public void LinkComputerAndMachine(EntityUid computerUid, EntityUid machineUid)
    {
        EnsureDefaultDeviceLinkPorts(computerUid);
        EnsureDefaultMachinePorts(machineUid);

        _deviceLink.SaveLinks(
            null,
            computerUid,
            machineUid,
            new List<DeviceLink> { new(ComputerCommandPort, MachineCommandSinkPort) });

        _deviceLink.SaveLinks(
            null,
            machineUid,
            computerUid,
            new List<DeviceLink> { new(MachineStatusSourcePort, ComputerStatusSinkPort) });
    }

    public void AutoDiscoverAndLinkPorts(Entity<OxydComputerComponent> ent)
    {
        if (ent.Comp.DeviceLinkWireCut)
            return;

        EnsureDefaultDeviceLinkPorts(ent.Owner);

        // If inside a host machine container, link directly with the parent host machine
        var parentUid = Transform(ent.Owner).ParentUid;
        if (parentUid.IsValid() && (HasComp<LatheComponent>(parentUid) || HasComp<ApcComponent>(parentUid) || HasComp<AirAlarmComponent>(parentUid)))
        {
            ent.Comp.HostMachine = parentUid;
            LinkComputerAndMachine(ent.Owner, parentUid);
            return;
        }

        if (ent.Comp.HostMachine is { } host && Exists(host))
        {
            LinkComputerAndMachine(ent.Owner, host);
        }
    }

    private void OnHardwareModified(Entity<OxydComputerComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (_spawningDefaultHardware || TerminatingOrDeleted(ent.Owner))
            return;

        RecalculateHardwareAndBoot(ent);
    }

    private void OnHardwareModified(Entity<OxydComputerComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (_spawningDefaultHardware || TerminatingOrDeleted(ent.Owner))
            return;

        RecalculateHardwareAndBoot(ent);
    }

    private void OnComputerPowerChanged(Entity<OxydComputerComponent> ent, ref PowerChangedEvent args)
    {
        if (TerminatingOrDeleted(ent.Owner))
            return;

        RecalculateHardwareAndBoot(ent);
    }

    public void RefreshComputerBootState(EntityUid uid, OxydComputerComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false))
            return;

        RecalculateHardwareAndBoot((uid, comp));
    }

    public void OnRemoteAccessRevoked(EntityUid uid)
    {
        var guid = _router.EnsureDeviceGuid(uid);
        _router.TerminateAllSessionsForGuid(guid);
        _remoteScreen.TerminateRemoteSessionsForDevice(uid);

        if (TryComp<OxydComputerComponent>(uid, out var comp) && comp.HostMachine is { } host && Exists(host))
        {
            var hostGuid = _router.EnsureDeviceGuid(host);
            _router.TerminateAllSessionsForGuid(hostGuid);
            _remoteScreen.TerminateRemoteSessionsForDevice(host);
        }
        else if (TryComp<EmbeddedComputerHostComponent>(uid, out var hostComp) && hostComp.ComputerEntity is { } compUid && Exists(compUid))
        {
            var compGuid = _router.EnsureDeviceGuid(compUid);
            _router.TerminateAllSessionsForGuid(compGuid);
            _remoteScreen.TerminateRemoteSessionsForDevice(compUid);
        }
    }

    public void OnRemoteControlCut(EntityUid uid, OxydComputerComponent comp)
    {
        OnRemoteAccessRevoked(uid);
        RefreshComputerBootState(uid, comp);
    }

    public void RecalculateHardwareAndBoot(Entity<OxydComputerComponent> ent)
    {
        var uid = ent.Owner;
        if (TerminatingOrDeleted(uid))
            return;

        var comp = ent.Comp;
        var stats = EnsureComp<ComputerStatsComponent>(uid);
        var bw = EnsureComp<NetworkBandwidthComponent>(uid);

        var storageDevices = new List<Entity<ComputerStorageDeviceComponent>>();
        var hardDrives = new List<Entity<ComputerHardDriveComponent>>();
        var ramModules = new List<ComputerRamComponent>();
        var cpuModules = new List<ComputerCpuComponent>();
        var netCards = new List<ComputerNetworkCardComponent>();
        var batteries = new List<ComputerBatteryModuleComponent>();

        CollectInstalledHardware(uid, storageDevices, hardDrives, ramModules, cpuModules, netCards, batteries);

        if (TryComp<ModularComputerComponent>(uid, out var modular))
        {
            var totalCpu = 0f;
            foreach (var cpu in cpuModules)
            {
                totalCpu += cpu.FrequencyMhz;
            }
            stats.CpuFrequencyMhz = totalCpu > 0 ? totalCpu : 100f;

            var totalRam = 0;
            var ramCount = 0;
            foreach (var ram in ramModules)
            {
                if (ramCount >= modular.MaxRamSlots)
                    break;
                totalRam += ram.SizeMb;
                ramCount++;
            }
            stats.RamMb = totalRam;

            var totalStorageBytes = 0;
            var hddCount = 0;
            foreach (var dev in storageDevices)
            {
                if (hddCount >= modular.MaxHardDriveSlots)
                    break;
                if (dev.Comp.Size <= modular.MaxHardDriveSize)
                {
                    totalStorageBytes += dev.Comp.CapacityBytes;
                    hddCount++;
                }
            }
            foreach (var hdd in hardDrives)
            {
                if (hddCount >= modular.MaxHardDriveSlots)
                    break;
                if (hdd.Comp.Size <= modular.MaxHardDriveSize)
                {
                    totalStorageBytes += hdd.Comp.CapacityBytes;
                    hddCount++;
                }
            }
            stats.StorageBytes = totalStorageBytes;
            stats.HardDriveCount = hddCount;

            if (TryComp<FileSystemComponent>(uid, out var fs))
                fs.MaxStorageBytes = Math.Max(1024, totalStorageBytes);

            if (netCards.Count > 0 && !comp.NetworkWireCut)
            {
                bw.MaxBps = netCards[0].BandwidthBps;
                bw.MaxUploadBps = netCards[0].MaxUploadBps;
                stats.NetworkBandwidthBps = netCards[0].BandwidthBps;
                stats.NetworkUploadBandwidthBps = netCards[0].MaxUploadBps;
            }
            else if (comp.NetworkWireCut)
            {
                bw.MaxBps = 0;
                bw.MaxUploadBps = 0;
                stats.NetworkBandwidthBps = 0;
                stats.NetworkUploadBandwidthBps = 0;
            }

            if (batteries.Count > 0 && batteries[0].Size <= modular.MaxBatterySize)
            {
                stats.HasBattery = true;
                stats.BatteryChargePercent = batteries[0].MaxCharge > 0
                    ? Math.Clamp(batteries[0].CurrentCharge / batteries[0].MaxCharge * 100f, 0f, 100f)
                    : 0f;
            }
            else
            {
                stats.HasBattery = false;
                stats.BatteryChargePercent = 0f;
            }
        }

        Dirty(uid, stats);
        Dirty(uid, bw);

        // Evaluate Boot Checks:
        // 1. Motherboard Power Wire
        if (comp.PowerWireCut)
        {
            SetBootFailure(ent, ComputerBootState.WireCut, "POST ERROR 0x01: Motherboard power bus severed.");
            return;
        }

        // 2. Power Check (APC Power Receiver or Battery Module)
        var hasPower = true;
        if (TryComp<ApcPowerReceiverComponent>(uid, out var powerReceiver) && comp.RequirePowerToBoot)
        {
            hasPower = powerReceiver.Powered || (stats.HasBattery && stats.BatteryChargePercent > 0f);
        }
        else if (comp.HostMachine is { } host && TryComp<ApcPowerReceiverComponent>(host, out var hostPower) && comp.RequirePowerToBoot)
        {
            hasPower = hostPower.Powered || (stats.HasBattery && stats.BatteryChargePercent > 0f);
        }

        if (!hasPower)
        {
            SetBootFailure(ent, ComputerBootState.NoPower, "POST ERROR 0x02: Insufficient power to boot computer and components.");
            return;
        }

        // 3. RAM Check
        if (comp.RequireRamToBoot && stats.RamMb <= 0)
        {
            SetBootFailure(ent, ComputerBootState.NoRam, "POST ERROR 0x03: No system memory (RAM) detected.");
            return;
        }

        // 4. Storage Wire & HardDrive Check
        if (comp.StorageWireCut)
        {
            SetBootFailure(ent, ComputerBootState.WireCut, "POST ERROR 0x04: Storage controller wire severed.");
            return;
        }

        if (comp.RequireHardDriveToBoot && HasComp<ModularComputerComponent>(uid) && storageDevices.Count == 0 && hardDrives.Count == 0)
        {
            SetBootFailure(ent, ComputerBootState.NoHardDrive, "BOOT FAILURE: No bootable Hard Drive detected. Insert a system drive.");
            return;
        }

        // Load programs from HardDrive entities into CartridgeLoader
        if (TryComp<CartridgeLoaderComponent>(uid, out var loader))
        {
            foreach (var dev in storageDevices)
            {
                foreach (var proto in dev.Comp.PreloadedPrograms)
                {
                    if (_prototypeManager.HasIndex<EntityPrototype>(proto) &&
                        !_cartridgeLoader.GetAllPrograms(uid).Any(p => string.Equals(MetaData(p).EntityPrototype?.ID, proto.Id)))
                    {
                        _cartridgeLoader.InstallProgram((uid, loader), proto, deinstallable: false);
                    }
                }
            }
            foreach (var hdd in hardDrives)
            {
                foreach (var proto in hdd.Comp.PreloadedPrograms)
                {
                    if (_prototypeManager.HasIndex<EntityPrototype>(proto) &&
                        !_cartridgeLoader.GetAllPrograms(uid).Any(p => string.Equals(MetaData(p).EntityPrototype?.ID, proto.Id)))
                    {
                        _cartridgeLoader.InstallProgram((uid, loader), proto, deinstallable: false);
                    }
                }
            }

            var allProgs = _cartridgeLoader.GetAllPrograms(uid).ToList();
            EntityUid? defaultProgram = null;

            if (comp.Template == ComputerUiTemplate.Embedded)
            {
                defaultProgram = allProgs.FirstOrDefault(p =>
                    HasComp<ApcControlCartridgeComponent>(p) ||
                    HasComp<AtmosControlCartridgeComponent>(p) ||
                    HasComp<LatheControlCartridgeComponent>(p));

                if (defaultProgram == null)
                    defaultProgram = allProgs.FirstOrDefault(p => HasComp<EmbeddedOsCartridgeComponent>(p));
            }
            else
            {
                defaultProgram = allProgs.FirstOrDefault(p => HasComp<OsCartridgeComponent>(p));
            }

            if (defaultProgram == null && allProgs.Count > 0)
                defaultProgram = allProgs[0];

            if (defaultProgram == null || !Exists(defaultProgram.Value))
            {
                if (comp.RequireHardDriveToBoot && HasComp<ModularComputerComponent>(uid))
                {
                    SetBootFailure(ent, ComputerBootState.NoBootProgram, "BOOT FAILURE: No bootable program found on storage medium.");
                    return;
                }
            }
            else
            {
                comp.BootProgram = defaultProgram;
                if (loader.ActiveProgram == null || !Exists(loader.ActiveProgram.Value))
                {
                    _cartridgeLoader.ActivateProgram((uid, loader), defaultProgram.Value);
                }
            }
        }

        comp.BootState = ComputerBootState.Running;
        comp.BootDiagnosticMessage = "SYSTEM ONLINE";
        Dirty(uid, comp);

        if (TryComp<PdaComponent>(uid, out var pda))
            _pda.UpdatePdaUi(uid, pda);
    }

    private void SetBootFailure(Entity<OxydComputerComponent> ent, ComputerBootState state, string diagnostic)
    {
        ent.Comp.BootState = state;
        ent.Comp.BootDiagnosticMessage = diagnostic;
        Dirty(ent);

        if (TryComp<CartridgeLoaderComponent>(ent.Owner, out var loader))
        {
            if (loader.ActiveProgram != null)
                _cartridgeLoader.DeactivateProgram((ent.Owner, loader), loader.ActiveProgram.Value);
        }

        if (TryComp<PdaComponent>(ent.Owner, out var pda))
            _pda.UpdatePdaUi(ent.Owner, pda);
    }

    private void CollectInstalledHardware(
        EntityUid uid,
        List<Entity<ComputerStorageDeviceComponent>> storageDevices,
        List<Entity<ComputerHardDriveComponent>> hardDrives,
        List<ComputerRamComponent> ramModules,
        List<ComputerCpuComponent> cpuModules,
        List<ComputerNetworkCardComponent> netCards,
        List<ComputerBatteryModuleComponent> batteries)
    {
        if (!TryComp<ContainerManagerComponent>(uid, out var containerMgr))
            return;

        foreach (var container in containerMgr.Containers.Values)
        {
            foreach (var contained in container.ContainedEntities)
            {
                if (TryComp<ComputerStorageDeviceComponent>(contained, out var storage))
                    storageDevices.Add((contained, storage));
                if (TryComp<ComputerHardDriveComponent>(contained, out var hdd))
                    hardDrives.Add((contained, hdd));
                if (TryComp<ComputerRamComponent>(contained, out var ram))
                    ramModules.Add(ram);
                if (TryComp<ComputerCpuComponent>(contained, out var cpu))
                    cpuModules.Add(cpu);
                if (TryComp<ComputerNetworkCardComponent>(contained, out var net))
                    netCards.Add(net);
                if (TryComp<ComputerBatteryModuleComponent>(contained, out var bat))
                    batteries.Add(bat);
            }
        }
    }

    /// <summary>
    /// Public helper allowing any Program Entity or Terminal to execute a console command on its Computer Entity.
    /// </summary>
    public bool ExecuteComputerCommand(EntityUid computerUid, string commandLine, out string output, EntityUid? programUid = null, EntityUid? userUid = null)
    {
        var ev = new ExecuteComputerCommandEvent(commandLine, programUid, userUid);
        RaiseLocalEvent(computerUid, ev);
        output = ev.Output;
        return ev.Success;
    }

    private void OnExecuteComputerCommand(Entity<OxydComputerComponent> ent, ref ExecuteComputerCommandEvent args)
    {
        if (args.Handled)
            return;

        var parts = args.CommandLine.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return;

        var cmd = parts[0].ToLowerInvariant();
        switch (cmd)
        {
            case "devlink":
            case "port":
            {
                if (ent.Comp.DeviceLinkWireCut)
                {
                    args.Output = "Error: Motherboard DeviceLink wire is cut.";
                    args.Handled = true;
                    return;
                }

                var sub = parts.Length > 1 ? parts[1].ToLowerInvariant() : "list";
                if (sub is "scan" or "autolink")
                {
                    AutoDiscoverAndLinkPorts(ent);
                    args.Output = "Scanned and linked local device ports.";
                    args.Success = true;
                }
                else if (sub is "trigger")
                {
                    var portName = parts.Length > 2 ? parts[2] : ComputerCommandPort;
                    var payload = new ComputerDeviceLinkPayload("trigger", portName);
                    _deviceLink.InvokePort(ent.Owner, portName, ref payload);
                    if (TryComp<DeviceLinkSourceComponent>(ent.Owner, out var srcComp))
                    {
                        foreach (var (sinkUid, links) in srcComp.LinkedPorts)
                        {
                            foreach (var (srcP, sinkP) in links)
                            {
                                if (srcP == portName)
                                {
                                    var signalEv = new SignalReceivedEvent<ComputerDeviceLinkPayload>(sinkP, payload, ent.Owner);
                                    RaiseLocalEvent(sinkUid, ref signalEv);
                                }
                            }
                        }
                    }
                    args.Output = $"Triggered DeviceLink source port '{portName}'.";
                    args.Success = true;
                }
                else if (sub is "send" && parts.Length >= 3)
                {
                    var hasCustomPort = parts.Length >= 4 && parts[2] == ComputerCommandPort;

                    var targetPort = hasCustomPort ? parts[2] : ComputerCommandPort;
                    var action = hasCustomPort ? parts[3] : parts[2];
                    var arg = hasCustomPort
                        ? (parts.Length > 4 ? string.Join(" ", parts[4..]) : string.Empty)
                        : (parts.Length > 3 ? string.Join(" ", parts[3..]) : string.Empty);

                    SendDeviceLinkCommand(ent.Owner, action, arg);
                    args.Output = $"Sent DeviceLink command '{action}' ({arg}) via port '{targetPort}'.";
                    args.Success = true;
                }
                else if (sub is "link" && parts.Length >= 3)
                {
                    var targetIdent = parts[2].ToUpperInvariant();
                    EntityUid? targetEnt = null;
                    if (_guidSystem.TryGetEntityByGuid(targetIdent, out var optTarget) && optTarget.HasValue)
                    {
                        targetEnt = optTarget.Value;
                    }
                    else if (NetEntity.TryParse(parts[2], out var netEnt) && TryGetEntity(netEnt, out var directEnt))
                    {
                        targetEnt = directEnt;
                    }

                    if (targetEnt == null || !Exists(targetEnt.Value))
                    {
                        args.Output = $"port: Device '{parts[2]}' not found on network.";
                        args.Handled = true;
                        return;
                    }

                    var srcPort = parts.Length > 3 ? parts[3] : ComputerCommandPort;
                    var sinkPort = parts.Length > 4 ? parts[4] : MachineCommandSinkPort;

                    _deviceLink.EnsureSourcePort(ent.Owner, srcPort);
                    _deviceLink.EnsureSinkPort(targetEnt.Value, sinkPort);
                    _deviceLink.SaveLinks(null, ent.Owner, targetEnt.Value, new List<DeviceLink> { new(srcPort, sinkPort) });

                    args.Output = $"Linked local port '{srcPort}' -> '{Name(targetEnt.Value)}' ({sinkPort}).";
                    args.Success = true;
                }
                else if (sub is "unlink" && parts.Length >= 3)
                {
                    var targetIdent = parts[2].ToUpperInvariant();
                    EntityUid? targetEnt = null;
                    if (_guidSystem.TryGetEntityByGuid(targetIdent, out var optTarget) && optTarget.HasValue)
                        targetEnt = optTarget.Value;
                    else if (NetEntity.TryParse(parts[2], out var netEnt) && TryGetEntity(netEnt, out var directEnt))
                        targetEnt = directEnt;

                    if (targetEnt != null && Exists(targetEnt.Value))
                    {
                        _deviceLink.SaveLinks(null, ent.Owner, targetEnt.Value, new List<DeviceLink>());
                        args.Output = $"Unlinked device '{Name(targetEnt.Value)}' [{targetIdent}].";
                        args.Success = true;
                    }
                    else
                    {
                        args.Output = $"unlink: Device '{parts[2]}' not found.";
                    }
                }
                else
                {
                    var links = GetLinkedPortsSummary(ent.Owner);
                    args.Output = links.Count == 0
                        ? "No active DeviceLink connections. Use 'port scan' or 'port link <guid>'."
                        : string.Join("\n", links.ConvertAll(l => $"[{l.SourcePort}] -> {l.TargetEntityName} ({l.SinkPort})"));
                    args.Success = true;
                }

                args.Handled = true;
                break;
            }

            case "lathe":
            {
                if (ent.Comp.DeviceLinkWireCut)
                {
                    args.Output = "Error: Motherboard DeviceLink wire is cut.";
                    args.Handled = true;
                    return;
                }

                var sub = parts.Length > 1 ? parts[1].ToLowerInvariant() : "status";
                if (sub == "link")
                {
                    if (parts.Length >= 3)
                    {
                        var targetGuid = parts[2].ToUpperInvariant();
                        if (_guidSystem.TryGetEntityByGuid(targetGuid, out var optTarget) && optTarget.HasValue && HasComp<LatheComponent>(optTarget.Value))
                        {
                            LinkComputerAndMachine(ent.Owner, optTarget.Value);
                            args.Output = $"Linked computer to Lathe [{targetGuid}] ({Name(optTarget.Value)}).";
                            args.Success = true;
                        }
                        else
                        {
                            args.Output = $"lathe link: Target Lathe '{parts[2]}' not found.";
                        }
                    }
                    else
                    {
                        AutoDiscoverAndLinkPorts(ent);
                        args.Output = "Linked computer to Autolathe ports.";
                        args.Success = true;
                    }
                }
                else if (sub == "sync_disk")
                {
                    var synced = SyncDiskDesignsToLathe(ent.Owner);
                    args.Output = $"Synchronized {synced} design(s) from storage to linked Lathe.";
                    args.Success = true;
                }
                else if (sub == "print" && parts.Length >= 3)
                {
                    var recipeId = parts[2];
                    var amount = parts.Length > 3 && int.TryParse(parts[3], out var parsedAmt) ? Math.Max(1, parsedAmt) : 1;
                    SendDeviceLinkCommand(ent.Owner, "lathe_print", $"{recipeId}:{amount}");
                    args.Output = $"Queued {amount}x '{recipeId}' on linked Lathe via DeviceLink.";
                    args.Success = true;
                }
                else if (sub == "cancel")
                {
                    SendDeviceLinkCommand(ent.Owner, "lathe_cancel", string.Empty);
                    args.Output = "Sent cancel order to linked Lathe via DeviceLink.";
                    args.Success = true;
                }
                else
                {
                    SendDeviceLinkCommand(ent.Owner, "lathe_status", string.Empty);
                    var latheUid = FindLinkedTarget<LatheComponent>(ent.Owner);
                    if (latheUid != null && TryComp<LatheComponent>(latheUid.Value, out var latheComp))
                    {
                        var current = latheComp.CurrentRecipe != null && _prototypeManager.TryIndex(latheComp.CurrentRecipe.Value, out var curProto)
                            ? _lathe.GetRecipeName(curProto)
                            : "Idle";
                        args.Output = $"[Lathe Status: {Name(latheUid.Value)}] Currently Producing: {current} | Queue Count: {latheComp.Queue.Count}";
                    }
                    else
                    {
                        args.Output = "Requested Lathe telemetry over DeviceLink (No Lathe currently linked).";
                    }
                    args.Success = true;
                }

                args.Handled = true;
                break;
            }

            case "apc":
            {
                if (ent.Comp.DeviceLinkWireCut)
                {
                    args.Output = "Error: Motherboard DeviceLink wire is cut.";
                    args.Handled = true;
                    return;
                }

                var sub = parts.Length > 1 ? parts[1].ToLowerInvariant() : "status";
                if (sub == "link")
                {
                    if (parts.Length >= 3)
                    {
                        var targetGuid = parts[2].ToUpperInvariant();
                        if (_guidSystem.TryGetEntityByGuid(targetGuid, out var optTarget) && optTarget.HasValue && HasComp<ApcComponent>(optTarget.Value))
                        {
                            LinkComputerAndMachine(ent.Owner, optTarget.Value);
                            args.Output = $"Linked computer to APC [{targetGuid}] ({Name(optTarget.Value)}).";
                            args.Success = true;
                        }
                        else
                        {
                            args.Output = $"apc link: Target APC '{parts[2]}' not found.";
                        }
                    }
                    else
                    {
                        AutoDiscoverAndLinkPorts(ent);
                        args.Output = "Linked computer to APC ports.";
                        args.Success = true;
                    }
                }
                else if (sub == "on")
                {
                    SendDeviceLinkCommand(ent.Owner, "apc_set_breaker", "on");
                    args.Output = "Turned on APC main breaker via DeviceLink.";
                    args.Success = true;
                }
                else if (sub == "off")
                {
                    SendDeviceLinkCommand(ent.Owner, "apc_set_breaker", "off");
                    args.Output = "Turned off APC main breaker via DeviceLink.";
                    args.Success = true;
                }
                else if (sub == "toggle")
                {
                    SendDeviceLinkCommand(ent.Owner, "apc_toggle_breaker", string.Empty);
                    args.Output = "Toggled APC main breaker via DeviceLink.";
                    args.Success = true;
                }
                else
                {
                    SendDeviceLinkCommand(ent.Owner, "apc_status", string.Empty);
                    var apcUid = FindLinkedTarget<ApcComponent>(ent.Owner);
                    if (apcUid != null && TryComp<ApcComponent>(apcUid.Value, out var apcComp))
                    {
                        var chargePct = 0f;
                        var loadWatts = 0;
                        if (TryComp<PowerNetworkBatteryComponent>(apcUid.Value, out var netBat))
                        {
                            var bat = netBat.NetworkBattery;
                            if (bat.Capacity > 0)
                                chargePct = Math.Clamp(bat.CurrentStorage / bat.Capacity * 100f, 0f, 100f);
                            loadWatts = (int) MathF.Ceiling(bat.CurrentSupply);
                        }
                        args.Output = $"[APC Status: {Name(apcUid.Value)}] Main Breaker: {(apcComp.MainBreakerEnabled ? "ON" : "OFF")} | Battery: {chargePct:F1}% | Load: {loadWatts}W | External Power: {apcComp.LastExternalState}";
                    }
                    else
                    {
                        args.Output = "Requested APC telemetry over DeviceLink (No APC currently linked).";
                    }
                    args.Success = true;
                }

                args.Handled = true;
                break;
            }

            case "atmos":
            {
                if (ent.Comp.DeviceLinkWireCut)
                {
                    args.Output = "Error: Motherboard DeviceLink wire is cut.";
                    args.Handled = true;
                    return;
                }

                var sub = parts.Length > 1 ? parts[1].ToLowerInvariant() : "status";
                if (sub == "link")
                {
                    if (parts.Length >= 3)
                    {
                        var targetGuid = parts[2].ToUpperInvariant();
                        if (_guidSystem.TryGetEntityByGuid(targetGuid, out var optTarget) && optTarget.HasValue && HasComp<AirAlarmComponent>(optTarget.Value))
                        {
                            LinkComputerAndMachine(ent.Owner, optTarget.Value);
                            args.Output = $"Linked computer to Air Alarm [{targetGuid}] ({Name(optTarget.Value)}).";
                            args.Success = true;
                        }
                        else
                        {
                            args.Output = $"atmos link: Target Air Alarm '{parts[2]}' not found.";
                        }
                    }
                    else
                    {
                        AutoDiscoverAndLinkPorts(ent);
                        args.Output = "Linked computer to Air Alarm ports.";
                        args.Success = true;
                    }
                }
                else if (sub == "mode" && parts.Length >= 3)
                {
                    var mode = parts[2];
                    SendDeviceLinkCommand(ent.Owner, "atmos_set_mode", mode);
                    args.Output = $"Set Air Alarm mode to '{mode}' via DeviceLink.";
                    args.Success = true;
                }
                else
                {
                    SendDeviceLinkCommand(ent.Owner, "atmos_status", string.Empty);
                    var alarmUid = FindLinkedTarget<AirAlarmComponent>(ent.Owner);
                    if (alarmUid != null && TryComp<AirAlarmComponent>(alarmUid.Value, out var alarmComp))
                    {
                        var pressure = 0f;
                        var temp = 0f;
                        if (_atmosphere.GetContainingMixture(alarmUid.Value, false, false) is { } mix)
                        {
                            pressure = mix.Pressure;
                            temp = mix.Temperature;
                        }
                        args.Output = $"[Air Alarm Status: {Name(alarmUid.Value)}] Mode: {alarmComp.CurrentMode} | Danger: {alarmComp.State} | Pressure: {pressure:F1} kPa | Temperature: {temp:F1} K";
                    }
                    else
                    {
                        args.Output = "Requested Air Alarm telemetry over DeviceLink (No Air Alarm currently linked).";
                    }
                    args.Success = true;
                }

                args.Handled = true;
                break;
            }
        }
    }

    public void SendDeviceLinkCommand(EntityUid computerUid, string command, string argument, Dictionary<string, string>? data = null)
    {
        if (TryComp<OxydComputerComponent>(computerUid, out var comp) && comp.DeviceLinkWireCut)
            return;

        EnsureDefaultDeviceLinkPorts(computerUid);
        var payload = new ComputerDeviceLinkPayload(command, argument, data);

        // Invoke standard DeviceLinkSystem port and also deliver typed SignalReceivedEvent<ComputerDeviceLinkPayload>
        // to all linked sinks so machines without DeviceNetworkComponent receive the full payload struct.
        _deviceLink.InvokePort(computerUid, ComputerCommandPort, ref payload);

        if (TryComp<DeviceLinkSourceComponent>(computerUid, out var sourceComp))
        {
            foreach (var (sinkUid, links) in sourceComp.LinkedPorts)
            {
                foreach (var (sourcePort, sinkPort) in links)
                {
                    if (sourcePort == ComputerCommandPort)
                    {
                        var signalEv = new SignalReceivedEvent<ComputerDeviceLinkPayload>(sinkPort, payload, computerUid);
                        RaiseLocalEvent(sinkUid, ref signalEv);
                    }
                }
            }
        }
    }

    public void NotifyComputerFromMachine(EntityUid machineUid, string statusEvent = "state_changed")
    {
        if (TryComp<EmbeddedComputerHostComponent>(machineUid, out var host) && host.ComputerEntity is { } compUid && Exists(compUid))
        {
            var payload = new ComputerDeviceLinkPayload(statusEvent, Name(machineUid));
            var ev = new SignalReceivedEvent<ComputerDeviceLinkPayload>(ComputerStatusSinkPort, payload, machineUid);
            RaiseLocalEvent(compUid, ref ev);
        }

        if (TryComp<DeviceLinkSourceComponent>(machineUid, out var sourceComp))
        {
            var payload = new ComputerDeviceLinkPayload(statusEvent, Name(machineUid));
            foreach (var (sinkUid, links) in sourceComp.LinkedPorts)
            {
                foreach (var (sourcePort, sinkPort) in links)
                {
                    if (sourcePort == MachineStatusSourcePort)
                    {
                        var signalEv = new SignalReceivedEvent<ComputerDeviceLinkPayload>(sinkPort, payload, machineUid);
                        RaiseLocalEvent(sinkUid, ref signalEv);
                    }
                }
            }
        }
    }

    private void OnComputerSignalReceived(Entity<OxydComputerComponent> ent, ref SignalReceivedEvent args)
    {
        RefreshInstalledControlPrograms(ent.Owner);
    }

    private void OnComputerPayloadSignalReceived(Entity<OxydComputerComponent> ent, ref SignalReceivedEvent<ComputerDeviceLinkPayload> args)
    {
        RefreshInstalledControlPrograms(ent.Owner);
    }

    public void RefreshInstalledControlPrograms(EntityUid computerUid)
    {
        if (!TryComp<CartridgeLoaderComponent>(computerUid, out _))
            return;

        foreach (var prog in _cartridgeLoader.GetAllPrograms(computerUid))
        {
            if (TryComp<LatheControlCartridgeComponent>(prog, out var latheCart))
                UpdateLatheControlUi((prog, latheCart), computerUid);
            if (TryComp<ApcControlCartridgeComponent>(prog, out var apcCart))
                UpdateApcControlUi((prog, apcCart), computerUid);
            if (TryComp<AtmosControlCartridgeComponent>(prog, out var atmosCart))
                UpdateAtmosControlUi((prog, atmosCart), computerUid);
            if (TryComp<EmbeddedOsCartridgeComponent>(prog, out var embCart))
                UpdateEmbeddedOsUi((prog, embCart), computerUid);
            if (TryComp<OsCartridgeComponent>(prog, out var osCart))
                UpdateOsUi((prog, osCart), computerUid);
        }
    }

    private EntityUid? FindLinkedTarget<TComp>(EntityUid computerUid) where TComp : IComponent
    {
        if (TryComp<OxydComputerComponent>(computerUid, out var oxydComp) &&
            oxydComp.HostMachine is { } host &&
            HasComp<TComp>(host))
        {
            return host;
        }

        if (TryComp<DeviceLinkSourceComponent>(computerUid, out var sourceComp))
        {
            foreach (var sinkUid in sourceComp.LinkedPorts.Keys)
            {
                if (HasComp<TComp>(sinkUid))
                    return sinkUid;
            }
        }

        var parentUid = Transform(computerUid).ParentUid;
        if (parentUid.IsValid() && HasComp<TComp>(parentUid))
            return parentUid;

        return null;
    }

    private int SyncDiskDesignsToLathe(EntityUid computerUid)
    {
        var latheUid = FindLinkedTarget<LatheComponent>(computerUid);
        if (latheUid == null)
            return 0;

        var techDb = EnsureComp<TechnologyDatabaseComponent>(latheUid.Value);

        var designs = new HashSet<string>();
        var storageDevices = new List<Entity<ComputerStorageDeviceComponent>>();
        var hardDrives = new List<Entity<ComputerHardDriveComponent>>();
        CollectInstalledHardware(
            computerUid,
            storageDevices,
            hardDrives,
            new List<ComputerRamComponent>(),
            new List<ComputerCpuComponent>(),
            new List<ComputerNetworkCardComponent>(),
            new List<ComputerBatteryModuleComponent>());

        foreach (var dev in storageDevices)
        {
            foreach (var d in dev.Comp.StoredDesigns)
            {
                designs.Add(d);
            }
        }

        foreach (var hdd in hardDrives)
        {
            foreach (var d in hdd.Comp.StoredDesigns)
            {
                designs.Add(d);
            }
        }

        if (TryComp<FileSystemComponent>(computerUid, out var fs))
        {
            CollectDesignsFromFileSystem(fs, designs);
        }

        if (TryComp<PdaComponent>(computerUid, out var pda) &&
            pda.DiskSlot.Item is { } diskUid &&
            TryComp<FileSystemComponent>(diskUid, out var diskFs))
        {
            CollectDesignsFromFileSystem(diskFs, designs);
        }

        if (TryComp<LatheComponent>(latheUid.Value, out var latheComp) &&
            latheComp.diskSlot?.Item is { } latheDiskUid &&
            TryComp<FileSystemComponent>(latheDiskUid, out var latheDiskFs))
        {
            CollectDesignsFromFileSystem(latheDiskFs, designs);
        }

        var addedCount = 0;
        foreach (var designId in designs)
        {
            if (_prototypeManager.HasIndex<LatheRecipePrototype>(designId))
            {
                _research.AddLatheRecipe(latheUid.Value, designId, techDb);
                addedCount++;
            }
        }

        return addedCount;
    }

    private static void CollectDesignsFromFileSystem(FileSystemComponent fs, HashSet<string> designs)
    {
        foreach (var file in fs.Files.Values)
        {
            if (file is RecipeBlueprintFile bp && !bp.IsEncrypted)
            {
                foreach (var r in bp.GetAllRecipes())
                {
                    designs.Add(r.Id);
                }
            }
            else if (file is TextVirtualFile txt &&
                     (txt.Extension.Equals("design", StringComparison.OrdinalIgnoreCase) ||
                      txt.Extension.Equals("blueprint", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var line in txt.TextContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    designs.Add(line.Trim());
                }
            }
        }
    }

    private List<OsDeviceLinkEntry> GetLinkedPortsSummary(EntityUid computerUid)
    {
        var list = new List<OsDeviceLinkEntry>();
        if (!TryComp<DeviceLinkSourceComponent>(computerUid, out var sourceComp))
            return list;

        foreach (var (sinkUid, links) in sourceComp.LinkedPorts)
        {
            if (!Exists(sinkUid))
                continue;

            foreach (var (sourcePort, sinkPort) in links)
            {
                list.Add(new OsDeviceLinkEntry
                {
                    SourcePort = sourcePort.Id,
                    TargetEntityName = Name(sinkUid),
                    TargetEntity = GetNetEntity(sinkUid),
                    SinkPort = sinkPort.Id
                });
            }
        }

        return list;
    }

    // =========================================================================
    // OS Cartridge Handlers
    // =========================================================================

    private void OnOsUiReady(Entity<OsCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        UpdateOsUi(ent, args.Loader);
    }

    private void OnOsActivated(Entity<OsCartridgeComponent> ent, ref CartridgeActivatedEvent args)
    {
        UpdateOsUi(ent, args.Loader);
    }

    private void OnOsUiMessageDirect(Entity<OsCartridgeComponent> ent, ref OsUiMessageEvent args)
    {
        var msg = (CartridgeMessageEvent) args;
        OnOsUiMessage(ent, ref msg);
    }

    private void OnOsUiMessage(Entity<OsCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not OsUiMessageEvent msg)
            return;

        var loaderUid = GetEntity(args.LoaderUid);
        if (!TryComp<CartridgeLoaderComponent>(loaderUid, out var loader))
            return;

        var status = string.Empty;
        switch (msg.Action)
        {
            case OsUiAction.LaunchProgram:
                if (msg.TargetProgram is { } targetNet && TryGetEntity(targetNet, out var targetProg))
                {
                    if (TryComp<OxydComputerComponent>(loaderUid, out var comp) && !comp.OpenPrograms.Contains(targetProg.Value))
                        comp.OpenPrograms.Add(targetProg.Value);
                    if (TryComp<PdaComponent>(loaderUid, out var pda) && !pda.OpenPrograms.Contains(targetProg.Value))
                        pda.OpenPrograms.Add(targetProg.Value);
                    _cartridgeLoader.ActivateProgram((loaderUid, loader), targetProg.Value);
                    UpdateOsUi(loaderUid);
                    return;
                }
                break;

            case OsUiAction.CloseProgram:
                if (msg.TargetProgram is { } closeNet && TryGetEntity(closeNet, out var closeProg))
                {
                    if (TryComp<OxydComputerComponent>(loaderUid, out var comp) && comp.BootProgram == closeProg.Value)
                    {
                        status = "Cannot close the system boot program.";
                    }
                    else
                    {
                        if (TryComp<OxydComputerComponent>(loaderUid, out var comp1))
                            comp1.OpenPrograms.Remove(closeProg.Value);
                        if (TryComp<PdaComponent>(loaderUid, out var pda))
                            pda.OpenPrograms.Remove(closeProg.Value);

                        if (loader.ActiveProgram == closeProg.Value)
                        {
                            var openProgs = (comp?.OpenPrograms ?? new List<EntityUid>())
                                .Concat(TryComp<PdaComponent>(loaderUid, out var pdaComp) ? pdaComp.OpenPrograms : Enumerable.Empty<EntityUid>())
                                .Distinct()
                                .Where(p => Exists(p) && p != closeProg.Value)
                                .ToList();

                            if (openProgs.Count > 0)
                            {
                                var nextProg = openProgs.Last();
                                _cartridgeLoader.ActivateProgram((loaderUid, loader), nextProg);
                            }
                            else if (comp?.BootProgram is { } bootProg && Exists(bootProg))
                            {
                                _cartridgeLoader.ActivateProgram((loaderUid, loader), bootProg);
                            }
                            else
                            {
                                _cartridgeLoader.DeactivateProgram((loaderUid, loader), closeProg.Value);
                            }
                        }
                        else
                        {
                            var deactEvt = new CartridgeDeactivatedEvent((loaderUid, loader));
                            RaiseLocalEvent(closeProg.Value, ref deactEvt);
                            var loaderDeactEvt = new CartridgeLoaderProgramDeactivatedEvent(closeProg.Value);
                            RaiseLocalEvent(loaderUid, ref loaderDeactEvt);
                        }

                        status = $"Closed background program '{Name(closeProg.Value)}'.";
                    }
                }
                break;

            case OsUiAction.InstallProgram:
                if (msg.TargetProgram is { } installNet && TryGetEntity(installNet, out var installProg))
                {
                    if (_cartridgeLoader.InstallCartridge((loaderUid, loader), installProg.Value))
                    {
                        status = $"Installed program '{Name(installProg.Value)}'.";
                    }
                    else if (MetaData(installProg.Value).EntityPrototype is { } proto &&
                             _cartridgeLoader.InstallProgram((loaderUid, loader), proto.ID))
                    {
                        status = $"Installed program '{Name(installProg.Value)}'.";
                    }
                    else
                    {
                        status = $"Failed to install '{Name(installProg.Value)}'.";
                    }
                }
                break;

            case OsUiAction.UninstallProgram:
                if (msg.TargetProgram is { } uninstallNet && TryGetEntity(uninstallNet, out var uninstallProg))
                {
                    _cartridgeLoader.UninstallProgram((loaderUid, loader), uninstallProg.Value);
                    status = $"Uninstalled program '{Name(uninstallProg.Value)}'.";
                }
                break;

            case OsUiAction.ToggleRemoteControl:
                if (TryComp<OxydComputerComponent>(loaderUid, out var oxydComp))
                {
                    if (oxydComp.RemoteControlWireCut)
                    {
                        status = "Error: Remote control wire is cut on motherboard.";
                    }
                    else
                    {
                        oxydComp.AllowRemoteControl = !oxydComp.AllowRemoteControl;
                        Dirty(loaderUid, oxydComp);
                        if (!oxydComp.AllowRemoteControl)
                            OnRemoteAccessRevoked(loaderUid);
                        status = oxydComp.AllowRemoteControl ? "Remote control enabled." : "Remote control disabled.";
                    }
                }
                break;

            case OsUiAction.ScanAndLinkDevices:
                ExecuteComputerCommand(loaderUid, "devlink scan", out status, ent.Owner);
                break;

            case OsUiAction.RunCommand:
                if (!string.IsNullOrWhiteSpace(msg.Command))
                    ExecuteComputerCommand(loaderUid, msg.Command, out status, ent.Owner);
                break;

            case OsUiAction.ShowRingtone:
                if (HasComp<RingerComponent>(loaderUid) && args.Actor is { } ringerActor)
                    _ringer.TryToggleRingerUi(loaderUid, ringerActor);
                break;

            case OsUiAction.ShowMusic:
                if (TryComp<InstrumentComponent>(loaderUid, out var instrument) && args.Actor is { } musicActor)
                    _instrument.ToggleInstrumentUi(loaderUid, musicActor, instrument);
                break;

            case OsUiAction.ShowUplink:
                if (args.Actor is { } uplinkActor && _pda.TryGetUnlockedStore(loaderUid, out var store))
                {
                    if (store != loaderUid && TryComp<RemoteStoreComponent>(loaderUid, out var remoteStore))
                        remoteStore.Store = store;
                    _ui.TryToggleUi(loaderUid, StoreUiKey.Key, uplinkActor);
                }
                break;

            case OsUiAction.LockUplink:
                if (TryComp<RingerUplinkComponent>(loaderUid, out var uplink))
                {
                    if (TryComp<RemoteStoreComponent>(loaderUid, out var remoteStore))
                        remoteStore.Store = null;
                    _ringer.LockUplink((loaderUid, uplink));
                    if (TryComp<PdaComponent>(loaderUid, out var pdaComp1))
                        _pda.UpdatePdaUi(loaderUid, pdaComp1);
                }
                break;

            case OsUiAction.ToggleFlashlight:
                _unpoweredFlashlight.TryToggleLight(loaderUid, user: args.Actor);
                break;

            case OsUiAction.EjectId:
                if (TryComp<PdaComponent>(loaderUid, out var pdaId))
                    TryEjectOrDropItemSlot(loaderUid, pdaId.IdSlot, args.Actor);
                break;

            case OsUiAction.EjectPen:
                if (TryComp<PdaComponent>(loaderUid, out var pdaPen))
                    TryEjectOrDropItemSlot(loaderUid, pdaPen.PenSlot, args.Actor);
                break;

            case OsUiAction.EjectPai:
                if (TryComp<PdaComponent>(loaderUid, out var pdaPai))
                    TryEjectOrDropItemSlot(loaderUid, pdaPai.PaiSlot, args.Actor);
                break;

            case OsUiAction.EjectDisk:
                if (TryComp<PdaComponent>(loaderUid, out var pdaDisk))
                    TryEjectOrDropItemSlot(loaderUid, pdaDisk.DiskSlot, args.Actor);
                break;

            case OsUiAction.InsertId:
                if (TryComp<PdaComponent>(loaderUid, out var insId))
                    TryInsertOrDropItemSlot(loaderUid, insId.IdSlot, args.Actor);
                break;

            case OsUiAction.InsertPen:
                if (TryComp<PdaComponent>(loaderUid, out var insPen))
                    TryInsertOrDropItemSlot(loaderUid, insPen.PenSlot, args.Actor);
                break;

            case OsUiAction.InsertPai:
                if (TryComp<PdaComponent>(loaderUid, out var insPai))
                    TryInsertOrDropItemSlot(loaderUid, insPai.PaiSlot, args.Actor);
                break;

            case OsUiAction.InsertDisk:
                if (TryComp<PdaComponent>(loaderUid, out var insDisk))
                    TryInsertOrDropItemSlot(loaderUid, insDisk.DiskSlot, args.Actor);
                break;

            case OsUiAction.PowerOff:
                if (args.Actor is { } actor)
                {
                    _ui.CloseUi(loaderUid, PdaUiKey.Key, actor);
                    _ui.CloseUi(loaderUid, ComputerUiKey.Key, actor);
                }
                break;
        }

        UpdateOsUi(ent, loaderUid, status);
    }


    private void TryEjectOrDropItemSlot(EntityUid loaderUid, ItemSlot slot, EntityUid? user)
    {
        if (!slot.HasItem || slot.DisableEject)
            return;

        if (user != null && _interaction.InRangeUnobstructed(user.Value, loaderUid))
        {
            _itemSlots.TryEjectToHands(loaderUid, slot, user);
        }
        else
        {
            _itemSlots.TryEject(loaderUid, slot, null, out _);
        }
    }

    private void TryInsertOrDropItemSlot(EntityUid loaderUid, ItemSlot slot, EntityUid? user)
    {
        if (slot.HasItem || user == null)
            return;

        if (_interaction.InRangeUnobstructed(user.Value, loaderUid))
        {
            _itemSlots.TryInsertFromHand(loaderUid, slot, user.Value);
        }
        else
        {
            _hands.TryDrop(user.Value);
        }
    }

    public void UpdateOsUi(EntityUid loaderUid, string statusMessage = "")
    {
        if (!TryComp<CartridgeLoaderComponent>(loaderUid, out var loader))
            return;

        EntityUid? osProg = null;
        if (TryComp<OxydComputerComponent>(loaderUid, out var comp) && comp.BootProgram is { } bootProg && HasComp<OsCartridgeComponent>(bootProg))
        {
            osProg = bootProg;
        }
        else
        {
            osProg = _cartridgeLoader.GetAllPrograms(loaderUid).FirstOrDefault(p => HasComp<OsCartridgeComponent>(p));
        }

        if (osProg != null && TryComp<OsCartridgeComponent>(osProg.Value, out var osCart))
        {
            UpdateOsUi((osProg.Value, osCart), loaderUid, statusMessage);
        }
    }

    public void UpdateOsUi(Entity<OsCartridgeComponent> ent, EntityUid loaderUid, string statusMessage = "")
    {
        if (!TryComp<CartridgeLoaderComponent>(loaderUid, out var loader))
            return;

        var oxydComp = EnsureComp<OxydComputerComponent>(loaderUid);
        var guid = _router.EnsureDeviceGuid(loaderUid);

        var statsComp = CompOrNull<ComputerStatsComponent>(loaderUid);
        var fsComp = CompOrNull<FileSystemComponent>(loaderUid);
        var bwComp = CompOrNull<NetworkBandwidthComponent>(loaderUid);
        var pdaComp = CompOrNull<PdaComponent>(loaderUid);

        var openCount = pdaComp != null && pdaComp.OpenPrograms.Count > 0 ? pdaComp.OpenPrograms.Count : 1;
        ComputerMetricsHelper.CalculateMetrics(
            openCount,
            statsComp,
            fsComp,
            bwComp,
            out var cpuLoad,
            out var ramUsed,
            out var ramTotal,
            out var diskUsedBytes,
            out var diskTotalBytes,
            out _,
            out _,
            curTime: _timing.CurTime.TotalSeconds);

        var uploadBps = bwComp?.MaxUploadBps ?? 250000;
        var downloadBps = bwComp?.MaxBps ?? 250000;
        var batteryPercent = statsComp?.BatteryChargePercent ?? 100f;
        var hasBattery = statsComp?.HasBattery ?? false;

        var pdaOwnerInfo = default(PdaIdInfoText);
        string? stationName = null;
        string? sshPassword = null;
        bool hasCaptainAccess = false;
        bool canPlayMusic = false;
        bool hasUplink = false;

        if (pdaComp != null)
        {
            IdCardComponent? id = null;
            if (_idCard.TryFindIdCard(loaderUid, out var idCard))
                id = idCard;

            pdaOwnerInfo = new PdaIdInfoText
            {
                ActualOwnerName = pdaComp.OwnerName,
                IdOwner = id?.FullName,
                JobTitle = id?.LocalizedJobTitle,
                StationAlertLevel = pdaComp.StationAlertLevel,
                StationAlertColor = pdaComp.StationAlertColor
            };
            stationName = pdaComp.StationName;
            sshPassword = pdaComp.SshPassword;
            if (pdaComp.ContainedId is { } idEnt)
            {
                var tags = _accessReader.FindAccessTags(idEnt);
                hasCaptainAccess = tags.Any(t => t.Id == "Captain" || t.Id == "Command");
            }
            canPlayMusic = HasComp<InstrumentComponent>(loaderUid);
            hasUplink = _pda.TryGetUnlockedStore(loaderUid, out _);
        }

        var programs = new List<OsProgramEntry>();
        foreach (var prog in _cartridgeLoader.GetAllPrograms(loaderUid))
        {
            var cart = CompOrNull<CartridgeComponent>(prog);
            var progName = cart != null
                ? Loc.GetString(cart.ProgramName)
                : Name(prog);
            var desc = !string.IsNullOrWhiteSpace(MetaData(prog).EntityDescription) ? MetaData(prog).EntityDescription : null;
            var icon = cart?.Icon;
            var installStatus = cart?.InstallationStatus ?? InstallationStatus.Installed;

            var isRunning = loader.ActiveProgram == prog ||
                            oxydComp.BootProgram == prog ||
                            (pdaComp != null && pdaComp.OpenPrograms.Contains(prog));

            programs.Add(new OsProgramEntry
            {
                Uid = GetNetEntity(prog),
                Name = progName,
                Description = desc,
                Icon = icon,
                InstallationStatus = installStatus,
                IsRunning = isRunning,
                IsBootProgram = oxydComp.BootProgram == prog
            });
        }

        var hardwareEntries = BuildHardwareSummary(loaderUid);
        var linkedPorts = GetLinkedPortsSummary(loaderUid);

        var state = new OsUiState(
            guid,
            oxydComp.Template,
            oxydComp.AllowRemoteControl && !oxydComp.RemoteControlWireCut,
            (int) cpuLoad,
            ramUsed,
            ramTotal,
            diskUsedBytes / 1024,
            diskTotalBytes / 1024,
            uploadBps,
            downloadBps,
            batteryPercent,
            hasBattery,
            programs,
            hardwareEntries,
            linkedPorts,
            statusMessage,
            pdaOwnerInfo,
            stationName,
            sshPassword,
            hasCaptainAccess,
            canPlayMusic,
            hasUplink,
            flashlightEnabled: pdaComp?.FlashlightOn ?? false,
            hasFlashlight: pdaComp != null,
            hasPen: pdaComp?.PenSlot.HasItem ?? false,
            hasPai: pdaComp?.PaiSlot.HasItem ?? false,
            hasDisk: pdaComp?.DiskSlot.HasItem ?? false,
            diskName: pdaComp?.DiskSlot.Item is { } diskEnt ? MetaData(diskEnt).EntityName : null,
            hasId: pdaComp?.IdSlot.HasItem ?? false,
            activeProgram: loader.ActiveProgram != null ? GetNetEntity(loader.ActiveProgram) : null);

        if (pdaComp != null)
        {
            pdaComp.OsUiState = state;
            if (loader.ActiveProgram == ent.Owner)
                pdaComp.ActiveCartridgeState = state;
            _pda.UpdatePdaUi(loaderUid, pdaComp);
        }
        else
        {
            _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state, loader);
        }
    }

    // =========================================================================
    // Embedded OS Cartridge Handlers
    // =========================================================================

    private void OnEmbeddedOsUiReady(Entity<EmbeddedOsCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        UpdateEmbeddedOsUi(ent, args.Loader);
    }

    private void OnEmbeddedOsUiMessage(Entity<EmbeddedOsCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not EmbeddedOsUiMessageEvent msg)
            return;

        var loaderUid = GetEntity(args.LoaderUid);
        if (!TryComp<CartridgeLoaderComponent>(loaderUid, out var loader))
            return;

        var status = string.Empty;

        switch (msg.Action)
        {
            case EmbeddedOsUiAction.LaunchProgram:
                if (msg.TargetProgram is { } targetNet && TryGetEntity(targetNet, out var targetProg))
                {
                    _cartridgeLoader.ActivateProgram((loaderUid, loader), targetProg.Value);
                    if (TryComp<PdaComponent>(loaderUid, out var pda))
                    {
                        if (!pda.OpenPrograms.Contains(targetProg.Value))
                            pda.OpenPrograms.Add(targetProg.Value);
                        _pda.UpdatePdaUi(loaderUid, pda);
                    }
                    status = $"Launched program '{Name(targetProg.Value)}'.";
                }
                break;

            case EmbeddedOsUiAction.CloseProgram:
                if (msg.TargetProgram is { } closeNet && TryGetEntity(closeNet, out var closeProg))
                {
                    if (TryComp<PdaComponent>(loaderUid, out var pdaClose))
                    {
                        pdaClose.OpenPrograms.Remove(closeProg.Value);
                        _pda.UpdatePdaUi(loaderUid, pdaClose);
                    }

                    if (loader.ActiveProgram == closeProg.Value)
                    {
                        var comp = CompOrNull<OxydComputerComponent>(loaderUid);
                        var openProgs = TryComp<PdaComponent>(loaderUid, out var p) ? p.OpenPrograms : new();
                        if (openProgs.Count > 0)
                        {
                            var nextProg = openProgs.Last();
                            _cartridgeLoader.ActivateProgram((loaderUid, loader), nextProg);
                        }
                        else if (comp?.BootProgram is { } bootProg && Exists(bootProg))
                        {
                            _cartridgeLoader.ActivateProgram((loaderUid, loader), bootProg);
                        }
                        else
                        {
                            _cartridgeLoader.DeactivateProgram((loaderUid, loader), closeProg.Value);
                        }
                    }
                    else
                    {
                        var deactEvt = new CartridgeDeactivatedEvent((loaderUid, loader));
                        RaiseLocalEvent(closeProg.Value, ref deactEvt);
                        var loaderDeactEvt = new CartridgeLoaderProgramDeactivatedEvent(closeProg.Value);
                        RaiseLocalEvent(loaderUid, ref loaderDeactEvt);
                    }

                    status = $"Closed program '{Name(closeProg.Value)}'.";
                }
                break;

            case EmbeddedOsUiAction.ToggleRemoteControl:
                if (TryComp<OxydComputerComponent>(loaderUid, out var oxydComp))
                {
                    if (oxydComp.RemoteControlWireCut)
                    {
                        status = "Error: Remote control wire is cut on motherboard.";
                    }
                    else
                    {
                        oxydComp.AllowRemoteControl = !oxydComp.AllowRemoteControl;
                        Dirty(loaderUid, oxydComp);
                        if (!oxydComp.AllowRemoteControl)
                            OnRemoteAccessRevoked(loaderUid);
                        status = oxydComp.AllowRemoteControl ? "Remote control enabled." : "Remote control disabled.";
                    }
                }
                break;

            case EmbeddedOsUiAction.ScanAndLinkDevices:
                ExecuteComputerCommand(loaderUid, "port scan", out status, ent.Owner);
                break;

            case EmbeddedOsUiAction.RunCommand:
                if (!string.IsNullOrWhiteSpace(msg.Command))
                    ExecuteComputerCommand(loaderUid, msg.Command, out status, ent.Owner);
                break;

            case EmbeddedOsUiAction.Refresh:
                status = "System telemetry updated.";
                break;
        }

        UpdateEmbeddedOsUi(ent, loaderUid, status);
    }

    public void UpdateEmbeddedOsUi(EntityUid loaderUid, string statusMessage = "")
    {
        if (!TryComp<CartridgeLoaderComponent>(loaderUid, out _))
            return;

        EntityUid? embProg = null;
        if (TryComp<OxydComputerComponent>(loaderUid, out var comp) && comp.BootProgram is { } bootProg && HasComp<EmbeddedOsCartridgeComponent>(bootProg))
        {
            embProg = bootProg;
        }
        else
        {
            embProg = _cartridgeLoader.GetAllPrograms(loaderUid).FirstOrDefault(p => HasComp<EmbeddedOsCartridgeComponent>(p));
        }

        if (embProg != null && TryComp<EmbeddedOsCartridgeComponent>(embProg.Value, out var embCart))
        {
            UpdateEmbeddedOsUi((embProg.Value, embCart), loaderUid, statusMessage);
        }
    }

    public void UpdateEmbeddedOsUi(Entity<EmbeddedOsCartridgeComponent> ent, EntityUid loaderUid, string statusMessage = "")
    {
        if (!TryComp<CartridgeLoaderComponent>(loaderUid, out var loader))
            return;

        var oxydComp = EnsureComp<OxydComputerComponent>(loaderUid);
        var guid = _router.EnsureDeviceGuid(loaderUid);

        var statsComp = CompOrNull<ComputerStatsComponent>(loaderUid);
        var fsComp = CompOrNull<FileSystemComponent>(loaderUid);
        var bwComp = CompOrNull<NetworkBandwidthComponent>(loaderUid);
        var pdaComp = CompOrNull<PdaComponent>(loaderUid);

        var openCount = pdaComp != null && pdaComp.OpenPrograms.Count > 0 ? pdaComp.OpenPrograms.Count : 1;
        ComputerMetricsHelper.CalculateMetrics(
            openCount,
            statsComp,
            fsComp,
            bwComp,
            out var cpuLoad,
            out var ramUsed,
            out var ramTotal,
            out var diskUsedBytes,
            out var diskTotalBytes,
            out _,
            out _,
            curTime: _timing.CurTime.TotalSeconds);

        var uploadBps = bwComp?.MaxUploadBps ?? 250000;
        var downloadBps = bwComp?.MaxBps ?? 250000;
        var batteryPercent = statsComp?.BatteryChargePercent ?? 100f;
        var hasBattery = statsComp?.HasBattery ?? false;

        var machineName = MetaData(loaderUid).EntityName;
        var machineType = "Embedded System";
        if (oxydComp.HostMachine is { } host && Exists(host))
        {
            machineName = MetaData(host).EntityName;
            if (HasComp<ApcComponent>(host))
                machineType = "Area Power Controller";
            else if (HasComp<AirAlarmComponent>(host))
                machineType = "Atmospheric Alarm Controller";
            else if (HasComp<LatheComponent>(host))
                machineType = "Manufacturing Lathe";
        }

        var programs = new List<OsProgramEntry>();
        foreach (var prog in _cartridgeLoader.GetAllPrograms(loaderUid))
        {
            var cart = CompOrNull<CartridgeComponent>(prog);
            var progName = cart != null
                ? Loc.GetString(cart.ProgramName)
                : Name(prog);
            var desc = !string.IsNullOrWhiteSpace(MetaData(prog).EntityDescription) ? MetaData(prog).EntityDescription : null;
            var icon = cart?.Icon;
            var installStatus = cart?.InstallationStatus ?? InstallationStatus.Installed;

            var isRunning = loader.ActiveProgram == prog ||
                            oxydComp.BootProgram == prog ||
                            (pdaComp != null && pdaComp.OpenPrograms.Contains(prog));

            programs.Add(new OsProgramEntry
            {
                Uid = GetNetEntity(prog),
                Name = progName,
                Description = desc,
                Icon = icon,
                InstallationStatus = installStatus,
                IsRunning = isRunning,
                IsBootProgram = oxydComp.BootProgram == prog
            });
        }

        var hardwareEntries = BuildHardwareSummary(loaderUid);
        var linkedPorts = GetLinkedPortsSummary(loaderUid);

        var state = new EmbeddedOsUiState(
            guid,
            machineName,
            machineType,
            oxydComp.Template,
            oxydComp.AllowRemoteControl && !oxydComp.RemoteControlWireCut,
            (int) cpuLoad,
            ramUsed,
            ramTotal,
            diskUsedBytes / 1024,
            diskTotalBytes / 1024,
            uploadBps,
            downloadBps,
            batteryPercent,
            hasBattery,
            programs,
            hardwareEntries,
            linkedPorts,
            statusMessage,
            loader.ActiveProgram != null ? GetNetEntity(loader.ActiveProgram) : null);

        if (pdaComp != null)
        {
            if (loader.ActiveProgram == ent.Owner || loader.ActiveProgram == null)
                pdaComp.ActiveCartridgeState = state;
            _pda.UpdatePdaUi(loaderUid, pdaComp);
        }
        else
        {
            _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state, loader);
        }
    }

    private List<OsHardwareEntry> BuildHardwareSummary(EntityUid computerUid)
    {
        var list = new List<OsHardwareEntry>();
        if (!TryComp<ContainerManagerComponent>(computerUid, out var containerMgr))
            return list;

        foreach (var (slotId, container) in containerMgr.Containers)
        {
            foreach (var item in container.ContainedEntities)
            {
                if (TryComp<ComputerCpuComponent>(item, out var cpu))
                {
                    list.Add(new OsHardwareEntry
                    {
                        SlotName = slotId,
                        PartName = Name(item),
                        Size = cpu.Size.ToString(),
                        Details = $"{cpu.FrequencyMhz:F0} MHz"
                    });
                }
                else if (TryComp<ComputerRamComponent>(item, out var ram))
                {
                    list.Add(new OsHardwareEntry
                    {
                        SlotName = slotId,
                        PartName = Name(item),
                        Size = ram.Size.ToString(),
                        Details = $"{ram.SizeMb} MB RAM"
                    });
                }
                else if (TryComp<ComputerStorageDeviceComponent>(item, out var storage))
                {
                    list.Add(new OsHardwareEntry
                    {
                        SlotName = slotId,
                        PartName = Name(item),
                        Size = storage.Size.ToString(),
                        Details = $"{storage.CapacityBytes / 1024} KB"
                    });
                }
                else if (TryComp<ComputerHardDriveComponent>(item, out var hdd))
                {
                    list.Add(new OsHardwareEntry
                    {
                        SlotName = slotId,
                        PartName = Name(item),
                        Size = hdd.Size.ToString(),
                        Details = $"{hdd.CapacityBytes / 1024} KB"
                    });
                }
                else if (TryComp<ComputerNetworkCardComponent>(item, out var net))
                {
                    list.Add(new OsHardwareEntry
                    {
                        SlotName = slotId,
                        PartName = Name(item),
                        Size = net.Size.ToString(),
                        Details = $"DL {net.BandwidthBps} B/s | UL {net.MaxUploadBps} B/s"
                    });
                }
                else if (TryComp<ComputerBatteryModuleComponent>(item, out var bat))
                {
                    list.Add(new OsHardwareEntry
                    {
                        SlotName = slotId,
                        PartName = Name(item),
                        Size = bat.Size.ToString(),
                        Details = $"{bat.CurrentCharge:F0}/{bat.MaxCharge:F0} J"
                    });
                }
                else if (TryComp<ComputerSensorModuleComponent>(item, out var sensor))
                {
                    list.Add(new OsHardwareEntry
                    {
                        SlotName = slotId,
                        PartName = Name(item),
                        Size = sensor.Size.ToString(),
                        Details = "Atmos/Power Telemetry"
                    });
                }
            }
        }

        return list;
    }

    // =========================================================================
    // Lathe Control Cartridge Handlers (via ExecuteComputerCommand + DeviceLink)
    // =========================================================================

    private void OnLatheControlUiReady(Entity<LatheControlCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        ExecuteComputerCommand(args.Loader, "lathe link", out _, ent.Owner);
        ExecuteComputerCommand(args.Loader, "lathe sync_disk", out _, ent.Owner);
        UpdateLatheControlUi(ent, args.Loader);
    }

    private void OnLatheControlUiMessage(Entity<LatheControlCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not LatheControlUiMessageEvent msg)
            return;

        var loaderUid = GetEntity(args.LoaderUid);
        var status = string.Empty;

        switch (msg.Action)
        {
            case LatheControlUiAction.LinkLathe:
                var latheCmd = string.IsNullOrEmpty(msg.TargetGuid) ? "lathe link" : $"lathe link {msg.TargetGuid}";
                ExecuteComputerCommand(loaderUid, latheCmd, out status, ent.Owner);
                break;

            case LatheControlUiAction.SyncDiskDesigns:
                ExecuteComputerCommand(loaderUid, "lathe sync_disk", out status, ent.Owner);
                break;

            case LatheControlUiAction.PrintDesign:
                ExecuteComputerCommand(loaderUid, $"lathe print {msg.DesignId} {Math.Max(1, msg.Amount)}", out status, ent.Owner);
                break;

            case LatheControlUiAction.CancelQueue:
                ExecuteComputerCommand(loaderUid, "lathe cancel", out status, ent.Owner);
                break;

            case LatheControlUiAction.Refresh:
                ExecuteComputerCommand(loaderUid, "lathe status", out status, ent.Owner);
                break;
        }

        UpdateLatheControlUi(ent, loaderUid, status);
    }

    public void UpdateLatheControlUi(Entity<LatheControlCartridgeComponent> ent, EntityUid loaderUid, string statusMessage = "")
    {
        var latheUid = FindLinkedTarget<LatheComponent>(loaderUid);
        if (latheUid == null || !TryComp<LatheComponent>(latheUid.Value, out var latheComp))
        {
            var unlinkedState = new LatheControlUiState(
                false,
                "No Lathe Linked",
                "Idle",
                new List<string>(),
                new Dictionary<string, int>(),
                new List<LatheDesignView>(),
                string.IsNullOrEmpty(statusMessage) ? "Use 'Link Lathe' to establish DeviceLink." : statusMessage);

            _cartridgeLoader.UpdateCartridgeUiState(loaderUid, unlinkedState);
            return;
        }

        ent.Comp.LinkedLathe = latheUid;

        var currentProducing = latheComp.CurrentRecipe != null && _prototypeManager.TryIndex(latheComp.CurrentRecipe.Value, out var curProto)
            ? _lathe.GetRecipeName(curProto)
            : "Idle";

        var queueItems = new List<string>();
        foreach (var batch in latheComp.Queue)
        {
            if (_prototypeManager.TryIndex(batch.Recipe, out var recipeProto))
                queueItems.Add($"{batch.ItemsRequested - batch.ItemsPrinted}x {_lathe.GetRecipeName(recipeProto)}");
        }

        var materials = new Dictionary<string, int>();
        if (TryComp<MaterialStorageComponent>(latheUid.Value, out var storage))
        {
            foreach (var (matId, amount) in storage.Storage)
            {
                materials[matId.Id] = amount;
            }
        }

        var designs = new List<LatheDesignView>();
        var recipes = _lathe.GetAvailableRecipes(latheUid.Value, latheComp);
        if (TryComp<TechnologyDatabaseComponent>(latheUid.Value, out var techDb))
        {
            foreach (var r in techDb.UnlockedRecipes)
            {
                if (!recipes.Contains(r))
                    recipes.Add(r);
            }
        }

        foreach (var recipeId in recipes)
        {
            if (!_prototypeManager.TryIndex(recipeId, out var proto))
                continue;

            var sb = new StringBuilder();
            foreach (var (mat, amt) in proto.Materials)
            {
                var adj = SharedLatheSystem.AdjustMaterial(amt, proto.ApplyMaterialDiscount, latheComp.MaterialUseMultiplier);
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append($"{mat}: {adj}");
            }

            designs.Add(new LatheDesignView
            {
                Id = proto.ID,
                Name = _lathe.GetRecipeName(proto),
                MaterialsSummary = sb.ToString(),
                CanProduce = _lathe.CanProduce(latheUid.Value, proto, 1, latheComp)
            });
        }

        var state = new LatheControlUiState(
            true,
            Name(latheUid.Value),
            currentProducing,
            queueItems,
            materials,
            designs,
            statusMessage);

        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
    }

    // =========================================================================
    // APC Control Cartridge Handlers (via ExecuteComputerCommand + DeviceLink)
    // =========================================================================

    private void OnApcControlUiReady(Entity<ApcControlCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        ExecuteComputerCommand(args.Loader, "apc link", out _, ent.Owner);
        UpdateApcControlUi(ent, args.Loader);
    }

    private void OnApcControlUiMessage(Entity<ApcControlCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not ApcControlUiMessageEvent msg)
            return;

        var loaderUid = GetEntity(args.LoaderUid);
        var status = string.Empty;

        switch (msg.Action)
        {
            case ApcControlUiAction.LinkApc:
                var apcCmd = string.IsNullOrEmpty(msg.TargetGuid) ? "apc link" : $"apc link {msg.TargetGuid}";
                ExecuteComputerCommand(loaderUid, apcCmd, out status, ent.Owner);
                break;
            case ApcControlUiAction.ToggleMainBreaker:
                ExecuteComputerCommand(loaderUid, "apc toggle", out status, ent.Owner);
                break;
            case ApcControlUiAction.SetBreakerOn:
                ExecuteComputerCommand(loaderUid, "apc on", out status, ent.Owner);
                break;
            case ApcControlUiAction.SetBreakerOff:
                ExecuteComputerCommand(loaderUid, "apc off", out status, ent.Owner);
                break;
            case ApcControlUiAction.Refresh:
                ExecuteComputerCommand(loaderUid, "apc status", out status, ent.Owner);
                break;
        }

        UpdateApcControlUi(ent, loaderUid, status);
    }

    public void UpdateApcControlUi(Entity<ApcControlCartridgeComponent> ent, EntityUid loaderUid, string statusMessage = "")
    {
        var apcUid = FindLinkedTarget<ApcComponent>(loaderUid);
        if (apcUid == null || !TryComp<ApcComponent>(apcUid.Value, out var apcComp))
        {
            var unlinkedState = new ApcControlUiState(
                false,
                "No APC Linked",
                false,
                0f,
                0,
                "Disconnected",
                string.IsNullOrEmpty(statusMessage) ? "Use 'Link APC' to establish DeviceLink." : statusMessage);

            _cartridgeLoader.UpdateCartridgeUiState(loaderUid, unlinkedState);
            return;
        }

        ent.Comp.LinkedApc = apcUid;
        var chargePct = 0f;
        var loadWatts = 0;
        if (TryComp<PowerNetworkBatteryComponent>(apcUid.Value, out var netBat))
        {
            var bat = netBat.NetworkBattery;
            if (bat.Capacity > 0)
                chargePct = Math.Clamp(bat.CurrentStorage / bat.Capacity * 100f, 0f, 100f);
            loadWatts = (int) MathF.Ceiling(bat.CurrentSupply);
        }

        var state = new ApcControlUiState(
            true,
            Name(apcUid.Value),
            apcComp.MainBreakerEnabled,
            chargePct,
            loadWatts,
            apcComp.LastExternalState.ToString(),
            statusMessage);

        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
    }

    // =========================================================================
    // Atmos Control Cartridge Handlers (via ExecuteComputerCommand + DeviceLink)
    // =========================================================================

    private void OnAtmosControlUiReady(Entity<AtmosControlCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        ExecuteComputerCommand(args.Loader, "atmos link", out _, ent.Owner);
        UpdateAtmosControlUi(ent, args.Loader);
    }

    private void OnAtmosControlUiMessage(Entity<AtmosControlCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not AtmosControlUiMessageEvent msg)
            return;

        var loaderUid = GetEntity(args.LoaderUid);
        var status = string.Empty;

        switch (msg.Action)
        {
            case AtmosControlUiAction.LinkAlarm:
                var atmosCmd = string.IsNullOrEmpty(msg.TargetGuid) ? "atmos link" : $"atmos link {msg.TargetGuid}";
                ExecuteComputerCommand(loaderUid, atmosCmd, out status, ent.Owner);
                break;
            case AtmosControlUiAction.SetModeFiltering:
                ExecuteComputerCommand(loaderUid, "atmos mode Filtering", out status, ent.Owner);
                break;
            case AtmosControlUiAction.SetModeFill:
                ExecuteComputerCommand(loaderUid, "atmos mode Fill", out status, ent.Owner);
                break;
            case AtmosControlUiAction.SetModePanic:
                ExecuteComputerCommand(loaderUid, "atmos mode Panic", out status, ent.Owner);
                break;
            case AtmosControlUiAction.SetModeNone:
                ExecuteComputerCommand(loaderUid, "atmos mode None", out status, ent.Owner);
                break;
            case AtmosControlUiAction.Refresh:
                ExecuteComputerCommand(loaderUid, "atmos status", out status, ent.Owner);
                break;
        }

        UpdateAtmosControlUi(ent, loaderUid, status);
    }

    public void UpdateAtmosControlUi(Entity<AtmosControlCartridgeComponent> ent, EntityUid loaderUid, string statusMessage = "")
    {
        var alarmUid = FindLinkedTarget<AirAlarmComponent>(loaderUid);
        if (alarmUid == null || !TryComp<AirAlarmComponent>(alarmUid.Value, out var alarmComp))
        {
            var unlinkedState = new AtmosControlUiState(
                false,
                "No Air Alarm Linked",
                0f,
                0f,
                "None",
                "Offline",
                string.IsNullOrEmpty(statusMessage) ? "Use 'Link Air Alarm' to establish DeviceLink." : statusMessage);

            _cartridgeLoader.UpdateCartridgeUiState(loaderUid, unlinkedState);
            return;
        }

        ent.Comp.LinkedAirAlarm = alarmUid;
        var pressure = 0f;
        var temp = 0f;
        if (_atmosphere.GetContainingMixture(alarmUid.Value, false, false) is { } mix)
        {
            pressure = mix.Pressure;
            temp = mix.Temperature;
        }

        var state = new AtmosControlUiState(
            true,
            Name(alarmUid.Value),
            pressure,
            temp,
            alarmComp.CurrentMode.ToString(),
            alarmComp.State.ToString(),
            statusMessage);

        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
    }
}
