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
    public static readonly TimeSpan ImmediateTime = TimeSpan.FromMilliseconds(122);

    [SubscribeLocalEvent]
    public void OnContRemoved(EntityUid uid, OxydPredContComponent comp, EntRemovedFromContainerMessage args)
    {
        if (args.Container is Container baseCont && comp.containers.TryGetValue(baseCont.ID, out var cont))
        {
            removeEntity(uid, baseCont.ID, args.Entity);
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

    public bool insertEntity(EntityUid uid, string key, EntityUid target, bool? prediction = null)
    {
        var sc = cmq.CompOrNull(uid);
        var oc = opcq.CompOrNull(uid);
        if (sc is null || oc is null)
            return false;
        prediction ??= !gametime.IsFirstTimePredicted;
        if (!GetContainer(uid, key, out var container))
            return false;
        if(!container.canInsert(target, prediction.Value))
            return false;
        //Log.Info($"--Inserting {target} into {uid} from {key}");
        var mirror = containers.GetContainer(uid, key, sc);
        if (hands.IsHeld(target, out var user))
        {
            hands.TryDropIntoContainer((user.Value, null), target, mirror, false);
        }
        else
            containers.Insert(target, mirror);
        Log.Info($"Inserted {target} into {uid} from {key} on tick {gametime.CurTick}, is pred {gametime.IsFirstTimePredicted}");
        var count = container.contained.Count;
        container.insert(target, GetNetEntity(target));
        if(count != container.contained.Capacity)
            container.lastChange = gametime.CurTick;
        if (!TerminatingOrDeleted(target))
        {
            Log.Info($"Raising insert event {target} into {uid} from {key} prediction");
            var ev = new PredContInserted(target, (uid, oc), realChange: !prediction.Value);
            RaiseLocalEvent(target, ev);
            RaiseLocalEvent(uid, ev);
        }
        Dirty(uid, oc);
        return true;
    }
    

    public bool removeEntity(EntityUid uid, string key, EntityUid target, bool? prediction = null, EntityUid? insertionTarget = null)
    {
        var sc = cmq.CompOrNull(uid);
        var oc = opcq.CompOrNull(uid);
        prediction ??= !gametime.IsFirstTimePredicted;
        if (sc is null || oc is null)
            return false;
        if (!GetContainer(uid, key, out var container))
            return false;
        if (!container.canRemove(target, GetNetEntity(target),prediction.Value))
            return false;
        var mirror = containers.GetContainer(uid, key, sc);
        if (insertionTarget is not null)
        {
            containers.Insert(insertionTarget.Value, mirror);
        }
        else
        {
            containers.Remove(target, mirror);
        }
        Log.Info($"--Removing {target} from {uid} from {key}");
        var count = container.contained.Count;
        container.remove(target, GetNetEntity(target));
        if(count != container.contained.Capacity)
            container.lastChange = gametime.CurTick;
        var ev = new PredContRemoved(target, (uid, oc), realChange: !prediction.Value);
        RaiseLocalEvent(target, ev);
        RaiseLocalEvent(uid, ev);
        Log.Info($"Raising remove event {target} from {uid} from {key} prediction");
    
        Dirty(uid, oc);
        return true;   
    }
}