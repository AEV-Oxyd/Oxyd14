using System.Linq;
using Content.Shared.Hands.Components;
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
    [Dependency] private IPrototypeManager prototypes = default!;
    [Dependency] private INetManager network = default!;
    [Dependency] private IGameTiming timing = default!;
    [Dependency] private SharedOxydHelpers helpers = default!;
    [Dependency] private SharedInteractionSystem interact = default!;
    private IRobustRandom random = new RobustRandom();

    public static readonly string storeKey = "storagebase";
    public static readonly ProtoId<BundleGroup> bundleProto = "BaseBundle";
    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<BundleComponent, ComponentStartup>(onStart);
        SubscribeLocalEvent<BundableComponent, AfterInteractEvent>(onUse);
        SubscribeLocalEvent<BundleComponent, AfterInteractEvent>(onUseBundle);
        SubscribeLocalEvent<BundleComponent, EntRemovedFromContainerMessage>(handleRemove);

    }

    public void handleRemove(EntityUid uid, BundleComponent component, EntRemovedFromContainerMessage args)
    {

        helpers.GetParentWithComp(uid, out Entity<HandsComponent>? user);
        RemoveFromBundle((uid, component), (args.Entity, Comp<BundableComponent>(args.Entity)));
        if (user is not null && component.containing.Count == 1)
        {
            var last = GetEntity(component.containing[0]);
            RemoveFromBundle((uid, component), (last, Comp<BundableComponent>(last)));
            if(hands.TryDrop((user.Value.Owner, user.Value.Comp), uid))
                hands.TryPickup(user.Value, last);
            helpers.QueueDel(uid);
        }
    }

    public void onStart(Entity<BundleComponent> ent, ref ComponentStartup args)
    {
        containers.EnsureContainer<Container>(ent.Owner, storeKey);
    }

    public void onUse(Entity<BundableComponent> ent, ref AfterInteractEvent ev)
    {
        if (ev.Target is null)
            return;
        if (!timing.IsFirstTimePredicted)
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
        if (ev.Handled)
            return;
        if (!timing.IsFirstTimePredicted)
            return;
        if (ev.Target is null)
            return;
        if (HasComp<BundleComponent>(ev.Target.Value) || HasComp<BundableComponent>(ev.Target.Value))
            return;
        foreach (var thing in ent.Comp.containing)
        {
            var resolved = GetEntity(thing);
            if (TerminatingOrDeleted(resolved))
                continue;
            if (interact.InteractUsing(ev.User, resolved, ev.Target.Value, ev.ClickLocation))
            {
                ev.Handled = true;
                return;
            }
        }
    }

    public bool TryMerge(Entity<BundableComponent> ent, Entity<BundleComponent> bundle)
    {
        Log.Debug($"Trying to merge {ent} into {bundle}");
        if (ent.Comp.group != bundle.Comp.group)
            return false;
        var proto = prototypes.Index<BundleGroup>(bundle.Comp.group);
        if (ent.Comp.volume + bundle.Comp.usedVolume >= proto.volume)
            return false;
        var cont = containers.GetContainer(bundle.Owner, storeKey);
        if (bundle.Comp.bundlePositions.ContainsKey(GetNetEntity(ent.Owner)))
            return false;
        if (!containers.Insert(ent.Owner, cont))
            return false;
        if (!bundle.Comp.bundlePositions.ContainsKey(GetNetEntity(ent.Owner)))
        {
            bundle.Comp.usedVolume += ent.Comp.volume;
            bundle.Comp.containing.Add(GetNetEntity(ent.Owner));
            bundle.Comp.bundlePositions.Add(GetNetEntity(ent.Owner), random.NextVector2(0.01f, 0.2f));
        }
        afterMerge(bundle);
        Dirty(bundle, bundle.Comp);
        return true;
    }
    // doesn't do any container interactions!!
    public void RemoveFromBundle(Entity<BundleComponent> bundle, Entity<BundableComponent> ent)
    {
        Log.Debug($"Removing {ent} from {bundle}");
        if (!bundle.Comp.containing.Contains(GetNetEntity(ent.Owner)))
        {
            Log.Error($"Tried to remove {ent} from {bundle} but it wasn't in it!");
            return;
        }
        bundle.Comp.containing.Remove(GetNetEntity(ent.Owner));
        bundle.Comp.usedVolume -= ent.Comp.volume;
        afterRemove(bundle);
        Dirty(bundle, bundle.Comp);
    }

    public virtual void afterMerge(Entity<BundleComponent> bundle){}

    public virtual void afterRemove(Entity<BundleComponent> bundle){}

    public EntityUid CreateBundle(Entity<BundableComponent> ent, EntityUid user)
    {
        if (!network.IsServer)
            return EntityUid.Invalid;
        var bundle = SpawnNextToOrDrop(bundleProto, user);
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
