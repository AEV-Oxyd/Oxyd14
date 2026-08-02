using System.Linq;
using Content.Server._Oxyd.Framework;
using Content.Server._Oxyd.Framework.Objectives;
using Content.Server._Oxyd.Framework.ViewCalc;
using Content.Server.Database.Migrations.Postgres;
using Content.Server.Mind;
using Content.Server.Mind.Toolshed;
using Content.Server.Objectives;
using Content.Shared._Oxyd.Framework.Objectives;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Mobs.Events;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Server.GameObjects;

namespace Content.Server._Oxyd.SanityInsightAndResting;


/// <summary>
/// This handles...
/// </summary>
public sealed class SanitySystem : EntitySystem
{
    [Dependency] private ObjectivesSystem objectivesys = default!;
    [Dependency] private MindSystem mindsys = default!;
    [Dependency] private ServerOxydHelpers helpers = default!;
    [Dependency] private UserInterfaceSystem uimanager = default!;
    public EntityQuery<InfluenceSanityOnViewComponent> influenceQuery;
    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<SanityComponent, ViewTickEvent>(onSanityTick);
        SubscribeLocalEvent<SanityComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<InfluenceSanityOnViewComponent, ComponentInit>(onInfluencerInit);
        SubscribeLocalEvent<InfluenceSanityOnTasteComponent, FlavorProfileModificationEvent>(OnBite);
        SubscribeLocalEvent<ObjectiveGiveInsightComponent, ObjectiveCompletedEvent>(ObjectiveGiveInsight);
        SubscribeLocalEvent<ObjectiveGiveRestComponent, ObjectiveCompletedEvent>(ObjectiveGiveRest);
        influenceQuery = GetEntityQuery<InfluenceSanityOnViewComponent>();
    }

    private void OnBite(Entity<InfluenceSanityOnTasteComponent> ent, ref FlavorProfileModificationEvent args)
    {
        if (TryComp<SanityComponent>(args.User, out var hisComp))
        {
            ApplySanityDelta((args.User, hisComp), ent.Comp.sanityType, ent.Comp.sanityDelta);
        }
    }

    private void onInfluencerInit(Entity<InfluenceSanityOnViewComponent> ent, ref ComponentInit args)
    {
        EnsureComp<ViewRelevantComponent>(ent);
    }

    private void ObjectiveGiveRest(Entity<ObjectiveGiveRestComponent> ent, ref ObjectiveCompletedEvent args)
    {
        Log.Fatal($"Triggered rest");
        if (args.mind.Comp.OwnedEntity is EntityUid exist)
        {
            var oddities = helpers.GetChildrenWithComp<OddityComponent>(exist);

        }
    }

    private void ObjectiveGiveInsight(Entity<ObjectiveGiveInsightComponent> ent, ref ObjectiveCompletedEvent args)
    {
        if (args.mind.Comp.OwnedEntity is EntityUid val)
        {
            if (TryComp<SanityComponent>(val, out var sanity))
                GiveInsight((val, sanity), ent.Comp.amount);
        }
    }

    private void OnInit(Entity<SanityComponent> ent, ref ComponentInit args)
    {
        foreach (var f in Enum.GetValues<SanitySource>())
        {
            if (ent.Comp.modifiers.ContainsKey(f))
                continue;
            ent.Comp.modifiers[f] = new float[Enum.GetValues<SanIndex>().Length];
            ent.Comp.modifiers[f][(int)SanIndex.damageToInsight] = 0.05f;
            ent.Comp.modifiers[f][(int)SanIndex.deltaMult] = 1f;
        }

        EnsureComp<ViewTickerComponent>(ent);
    }


    private void onSanityTick(Entity<SanityComponent> ent, ref ViewTickEvent args)
    {
        Dictionary<SanitySource, float> affect = new(3);
        foreach (var f in Enum.GetValues<SanitySource>())
            affect[f] = 0;
        foreach (var entity in args.seen)
        {
            if (!influenceQuery.TryComp(entity, out var comp))
                continue;
            affect[comp.sanityType] += comp.sanityDelta;
        }

        foreach (var f in Enum.GetValues<SanitySource>())
        {
            ApplySanityDelta(ent, f, affect[f]);
        }


    }

    public float ApplySanityDelta(Entity<SanityComponent> ent, SanitySource type, float amount)
    {
        var mods = ent.Comp.modifiers[type];
        amount *= mods[(int)SanIndex.deltaMult];
        var result = ent.Comp.Sanity + amount;
        if (amount < 0)
        {
            if (ent.Comp.Sanity >= mods[(int)SanIndex.damageCap] && result < mods[(int)SanIndex.damageCap])
            {
                amount = ent.Comp.Sanity - mods[(int)SanIndex.damageCap];
                result = ent.Comp.Sanity + amount;
            }

            if (ent.Comp.Sanity >= ent.Comp.MinSanity && result < ent.Comp.MinSanity)
            {
                amount = ent.Comp.Sanity - ent.Comp.MinSanity;
                result = ent.Comp.Sanity + amount;
            }
            GiveInsight(ent, -amount * mods[(int)SanIndex.damageToInsight]);
        }
        else
        {
            if (ent.Comp.Sanity <= ent.Comp.MaxSanity && result > ent.Comp.MaxSanity)
            {
                amount = ent.Comp.MaxSanity - ent.Comp.Sanity;
                result = ent.Comp.Sanity + amount;
            }
        }
        if(ent.Comp.Sanity - result > 0.1f)
            DirtyField(ent.Owner, ent.Comp, nameof(SanityComponent.Sanity));
        ent.Comp.Sanity = result;
        return amount;
    }

    public void GiveInsight(Entity<SanityComponent> ent, float amount)
    {
        ent.Comp.Insight += amount;
        DirtyField(ent.Owner, ent.Comp, nameof(SanityComponent.Insight));
        while (ent.Comp.Insight > 100f)
        {
            ent.Comp.Insight = 0f;
            ent.Comp.RestAccumulated++;
            if (!mindsys.TryGetMind(ent.Owner, out var mindent, out var mindcomp))
                continue;
            var objective = objectivesys.GetRandomObjective(mindent, mindcomp, "RestObjectives", 9999);
            if (objective is EntityUid existing)
            {
                ent.Comp.desireProg[existing] = new Tuple<string, float>(MetaData(existing).EntityDescription, 0f);
                DirtyField(ent.Owner, ent.Comp, nameof(SanityComponent.desireProg));
            }
        }
    }

    public void UpdateDesireData(Entity<SanityComponent> ent)
    {
        var mind = mindsys.GetMind(ent.Owner);
        if (mind is EntityUid exist)
        {
            var mc = (exist, Comp<MindComponent>(ent.Owner));
            foreach (var key in ent.Comp.desireProg.Keys.ToList())
            {
                if (TerminatingOrDeleted(key))
                    ent.Comp.desireProg.Remove(key);
            foreach (var (objId, data) in ent.Comp.desireProg)
            {
                var prog = objectivesys.GetProgress(objId, mc);
                if (prog is null)
                    continue;
                ent.Comp.desireProg[objId].Item2 = prog;
            }

        }
    }
}


