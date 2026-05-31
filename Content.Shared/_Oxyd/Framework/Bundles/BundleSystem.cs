using System.Linq;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._Oxyd.Framework.Bundles;

/// <summary>
/// This handles...
/// </summary>
public abstract partial class BundleSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem containers = default!;
    [Dependency] private SharedHandsSystem hands = default!;
    [Dependency] private SharedInteractionSystem interaction = default!;
    [Dependency] private IPrototypeManager prototypes = default!;
    [Dependency] private INetManager network = default!;
    [Dependency] private IGameTiming timing = default!;
    private IRobustRandom random = new RobustRandom();

    public static readonly string storeKey = "storagebase";
    public static readonly ProtoId<BundleGroup> bundleProto = "BaseBundle";
    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<BundleComponent, ComponentStartup>(onStart);
        SubscribeLocalEvent<BundableComponent, AfterInteractEvent>(onUse);
        SubscribeLocalEvent<BundleComponent, AfterInteractEvent>(onUseBundle);

    }

    public void onStart(Entity<BundleComponent> ent, ref ComponentStartup args)
    {
        containers.EnsureContainer<Container>(ent.Owner, storeKey);
    }

    public void onUse(Entity<BundableComponent> ent, ref AfterInteractEvent ev)
    {
        if (ev.Target is null)
            return;

        var thing = ev.Target.Value;
        if (TryComp<BundleComponent>(thing, out var bundle))
        {
            if (TryMerge(ent, (thing, bundle)))
            {
                ev.Handled = true;
                return;
            }
        }
        else if (TryComp<BundableComponent>(thing, out var other))
        {
            var created = CreateBundle((thing, other), ev.User);
            if (created == EntityUid.Invalid)
                return;
            hands.TryDrop(ev.User);
            if (TryMerge(ent, (created, Comp<BundleComponent>(created))))
            {
                ev.Handled = true;
                hands.TryPickup(ev.User, created);
                return;
            }
        }
    }

    public void onUseBundle(Entity<BundleComponent> ent, ref AfterInteractEvent ev)
    {

    }

    public bool TryMerge(Entity<BundableComponent> ent, Entity<BundleComponent> bundle)
    {
        if (ent.Comp.group != bundle.Comp.group)
            return false;
        var proto = prototypes.Index<BundleGroup>(bundle.Comp.group);
        if (ent.Comp.volume + bundle.Comp.usedVolume >= proto.volume)
            return false;
        var cont = containers.GetContainer(bundle.Owner, storeKey);
        if (!containers.Insert(ent.Owner, cont))
            return false;
        bundle.Comp.usedVolume += ent.Comp.volume;
        bundle.Comp.containing.Add(GetNetEntity(ent.Owner));
        if(!bundle.Comp.bundlePositions.ContainsKey(GetNetEntity(ent.Owner)))
            bundle.Comp.bundlePositions.Add(GetNetEntity(ent.Owner), random.NextVector2(0.01f,0.2f));
        afterMerge(bundle);
        return true;
    }

    public void RemoveFromBundle(Entity<BundleComponent> bundle, Entity<BundableComponent> ent)
    {
        var cont = containers.GetContainer(bundle.Owner, storeKey);
        containers.Remove(ent.Owner, cont);
        bundle.Comp.usedVolume -= ent.Comp.volume;
        bundle.Comp.containing.Remove(GetNetEntity(ent.Owner));
    }

    public virtual void afterMerge(Entity<BundleComponent> bundle){}

    public EntityUid CreateBundle(Entity<BundableComponent> ent, EntityUid user)
    {
        if (!timing.IsFirstTimePredicted)
            return EntityUid.Invalid;
        var bundle = PredictedSpawnNextToOrDrop(bundleProto, user);
        var comp = EnsureComp<BundleComponent>(bundle);
        comp.group = ent.Comp.group;
        if (!TryMerge(ent, (bundle, comp)))
        {
            QueueDel(bundle);
            return EntityUid.Invalid;
        }
        //Dirty(bundle,comp);
        return bundle;
    }
}
