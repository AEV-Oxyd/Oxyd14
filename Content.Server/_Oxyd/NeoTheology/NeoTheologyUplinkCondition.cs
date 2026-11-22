using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Mind;
using Content.Shared.Store;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>Source category authority is checked on every listing, not inferred from an open window.</summary>
public sealed partial class NeoTheologyUplinkCondition : ListingCondition
{
    public override bool Condition(ListingConditionArgs args)
    {
        var em = args.EntityManager;
        var body = em.TryGetComponent<MindComponent>(args.Buyer, out var mind) ? mind.OwnedEntity : args.Buyer;
        return body is { } user && em.System<CruciformSystem>().TryGetCruciform(user, out _, out var comp) &&
            (comp.InstalledModules.Contains(NeoTheologyPrototypes.InquisitorModule) || comp.UnlockedSets.Contains(NeoTheologyPrototypes.CrusaderSet));
    }
}
