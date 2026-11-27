using Content.Shared._Oxyd.Computers.Packets;
using Content.Shared.DeviceLinking;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Computers.Programs;

/// <summary>
/// Raised locally on a program entity when it has been booted or launched inside a computer.
/// </summary>
[ByRefEvent]
public readonly record struct ProgramStartupEvent(EntityUid ComputerUid, EntityUid ProgramUid);

/// <summary>
/// Raised locally on a program entity when it becomes the active foreground program.
/// </summary>
[ByRefEvent]
public readonly record struct ProgramActivatedEvent(EntityUid ComputerUid, EntityUid ProgramUid);

/// <summary>
/// Raised locally on a program entity when it is backgrounded/deactivated.
/// </summary>
[ByRefEvent]
public readonly record struct ProgramDeactivatedEvent(EntityUid ComputerUid, EntityUid ProgramUid);

/// <summary>
/// Raised locally on a program entity right before it is terminated and deleted.
/// </summary>
[ByRefEvent]
public readonly record struct ProgramTerminatedEvent(EntityUid ComputerUid, EntityUid ProgramUid);

/// <summary>
/// Raised on running program entities whenever a DeviceLink signal/packet arrives
/// either on the computer itself or mirrored from the host machine it represents.
/// </summary>
[ByRefEvent]
public readonly record struct ProgramDeviceLinkReceivedEvent(
    EntityUid ComputerUid,
    EntityUid SourceEntity,
    string Port,
    EntityUid? Trigger,
    ComputerDeviceLinkPayload? DeviceLinkPayload,
    ComputerPacket? Packet,
    bool? BoolSignal);

/// <summary>
/// Relays physical world interactions from a host device (e.g., PDA InteractUsing or AfterInteract)
/// down to active programs running inside its embedded computer.
/// </summary>
[ByRefEvent]
public record struct ProgramRelayedEvent<TEvent>(EntityUid ComputerUid, TEvent Args) where TEvent : notnull;


/// <summary>
/// Broadcast on the server when a <see cref="ComputerPacket"/> arrives at an
/// <see cref="EmbeddedComputerHostComponent"/> or <see cref="OxydComputerComponent"/> DeviceLink sink port.
/// </summary>
public readonly record struct ComputerSinkPacketReceivedEvent(
    EntityUid ComputerUid,
    EntityUid ReceiverUid,
    string Port,
    ComputerPacket Packet);

/// <summary>
/// Raised by an embedded computer host on its computer entity whenever any item is used on the host.
/// The ID card reader hardware system consumes it when the item is an ID card (or a PDA carrying one),
/// scanning it through an installed card scanner or inserting it into an installed card slot.
/// </summary>
[ByRefEvent]
public record struct ComputerCardPresentedEvent(EntityUid ComputerUid, EntityUid HostUid, EntityUid Used, EntityUid User)
{
    public bool Handled;
}

/// <summary>
/// Raised on the active program of a computer after an ID card was read by card scanner / card slot hardware.
/// Programs must only act on swiped / inserted cards through this event.
/// </summary>
[ByRefEvent]
public record struct ProgramCardScannedEvent(
    EntityUid ComputerUid,
    EntityUid HostUid,
    EntityUid Card,
    EntityUid User,
    EntityUid Reader,
    bool Retained)
{
    public bool Handled;
}
