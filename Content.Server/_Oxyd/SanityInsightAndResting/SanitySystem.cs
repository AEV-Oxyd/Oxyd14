using System.Linq;
using Content.Server._Oxyd.Framework;
using Content.Server._Oxyd.Framework.Objectives;
using Content.Server._Oxyd.Framework.RadialMenu;
using Content.Server._Oxyd.Framework.ViewCalc;
using Content.Server.Database.Migrations.Postgres;
using Content.Server.Mind;
using Content.Server.Mind.Toolshed;
using Content.Server.Objectives;
using Content.Server.Storage.EntitySystems;
using Content.Shared._Oxyd.Framework.Objectives;
using Content.Shared._Oxyd.Framework.RadialMenu;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Mobs.Events;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Server.Player;

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
    [Dependency] private ContainerSystem containers = default!;
    [Dependency] private ServerRadialMenuSystem radials = default!;
    [Dependency] private IPlayerManager players = default!;
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
        if (args.mind.Comp.OwnedEntity is EntityUid exist && TryComp<SanityComponent>(exist, out var sancomp))
        {
            var c = ent.Comp;
            FulfillRest((exist, sancomp), c.mod, c.baseG, c.topG);
        }
    }
    public void FulfillRest(Entity<SanityComponent> ent , float gainModifier, float baseGain, float topGain)
    {
        var c = ent.Comp;
        if (c.RestAccumulated < 1)
            return;
        if(!players.TryGetSessionByEntity(ent, out var sesh))
            return;
        var rand = new Random();
        var oddities = helpers.GetChildrenWithComp<OddityComponent>(ent);
        List<RadialMenuOption> options = new();
        foreach (var oddit in oddities)
        {
            var opt = new PrototypeRadialMenuOption()
            {
                Prototype = MetaData(oddit).EntityPrototype!.ID,
            };
            options.Add(opt);
        }

        radials.ShowRadial(sesh, );


    }

    private void ObjectiveGiveInsight(Entity<ObjectiveGiveInsightComponent> ent, ref ObjectiveCompletedEvent args)
    {
        if (args.mind.Comp.OwnedEntity is EntityUid val)
        {
            if (TryComp<SanityComponent>(val, out var sanity))
            {
                GiveInsight((val, sanity), ent.Comp.amount);
            }

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
        UpdateDesireData(ent);
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

    public bool TryGiveRestObjective(Entity<SanityComponent> ent)
    {
        if (!mindsys.TryGetMind(ent.Owner, out var mindent, out var mindcomp))
            return false;
        if (!TerminatingOrDeleted(ent.Comp.desireId))
            return false;
        var objective = objectivesys.GetRandomObjective(mindent, mindcomp, "RestObjectives", 9999);
        if (objective is EntityUid existing)
        {
            ent.Comp.DesireProgress = 0f;
            ent.Comp.DesireDescription = MetaData(existing).EntityDescription;
            ent.Comp.desireId = existing;
            DirtyFields(ent.Owner, ent.Comp, null, nameof(SanityComponent.DesireProgress), nameof(SanityComponent.DesireDescription));
        }
        return true;
    }
    public void GiveInsight(Entity<SanityComponent> ent, float amount)
    {
        ent.Comp.Insight += amount;
        DirtyField(ent.Owner, ent.Comp, nameof(SanityComponent.Insight));
        while (ent.Comp.Insight > 100f)
        {
            ent.Comp.Insight = 0f;
            ent.Comp.RestAccumulated++;
            TryGiveRestObjective(ent);
        }
    }

    public void UpdateDesireData(Entity<SanityComponent> ent)
    {
        if (TerminatingOrDeleted(ent.Comp.desireId))
            return;
        var mind = mindsys.GetMind(ent.Owner);
        if (mind is EntityUid exist)
        {
            var prog = objectivesys.GetProgress(ent.Comp.desireId, (exist, Comp<MindComponent>(exist)));
            if (prog is null)
                return;
            ent.Comp.DesireProgress = prog.Value;
            DirtyField(ent.Owner, ent.Comp, nameof(SanityComponent.DesireProgress));
        }
    }
}


