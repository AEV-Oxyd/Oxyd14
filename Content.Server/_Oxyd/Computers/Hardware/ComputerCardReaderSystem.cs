using System.Linq;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Access.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.PDA;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Wires;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;

namespace Content.Server._Oxyd.Computers.Hardware;

/// <summary>
/// Raised locally on a computer and on each of its running programs whenever a card slot
/// hardware module gains or loses its retained ID card.
/// </summary>
[ByRefEvent]
public readonly record struct ComputerCardReaderChangedEvent(EntityUid ComputerUid);

/// <summary>
/// Owns every way a computer reads ID cards. Programs never read cards from the world themselves:
/// swiping or inserting a card on a computer is only possible through installed
/// <see cref="ComputerIdCardSlotModuleComponent"/> hardware.
/// Scanner modules read a card transiently; slot modules physically hold the card.
/// Readers are ordered deterministically (<see cref="ModularComputerComponent.SlotIdModule"/> first,
/// <see cref="ModularComputerComponent.SlotIdModuleSecondary"/> second), so slot #0 and slot #1 map
/// to the Modifying ID and Target ID slots of the ID modification console.
/// </summary>
public sealed partial class ComputerCardReaderSystem : EntitySystem
{
    [Dependency] private OxydComputerSystem _computer = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    [Dependency] private EntityQuery<ComputerIdCardSlotModuleComponent> _readerQuery = default!;
    [Dependency] private EntityQuery<IdCardComponent> _idCardQuery = default!;
    [Dependency] private EntityQuery<OxydComputerComponent> _computerQuery = default!;
    [Dependency] private EntityQuery<WiresPanelComponent> _panelQuery = default!;

    private static readonly SoundPathSpecifier SwipeSound = new("/Audio/Machines/id_swipe.ogg");

    /// <summary>
    /// All card scanner / card slot parts installed in the computer in deterministic slot order:
    /// <see cref="ModularComputerComponent.SlotIdModule"/> first,
    /// <see cref="ModularComputerComponent.SlotIdModuleSecondary"/> second, then any remaining parts.
    /// </summary>
    public List<Entity<ComputerIdCardSlotModuleComponent>> GetReaders(EntityUid compUid)
    {
        var readers = new List<Entity<ComputerIdCardSlotModuleComponent>>();
        var seen = new HashSet<EntityUid>();

        void TryAddReader(EntityUid candidate)
        {
            if (Exists(candidate) &&
                !TerminatingOrDeleted(candidate) &&
                seen.Add(candidate) &&
                _readerQuery.TryComp(candidate, out var reader))
            {
                readers.Add((candidate, reader));
            }
        }

        if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotIdModule, out var idSlot1) &&
            idSlot1.Item is { Valid: true } p1)
        {
            TryAddReader(p1);
        }

        if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotIdModuleSecondary, out var idSlot2) &&
            idSlot2.Item is { Valid: true } p2)
        {
            TryAddReader(p2);
        }

        foreach (var part in _computer.GetInstalledHardwareEntities(compUid).OrderBy(p => p.Id))
        {
            TryAddReader(part);
        }

        return readers;
    }

    /// <summary>Only the card slot (retaining) readers, in deterministic slot order.</summary>
    public List<Entity<ComputerIdCardSlotModuleComponent>> GetSlotReaders(EntityUid compUid)
    {
        return GetReaders(compUid).Where(r => r.Comp.Mode == ComputerCardReaderMode.Slot).ToList();
    }

    /// <summary>True if at least one transient card scanner module is installed.</summary>
    public bool HasScannerReader(EntityUid compUid)
    {
        return GetReaders(compUid).Any(r => r.Comp.Mode == ComputerCardReaderMode.Scanner);
    }

    public bool HasReaders(EntityUid compUid) => GetReaders(compUid).Count > 0;

    public bool TryGetRetainedCard(Entity<ComputerIdCardSlotModuleComponent> reader, out EntityUid card)
    {
        card = EntityUid.Invalid;
        if (reader.Comp.Mode != ComputerCardReaderMode.Slot ||
            !_itemSlots.TryGetSlot(reader.Owner, reader.Comp.CardSlotId, out var slot) ||
            slot.Item is not { Valid: true } item ||
            !Exists(item) ||
            !_idCardQuery.HasComp(item))
        {
            return false;
        }

        card = item;
        return true;
    }

    /// <summary>Gets the card retained in the n-th card slot hardware part (0 = modifying ID, 1 = target ID).</summary>
    public bool TryGetSlotCard(EntityUid compUid, int slotIndex, out EntityUid card)
    {
        card = EntityUid.Invalid;
        var slots = GetSlotReaders(compUid);
        return slotIndex >= 0 && slotIndex < slots.Count && TryGetRetainedCard(slots[slotIndex], out card);
    }

    /// <summary>First card retained in any card slot hardware part.</summary>
    public bool TryGetAnyRetainedCard(EntityUid compUid, out EntityUid card)
    {
        foreach (var reader in GetSlotReaders(compUid))
        {
            if (TryGetRetainedCard(reader, out card))
                return true;
        }

        card = EntityUid.Invalid;
        return false;
    }

    private bool IsUserPhysicallyNearReader(EntityUid readerUid, EntityUid user, EntityUid? physicalHost = null)
    {
        var anchor = physicalHost is { Valid: true } h && Exists(h) ? h : readerUid;
        var current = anchor;
        while (_container.TryGetContainingContainer((current, null), out var container))
        {
            if (container.Owner == user)
                return true;
            current = container.Owner;
        }

        return _interaction.InRangeUnobstructed(user, current);
    }

    public bool TryInsertCard(Entity<ComputerIdCardSlotModuleComponent> reader, EntityUid card, EntityUid? user, EntityUid? physicalHost = null)
    {
        if (user is { Valid: true } actor && !IsUserPhysicallyNearReader(reader.Owner, actor, physicalHost))
            return false;

        return reader.Comp.Mode == ComputerCardReaderMode.Slot &&
               _itemSlots.TryGetSlot(reader.Owner, reader.Comp.CardSlotId, out var slot) &&
               slot.Item == null &&
               _itemSlots.TryInsert(reader.Owner, slot, card, user);
    }

    public bool TryEjectCard(Entity<ComputerIdCardSlotModuleComponent> reader, EntityUid? user, EntityUid? dropNear = null)
    {
        if (reader.Comp.Mode != ComputerCardReaderMode.Slot ||
            !_itemSlots.TryGetSlot(reader.Owner, reader.Comp.CardSlotId, out var slot) ||
            slot.Item == null)
        {
            return false;
        }

        if (user is { Valid: true } actor)
        {
            if (!IsUserPhysicallyNearReader(reader.Owner, actor, dropNear))
                return false;

            if (_itemSlots.TryEjectToHands(reader.Owner, slot, actor))
                return true;
        }

        if (!_itemSlots.TryEject(reader.Owner, slot, null, out var ejected))
            return false;

        if (dropNear is { Valid: true } host && Exists(host) && Exists(ejected.Value))
            _transform.SetCoordinates(ejected.Value, Transform(host).Coordinates);

        return true;
    }

    /// <summary>
    /// Swaps the cards retained in the Modifying ID slot (#0) and Target ID slot (#1).
    /// </summary>
    public bool TrySwapSlotCards(EntityUid compUid, EntityUid? user)
    {
        var slots = GetSlotReaders(compUid);
        if (slots.Count < 2)
            return false;

        var r0 = slots[0];
        var r1 = slots[1];
        if (!_itemSlots.TryGetSlot(r0.Owner, r0.Comp.CardSlotId, out var s0) ||
            !_itemSlots.TryGetSlot(r1.Owner, r1.Comp.CardSlotId, out var s1))
        {
            return false;
        }

        if (s0.Item == null && s1.Item == null)
            return false;

        EntityUid? card0 = null;
        EntityUid? card1 = null;

        if (s0.Item != null && _itemSlots.TryEject(r0.Owner, s0, null, out var ej0, excludeUserAudio: true))
            card0 = ej0;
        if (s1.Item != null && _itemSlots.TryEject(r1.Owner, s1, null, out var ej1, excludeUserAudio: true))
            card1 = ej1;

        if (card1 is { Valid: true } c1)
            _itemSlots.TryInsert(r0.Owner, s0, c1, user);
        if (card0 is { Valid: true } c0)
            _itemSlots.TryInsert(r1.Owner, s1, c0, user);

        NotifyReaderChanged(r0.Owner);
        return true;
    }

    /// <summary>Resolves the ID card carried by an item (an ID card itself, or a PDA holding one).</summary>
    public bool TryExtractCard(EntityUid used, out EntityUid card, out bool isLooseCard)
    {
        if (_idCardQuery.HasComp(used))
        {
            card = used;
            isLooseCard = true;
            return true;
        }

        isLooseCard = false;
        if (TryComp<PdaComponent>(used, out var pda))
        {
            // Keep PDA probe tools (LogProbe / NetProbe) working when clicking devices.
            if (TryComp<EmbeddedComputerHostComponent>(used, out var usedHost) &&
                usedHost.ComputerEntity is { } usedCompUid &&
                _computerQuery.TryComp(usedCompUid, out var usedComp) &&
                usedComp.IsBooted)
            {
                foreach (var progUid in usedComp.RunningPrograms)
                {
                    if (HasComp<LogProbeProgramComponent>(progUid) || HasComp<NetProbeProgramComponent>(progUid))
                    {
                        card = EntityUid.Invalid;
                        return false;
                    }
                }
            }

            if ((pda.IdSlot.Item ?? pda.ContainedId) is { Valid: true } pdaCard &&
                Exists(pdaCard) &&
                _idCardQuery.HasComp(pdaCard))
            {
                card = pdaCard;
                return true;
            }
        }

        card = EntityUid.Invalid;
        return false;
    }

    /// <summary>
    /// Installs a <see cref="ComputerIdCardSlotModuleComponent"/> part into the computer's ID module slots.
    /// </summary>
    public bool TryInstallReaderModule(EntityUid compUid, EntityUid moduleUid, EntityUid? user = null)
    {
        if (!_readerQuery.HasComp(moduleUid))
            return false;

        if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotIdModule, out var idSlot1) && idSlot1.Item == null)
            return _itemSlots.TryInsert(compUid, idSlot1, moduleUid, user);

        if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotIdModuleSecondary, out var idSlot2) && idSlot2.Item == null)
            return _itemSlots.TryInsert(compUid, idSlot2, moduleUid, user);

        return false;
    }

    /// <summary>
    /// Removes an installed <see cref="ComputerIdCardSlotModuleComponent"/> part from the computer,
    /// ejecting any retained ID card inside it first so cards are never trapped inside a loose module.
    /// </summary>
    public bool TryRemoveReaderModule(EntityUid compUid, EntityUid moduleUid, EntityUid? user = null, EntityUid? dropNear = null)
    {
        if (!_readerQuery.TryComp(moduleUid, out var reader))
            return false;

        if (reader.Mode == ComputerCardReaderMode.Slot && TryGetRetainedCard((moduleUid, reader), out _))
            TryEjectCard((moduleUid, reader), user, dropNear);

        if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotIdModule, out var idSlot1) && idSlot1.Item == moduleUid)
        {
            if (user is { } a1 && _itemSlots.TryEjectToHands(compUid, idSlot1, a1))
                return true;
            if (_itemSlots.TryEject(compUid, idSlot1, user, out var ej1))
            {
                if (dropNear is { Valid: true } h1 && Exists(h1) && Exists(ej1.Value))
                    _transform.SetCoordinates(ej1.Value, Transform(h1).Coordinates);
                return true;
            }
        }

        if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotIdModuleSecondary, out var idSlot2) && idSlot2.Item == moduleUid)
        {
            if (user is { } a2 && _itemSlots.TryEjectToHands(compUid, idSlot2, a2))
                return true;
            if (_itemSlots.TryEject(compUid, idSlot2, user, out var ej2))
            {
                if (dropNear is { Valid: true } h2 && Exists(h2) && Exists(ej2.Value))
                    _transform.SetCoordinates(ej2.Value, Transform(h2).Coordinates);
                return true;
            }
        }

        if (_container.TryGetContainingContainer(compUid, moduleUid, out var container))
        {
            if (_container.Remove(moduleUid, container))
            {
                if (dropNear is { Valid: true } h3 && Exists(h3))
                    _transform.SetCoordinates(moduleUid, Transform(h3).Coordinates);
                return true;
            }
        }

        return false;
    }

    [SubscribeLocalEvent]
    private void OnCardPresented(Entity<OxydComputerComponent> ent, ref ComputerCardPresentedEvent args)
    {
        if (args.Handled)
            return;

        // Allow technicians to install a Card Scanner or Card Slot module when the host's maintenance panel is open.
        if (_readerQuery.HasComp(args.Used) &&
            _panelQuery.TryComp(args.HostUid, out var panel) &&
            panel.Open)
        {
            args.Handled = true;
            if (TryInstallReaderModule(ent.Owner, args.Used, args.User))
                _popup.PopupEntity($"Installed {MetaData(args.Used).EntityName}.", args.HostUid, args.User);
            else
                _popup.PopupEntity("All ID module expansion slots are full.", args.HostUid, args.User);
            return;
        }

        if (!TryExtractCard(args.Used, out var cardUid, out var isLooseCard))
            return;

        var readers = GetReaders(ent.Owner);
        if (readers.Count == 0)
        {
            _popup.PopupEntity("No ID card scanner or card slot hardware installed.", args.HostUid, args.User);
            args.Handled = true;
            return;
        }

        if (!ent.Comp.IsBooted || ent.Comp.ActiveProgram is not { } activeProg || !Exists(activeProg))
        {
            _popup.PopupEntity("The computer is not running.", args.HostUid, args.User);
            args.Handled = true;
            return;
        }

        Entity<ComputerIdCardSlotModuleComponent>? chosen = null;
        var retained = false;

        foreach (var reader in readers)
        {
            if (reader.Comp.Mode == ComputerCardReaderMode.Scanner)
            {
                chosen = reader;
                break;
            }
        }

        if (chosen == null && isLooseCard)
        {
            foreach (var reader in readers)
            {
                if (!TryInsertCard(reader, cardUid, args.User))
                    continue;

                chosen = reader;
                retained = true;
                break;
            }
        }

        args.Handled = true;
        if (chosen == null)
        {
            _popup.PopupEntity(
                isLooseCard ? "All ID card slots are occupied." : "Use the ID card itself to insert it into a card slot.",
                args.HostUid,
                args.User);
            return;
        }

        _audio.PlayPvs(SwipeSound, args.HostUid);

        var scanned = new ProgramCardScannedEvent(ent.Owner, args.HostUid, cardUid, args.User, chosen.Value.Owner, retained);
        RaiseLocalEvent(activeProg, ref scanned);
    }

    [SubscribeLocalEvent]
    private void OnCardInserted(Entity<ComputerIdCardSlotModuleComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        NotifyReaderChanged(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnCardRemoved(Entity<ComputerIdCardSlotModuleComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        NotifyReaderChanged(ent.Owner);
    }

    private void NotifyReaderChanged(EntityUid readerUid)
    {
        var parent = Transform(readerUid).ParentUid;
        if (!parent.IsValid() || !_computerQuery.TryComp(parent, out var comp))
            return;

        var ev = new ComputerCardReaderChangedEvent(parent);
        RaiseLocalEvent(parent, ref ev);
        foreach (var prog in comp.RunningPrograms.ToList())
        {
            if (Exists(prog))
                RaiseLocalEvent(prog, ref ev);
        }
    }

    [SubscribeLocalEvent]
    private void OnGetEjectVerbs(Entity<EmbeddedComputerHostComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || ent.Comp.ComputerEntity is not { } compUid)
            return;

        var user = args.User;
        var hostUid = ent.Owner;
        var slots = GetSlotReaders(compUid);
        for (var i = 0; i < slots.Count; i++)
        {
            var reader = slots[i];
            if (!TryGetRetainedCard(reader, out var card))
                continue;

            var cardName = MetaData(card).EntityName;
            var slotLabel = slots.Count >= 2 ? (i == 0 ? "Modifying ID slot" : i == 1 ? "Target ID slot" : $"ID slot {i + 1}") : $"ID slot {i + 1}";
            args.Verbs.Add(new AlternativeVerb
            {
                Text = $"Eject {cardName} ({slotLabel})",
                Priority = 4 - i,
                Act = () => TryEjectCard(reader, user, hostUid),
            });
        }

        if (_panelQuery.TryComp(hostUid, out var panel) && panel.Open)
        {
            var readers = GetReaders(compUid);
            for (var i = 0; i < readers.Count; i++)
            {
                var reader = readers[i];
                var partName = MetaData(reader.Owner).EntityName;
                args.Verbs.Add(new AlternativeVerb
                {
                    Text = $"Remove {partName} (Module {i + 1})",
                    Priority = 1 - i,
                    Act = () => TryRemoveReaderModule(compUid, reader.Owner, user, hostUid),
                });
            }
        }
    }
}
