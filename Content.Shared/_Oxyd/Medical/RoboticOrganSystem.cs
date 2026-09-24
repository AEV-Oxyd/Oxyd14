using Content.Shared.Body;

namespace Content.Shared._Oxyd.Medical;

/// <summary>Uses the native body relay and detachment APIs. It never deletes the detached organ or its children.</summary>
public sealed partial class RoboticOrganSystem : EntitySystem
{
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly DetachableOrganSystem _detachable = default!;

    [SubscribeLocalEvent]
    private void OnCollect(Entity<RoboticOrganComponent> ent, ref BodyRelayedEvent<CollectRoboticOrgansEvent> args)
    {
        if (HasComp<DetachableOrganComponent>(ent))
            args.Args.Organs.Add(ent);
    }

    public int Reject(EntityUid uid)
    {
        if (!TryComp<BodyComponent>(uid, out var body))
            return 0;

        var collect = new CollectRoboticOrgansEvent(new List<EntityUid>());
        _body.RelayEvent((uid, body), ref collect);
        var count = 0;
        foreach (var organ in collect.Organs)
        {
            // An earlier detachment can move child organs into the detached body.
            if (TryComp<OrganComponent>(organ, out var comp) && comp.Body == uid &&
                _detachable.Detach(organ) != null)
                count++;
        }
        return count;
    }
}

[ByRefEvent]
public readonly record struct CollectRoboticOrgansEvent(List<EntityUid> Organs);
