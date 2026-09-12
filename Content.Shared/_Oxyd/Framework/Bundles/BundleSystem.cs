using System.Linq;
using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mind.Components;
using Content.Shared.Throwing;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._Oxyd.Framework.Bundles;

/// <summary>
/// This handles...
/// </summary>
public abstract partial class BundleSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem hands = default!;
    [Dependency] private IPrototypeManager prototypes = default!;
    [Dependency] private INetManager network = default!;
    [Dependency] protected IGameTiming timing = default!;
    [Dependency] private SharedOxydHelpers helpers = default!;
    [Dependency] private SharedInteractionSystem interact = default!;
    [Dependency] protected SharedTransformSystem transform = default!;
    [Dependency] protected ThrowingSystem throwing = default!;
    [Dependency] protected OxydPredContainerSystem predcontainers = default!;
    private IRobustRandom random = new RobustRandom();

    public static readonly string storeKey = "storagebase";
    public static readonly ProtoId<BundleGroup> bundleProto = "BaseBundle";
    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BundleComponent, ComponentStartup>(onStart);
        SubscribeLocalEvent<BundableComponent, AfterInteractEvent>(onUse);
        SubscribeLocalEvent<BundleGenericInteractionComponent, AfterInteractEvent>(onUseBundle);
        SubscribeLocalEvent<BundleGenericInteractionComponent, ThrownEvent>(onThrowBundle);
        SubscribeLocalEvent<BundleGenericInteractionComponent, ComponentStartup>(initRandom);
        SubscribeLocalEvent<BundleGenericInteractionComponent, UseInHandEvent>(onHandInteract);

    }

    [SubscribeLocalEvent]
    public void OnRemove(EntityUid uid, BundleComponent comp, PredContRemoved args)
    {
        if (!TryComp<BundableComponent>(args.uid, out var bundable))
            return;
        if (args.realChange)
        {
            comp.bundlePositions.Remove(args.uid);
            comp.usedVolume -= bundable.volume;
            afterRemove((uid, comp));
        }
        if(predcontainers.GetContainer(uid, storeKey, out var cont) && cont.contained.Count == 0)
            PredictedQueueDel(uid);
        else
            Dirty(uid,comp);
    }
    [SubscribeLocalEvent]
    public void OnInsert(EntityUid uid, BundleComponent comp, PredContInserted args)
    {
        if (!TryComp<BundableComponent>(args.uid, out var bundable))
            return;
        if (!args.realChange)
            return;
        comp.bundlePositions.Add(args.uid, new BundleComponent.BundleEntData(){ pos = Vector2.Zero, storeAngle = Angle.Zero});
        comp.usedVolume += bundable.volume;
        afterMerge((uid,comp));
        Dirty(uid, comp);
    }

    public void onHandInteract(Entity<BundleGenericInteractionComponent> ent,ref UseInHandEvent ev)
    {
        if (ev.Handled)
            return;
        if (!predcontainers.GetContainer(ent.Owner, storeKey, out var cont))
            return;
        var cmp = Comp<BundleComponent>(ent);
        var uid = cont.contained.FirstOrDefault();
        if (TerminatingOrDeleted(uid))
            return;
        if(hands.TryGetEmptyHand(ev.User, out var hand) && hands.TryPickup(ev.User, uid))
            ev.Handled = true;
    }

    public void initRandom(Entity<BundleGenericInteractionComponent> ent, ref ComponentStartup ev)
    {
        if (network.IsServer)
        {
            ent.Comp.throwRandom = new RobustRandom().Next();
            Dirty(ent);
        }
    }
    public void onThrowBundle(EntityUid ent, BundleGenericInteractionComponent comp, ThrownEvent ev)
    {
        Log.Debug($"Bundle thrown");
        var bundle = Comp<BundleComponent>(ent);
        var rand = new RobustRandom();
        rand.SetSeed(comp.throwRandom);
        if (!predcontainers.GetContainer(ent, storeKey, out var cont))
            return;
        var maxAngle = Angle.FromDegrees(20);
        foreach (var resolve in cont.contained.ToList())
        {
            if (TerminatingOrDeleted(resolve))
                continue;
            handleRemove((ent, bundle), (resolve, Comp<BundableComponent>(resolve)));
            transform.SetWorldPosition(resolve, transform.GetWorldPosition(ent));
            var vel = Comp<PhysicsComponent>(ev.Thrown).LinearVelocity;
            var impulse = vel.Normalized() + maxAngle.ToVec() * (rand.NextFloat() - 0.5f) ;
            //Log.Debug($"Throwing {resolve} with {vel} and {maxAngle},velG {vel.ToAngle()} , worldG {vel.ToWorldAngle()} , impulse is {impulse} , {impulse.ToWorldAngle()} {impulse.ToAngle()} ");
            throwing.TryThrow(resolve, impulse);

        }
        helpers.QueueDel(ent);
    }
    
    public void handleRemove(Entity<BundleComponent> own,Entity<BundableComponent> targ)
    {
        RemoveFromBundle(own, targ);
        /* unpredictable ,  due to no predicted spawn . Causes issues with the last item. SPCR 2026
        if (user is not null && own.Comp.containing.Count == 1 && !lastover)
        {
            var last = GetEntity(own.Comp.containing[0]);
            RemoveFromBundle(own, (last, Comp<BundableComponent>(last)));
            if(hands.TryDrop((user.Value.Owner, user.Value.Comp), own.Owner))
                hands.TryPickup(user.Value, last);
        }
        */
        if(own.Comp.bundlePositions.Count == 0)
            PredictedQueueDel(own);
            //helpers.QueueDel(own);
    }

    public void onStart(Entity<BundleComponent> ent, ref ComponentStartup args)
    {
        predcontainers.CreateContainer(ent, storeKey, null);
    }

    public void onUse(Entity<BundableComponent> ent, ref AfterInteractEvent ev)
    {
        if (ev.Target is null)
            return;
        if (!ev.CanReach)
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

    public void onUseBundle(Entity<BundleGenericInteractionComponent> ent, ref AfterInteractEvent ev)
    {
        if (ev.Handled)
            return;
        if (!ev.CanReach)
            return;
        if (ev.Target is null)
            return;
        var comp = Comp<BundleComponent>(ent);
        if (TryComp<BundleComponent>(ev.Target.Value, out var targbund))
        {
            if (!predcontainers.GetContainer(ev.Target.Value, storeKey, out var targcont))
                return;
            foreach (var thing in targcont.contained.ToList())
            {
                if (TerminatingOrDeleted(thing))
                    continue;
                handleRemove((ev.Target.Value, targbund), (thing, Comp<BundableComponent>(thing)));
                if (!TryMerge((thing, Comp<BundableComponent>(thing)), (ent, Comp<BundleComponent>(ent))))
                {
                    TryMerge( (thing, Comp<BundableComponent>(thing)), (ev.Target.Value, targbund));
                    break;
                }

                ev.Handled = true;
            }
            return;
        }

        if (HasComp<BundableComponent>(ev.Target.Value))
        {
            if (TryMerge((ev.Target.Value, Comp<BundableComponent>(ev.Target.Value)),
                    (ent, Comp<BundleComponent>(ent))))
            {
                ev.Handled = true;
                return;
            }
        }
        var tick = timing.CurTick.Value;
        if (!predcontainers.GetContainer(ent.Owner, storeKey, out var cont))
            return;
        if (timing.IsFirstTimePredicted)
        {
            foreach (var thing in cont.contained.ToList())
            {
                if (TerminatingOrDeleted(thing))
                    continue;
                //var wasUsed = false;
                //Log.Debug($"--Using {resolved} on {ev.Target.Value} from bundle {ent.Owner},  tick {timing.CurTick}");
                if (interact.InteractUsing(ev.User, thing, ev.Target.Value, ev.ClickLocation, dropOverride: true))
                {
                    if (!comp.predictions.Get(tick, out var theque))
                    {
                        comp.predictions.Insert(tick, new Queue<EntityUid>());
                    }
                    comp.predictions[tick].Enqueue(thing);
                    //wasUsed = true;
                    Log.Debug($"--Used {thing} on {ev.Target.Value} from bundle {ent.Owner}");
                    ev.Handled = true;
                    break;
                }
            }
        }
        else if(comp.predictions.Get(tick, out var theque) && theque.TryDequeue(out var resolved))
        {
            interact.InteractUsing(ev.User, resolved, ev.Target.Value, ev.ClickLocation, dropOverride: true);
            theque.Enqueue(resolved);
            ev.Handled = true;
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
        var targ = ent.Owner;
        if (!predcontainers.insertEntity(bundle, storeKey, ref targ))
            return false;
        return true;
    }
    public void RemoveFromBundle(Entity<BundleComponent> bundle, Entity<BundableComponent> ent)
    {
        var cpy = ent.Owner;
        if (!predcontainers.removeEntity(bundle, storeKey, ref cpy))
            return;
        Log.Debug($"Removing {ent} from {bundle}");
        bundle.Comp.usedVolume -= ent.Comp.volume;
        afterRemove(bundle);
        Dirty(bundle, bundle.Comp);
    }

    public virtual void afterMerge(Entity<BundleComponent> bundle){}

    public virtual void afterRemove(Entity<BundleComponent> bundle){}

    public EntityUid CreateBundle(Entity<BundableComponent> ent, EntityUid user)
    {
        if (!prototypes.TryIndex<BundleGroup>(ent.Comp.group, out var indexed))
        {
            return EntityUid.Invalid;
        }
        if(!network.IsServer)
            return EntityUid.Invalid;
        var bundle = SpawnNextToOrDrop(bundleProto, user, null, indexed.components);
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
