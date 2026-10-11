using System.Linq;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Examine;
using Content.Shared.Humanoid;
using Content.Shared.Implants;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Objectives.Components;
using Content.Shared.Warps;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>Native objectives consume conversion/revelation/sanctification/destruction, rather than orphaned signals.</summary>
public sealed partial class NeoTheologyWorldSystem : EntitySystem
{
    [Dependency] private CruciformSystem _cruciform = default!;
    [Dependency] private SharedMindSystem _minds = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly EntProtoId[] Objectives =
    {
        NeoTheologyPrototypes.ConvertObjective, NeoTheologyPrototypes.RevealObjective,
        NeoTheologyPrototypes.SanctifyObjective, NeoTheologyPrototypes.DestroyObjective,
    };

    [SubscribeLocalEvent]
    private void OnActivityChanged(ref CruciformActivityChangedEvent args)
    {
        if (args.Active && args.Body is { } body)
            AssignObjectives(body);
    }

    public void AssignObjectives(EntityUid body)
    {
        if (!_cruciform.TryGetCruciform(body, out _, out var comp) ||
            !_minds.TryGetMind(body, out var mindId, out var mind))
            return;
        var church = comp.InstalledModules.Any(module => module == NeoTheologyPrototypes.PriestModule ||
            module == NeoTheologyPrototypes.InquisitorModule || module == NeoTheologyPrototypes.AcolyteModule ||
            module == NeoTheologyPrototypes.AgrolyteModule || module == NeoTheologyPrototypes.CustodianModule);
        foreach (var id in Objectives)
        {
            if (!church && (id == NeoTheologyPrototypes.RevealObjective || id == NeoTheologyPrototypes.SanctifyObjective))
                continue;
            if (!mind.Objectives.Any(o => MetaData(o).EntityPrototype?.ID == id.Id))
                _minds.TryAddObjective(mindId, mind, id);
        }
    }

    [SubscribeLocalEvent]
    private void OnAssign(Entity<NeoTheologyObjectiveComponent> ent, ref ObjectiveAssignedEvent args)
    {
        if (args.Cancelled || args.Mind.OwnedEntity is not { } actor || !_cruciform.IsActiveBearer(actor))
        {
            args.Cancelled = true;
            return;
        }
        var title = string.Empty;
        switch (ent.Comp.Kind)
        {
            case NeoTheologyObjectiveKind.Convert:
            case NeoTheologyObjectiveKind.Reveal:
                var candidates = new List<EntityUid>();
                var minds = EntityQueryEnumerator<MindComponent>();
                while (minds.MoveNext(out var id, out var mind))
                    if (id != args.MindId && mind.UserId != null && mind.OwnedEntity is { } body &&
                        HasComp<HumanoidProfileComponent>(body) && !_mobState.IsDead(body) &&
                        !_cruciform.IsActiveBearer(body))
                        candidates.Add(id);
                if (candidates.Count == 0)
                {
                    args.Cancelled = true;
                    return;
                }
                ent.Comp.TargetMind = _random.Pick(candidates);
                title = Loc.GetString(ent.Comp.Kind == NeoTheologyObjectiveKind.Convert ? "oxyd-nt-objective-convert" : "oxyd-nt-objective-reveal",
                    ("target", Comp<MindComponent>(ent.Comp.TargetMind.Value).CharacterName ?? Loc.GetString("oxyd-nt-unknown-soul")));
                break;
            case NeoTheologyObjectiveKind.Sanctify:
                var places = new List<EntityUid>();
                var warps = EntityQueryEnumerator<WarpPointComponent, TransformComponent>();
                while (warps.MoveNext(out var uid, out _, out var xform))
                    if (xform.GridUid != null && xform.MapID == Transform(actor).MapID)
                        places.Add(uid);
                if (places.Count == 0)
                {
                    args.Cancelled = true;
                    return;
                }
                var place = _random.Pick(places);
                ent.Comp.TargetGrid = Transform(place).GridUid;
                ent.Comp.TargetTile = _transform.WithEntityId(Transform(place).Coordinates, Transform(place).GridUid!.Value).Position.Floored();
                title = Loc.GetString("oxyd-nt-objective-sanctify", ("target", MetaData(place).EntityName));
                break;
            case NeoTheologyObjectiveKind.Destroy:
                var artifacts = new List<EntityUid>();
                var items = EntityQueryEnumerator<NeoTheologyFactionItemComponent>();
                while (items.MoveNext(out var item, out var faction))
                    if (!faction.Church && MetaData(item).EntityPrototype != null)
                        artifacts.Add(item);
                if (artifacts.Count == 0)
                {
                    args.Cancelled = true;
                    return;
                }
                var artifact = _random.Pick(artifacts);
                ent.Comp.TargetPrototype = MetaData(artifact).EntityPrototype!.ID;
                title = Loc.GetString("oxyd-nt-objective-destroy", ("target", MetaData(artifact).EntityName));
                break;
        }
        _metadata.SetEntityName(ent.Owner, title);
    }

    [SubscribeLocalEvent]
    private void OnProgress(Entity<NeoTheologyObjectiveComponent> ent, ref ObjectiveGetProgressEvent args) =>
        args.Progress = ent.Comp.Completed ? 1 : 0;

    public void RecordConversion(EntityUid body)
    {
        if (!_minds.TryGetMind(body, out var mind, out _))
            return;
        var query = EntityQueryEnumerator<NeoTheologyObjectiveComponent>();
        while (query.MoveNext(out _, out var objective))
            if (objective.Kind == NeoTheologyObjectiveKind.Convert && objective.TargetMind == mind)
                objective.Completed = true;
    }

    [SubscribeLocalEvent]
    private void OnRevelation(ref NeoTheologyRevelationEvent args)
    {
        if (!_minds.TryGetMind(args.User, out _, out var owner) ||
            !_minds.TryGetMind(args.Target, out var target, out _))
            return;
        foreach (var id in owner.Objectives)
            if (TryComp<NeoTheologyObjectiveComponent>(id, out var objective) &&
                objective.Kind == NeoTheologyObjectiveKind.Reveal && objective.TargetMind == target)
                objective.Completed = true;
    }

    public bool Sanctify(EntityUid user)
    {
        var xform = Transform(user);
        if (xform.GridUid is not { } grid)
            return false;
        var area = EnsureComp<NeoTheologySanctifiedAreaComponent>(grid);
        var centre = _transform.WithEntityId(xform.Coordinates, grid).Position.Floored();
        for (var x = -7; x <= 7; x++)
            for (var y = -7; y <= 7; y++)
                if (x * x + y * y <= 49)
                    area.Tiles.Add(centre + new Vector2i(x, y));
        var objectives = EntityQueryEnumerator<NeoTheologyObjectiveComponent>();
        while (objectives.MoveNext(out _, out var objective))
            if (objective.Kind == NeoTheologyObjectiveKind.Sanctify && objective.TargetGrid == grid &&
                area.Tiles.Contains(objective.TargetTile))
                objective.Completed = true;
        return true;
    }

    [SubscribeLocalEvent]
    private void OnCrusade(ref NeoTheologyCrusadeEvent args)
    {
        var query = EntityQueryEnumerator<NeoTheologyFactionItemComponent>();
        while (query.MoveNext(out _, out var item))
            item.CrusadeActivated = true;
        args.Handled = true;
    }

    public void RecordDestruction(EntityUid user, string prototype)
    {
        if (!_minds.TryGetMind(user, out _, out var mind))
            return;
        foreach (var id in mind.Objectives)
            if (TryComp<NeoTheologyObjectiveComponent>(id, out var objective) &&
                objective.Kind == NeoTheologyObjectiveKind.Destroy && objective.TargetPrototype == prototype)
                objective.Completed = true;
    }
}
