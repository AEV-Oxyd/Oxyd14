using System.Linq;
using System.Numerics;
using System.Reflection.PortableExecutable;
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
    [Dependency] private SharedContainerSystem containers = default!;
    [Dependency] private SharedHandsSystem hands = default!;
    [Dependency] private IPrototypeManager prototypes = default!;
    [Dependency] private INetManager network = default!;
    [Dependency] protected IGameTiming timing = default!;
    [Dependency] private SharedOxydHelpers helpers = default!;
    [Dependency] private SharedInteractionSystem interact = default!;
    [Dependency] protected SharedTransformSystem transform = default!;
    [Dependency] protected ThrowingSystem throwing = default!;
    [Dependency] protected SharedPhysicsSystem physics = default!;
    private IRobustRandom random = new RobustRandom();

    public static readonly string storeKey = "storagebase";
    public static readonly ProtoId<BundleGroup> bundleProto = "BaseBundle";
    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<BundleComponent, ComponentStartup>(onStart);
        SubscribeLocalEvent<BundableComponent, AfterInteractEvent>(onUse);
        SubscribeLocalEvent<BundleGenericInteractionComponent, AfterInteractEvent>(onUseBundle);
        SubscribeLocalEvent<BundleGenericInteractionComponent, ThrownEvent>(onThrowBundle);
        SubscribeLocalEvent<BundleGenericInteractionComponent, ComponentStartup>(initRandom);
        SubscribeLocalEvent<BundleGenericInteractionComponent, UseInHandEvent>(onHandInteract);
        //SubscribeLocalEvent<BundleComponent, EntRemovedFromContainerMessage>(handleRemove);
        SubscribeLocalEvent<BundleComponent, ComponentGetState>(onGetState);

    }

    public void onHandInteract(Entity<BundleGenericInteractionComponent> ent,ref UseInHandEvent ev)
    {
        if (!timing.IsFirstTimePredicted)
            return;
        if (ev.Handled)
            return;
        var cmp = Comp<BundleComponent>(ent);
        var uid = GetEntity(cmp.containing.FirstOrDefault());
        if (TerminatingOrDeleted(uid))
            return;
        handleRemove((ent,cmp), (uid, Comp<BundableComponent>(uid)));
        ev.Handled = true;
        hands.SwapHands(ev.User);
        hands.TryPickup(ev.User, uid);
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
        var cont = containers.GetContainer(ent, storeKey);
        var maxAngle = Angle.FromDegrees(20);
        var copy = bundle.containing.ToList();
        foreach (var thing in copy)
        {
            var resolve = GetEntity(thing);
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

    public void onGetState(Entity<BundleComponent> ent, ref ComponentGetState args)
    {
        args.State = new BundleComponent.BundleState()
        {
            Group = ent.Comp.group,
            Containing = ent.Comp.containing,
            UsedVolume = ent.Comp.usedVolume,
            Checksum = new List<BundleComponent.BundleAct>(ent.Comp.checksum.TakeLast(20)),
            BundlePositions = ent.Comp.bundlePositions,
        };
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
        if(own.Comp.containing.Count == 0)
            PredictedQueueDel(own);
            //helpers.QueueDel(own);
    }

    public void onStart(Entity<BundleComponent> ent, ref ComponentStartup args)
    {
        containers.EnsureContainer<Container>(ent.Owner, storeKey);
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
        if (!timing.IsFirstTimePredicted)
            return;
        if (ev.Target is null)
            return;
        var comp = Comp<BundleComponent>(ent);
        // isFirstTimePredicted doesnt cover this case somehow gg SPCR 2026
        if (comp.containing.Contains(GetNetEntity(ev.Target.Value)))
        {
            ev.Handled = true;
            return;
        }

        if (TryComp<BundleComponent>(ev.Target.Value, out var targbund))
        {
            var copy = targbund.containing.ToList();
            foreach (var thing in copy)
            {
                var resolved = GetEntity(thing);
                if (TerminatingOrDeleted(resolved))
                    continue;
                handleRemove((ev.Target.Value, targbund), (resolved, Comp<BundableComponent>(resolved)));
                if (!TryMerge((resolved, Comp<BundableComponent>(resolved)), (ent, Comp<BundleComponent>(ent))))
                {
                    TryMerge( (resolved, Comp<BundableComponent>(resolved)), (ev.Target.Value, targbund));
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
        var cont = containers.GetContainer(ent, storeKey);
        foreach (var thing in comp.containing)
        {
            var resolved = GetEntity(thing);
            if (TerminatingOrDeleted(resolved))
                continue;
            //var wasUsed = false;
            //Log.Debug($"--Using {resolved} on {ev.Target.Value} from bundle {ent.Owner},  tick {timing.CurTick}");
            if (interact.InteractUsing(ev.User, resolved, ev.Target.Value, ev.ClickLocation, dropOverride: true))
            {
                //wasUsed = true;
                //Log.Debug($"--Used {resolved} on {ev.Target.Value} from bundle {ent.Owner}");
                ev.Handled = true;
            }

            if (!cont.Contains(resolved))
            {
                handleRemove((ent, comp), (resolved, Comp<BundableComponent>(resolved)));
                /*
                if (!wasUsed)
                {
                    Log.Debug($"--Used {resolved} but was not marked as used!");
                }
                */

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
            bundle.Comp.checksum.Add(new BundleComponent.BundleAct(){entity = GetNetEntity(ent.Owner), id = 'A'});
            bundle.Comp.usedVolume += ent.Comp.volume;
            bundle.Comp.containing.Add(GetNetEntity(ent.Owner));
            bundle.Comp.bundlePositions.Add(GetNetEntity(ent.Owner), random.NextVector2(0.01f, 0.2f));
            if (bundle.Comp.checksum.Count > 50)
            {
                bundle.Comp.checksum = bundle.Comp.checksum.GetRange(20, bundle.Comp.checksum.Count);
            }
        }
        afterMerge(bundle);
        Dirty(bundle, bundle.Comp);
        return true;
    }
    public void RemoveFromBundle(Entity<BundleComponent> bundle, Entity<BundableComponent> ent)
    {
        Log.Debug($"Removing {ent} from {bundle}");
        if (!bundle.Comp.containing.Contains(GetNetEntity(ent.Owner)))
        {
            Log.Error($"Tried to remove {ent} from {bundle} but it wasn't in it!");
            return;
        }
        containers.Remove(ent.Owner, containers.GetContainer(bundle.Owner, storeKey));
        bundle.Comp.checksum.Add(new BundleComponent.BundleAct(){entity = GetNetEntity(ent.Owner), id = 'R'});
        bundle.Comp.containing.Remove(GetNetEntity(ent.Owner));
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

        if (!network.IsServer)
            return EntityUid.Invalid;
        var bundle = SpawnNextToOrDrop(bundleProto, user,null, indexed.components);
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
