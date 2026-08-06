using Content.Server.Objectives;
using Content.Shared._Oxyd.Framework.Objectives;
using Content.Shared.Mind;
using Content.Shared.Objectives.Components;

namespace Content.Server._Oxyd.Framework.Objectives;

public class ObjectiveCompletedEvent : EntityEventArgs
{
    public Entity<MindComponent> mind;
}

/// <summary>
/// This handles...
/// </summary>
public sealed class OxydObjectiveMonitorSystem : EntitySystem
{
    [Dependency] private ObjectivesSystem objsys = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ObjectiveEventOnCompleteComponent, ObjectiveAssignedEvent>(OnAssign);
    }

    private void OnAssign(Entity<ObjectiveEventOnCompleteComponent> ent, ref ObjectiveAssignedEvent args)
    {
        ent.Comp.mind = (args.MindId, args.Mind);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var en = EntityQueryEnumerator<ObjectiveEventOnCompleteComponent>();
        while (en.MoveNext(out var uid, out var comp))
        {
            var p = objsys.GetProgress(uid, comp.mind);
            if (p >= 0.999f)
            {
                var ev = new  ObjectiveCompletedEvent(){mind = comp.mind};
                RaiseLocalEvent(uid, ev);
                RemCompDeferred<ObjectiveEventOnCompleteComponent>(uid);
            }

        }

    }
}
