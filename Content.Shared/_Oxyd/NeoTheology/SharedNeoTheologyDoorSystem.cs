using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Doors;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;

namespace Content.Shared._Oxyd.NeoTheology;

/// <summary>
/// Predicted holy-door access. The check has to run on the client too: the door
/// system opens locally before the server answers, so a server-only cancel leaves
/// the door open on screen and shut on the server.
/// </summary>
public sealed partial class SharedNeoTheologyDoorSystem : EntitySystem
{
    [Dependency] private SharedCruciformSystem _cruciform = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    [SubscribeLocalEvent]
    private void OnBeforeOpened(Entity<NeoTheologyDoorComponent> ent, ref BeforeDoorOpenedEvent args)
    {
        if (ent.Comp.MinimumClearance == NeoTheologyClearance.None)
            return;

        if (args.User is not { } user)
        {
            args.Cancel();
            return;
        }

        if (HoldsTauCross(user))
            return;

        if (_cruciform.TryGetCruciform(user, out _, out var cruciform) &&
            cruciform.Active &&
            cruciform.Clearance >= ent.Comp.MinimumClearance)
            return;

        args.Cancel();
    }

    private bool HoldsTauCross(EntityUid user)
    {
        if (!TryComp<HandsComponent>(user, out var hands))
            return false;

        foreach (var held in _hands.EnumerateHeld((user, hands)))
        {
            if (HasComp<TauCrossComponent>(held))
                return true;
        }

        return false;
    }
}
