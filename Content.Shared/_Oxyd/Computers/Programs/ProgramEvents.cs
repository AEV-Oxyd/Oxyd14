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
