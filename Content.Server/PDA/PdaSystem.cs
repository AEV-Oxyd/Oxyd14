using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server.Access.Systems;
using Content.Server.Chat.Managers;
using Content.Server.Instruments;
using Content.Server.PDA.Ringer;
using Content.Server.Station.Systems;
using Content.Server.Store.Systems;
using Content.Shared._Oxyd.Computers;
using Content.Shared.Access.Components;
using Content.Shared.AlertLevel;
using Content.Shared.CartridgeLoader;
using Content.Shared.Chat;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Implants;
using Content.Shared.Inventory;
using Content.Shared.Light;
using Content.Shared.Light.EntitySystems;
using Content.Shared.PDA;
using Content.Shared.PDA.Ringer;
using Content.Shared.Store.Components;
using Content.Shared.VoiceMask;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.FileSystem;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.PDA
{
    public sealed partial class PdaSystem : SharedPdaSystem
    {
        [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
        [Dependency] private InstrumentSystem _instrument = default!;
        [Dependency] private RingerSystem _ringer = default!;
        [Dependency] private ServerStationSystem _station = default!;
        [Dependency] private StoreSystem _store = default!;
        [Dependency] private IChatManager _chatManager = default!;
        [Dependency] private UserInterfaceSystem _ui = default!;
        [Dependency] private UnpoweredFlashlightSystem _unpoweredFlashlight = default!;
        [Dependency] private ContainerSystem _containerSystem = default!;
        [Dependency] private IdCardSystem _idCard = default!;
        [Dependency] private IPrototypeManager _prototype = default!;
        [Dependency] private IGameTiming _timing = default!;
        [Dependency] private SharedFileSystemSystem _fileSystem = default!;
        [Dependency] private Content.Shared.Access.Systems.AccessReaderSystem _accessReader = default!;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<PdaComponent, LightToggleEvent>(OnLightToggle);

            // UI Events:
            SubscribeLocalEvent<PdaComponent, BoundUIOpenedEvent>(OnPdaOpen);
            SubscribeLocalEvent<PdaComponent, BoundUIClosedEvent>(OnPdaClose);
            SubscribeLocalEvent<PdaComponent, PdaRequestUpdateInterfaceMessage>(OnUiMessage);
            SubscribeLocalEvent<PdaComponent, PdaToggleFlashlightMessage>(OnUiMessage);
            SubscribeLocalEvent<PdaComponent, PdaShowRingtoneMessage>(OnUiMessage);
            SubscribeLocalEvent<PdaComponent, PdaShowMusicMessage>(OnUiMessage);
            SubscribeLocalEvent<PdaComponent, PdaShowUplinkMessage>(OnUiMessage);
            SubscribeLocalEvent<PdaComponent, PdaLockUplinkMessage>(OnUiMessage);
            SubscribeLocalEvent<PdaComponent, PdaCloseProgramTabMessage>(OnUiMessage);
            SubscribeLocalEvent<PdaComponent, PdaSwitchViewMessage>(OnUiMessage);

            SubscribeLocalEvent<PdaComponent, CartridgeLoaderNotificationSentEvent>(OnNotification);

            SubscribeLocalEvent<StationRenamedEvent>(OnStationRenamed);
            SubscribeLocalEvent<EntityRenamedEvent>(OnEntityRenamed, after: new[] { typeof(IdCardSystem) });
            SubscribeLocalEvent<AlertLevelChangedEvent>(OnAlertLevelChanged);
            SubscribeLocalEvent<PdaComponent, InventoryRelayedEvent<ChameleonControllerOutfitSelectedEvent>>(OnRelayedEventToIdCard);
            SubscribeLocalEvent<PdaComponent, InventoryRelayedEvent<VoiceMaskNameUpdatedEvent>>(OnRelayedEventToIdCard);
            SubscribeLocalEvent<PdaComponent, CartridgeLoaderProgramActivatedEvent>(OnProgramActivated);
            SubscribeLocalEvent<PdaComponent, CartridgeLoaderProgramDeactivatedEvent>(OnProgramDeactivated);
            SubscribeLocalEvent<PdaComponent, CartridgeLoaderProgramUninstalledEvent>(OnProgramUninstalled);
            SubscribeLocalEvent<PdaComponent, FileSystemChangedEvent>(OnFileSystemChanged);
        }

        private void OnProgramActivated(EntityUid uid, PdaComponent comp, ref CartridgeLoaderProgramActivatedEvent args)
        {
            if (!comp.OpenPrograms.Contains(args.Program))
                comp.OpenPrograms.Add(args.Program);
            comp.CurrentView = 3;
            UpdatePdaUi(uid, comp);
        }

        private void OnProgramDeactivated(EntityUid uid, PdaComponent comp, ref CartridgeLoaderProgramDeactivatedEvent args)
        {
            if (TryComp<CartridgeLoaderComponent>(uid, out var loader) && loader.ActiveProgram == null)
            {
                comp.ActiveCartridgeState = null;
                if (comp.CurrentView == 3)
                    comp.CurrentView = 1;
                UpdatePdaUi(uid, comp);
            }
        }

        private void OnProgramUninstalled(EntityUid uid, PdaComponent pda, ref CartridgeLoaderProgramUninstalledEvent args)
        {
            pda.OpenPrograms.Remove(args.Program);

            if (TryComp<FileSystemComponent>(uid, out var fs))
            {
                var targetProtoId = MetaData(args.Program).EntityPrototype?.ID;
                string? toDeletePath = null;
                foreach (var (path, file) in fs.Files)
                {
                    if (file is ProgramVirtualFile prog && prog.ProgramId == targetProtoId)
                    {
                        toDeletePath = path;
                        break;
                    }
                }

                if (toDeletePath != null)
                {
                    _fileSystem.TryDeleteFile(uid, toDeletePath, fs);
                }
            }

            UpdatePdaUi(uid, pda);
        }

        private void OnFileSystemChanged(EntityUid uid, PdaComponent pda, ref FileSystemChangedEvent args)
        {
            UpdatePdaUi(uid, pda);
        }



        private void OnRelayedEventToIdCard<T>(Entity<PdaComponent> ent, ref InventoryRelayedEvent<T> args)
        {
            // Relay it to your ID so it can update as well.
            if (ent.Comp.ContainedId != null)
                RaiseLocalEvent(ent.Comp.ContainedId.Value, args);
        }

        private void OnEntityRenamed(ref EntityRenamedEvent ev)
        {
            if (HasComp<IdCardComponent>(ev.Uid))
                return;

            if (_idCard.TryFindIdCard(ev.Uid, out var idCard))
            {
                var query = EntityQueryEnumerator<PdaComponent>();

                while (query.MoveNext(out var uid, out var comp))
                {
                    if (comp.ContainedId == idCard)
                    {
                        SetOwner(uid, comp, ev.Uid, ev.NewName);
                    }
                }
            }
        }

        protected override void OnComponentInit(EntityUid uid, PdaComponent pda, ComponentInit args)
        {
            base.OnComponentInit(uid, pda, args);

            if (!HasComp<UserInterfaceComponent>(uid))
                return;

            UpdateAlertLevel(uid, pda);
            UpdateStationName(uid, pda);
        }

        protected override void OnItemInserted(EntityUid uid, PdaComponent pda, EntInsertedIntoContainerMessage args)
        {
            base.OnItemInserted(uid, pda, args);
            var id = CompOrNull<IdCardComponent>(pda.ContainedId);
            if (id != null)
                pda.OwnerName = id.FullName;
            UpdatePdaUi(uid, pda);
        }

        protected override void OnItemRemoved(EntityUid uid, PdaComponent pda, EntRemovedFromContainerMessage args)
        {
            // TODO: This is super cursed just use compstates please.
            if (MetaData(uid).EntityLifeStage >= EntityLifeStage.Terminating)
                return;

            base.OnItemRemoved(uid, pda, args);
            UpdatePdaUi(uid, pda);
        }

        private void OnLightToggle(EntityUid uid, PdaComponent pda, LightToggleEvent args)
        {
            pda.FlashlightOn = args.IsOn;
            UpdatePdaUi(uid, pda);
        }

        private void OnStationRenamed(StationRenamedEvent ev)
        {
            UpdateAllPdaUisOnStation();
        }

        private void OnAlertLevelChanged(ref AlertLevelChangedEvent args)
        {
            UpdateAllPdaUisOnStation();
        }

        private void UpdateAllPdaUisOnStation()
        {
            var query = AllEntityQuery<PdaComponent>();
            while (query.MoveNext(out var ent, out var comp))
            {
                UpdatePdaUi(ent, comp);
            }
        }

        private void OnNotification(Entity<PdaComponent> ent, ref CartridgeLoaderNotificationSentEvent args)
        {
            _ringer.RingerPlayRingtone(ent.Owner);

            if (!_containerSystem.TryGetContainingContainer((ent, null, null), out var container)
                || !TryComp<ActorComponent>(container.Owner, out var actor))
                return;

            var message = FormattedMessage.EscapeText(args.Message);
            var wrappedMessage = Loc.GetString("pda-notification-message",
                ("header", args.Header),
                ("message", message));

            _chatManager.ChatMessageToOne(
                ChatChannel.Notifications,
                message,
                wrappedMessage,
                EntityUid.Invalid,
                false,
                actor.PlayerSession.Channel);
        }

        /// <summary>
        /// Send new UI state to clients, call if you modify something like uplink.
        /// </summary>
        public override void UpdatePdaUi(EntityUid uid, PdaComponent? pda = null)
        {
            if (!Resolve(uid, ref pda, false))
                return;

            if (!HasComp<DeviceGuidComponent>(uid) || !HasComp<FileSystemComponent>(uid))
                EnsurePdaInitialized(uid);

            var address = GetDeviceNetAddress(uid);
            var hasInstrument = HasComp<InstrumentComponent>(uid);
            var showUplink = TryGetUnlockedStore(uid, out _);

            UpdateStationName(uid, pda);
            UpdateAlertLevel(uid, pda);
            // TODO: Update the level and name of the station with each call to UpdatePdaUi is only needed for latejoin players.
            // TODO: If someone can implement changing the level and name of the station when changing the PDA grid, this can be removed.

            // TODO don't make this depend on cartridge loader!?!
            if (!TryComp(uid, out CartridgeLoaderComponent? loader))
                return;

            var programs = GetNetEntityList(_cartridgeLoader.GetAllPrograms(uid).ToList());
            var id = CompOrNull<IdCardComponent>(pda.ContainedId);

            var guidComp = CompOrNull<DeviceGuidComponent>(uid);
            var statsComp = CompOrNull<ComputerStatsComponent>(uid);
            var fsComp = CompOrNull<FileSystemComponent>(uid);
            var bwComp = CompOrNull<NetworkBandwidthComponent>(uid);
            var oxydComp = CompOrNull<OxydComputerComponent>(uid);

            // Synchronize running open programs
            pda.OpenPrograms.RemoveAll(p => !Exists(p) || EntityManager.IsQueuedForDeletion(p));
            if (oxydComp?.BootProgram is { } bootProg && Exists(bootProg) && !EntityManager.IsQueuedForDeletion(bootProg) && !pda.OpenPrograms.Contains(bootProg))
            {
                pda.OpenPrograms.Insert(0, bootProg);
            }
            if (loader.ActiveProgram.HasValue && !EntityManager.IsQueuedForDeletion(loader.ActiveProgram.Value) && !pda.OpenPrograms.Contains(loader.ActiveProgram.Value))
            {
                pda.OpenPrograms.Add(loader.ActiveProgram.Value);
            }

            var openCount = pda.OpenPrograms.Count > 0 ? pda.OpenPrograms.Count : (loader.ActiveProgram != null && !EntityManager.IsQueuedForDeletion(loader.ActiveProgram.Value) ? 1 : 0);

            ComputerMetricsHelper.CalculateMetrics(
                openCount,
                statsComp,
                fsComp,
                bwComp,
                out var cpuPercent,
                out var ramUsed,
                out var ramTotal,
                out var storageUsed,
                out var storageTotal,
                out var isSaturated,
                out _,
                curTime: _timing.CurTime.TotalSeconds);

            var openNetPrograms = pda.OpenPrograms.Select(p => GetNetEntity(p)).ToList();

            if (string.IsNullOrEmpty(pda.SshPassword))
            {
                var guidPart = guidComp?.Guid != null && guidComp.Guid.Length >= 4
                    ? guidComp.Guid[..4].ToLowerInvariant()
                    : "sec";
                pda.SshPassword = $"pda-{guidPart}";
            }

            var hasCaptainAccess = false;
            if (pda.ContainedId is { } idUid)
            {
                var tags = _accessReader.FindAccessTags(idUid);
                hasCaptainAccess = tags.Any(t => t.Id == "Captain" || t.Id == "Command");
            }

            var template = oxydComp?.Template ?? ComputerUiTemplate.Pda;
            var bootState = oxydComp?.BootState ?? ComputerBootState.Running;
            var bootMsg = oxydComp?.BootDiagnosticMessage ?? string.Empty;
            var screenSize = oxydComp?.DefaultScreenSize ?? new Vector2(580f, 480f);
            var bootProgramNet = oxydComp?.BootProgram != null ? GetNetEntity(oxydComp.BootProgram) : null;

            var state = new PdaUpdateState(
                programs,
                GetNetEntity(loader.ActiveProgram),
                pda.FlashlightOn,
                pda.PenSlot.HasItem,
                pda.PaiSlot.HasItem,
                new PdaIdInfoText
                {
                    ActualOwnerName = pda.OwnerName,
                    IdOwner = id?.FullName,
                    JobTitle = id?.LocalizedJobTitle,
                    StationAlertLevel = pda.StationAlertLevel,
                    StationAlertColor = pda.StationAlertColor
                },
                pda.StationName,
                showUplink,
                hasInstrument,
                address,
                guidComp?.Guid,
                cpuPercent,
                ramUsed,
                ramTotal,
                storageUsed,
                storageTotal,
                bwComp?.IsSaturated ?? false,
                openPrograms: openNetPrograms,
                cartridgeState: pda.ActiveCartridgeState,
                hasDisk: pda.DiskSlot.HasItem,
                diskName: pda.DiskSlot.Item is { } dEnt ? MetaData(dEnt).EntityName : null,
                hasCaptainAccess: hasCaptainAccess,
                sshPassword: hasCaptainAccess ? pda.SshPassword : null,
                currentView: pda.CurrentView,
                template: template,
                bootState: bootState,
                bootDiagnosticMessage: bootMsg,
                defaultScreenSize: screenSize,
                bootProgram: bootProgramNet,
                uploadBps: bwComp?.CurrentBytesSent ?? 0,
                downloadBps: bwComp?.CurrentBytesReceived ?? 0,
                batteryChargePercent: statsComp?.BatteryChargePercent ?? 100f,
                hasBattery: statsComp?.HasBattery ?? false,
                osState: pda.OsUiState);

            pda.LastState = state;
            if (_ui.HasUi(uid, PdaUiKey.Key))
                _ui.SetUiState(uid, PdaUiKey.Key, state);
            if (_ui.HasUi(uid, ComputerUiKey.Key))
                _ui.SetUiState(uid, ComputerUiKey.Key, state);
            Get<Content.Server._Oxyd.Computers.Cartridges.TerminalCartridgeSystem>().OnRemoteDeviceUpdated(uid);
        }

        public void EnsurePdaInitialized(EntityUid uid)
        {
            var guid = EnsureComp<DeviceGuidComponent>(uid);
            EnsureComp<ComputerStatsComponent>(uid);
            var pdaBw = EnsureComp<NetworkBandwidthComponent>(uid);
            if (pdaBw.MaxBps < 250000)
            {
                pdaBw.MaxBps = 250000;
                Dirty(uid, pdaBw);
            }
            var fs = EnsureComp<FileSystemComponent>(uid);
            if (fs.Files.Count == 0)
            {
                var osFile = new TextVirtualFile
                {
                    Name = "os",
                    Extension = "sys",
                    Directory = "/sys/",
                    Size = 64,
                    TextContent = "NTOS-PORTABLE-EDITION-4.2",
                    IsReadOnly = true
                };
                fs.Files[osFile.FullPath] = osFile;

                var notesFile = new TextVirtualFile
                {
                    Name = "shift_notes",
                    Extension = "txt",
                    Directory = "/notes/",
                    Size = 32,
                    TextContent = "Station operations in progress.",
                    IsReadOnly = false
                };
                fs.Files[notesFile.FullPath] = notesFile;
            }

            if (TryComp<CartridgeLoaderComponent>(uid, out var loader))
            {
                if (!TryComp<OxydComputerComponent>(uid, out var comp) || comp.Template != ComputerUiTemplate.Embedded)
                {
                    EnsureDefaultPrograms(uid, loader);
                }
                EnsureProgramFiles(uid, loader, fs);

                var oxydComp = EnsureComp<OxydComputerComponent>(uid);
                if (oxydComp.BootProgram == null || !Exists(oxydComp.BootProgram.Value))
                {
                    var allProgs = _cartridgeLoader.GetAllPrograms(uid).ToList();
                    EntityUid? bootProg = null;
                    if (oxydComp.Template == ComputerUiTemplate.Embedded)
                    {
                        bootProg = allProgs.FirstOrDefault(p =>
                            HasComp<Content.Shared._Oxyd.Computers.ApcControlCartridgeComponent>(p) ||
                            HasComp<Content.Shared._Oxyd.Computers.AtmosControlCartridgeComponent>(p) ||
                            HasComp<Content.Shared._Oxyd.Computers.LatheControlCartridgeComponent>(p) ||
                            HasComp<Content.Shared._Oxyd.Computers.EmbeddedOsCartridgeComponent>(p));
                    }
                    else
                    {
                        bootProg = allProgs.FirstOrDefault(p => HasComp<Content.Shared._Oxyd.Computers.OsCartridgeComponent>(p));
                    }
                    oxydComp.BootProgram = (bootProg is { } bp && bp.IsValid()) ? bootProg : (allProgs.Count > 0 ? allProgs[0] : null);
                    Dirty(uid, oxydComp);
                }
                if (loader.ActiveProgram == null && oxydComp.BootProgram is { } boot)
                    _cartridgeLoader.ActivateProgram((uid, loader), boot);
            }
        }

        private void OnPdaOpen(Entity<PdaComponent> ent, ref BoundUIOpenedEvent args)
        {
            if (!PdaUiKey.Key.Equals(args.UiKey) && !ComputerUiKey.Key.Equals(args.UiKey))
                return;

            EnsurePdaInitialized(ent.Owner);

            if (TryComp<CartridgeLoaderComponent>(ent.Owner, out var loader))
            {
                var oxydComp = EnsureComp<OxydComputerComponent>(ent.Owner);
                if (oxydComp.BootProgram == null || !Exists(oxydComp.BootProgram.Value))
                {
                    var allProgs = _cartridgeLoader.GetAllPrograms(ent.Owner).ToList();
                    EntityUid? bootProg = null;
                    if (oxydComp.Template == ComputerUiTemplate.Embedded)
                    {
                        bootProg = allProgs.FirstOrDefault(p =>
                            HasComp<Content.Shared._Oxyd.Computers.ApcControlCartridgeComponent>(p) ||
                            HasComp<Content.Shared._Oxyd.Computers.AtmosControlCartridgeComponent>(p) ||
                            HasComp<Content.Shared._Oxyd.Computers.LatheControlCartridgeComponent>(p) ||
                            HasComp<Content.Shared._Oxyd.Computers.EmbeddedOsCartridgeComponent>(p));
                    }
                    else
                    {
                        bootProg = allProgs.FirstOrDefault(p => HasComp<Content.Shared._Oxyd.Computers.OsCartridgeComponent>(p));
                    }
                    oxydComp.BootProgram = (bootProg is { } bp && bp.IsValid()) ? bootProg : (allProgs.Count > 0 ? allProgs[0] : null);
                    Dirty(ent.Owner, oxydComp);
                }
                if (loader.ActiveProgram == null && oxydComp.BootProgram is { } bootProgToActivate)
                    _cartridgeLoader.ActivateProgram((ent.Owner, loader), bootProgToActivate);

                // OS / Boot program is always ready and in charge
                if (oxydComp.BootProgram is { } boot && Exists(boot))
                {
                    var readyEvt = new CartridgeUiReadyEvent((ent.Owner, loader));
                    RaiseLocalEvent(boot, ref readyEvt);
                }

                // Foreground running program (if different from OS) is also made ready
                if (loader.ActiveProgram is { } activeProg && activeProg != oxydComp.BootProgram && Exists(activeProg))
                {
                    var readyEvt = new CartridgeUiReadyEvent((ent.Owner, loader));
                    RaiseLocalEvent(activeProg, ref readyEvt);
                }
            }

            var devName = GetDeviceDisplayName(ent.Owner);
            _fileSystem.LogSystemActivity(ent.Owner, "POWER", $"{devName} display powered ON (User: {args.Actor})");
            if (!Get<Content.Server._Oxyd.Computers.Cartridges.TerminalCartridgeSystem>().OnRemoteScreenOpened(args.Actor, ent.Owner))
                UpdatePdaUi(ent.Owner, ent.Comp);
        }

        private void OnPdaClose(Entity<PdaComponent> ent, ref BoundUIClosedEvent args)
        {
            if (!PdaUiKey.Key.Equals(args.UiKey) && !ComputerUiKey.Key.Equals(args.UiKey))
                return;

            if (args.Actor is { } actor)
            {
                Get<Content.Server._Oxyd.Computers.Cartridges.TerminalCartridgeSystem>().OnRemoteScreenClosed(actor, ent.Owner);
            }

            var devName = GetDeviceDisplayName(ent.Owner);
            _fileSystem.LogSystemActivity(ent.Owner, "POWER", $"{devName} display powered OFF / standby (User: {args.Actor})");
        }

        private string GetDeviceDisplayName(EntityUid uid)
        {
            if (TryComp<OxydComputerComponent>(uid, out var comp))
            {
                if (comp.HostMachine is { } host && Exists(host))
                    return MetaData(host).EntityName;
                if (comp.Template == ComputerUiTemplate.Embedded)
                    return "Embedded controller";
                if (comp.Template == ComputerUiTemplate.Desktop)
                    return "Computer";
            }
            return MetaData(uid).EntityName;
        }

        private void EnsureDefaultPrograms(EntityUid uid, CartridgeLoaderComponent loader)
        {
            if (TryComp<OxydComputerComponent>(uid, out var comp) && comp.Template == ComputerUiTemplate.Embedded)
                return;

            var existingProtos = _cartridgeLoader.GetAllPrograms(uid)
                .Select(p => MetaData(p).EntityPrototype?.ID)
                .Where(id => id != null)
                .ToHashSet();

            string[] defaultCarts = ["OxydOsCartridge", "OxydFileBrowserCartridge", "OxydTerminalCartridge", "OxydDownloaderCartridge"];
            foreach (var cartId in defaultCarts)
            {
                if (!existingProtos.Contains(cartId) && _prototype.HasIndex<EntityPrototype>(cartId))
                {
                    _cartridgeLoader.InstallProgram((uid, loader), cartId, deinstallable: false);
                }
            }
        }

        private void EnsureProgramFiles(EntityUid loaderUid, CartridgeLoaderComponent loader, FileSystemComponent fs)
        {
            foreach (var prog in _cartridgeLoader.GetAllPrograms(loaderUid))
            {
                var meta = MetaData(prog);
                if (meta.EntityPrototype == null)
                    continue;

                var protoId = meta.EntityPrototype.ID;
                var cleanName = meta.EntityPrototype.Name.ToLowerInvariant().Replace(' ', '_').Replace("-", "_");
                if (cleanName.EndsWith("_cartridge"))
                    cleanName = cleanName.Substring(0, cleanName.Length - "_cartridge".Length);
                var fullPath = $"/bin/{cleanName}.bin";

                if (!fs.Files.TryGetValue(fullPath, out var existingFile) || existingFile.Size == 2048)
                {
                    var title = CompOrNull<CartridgeComponent>(prog)?.ProgramName is { } nameLoc ? Loc.GetString(nameLoc) : meta.EntityPrototype.Name;
                    var progFile = new ProgramVirtualFile
                    {
                        Name = cleanName,
                        Extension = "bin",
                        Directory = "/bin/",
                        ProgramId = protoId,
                        Title = title,
                        Size = ComputerMetricsHelper.GetProgramSize(protoId),
                        IsReadOnly = false
                    };
                    _fileSystem.TryWriteFile(loaderUid, progFile, fs);
                }
            }
        }

        private void OnUiMessage(EntityUid uid, PdaComponent pda, PdaRequestUpdateInterfaceMessage msg)
        {
            if (!PdaUiKey.Key.Equals(msg.UiKey))
                return;

            UpdatePdaUi(uid, pda);
        }

        private void OnUiMessage(EntityUid uid, PdaComponent pda, PdaToggleFlashlightMessage msg)
        {
            if (!PdaUiKey.Key.Equals(msg.UiKey))
                return;

            // TODO PREDICTION
            // When moving this to shared, fill in the user field
            _unpoweredFlashlight.TryToggleLight(uid, user: null);
        }

        private void OnUiMessage(EntityUid uid, PdaComponent pda, PdaShowRingtoneMessage msg)
        {
            if (!PdaUiKey.Key.Equals(msg.UiKey))
                return;

            if (HasComp<RingerComponent>(uid))
                _ringer.TryToggleRingerUi(uid, msg.Actor);
        }

        private void OnUiMessage(EntityUid uid, PdaComponent pda, PdaShowMusicMessage msg)
        {
            if (!PdaUiKey.Key.Equals(msg.UiKey))
                return;

            if (TryComp<InstrumentComponent>(uid, out var instrument))
                _instrument.ToggleInstrumentUi(uid, msg.Actor, instrument);
        }

        private void OnUiMessage(EntityUid uid, PdaComponent pda, PdaShowUplinkMessage msg)
        {
            if (!PdaUiKey.Key.Equals(msg.UiKey))
                return;

            // check if its locked again to prevent malicious clients opening locked uplinks
            if (TryGetUnlockedStore(uid, out var store))
            {
                if (store != uid)
                {
                    if (TryComp<RemoteStoreComponent>(uid, out var remoteStore))
                        remoteStore.Store = store;
                    _store.ToggleUi(msg.Actor, store.Value, remoteAccess: uid);
                }
                else
                {
                    _store.ToggleUi(msg.Actor, store.Value);
                }
            }
        }

        private void OnUiMessage(EntityUid uid, PdaComponent pda, PdaLockUplinkMessage msg)
        {
            if (!PdaUiKey.Key.Equals(msg.UiKey))
                return;

            if (TryComp<RingerUplinkComponent>(uid, out var uplink))
            {
                if (TryComp<RemoteStoreComponent>(uid, out var remoteStore))
                    remoteStore.Store = null;
                _ringer.LockUplink((uid, uplink));
                UpdatePdaUi(uid, pda);
            }
        }

        private void OnUiMessage(EntityUid uid, PdaComponent pda, PdaCloseProgramTabMessage msg)
        {
            var progUid = GetEntity(msg.Program);
            if (TryComp<OxydComputerComponent>(uid, out var oxydComp) && oxydComp.BootProgram == progUid)
                return;

            pda.OpenPrograms.Remove(progUid);
            if (TryComp<CartridgeLoaderComponent>(uid, out var loader))
            {
                if (loader.ActiveProgram == progUid)
                {
                    if (pda.OpenPrograms.Count > 0)
                    {
                        var nextProg = pda.OpenPrograms.Last();
                        _cartridgeLoader.ActivateProgram((uid, loader), nextProg);
                    }
                    else
                    {
                        _cartridgeLoader.DeactivateProgram((uid, loader), progUid);
                    }
                }
            }
            UpdatePdaUi(uid, pda);
        }

        private void OnUiMessage(EntityUid uid, PdaComponent pda, PdaSwitchViewMessage msg)
        {
            var clamped = Math.Clamp(msg.View, 0, 3);
            if (pda.CurrentView == clamped)
                return;

            pda.CurrentView = clamped;
            UpdatePdaUi(uid, pda);
        }

        /// <summary>
        /// Returns the currently unlocked store, if there is one.
        /// </summary>
        public bool TryGetUnlockedStore(EntityUid uid, [NotNullWhen(true)] out EntityUid? store)
        {
            store = null;
            if (!TryComp<RingerUplinkComponent>(uid, out var uplink) || !uplink.Unlocked)
                return false;

            store = _store.GetStore(uid);
            return store != null;
        }

        private void UpdateStationName(EntityUid uid, PdaComponent pda)
        {
            var station = _station.GetOwningStation(uid);
            pda.StationName = station is null ? null : Name(station.Value);
        }

        private void UpdateAlertLevel(EntityUid uid, PdaComponent pda)
        {
            var station = _station.GetOwningStation(uid);
            if (!TryComp(station, out AlertLevelComponent? alertComp))
                return;
            pda.StationAlertLevel = alertComp.CurrentAlertLevel;
            if (_prototype.Resolve(alertComp.CurrentAlertLevel, out var level))
                pda.StationAlertColor = level.Color;
        }

        private string? GetDeviceNetAddress(EntityUid uid)
        {
            string? address = null;

            if (TryComp(uid, out DeviceNetworkComponent? deviceNetworkComponent))
            {
                address = DeviceLocalizationHelpers.GetAddressFromId(deviceNetworkComponent);
            }

            return address;
        }
    }
}
