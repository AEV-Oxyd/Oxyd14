using Content.Server._Oxyd.Framework.ViewCalc;
using Content.Server.Database.Migrations.Postgres;
using Content.Shared.Mobs.Events;

namespace Content.Server._Oxyd.SanityInsightAndResting;

[Flags]
public enum SanityDamageSource : byte
{
    Environmental = 0,
    Witness = 1 << 0,
    Actor = 1 << 1
}

/// <summary>
/// This handles...
/// </summary>
public sealed class SanitySystem : EntitySystem
{
    public EntityQuery<SanityInfluencerComponent> influenceQuery;
    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<SanityComponent, ViewTickEvent>(onSanityTick);
        SubscribeLocalEvent<SanityComponent, ComponentInit>(OnInit);
        influenceQuery = GetEntityQuery<SanityInfluencerComponent>();
    }

    private void OnInit(Entity<SanityComponent> ent, ref ComponentInit args)
    {
        foreach (var f in Enum.GetValues<SanityDamageSource>())
        {
            if (ent.Comp.modifiers.ContainsKey(f))
                continue;
            ent.Comp.modifiers[f] = new float[Enum.GetValues<SanIndex>().Length];

        }
    }


    private void onSanityTick(Entity<SanityComponent> ent, ref ViewTickEvent args)
    {
        Dictionary<SanityDamageSource, float> affect = new(3);
        foreach (var f in Enum.GetValues<SanityDamageSource>())
            affect[f] = 0;
        foreach (var entity in args.seen)
        {
            if (!influenceQuery.TryComp(entity, out var comp))
                continue;
            affect[comp.sanityType] += comp.sanityDelta;
        }

        foreach (var f in Enum.GetValues<SanityDamageSource>())
        {
            ApplySanityDamage(ent, f, affect[f]);
        }
    }

    public float ApplySanityDamage(Entity<SanityComponent> ent, SanityDamageSource type, float amount)
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
            GiveInsight(ent, -amount * mods[(int)SanIndex.damageToInsight]));
        return amount;
    }

    public float GiveInsight(Entity<SanityComponent> ent, float amount)
    {

    }
}


