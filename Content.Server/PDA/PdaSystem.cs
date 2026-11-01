using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Access.Systems;
using Content.Server.Chat.Managers;
using Content.Server.Instruments;
using Content.Server.PDA.Ringer;
using Content.Server.Station.Systems;
using Content.Server.Store.Systems;
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

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<PdaComponent, LightToggleEvent>(OnLightToggle);

            // UI Events:
            SubscribeLocalEvent<PdaComponent, BoundUIOpenedEvent>(OnPdaOpen);
            SubscribeLocalEvent<PdaComponent, PdaRequestUpdateInterfaceMessage>(OnUiMessage);
            SubscribeLocalEvent<PdaComponent, PdaToggleFlashlightMessage>(OnUiMessage);
            SubscribeLocalEvent<PdaComponent, PdaShowRingtoneMessage>(OnUiMessage);
            SubscribeLocalEvent<PdaComponent, PdaShowMusicMessage>(OnUiMessage);
            SubscribeLocalEvent<PdaComponent, PdaShowUplinkMessage>(OnUiMessage);
            SubscribeLocalEvent<PdaComponent, PdaLockUplinkMessage>(OnUiMessage);
            SubscribeLocalEvent<PdaComponent, PdaCloseProgramTabMessage>(OnUiMessage);

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
            comp.ActiveCartridgeState = null;
            UpdatePdaUi(uid, comp);
        }

        private void OnProgramDeactivated(EntityUid uid, PdaComponent comp, ref CartridgeLoaderProgramDeactivatedEvent args)
        {
            comp.ActiveCartridgeState = null;
            UpdatePdaUi(uid, comp);
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

            if (!_ui.HasUi(uid, PdaUiKey.Key))
                return;

            var address = GetDeviceNetAddress(uid);
            var hasInstrument = HasComp<InstrumentComponent>(uid);
            var showUplink = TryGetUnlockedStore(uid, out _);

            UpdateStationName(uid, pda);
            UpdateAlertLevel(uid, pda);
            // TODO: Update the level and name of the station with each call to UpdatePdaUi is only needed for latejoin players.
            // TODO: If someone can implement changing the level and name of the station when changing the PDA grid, this can be removed.

            // TODO don't make this depend on cartridge loader!?!?
            if (!TryComp(uid, out CartridgeLoaderComponent? loader))
                return;

            var programs = GetNetEntityList(_cartridgeLoader.GetAllPrograms(uid).ToList());
            var id = CompOrNull<IdCardComponent>(pda.ContainedId);

            var guidComp = CompOrNull<DeviceGuidComponent>(uid);
            var statsComp = CompOrNull<ComputerStatsComponent>(uid);
            var fsComp = CompOrNull<FileSystemComponent>(uid);
            var bwComp = CompOrNull<NetworkBandwidthComponent>(uid);

            // Synchronize running open programs
            pda.OpenPrograms.RemoveAll(p => !Exists(p) || EntityManager.IsQueuedForDeletion(p));
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
                cartridgeState: pda.ActiveCartridgeState);

            _ui.SetUiState(uid, PdaUiKey.Key, state);
        }

        private void OnPdaOpen(Entity<PdaComponent> ent, ref BoundUIOpenedEvent args)
        {
            if (!PdaUiKey.Key.Equals(args.UiKey))
                return;

            EnsureComp<DeviceGuidComponent>(ent.Owner);
            EnsureComp<ComputerStatsComponent>(ent.Owner);
            EnsureComp<NetworkBandwidthComponent>(ent.Owner);
            var fs = EnsureComp<FileSystemComponent>(ent.Owner);
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

            if (TryComp<CartridgeLoaderComponent>(ent.Owner, out var loader))
            {
                EnsureDefaultPrograms(ent.Owner, loader);
                EnsureProgramFiles(ent.Owner, loader, fs);
            }

            UpdatePdaUi(ent.Owner, ent.Comp);
        }

        private void EnsureDefaultPrograms(EntityUid uid, CartridgeLoaderComponent loader)
        {
            var existingProtos = _cartridgeLoader.GetAllPrograms(uid)
                .Select(p => MetaData(p).EntityPrototype?.ID)
                .Where(id => id != null)
                .ToHashSet();

            string[] defaultCarts = ["OxydFileBrowserCartridge", "OxydTerminalCartridge", "OxydDownloaderCartridge"];
            foreach (var cartId in defaultCarts)
            {
                if (!existingProtos.Contains(cartId))
                {
                    var spawned = Spawn(cartId);
                    _cartridgeLoader.InstallCartridge((uid, loader), spawned);
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

        /// <summary>
        /// Returns the currently unlocked store, if there is one.
        /// </summary>
        private bool TryGetUnlockedStore(EntityUid uid, [NotNullWhen(true)] out EntityUid? store)
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
