using Content.Server._Oxyd.Framework;
using Content.Server._Oxyd.Framework.Objectives;
using Content.Server._Oxyd.Framework.ViewCalc;
using Content.Server.Database.Migrations.Postgres;
using Content.Server.Mind;
using Content.Server.Mind.Toolshed;
using Content.Server.Objectives;
using Content.Shared._Oxyd.Framework.Objectives;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Mobs.Events;
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
        SubscribeLocalEvent<InfluenceSanityOnMetabolizeComponent, SolutionChangedEvent>(OnMetabolize);
        SubscribeLocalEvent<ObjectiveGiveInsightComponent, ObjectiveCompletedEvent>(ObjectiveGiveInsight);
        SubscribeLocalEvent<ObjectiveGiveRestComponent, ObjectiveCompletedEvent>(ObjectiveGiveRest);
        influenceQuery = GetEntityQuery<InfluenceSanityOnViewComponent>();
    }

    private void OnMetabolize(Entity<InfluenceSanityOnMetabolizeComponent> ent, ref SolutionChangedEvent args)
    {
        throw new NotImplementedException();
    }

    private void ObjectiveGiveRest(Entity<ObjectiveGiveRestComponent> ent, ref ObjectiveCompletedEvent args)
    {
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

        }
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
            ApplySanityDamage(ent, f, affect[f]);
        }
    }

    public float ApplySanityDamage(Entity<SanityComponent> ent, SanitySource type, float amount)
    {
        var mods = ent.Comp.modifiers[type];
        amount *= mods[(int)SanIndex.deltaMult];
        var result = ent.Comp.Sanity + amount;
        if (ent.Comp.Sanity > mods[(int)SanIndex.damageCap] && result < mods[(int)SanIndex.damageCap])
        {
            amount = ent.Comp.Sanity - mods[(int)SanIndex.damageCap];
            result = ent.Comp.Sanity + amount;
        }
        if (ent.Comp.Sanity > ent.Comp.MinSanity && result < ent.Comp.MinSanity)
        {
            amount = ent.Comp.Sanity - ent.Comp.MinSanity;
            result = ent.Comp.Sanity + amount;
        }

        if (ent.Comp.Sanity < ent.Comp.MaxSanity && result > ent.Comp.MaxSanity)
        {
            amount = ent.Comp.MaxSanity - ent.Comp.Sanity;
            result = ent.Comp.Sanity + amount;
        }
        ent.Comp.Sanity = result;
        if(amount < 0)
            GiveInsight(ent, -amount * mods[(int)SanIndex.damageToInsight]);
        return amount;
    }

    public void GiveInsight(Entity<SanityComponent> ent, float amount)
    {
        ent.Comp.Insight += amount;
        while (ent.Comp.Insight > 100f)
        {
            ent.Comp.Insight = 0f;
            ent.Comp.RestAccumulated++;
            if (!mindsys.TryGetMind(ent.Owner, out var mindent, out var mindcomp))
                continue;
            objectivesys.GetRandomObjective(mindent, mindcomp, "RestObjectives", 9999);
        }
    }
}


