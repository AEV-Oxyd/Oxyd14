

using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Shared.EntityEffects;

namespace Content.Server._Oxyd.SanityInsightAndResting;

public sealed partial class GiveSanityEffectSystem : EntityEffectSystem<SanityComponent, GiveSanityEffect>
{
    [Dependency] private SanitySystem sanity = default!;

    protected override void Effect(Entity<SanityComponent> entity, ref EntityEffectEvent<GiveSanityEffect> args)
    {
        sanity.ApplySanityDelta(entity,args.Effect.sanityType, args.Effect.sanityDelta);
    }
}
