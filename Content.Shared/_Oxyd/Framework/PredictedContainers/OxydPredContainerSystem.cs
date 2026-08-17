using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Timing;

namespace Content.Shared;


public sealed partial class OxydPredContainerSystem : EntitySystem
{
    [Dependency] private EntityQuery<OxydPredContComponent> opcq = default!;
    [Dependency] private EntityQuery<ContainerManagerComponent> cmq = default!;
    [Dependency] private SharedContainerSystem containers = default!;
    [Dependency] private IGameTiming gametime = default!;
    [Dependency] private SharedHandsSystem hands = default!;

    [SubscribeLocalEvent]
    public void OnContRemoved(EntityUid uid, OxydPredContComponent comp, EntRemovedFromContainerMessage args)
    {
        if (!gametime.IsFirstTimePredicted)
            return;
        if (args.Container is Container baseCont && comp.containers.TryGetValue(baseCont.ID, out var cont))
        {
            removeEntity(uid, baseCont.ID, args.Entity, false);
        }
    }

    [SubscribeLocalEvent]
    public void GetState(Entity<OxydPredContComponent> ent, ref ComponentGetState args)
    {
        var state = new PredContState();
        foreach (var (key, content) in ent.Comp.containers)
        {
            state.containers[key] = new ContWrap()
            {
                c = content,
                s = content.checksums.Last()
            };
        }
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
            if (ent.Comp.containers.TryGetValue(key, out var old))
            {
                if (old.checksums.Contains(content.s) && (gametime.CurTick.Value - old.lastChange.Value) < 30)
                    continue;
            }
            content.c.key = key;
            content.c.checksums.Add(content.s);
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
            RaiseLocalEvent(ent, new PredContStateReset(ent, resetted));
        }
    }
    
    public OxydContainer CreateContainer(EntityUid entity, string key, int? capacity)
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
        Log.Info($"--Inserting {target} into {uid} from {key}");
        var mirror = containers.GetContainer(uid, key, sc);
        if (hands.IsHeld(target, out var user))
        {
            hands.TryDropIntoContainer((user.Value, null), target, mirror, false);
        }
        else
            containers.Insert(target, mirror);
        container.insert(target, GetNetEntity(target));
        if (!prediction.Value)
        {
            var ev = new PredContInserted(target, (uid, oc));
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
        if (!container.canRemove(target, prediction.Value))
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
        container.remove(target, GetNetEntity(target));
        if (!prediction.Value)
        {
            var ev = new PredContRemoved(target, (uid, oc));
            RaiseLocalEvent(target, ev);
            RaiseLocalEvent(uid, ev);
        }
        Dirty(uid, oc);
        return true;   
    }
}