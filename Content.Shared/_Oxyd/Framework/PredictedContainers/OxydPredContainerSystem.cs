using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Timing;

namespace Content.Shared;


public partial class OxydPredContainerSystem : EntitySystem
{
    [Dependency] private EntityQuery<OxydPredContComponent> opcq = default!;
    [Dependency] private EntityQuery<ContainerManagerComponent> cmq = default!;
    [Dependency] private SharedContainerSystem containers = default!;
    [Dependency] private IGameTiming gametime = default!;
    [Dependency] private SharedHandsSystem hands = default!;

    /// <summary>
    ///  How many checksums are networked, each checksum is a SHORT(2 bytes)
    /// </summary>
    public const int StateTrack = 8;
    /// <summary>
    /// If a state was sent recently , this is the limit used.
    /// </summary>
    public const int ImmediateStateTrack = 2;
    
    /// <summary>
    ///  If last state's delta to current time is below this , use immediate state track count.
    /// </summary>
    public static readonly TimeSpan ImmediateTime = TimeSpan.FromMilliseconds(88);

    [SubscribeLocalEvent]
    public void OnContRemoved(EntityUid uid, OxydPredContComponent comp, EntRemovedFromContainerMessage args)
    {
        if (args.Container is Container baseCont && comp.containers.TryGetValue(baseCont.ID, out var cont))
        {
            var cpy = args.Entity;
            removeEntity(uid, baseCont.ID, ref cpy, handlePredict: false);
        }
    }

    [SubscribeLocalEvent]
    public void GetState(Entity<OxydPredContComponent> ent, ref ComponentGetState args)
    {
        var state = new PredContState();
        var usingLimit = StateTrack;
        if(gametime.RealTime - ent.Comp.lastState < ImmediateTime)
            usingLimit = ImmediateStateTrack;
        foreach (var (key, content) in ent.Comp.containers)
        {
            List<short> itemList = new();
            int i = 0;
            while (i++ < usingLimit)
            {
                if (i > content.checksums.Count)
                    break;
                itemList.Add(content.checksums[^i]);
            }

            state.containers[key] = new ContWrap()
            {
                c = content,
                s = itemList,
            };
        }
        ent.Comp.lastState = gametime.RealTime;
        args.State = state;
    }

    [SubscribeLocalEvent]
    public void HandleState(Entity<OxydPredContComponent> ent,ref ComponentHandleState args)
    {
        if (args.Current is not PredContState state)
            return;
        Dictionary<string, OxydContainer> resetted = new();
        foreach (var (key, content) in state.containers)
        {
            
            if (ent.Comp.containers.TryGetValue(key, out var old) && (content.s.All(old.checksums.Contains) || old.capacityLimit != content.c.capacityLimit))
            {
                continue;
            }
            content.c.key = key;
            content.c.checksums = new();
            content.c.contained = new();
            content.c.checksums = content.s;
            ent.Comp.containers[key] = content.c;
            foreach (var id in content.c.netContained)
            {
                var thing = GetEntity(id);
                if (TerminatingOrDeleted(thing))
                    continue;
                content.c.contained.Add(thing);
            }
            resetted[key] = content.c;
        }

        if (resetted.Count > 0)
        {
            Log.Debug($"Resetting {resetted.Count} containers");
            RaiseLocalEvent(ent, new PredContStateReset(ent, resetted));
        }
    }
    
    public OxydContainer CreateContainer(EntityUid entity, string key, int? capacity = null)
    {
        var cont = new OxydContainer();
        cont.key = key;
        cont.capacityLimit = capacity;
        var sc = EnsureComp<ContainerManagerComponent>(entity);
        var oc = EnsureComp<OxydPredContComponent>(entity);
        oc.containers[key] = cont;
        containers.EnsureContainer<Container>(entity, key);
        return cont;
    }

    public bool GetContainer(EntityUid uid, string key, [NotNullWhen(true)] out OxydContainer? cont)
    {
        cont = null;
        if (opcq.TryComp(uid, out var opc))
        {
            if(opc.containers.TryGetValue(key, out cont))
                return true;
        }
        return false;
    }

    public void SetContainerCapacity(EntityUid uid, string key, int? capacity = null)
    {
        if (!GetContainer(uid, key, out var cont))
            return;
        if (capacity is null)
        {
            cont.capacityLimit = null;
            return;
        }

        if (cont.capacityLimit is not null && cont.capacityLimit > capacity)
        {
            for (var i = 1; i <= cont.capacityLimit - capacity; i++)
            {
                var buf = cont.contained.Last();
                removeEntity(uid, key, ref buf, false);
            }
        }

        cont.capacityLimit = capacity;
    }
    public bool insertEntity(EntityUid uid, string key,ref EntityUid target, bool? prediction = null, bool handlePredict = false, int? targetIndex = null)
    {
        if (!GetContainer(uid, key, out var container))
            return false;
        return insertEntity(uid, container, ref target, prediction, handlePredict, targetIndex);
        
    }

    public bool insertEntity(EntityUid uid, OxydContainer container,ref EntityUid target, bool? prediction = null, bool handlePredict = false, int? targetIndex = null)
    {
        var sc = cmq.CompOrNull(uid);
        var oc = opcq.CompOrNull(uid);
        if (sc is null || oc is null)
            return false;
        prediction ??= !gametime.IsFirstTimePredicted;
        if (handlePredict && prediction.Value)
        {
            var orig = target;
            target = ConsumePredictAct(container);
            Log.Info($"Handling predict, original {orig} , new {target} on tick {gametime.CurTick}, is pred {!gametime.IsFirstTimePredicted}");
            if (TerminatingOrDeleted(target))
                return false;
        }

        if(!container.canInsert(target, prediction.Value))
            return false;
        //Log.Info($"--Inserting {target} into {uid} from {key}");
        var mirror = containers.GetContainer(uid, container.key, sc);
        if (hands.IsHeld(target, out var user))
        {
            hands.TryDropIntoContainer((user.Value, null), target, mirror, false);
        }
        else
            containers.Insert(target, mirror);
        Log.Info($"Inserted {target} into {uid} from {container.key} on tick {gametime.CurTick}, is pred {!gametime.IsFirstTimePredicted}");
        var count = container.contained.Count;
        container.insert(target, GetNetEntity(target));
        if(count != container.contained.Capacity)
            container.lastChange = gametime.CurTick;
        if (!TerminatingOrDeleted(target))
        {
            if (handlePredict && !prediction.Value)
                StorePredictAct(container, target);
            Log.Info($"Raising insert event {target} into {uid} from {container.key} prediction");
            var ev = new PredContInserted(target, (uid, oc), container , !prediction.Value);
            RaiseLocalEvent(target, ev);
            RaiseLocalEvent(uid, ev);
        }
        Dirty(uid, oc);
        return true;
    }
    

    public bool removeEntity(EntityUid uid, string key, ref EntityUid target, bool? prediction = null, EntityUid? insertionTarget = null, bool handlePredict = false)
    {
        if (!GetContainer(uid, key, out var container))
            return false;
        return removeEntity(uid, container,ref target, prediction, insertionTarget, handlePredict);
    }

    public bool removeEntity(EntityUid uid, OxydContainer container,ref EntityUid target, bool? prediction = null, EntityUid? insertionTarget = null, bool handlePredict = false)
    {
        var sc = cmq.CompOrNull(uid);
        var oc = opcq.CompOrNull(uid);
        prediction ??= !gametime.IsFirstTimePredicted;
        if (handlePredict && prediction.Value)
        {
            target = ConsumePredictAct(container);
        }
        if (sc is null || oc is null)
            return false;
        if (!container.canRemove(target, GetNetEntity(target),prediction.Value))
            return false;
        var mirror = containers.GetContainer(uid, container.key, sc);
        if (insertionTarget is not null)
        {
            containers.Insert(insertionTarget.Value, mirror);
        }
        else
        {
            containers.Remove(target, mirror);
        }
        if(handlePredict && !prediction.Value)
            StorePredictAct(container, target);
        Log.Info($"--Removing {target} from {uid} from {container.key}");
        var count = container.contained.Count;
        container.remove(target, GetNetEntity(target));
        if(count != container.contained.Capacity)
            container.lastChange = gametime.CurTick;
        var ev = new PredContRemoved(target, (uid, oc),container, !prediction.Value);
        RaiseLocalEvent(target, ev);
        RaiseLocalEvent(uid, ev);
        Log.Info($"Raising remove event {target} from {uid} from {container.key} prediction");
    
        Dirty(uid, oc);
        return true;   
    }
    /// <summary>
    /// Stores a entity's insertion/removal for future actions/simulated ticks
    /// </summary>
    /// <param name="container"></param>
    /// <param name="target"></param>
    public void StorePredictAct(EntityUid uid, string key, EntityUid target)
    {
        if (GetContainer(uid, key, out var container))
        {
            StorePredictAct(container, target);
        }
    }
    /// <summary>
    /// Stores a entity's insertion/removal for future actions/simulated ticks
    /// </summary>
    /// <param name="container"></param>
    /// <param name="target"></param>
    public void StorePredictAct(OxydContainer container, EntityUid target)
    {
        if (!container.predictions.Get(gametime.CurTick.Value, out var pred))
            container.predictions[gametime.CurTick.Value] = new Queue<EntityUid>();
        container.predictions[gametime.CurTick.Value].Enqueue(target);
    }
    
    public EntityUid ConsumePredictAct(EntityUid uid, string key)
    {
        if (GetContainer(uid, key, out var container))
        {
            return ConsumePredictAct(container);
        }
        return EntityUid.Invalid;
    }
    
    /// <summary>
    /// Grabs the entity meant to be acted upon at this moment.
    /// If this is not returning what you're expecting, your order is off in simulated ticks
    /// or the game might not be raising events in the same order SPCR 2026
    /// </summary>
    /// <param name="container"></param>
    /// <returns></returns>
    public EntityUid ConsumePredictAct(OxydContainer container)
    {
        if (!container.predictions.Get(gametime.CurTick.Value, out var que))
            return EntityUid.Invalid;
        if (!que.TryDequeue(out var uid))
        {
            Log.Error($"Predicted container, {container.key} has returned no entity for ConsumePredictAct at tick {gametime.CurTick}");
            return EntityUid.Invalid;
        }
        que.Enqueue(uid);

        return uid;
    }
    
}