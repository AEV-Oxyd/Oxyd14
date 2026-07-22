using Content.Server._Oxyd.Framework.ViewCalc;
using Content.Shared.Mobs.Events;

namespace Content.Server._Oxyd.SanityInsightAndResting;

[Flags]
public enum SanityDamageSource
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
        influenceQuery = GetEntityQuery<SanityInfluencerComponent>();
    }

    private void onSanityTick(Entity<SanityComponent> ent, ref ViewTickEvent args)
    {
        public Dictionary<SanityDamageSource, float> damages = new();


    }
}

