using System.Linq;
using Content.Shared._Oxyd.Objectives;
using Content.Shared.Body.Events;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Objectives.Components;
using Robust.Shared.Utility;

namespace Content.Server._Oxyd.Objectives;

/// <summary>
/// This handles...
/// </summary>
public sealed class OxydObjectiveSystem : EntitySystem
{
    [Dependency] private FlavorProfileSystem flavours = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<ExperienceFlavourObjectiveComponent, ObjectiveAfterAssignEvent>(AfterAssignExperience);
        SubscribeLocalEvent<ExperienceFlavourObjectiveComponent, ObjectiveGetProgressEvent>(OnRequestProgressFlavor);
        SubscribeLocalEvent<ExperienceFlavourObjectiveTrackerComponent, FlavorProfileModificationEvent>(OnFlavorIngest);
        SubscribeLocalEvent<MetabolizeReagentObjectiveComponent, ObjectiveAfterAssignEvent>(AfterAssignMetabolize);
        SubscribeLocalEvent<MetabolizeReagentObjectiveTrackerComponent, SolutionChangedEvent>(MetabolizeObjectiveHandle);
    }

    void AfterAssignMetabolize(Entity<MetabolizeReagentObjectiveComponent> ent,ref ObjectiveAfterAssignEvent args)
    {
        if (args.Mind.CurrentEntity is EntityUid valid)
        {
            var c = EnsureComp<MetabolizeReagentObjectiveTrackerComponent>(valid);
            c.origin.Add(ent);
        }
    }

    void MetabolizeObjectiveHandle(Entity<MetabolizeReagentObjectiveTrackerComponent> ent, ref SolutionChangedEvent args)
    {
        var refCast = args.Solution.Comp.Solution;
        foreach (var target in ent.Comp.origin)
        {
            if(target.Comp.reagents.Any(thing => refCast.ContainsPrototype(thing)))
                target.Comp.metabolizeTimes++;
        }
    }

    void OnRequestProgressFlavor(EntityUid uid, ExperienceFlavourObjectiveComponent component, ObjectiveGetProgressEvent args)
    {
        args.Progress = (component.experienced+0.1f) / component.timesToExperience;
    }


    void OnFlavorIngest(Entity<ExperienceFlavourObjectiveTrackerComponent> ent, ref FlavorProfileModificationEvent ev)
    {
        var refCast = ev.Flavors;
        foreach (var flavorTarget in ent.Comp.origin)
        {
            if (flavorTarget.Comp.flavours.Any(targ => refCast.Contains(targ)))
                flavorTarget.Comp.experienced++;
        }
    }

    void AfterAssignExperience(Entity<ExperienceFlavourObjectiveComponent> ent, ref ObjectiveAfterAssignEvent args)
    {
        if (args.Mind.CurrentEntity is EntityUid valid)
        {
            var c = EnsureComp<ExperienceFlavourObjectiveTrackerComponent>(valid);
            c.origin.Add(ent);
        }
    }
}

