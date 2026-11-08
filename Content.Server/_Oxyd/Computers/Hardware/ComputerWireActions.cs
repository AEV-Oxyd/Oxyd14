using Content.Server._Oxyd.Computers;
using Content.Server.Wires;
using Content.Shared._Oxyd.Computers;
using Content.Shared.Wires;
using Robust.Shared.GameObjects;

namespace Content.Server._Oxyd.Computers.Hardware;

[DataDefinition]
public sealed partial class ComputerPowerWireAction : BaseWireAction
{
    public override Color Color { get; set; } = Color.Red;
    public override string Name { get; set; } = "wire-name-computer-power";

    public override StatusLightState? GetLightState(Wire wire)
    {
        if (EntityManager.TryGetComponent<OxydComputerComponent>(wire.Owner, out var comp))
            return comp.PowerWireCut ? StatusLightState.Off : StatusLightState.On;

        return StatusLightState.On;
    }

    public override object StatusKey => OxydComputerWireStatusKey.Power;

    public override bool Cut(EntityUid user, Wire wire)
    {
        base.Cut(user, wire);
        if (EntityManager.TryGetComponent<OxydComputerComponent>(wire.Owner, out var comp))
        {
            comp.PowerWireCut = true;
            EntityManager.System<OxydComputerSystem>().RefreshComputerBootState(wire.Owner, comp);
        }

        return true;
    }

    public override bool Mend(EntityUid user, Wire wire)
    {
        base.Mend(user, wire);
        if (EntityManager.TryGetComponent<OxydComputerComponent>(wire.Owner, out var comp))
        {
            comp.PowerWireCut = false;
            EntityManager.System<OxydComputerSystem>().RefreshComputerBootState(wire.Owner, comp);
        }

        return true;
    }

    public override void Pulse(EntityUid user, Wire wire)
    {
        base.Pulse(user, wire);
        if (EntityManager.TryGetComponent<OxydComputerComponent>(wire.Owner, out var comp))
        {
            EntityManager.System<OxydComputerSystem>().RefreshComputerBootState(wire.Owner, comp);
        }
    }
}

[DataDefinition]
public sealed partial class ComputerRemoteWireAction : BaseWireAction
{
    public override Color Color { get; set; } = Color.Blue;
    public override string Name { get; set; } = "wire-name-computer-remote";

    public override StatusLightState? GetLightState(Wire wire)
    {
        if (EntityManager.TryGetComponent<OxydComputerComponent>(wire.Owner, out var comp))
            return comp.RemoteControlWireCut ? StatusLightState.Off : StatusLightState.On;

        return StatusLightState.On;
    }

    public override object StatusKey => OxydComputerWireStatusKey.RemoteControl;

    public override bool Cut(EntityUid user, Wire wire)
    {
        base.Cut(user, wire);
        if (EntityManager.TryGetComponent<OxydComputerComponent>(wire.Owner, out var comp))
        {
            comp.RemoteControlWireCut = true;
            comp.AllowRemoteControl = false;
            EntityManager.System<OxydComputerSystem>().OnRemoteControlCut(wire.Owner, comp);
        }

        return true;
    }

    public override bool Mend(EntityUid user, Wire wire)
    {
        base.Mend(user, wire);
        if (EntityManager.TryGetComponent<OxydComputerComponent>(wire.Owner, out var comp))
        {
            comp.RemoteControlWireCut = false;
            comp.AllowRemoteControl = true;
            EntityManager.System<OxydComputerSystem>().RefreshComputerBootState(wire.Owner, comp);
        }

        return true;
    }

    public override void Pulse(EntityUid user, Wire wire)
    {
        base.Pulse(user, wire);
        if (EntityManager.TryGetComponent<OxydComputerComponent>(wire.Owner, out var comp))
        {
            EntityManager.System<OxydComputerSystem>().OnRemoteControlCut(wire.Owner, comp);
        }
    }
}

[DataDefinition]
public sealed partial class ComputerNetworkWireAction : BaseWireAction
{
    public override Color Color { get; set; } = Color.Green;
    public override string Name { get; set; } = "wire-name-computer-network";

    public override StatusLightState? GetLightState(Wire wire)
    {
        if (EntityManager.TryGetComponent<OxydComputerComponent>(wire.Owner, out var comp))
            return comp.NetworkWireCut ? StatusLightState.Off : StatusLightState.On;

        return StatusLightState.On;
    }

    public override object StatusKey => OxydComputerWireStatusKey.Network;

    public override bool Cut(EntityUid user, Wire wire)
    {
        base.Cut(user, wire);
        if (EntityManager.TryGetComponent<OxydComputerComponent>(wire.Owner, out var comp))
        {
            comp.NetworkWireCut = true;
            EntityManager.System<OxydComputerSystem>().RefreshComputerBootState(wire.Owner, comp);
        }

        return true;
    }

    public override bool Mend(EntityUid user, Wire wire)
    {
        base.Mend(user, wire);
        if (EntityManager.TryGetComponent<OxydComputerComponent>(wire.Owner, out var comp))
        {
            comp.NetworkWireCut = false;
            EntityManager.System<OxydComputerSystem>().RefreshComputerBootState(wire.Owner, comp);
        }

        return true;
    }

    public override void Pulse(EntityUid user, Wire wire)
    {
        base.Pulse(user, wire);
    }
}

[DataDefinition]
public sealed partial class ComputerStorageWireAction : BaseWireAction
{
    public override Color Color { get; set; } = Color.Yellow;
    public override string Name { get; set; } = "wire-name-computer-storage";

    public override StatusLightState? GetLightState(Wire wire)
    {
        if (EntityManager.TryGetComponent<OxydComputerComponent>(wire.Owner, out var comp))
            return comp.StorageWireCut ? StatusLightState.Off : StatusLightState.On;

        return StatusLightState.On;
    }

    public override object StatusKey => OxydComputerWireStatusKey.Storage;

    public override bool Cut(EntityUid user, Wire wire)
    {
        base.Cut(user, wire);
        if (EntityManager.TryGetComponent<OxydComputerComponent>(wire.Owner, out var comp))
        {
            comp.StorageWireCut = true;
            EntityManager.System<OxydComputerSystem>().RefreshComputerBootState(wire.Owner, comp);
        }

        return true;
    }

    public override bool Mend(EntityUid user, Wire wire)
    {
        base.Mend(user, wire);
        if (EntityManager.TryGetComponent<OxydComputerComponent>(wire.Owner, out var comp))
        {
            comp.StorageWireCut = false;
            EntityManager.System<OxydComputerSystem>().RefreshComputerBootState(wire.Owner, comp);
        }

        return true;
    }

    public override void Pulse(EntityUid user, Wire wire)
    {
        base.Pulse(user, wire);
    }
}

[DataDefinition]
public sealed partial class ComputerDeviceLinkWireAction : BaseWireAction
{
    public override Color Color { get; set; } = Color.Orange;
    public override string Name { get; set; } = "wire-name-computer-devicelink";

    public override StatusLightState? GetLightState(Wire wire)
    {
        if (EntityManager.TryGetComponent<OxydComputerComponent>(wire.Owner, out var comp))
            return comp.DeviceLinkWireCut ? StatusLightState.Off : StatusLightState.On;

        return StatusLightState.On;
    }

    public override object StatusKey => OxydComputerWireStatusKey.DeviceLink;

    public override bool Cut(EntityUid user, Wire wire)
    {
        base.Cut(user, wire);
        if (EntityManager.TryGetComponent<OxydComputerComponent>(wire.Owner, out var comp))
        {
            comp.DeviceLinkWireCut = true;
            EntityManager.System<OxydComputerSystem>().RefreshComputerBootState(wire.Owner, comp);
        }

        return true;
    }

    public override bool Mend(EntityUid user, Wire wire)
    {
        base.Mend(user, wire);
        if (EntityManager.TryGetComponent<OxydComputerComponent>(wire.Owner, out var comp))
        {
            comp.DeviceLinkWireCut = false;
            EntityManager.System<OxydComputerSystem>().RefreshComputerBootState(wire.Owner, comp);
        }

        return true;
    }

    public override void Pulse(EntityUid user, Wire wire)
    {
        base.Pulse(user, wire);
    }
}
