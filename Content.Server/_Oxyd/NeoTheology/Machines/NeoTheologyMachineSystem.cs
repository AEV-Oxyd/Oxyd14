using Content.Server.Power.Components;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>Checks machine operation at the point of use.</summary>
public sealed class NeoTheologyMachineSystem : EntitySystem
{
    public bool IsOperational(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid))
            return false;

        // Bare components need no power. Machine prototypes carry a receiver.
        return !TryComp<ApcPowerReceiverComponent>(uid, out var receiver) ||
               receiver.Powered && !receiver.PowerDisabled && Transform(uid).Anchored;
    }
}
