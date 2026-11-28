using System.Diagnostics.CodeAnalysis;
using Content.Server.Access.Systems;
using Content.Server.AlertLevel;
using Content.Server.Instruments;
using Content.Server.Light.EntitySystems;
using Content.Server.PDA.Ringer;
using Content.Server.Station.Systems;
using Content.Server.Store.Systems;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.Packets;
using Content.Shared.Access.Components;
using Content.Shared.AlertLevel;
using Content.Shared.Implants;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Components;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.DeviceLinking.Systems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Light;
using Content.Shared.Light.EntitySystems;
using Content.Shared.PDA;
using Content.Shared.PDA.Ringer;
using Content.Shared.Store.Components;
using Content.Shared.VoiceMask;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server.PDA
{
    /// <summary>
    /// Physical PDA hardware shell host (slots for ID, Pen, pAI, Data Disk, Flashlight, Ringer/Uplink, Instrument).
    /// All software, filesystems, and UI programs run inside its separate encapsulated computer entity
    /// and interface strictly through DeviceLink.
    /// </summary>
    public sealed partial class PdaSystem : SharedPdaSystem
    {
        [Dependency] private InstrumentSystem _instrument = default!;
        [Dependency] private RingerSystem _ringer = default!;
        [Dependency] private ServerStationSystem _station = default!;
        [Dependency] private StoreSystem _store = default!;
        [Dependency] private UnpoweredFlashlightSystem _unpoweredFlashlight = default!;
        [Dependency] private IdCardSystem _idCard = default!;
        [Dependency] private DeviceLinkSystem _deviceLink = default!;
        [Dependency] private SharedContainerSystem _container = default!;
        [Dependency] private SharedInteractionSystem _interaction = default!;

        private static readonly ProtoId<SourcePortPrototype> PortMachineStatusSender = "MachineStatusSender";
        private static readonly ProtoId<SourcePortPrototype> PortFlashlightStatusSource = "PdaFlashlightStatusSource";

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<PdaComponent, LightToggleEvent>(OnLightToggle);
            SubscribeLocalEvent<StationRenamedEvent>(OnStationRenamed);
            SubscribeLocalEvent<AlertLevelChangedEvent>(OnAlertLevelChanged);
            SubscribeLocalEvent<EntityRenamedEvent>(OnEntityRenamed, after: new[] { typeof(IdCardSystem) });
            SubscribeLocalEvent<PdaComponent, InventoryRelayedEvent<ChameleonControllerOutfitSelectedEvent>>(OnRelayedEventToIdCard);
            SubscribeLocalEvent<PdaComponent, InventoryRelayedEvent<VoiceMaskNameUpdatedEvent>>(OnRelayedEventToIdCard);

            SubscribeLocalEvent<PdaComponent, SignalReceivedEvent>(OnPdaSignalReceived);
            SubscribeLocalEvent<PdaComponent, SignalReceivedEvent<ComputerDeviceLinkPayload>>(OnPdaDeviceLinkPayload);
            SubscribeLocalEvent<PdaComponent, SignalReceivedEvent<ComputerPacket>>(OnPdaPacketSignalReceived);
        }

        private void OnPdaSignalReceived(Entity<PdaComponent> ent, ref SignalReceivedEvent args)
        {
            HandlePdaPortCommand(ent, args.Port, string.Empty, args.Trigger);
        }

        private void OnPdaDeviceLinkPayload(Entity<PdaComponent> ent, ref SignalReceivedEvent<ComputerDeviceLinkPayload> args)
        {
            var cmd = string.IsNullOrWhiteSpace(args.Data.Command) ? args.Port.Id : args.Data.Command;
            HandlePdaPortCommand(ent, args.Port, cmd, args.Trigger);
        }

        private void OnPdaPacketSignalReceived(Entity<PdaComponent> ent, ref SignalReceivedEvent<ComputerPacket> args)
        {
            EntityUid? actor = args.Data.ProtectedReferences.TryGetValue("Actor", out var actUid) ? actUid : args.Trigger;
            HandlePdaPortCommand(ent, args.Port, args.Data.PacketType, actor);
        }

        private void HandlePdaPortCommand(Entity<PdaComponent> ent, string port, string cmd, EntityUid? actor)
        {
            var c = cmd.Trim().ToLowerInvariant();
            if (port == "PdaFlashlightToggleSink" || c == "toggle_flashlight")
            {
                _unpoweredFlashlight.TryToggleLight(ent.Owner);
                NotifyHardwareStateChanged(ent.Owner, ent.Comp);
                return;
            }

            if (port == "PdaIdSlotEjectSink" || c == "eject_id")
            {
                TryEjectPdaSlot(ent.Owner, PdaComponent.PdaIdSlotId, actor);
                NotifyHardwareStateChanged(ent.Owner, ent.Comp);
                return;
            }

            if (port == "PdaPenSlotEjectSink" || c == "eject_pen")
            {
                TryEjectPdaSlot(ent.Owner, PdaComponent.PdaPenSlotId, actor);
                NotifyHardwareStateChanged(ent.Owner, ent.Comp);
                return;
            }

            if (c == "eject_disk")
            {
                TryEjectPdaSlot(ent.Owner, PdaComponent.PdaDiskSlotId, actor);
                NotifyHardwareStateChanged(ent.Owner, ent.Comp);
                return;
            }

            if (c == "eject_pai")
            {
                TryEjectPdaSlot(ent.Owner, PdaComponent.PdaPaiSlotId, actor);
                NotifyHardwareStateChanged(ent.Owner, ent.Comp);
                return;
            }

            if (actor is { } user)
            {
                if (c == "show_ringtone")
                    _ringer.TryToggleRingerUi(ent.Owner, user);
                else if (c == "show_music")
                    _instrument.ToggleInstrumentUi(ent.Owner, user);
                else if (c == "show_uplink" &&
                         (IsHeldOrWornByUser(ent.Owner, user) || _interaction.InRangeUnobstructed(user, ent.Owner)) &&
                         TryGetUnlockedStore(ent.Owner, out var store))
                {
                    if (store != ent.Owner)
                    {
                        if (TryComp<RemoteStoreComponent>(ent.Owner, out var remoteStore))
                            remoteStore.Store = store;
                        _store.ToggleUi(user, store.Value, remoteAccess: ent.Owner);
                    }
                    else
                    {
                        _store.ToggleUi(user, store.Value);
                    }
                }
                else if (c == "lock_uplink" && TryComp<RingerUplinkComponent>(ent.Owner, out var uplink))
                {
                    if (TryComp<RemoteStoreComponent>(ent.Owner, out var remoteStore))
                        remoteStore.Store = null;
                    _ringer.LockUplink((ent.Owner, uplink));
                    NotifyHardwareStateChanged(ent.Owner, ent.Comp);
                }
            }

            if (c is "status" or "status_request")
                NotifyHardwareStateChanged(ent.Owner, ent.Comp);
        }

        private void TryEjectPdaSlot(EntityUid pdaUid, string slotId, EntityUid? actor)
        {
            if (!ItemSlotsSystem.TryGetSlot(pdaUid, slotId, out var slot))
                return;

            if (actor is { } user &&
                (IsHeldOrWornByUser(pdaUid, user) || _interaction.InRangeUnobstructed(user, pdaUid)))
            {
                ItemSlotsSystem.TryEjectToHands(pdaUid, slot, user);
            }
            else
            {
                ItemSlotsSystem.TryEject(pdaUid, slot, null, out _);
            }
        }

        private bool IsHeldOrWornByUser(EntityUid entity, EntityUid user)
        {
            var current = entity;
            while (_container.TryGetContainingContainer((current, null), out var container))
            {
                if (container.Owner == user)
                    return true;
                current = container.Owner;
            }

            return false;
        }

        protected override void OnItemInserted(EntityUid uid, PdaComponent pda, EntInsertedIntoContainerMessage args)
        {
            base.OnItemInserted(uid, pda, args);

            if (args.Container.ID == PdaComponent.PdaIdSlotId && TryComp<IdCardComponent>(args.Entity, out var idCard))
            {
                pda.OwnerName = idCard.FullName;
                Dirty(uid, pda);
            }
            else if (args.Container.ID == PdaComponent.PdaDiskSlotId)
            {
                Dirty(uid, pda);
            }

            NotifyHardwareStateChanged(uid, pda);
        }

        protected override void OnItemRemoved(EntityUid uid, PdaComponent pda, EntRemovedFromContainerMessage args)
        {
            base.OnItemRemoved(uid, pda, args);

            if (args.Container.ID == PdaComponent.PdaIdSlotId)
            {
                pda.OwnerName = null;
                Dirty(uid, pda);
            }
            else if (args.Container.ID == PdaComponent.PdaDiskSlotId)
            {
                Dirty(uid, pda);
            }

            NotifyHardwareStateChanged(uid, pda);
        }

        private void OnLightToggle(EntityUid uid, PdaComponent pda, LightToggleEvent args)
        {
            pda.FlashlightOn = args.IsOn;
            Dirty(uid, pda);
            NotifyHardwareStateChanged(uid, pda);
        }

        private void OnStationRenamed(StationRenamedEvent ev)
        {
            UpdateStationDataOnAllPdas();
        }

        private void OnAlertLevelChanged(ref AlertLevelChangedEvent args)
        {
            UpdateStationDataOnAllPdas();
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
                    if (comp.ContainedId == idCard.Owner)
                    {
                        comp.OwnerName = idCard.Comp.FullName;
                        Dirty(uid, comp);
                        NotifyHardwareStateChanged(uid, comp);
                    }
                }
            }
        }

        private void OnRelayedEventToIdCard<T>(Entity<PdaComponent> ent, ref InventoryRelayedEvent<T> args)
        {
            if (ent.Comp.ContainedId != null)
                RaiseLocalEvent(ent.Comp.ContainedId.Value, args);
        }

        public bool TryGetUnlockedStore(EntityUid uid, [NotNullWhen(true)] out EntityUid? store)
        {
            store = null;
            if (!TryComp<RingerUplinkComponent>(uid, out var uplink) || !uplink.Unlocked)
                return false;

            store = _store.GetStore(uid);
            return store != null;
        }

        public override void NotifyHardwareStateChanged(EntityUid uid, PdaComponent? pda = null)
        {
            if (TerminatingOrDeleted(uid)
                || !TryComp<DeviceLinkSourceComponent>(uid, out var srcComp)
                || !Resolve(uid, ref pda, false))
                return;

            var jobTitle = pda.ContainedId is { } idUid && TryComp<IdCardComponent>(idUid, out var idCard)
                ? idCard.LocalizedJobTitle ?? string.Empty
                : string.Empty;

            var pkt = new ComputerPacket("PdaHardwareStatus");
            pkt.Set("FlashlightOn", pda.FlashlightOn.ToString());
            pkt.Set("OwnerName", pda.OwnerName ?? string.Empty);
            pkt.Set("JobTitle", jobTitle);
            pkt.Set("StationName", pda.StationName ?? string.Empty);

            _deviceLink.InvokePort((uid, srcComp), PortMachineStatusSender, ref pkt);
            _deviceLink.InvokePort((uid, srcComp), PortFlashlightStatusSource, ref pkt);
        }

        private void UpdateStationDataOnAllPdas()
        {
            var query = EntityQueryEnumerator<PdaComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                var st = _station.GetOwningStation(uid);
                comp.StationName = st is null ? null : Name(st.Value);
                if (st != null && TryComp<AlertLevelComponent>(st.Value, out var alert))
                    comp.StationAlertLevel = alert.CurrentAlertLevel;
                Dirty(uid, comp);
                NotifyHardwareStateChanged(uid, comp);
            }
        }
    }
}
