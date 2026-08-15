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
    public void GetState(EntityUid uid, OxydPredContComponent comp, ComponentGetState args)
    {
        var state = new PredContState();
        foreach (var (key, content) in comp.containers)
        {
            state.containers[key] = new ContWrap()
            {
                c = content,
                s = content.checksums.Last()
            };
        }
    }

    [SubscribeLocalEvent]
    public void HandleState(EntityUid uid, OxydPredContComponent comp, ComponentHandleState args)
    {
        if (args.Current is not PredContState state)
            return;
        foreach (var (key, content) in state.containers)
        {
            if (comp.containers.TryGetValue(key, out var old))
            {
                if (old.checksums.Contains(content.s) && (gametime.CurTick.Value - old.lastChange.Value) < 30)
                    continue;
            }
            content.c.key = key;
            content.c.checksums.Add(content.s);
            comp.containers[key] = content.c;
            foreach (var id in content.c.netContained)
            {
                var ent = GetEntity(id);
                if (TerminatingOrDeleted(ent))
                    continue;
                content.c.contained.Add(ent);
            }
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

    public bool insertEntity(EntityUid uid, string key, EntityUid target, bool prediction = false)
    {
        var sc = cmq.CompOrNull(uid);
        var oc = opcq.CompOrNull(uid);
        if (sc is null || oc is null)
            return false;
        if (!GetContainer(uid, key, out var container))
            return false;
        if(!container.canInsert(target, prediction))
            return false;
        var mirror = containers.GetContainer(uid, key, sc);
        if (hands.IsHeld(target, out var user))
        {
            hands.TryDropIntoContainer((user.Value, null), target, mirror, false);
        }
        else
            containers.Insert(target, mirror);
        container.insert(target, GetNetEntity(target));
        if (!prediction)
        {
            var ev = new PredContInserted(target, (uid, oc));
            RaiseLocalEvent(target, ev);
            RaiseLocalEvent(uid, ev);
        }
        Dirty(uid, oc);
        return true;
    }
    

    public bool removeEntity(EntityUid uid, string key, EntityUid target, bool prediction = false, EntityUid? insertionTarget = null)
    {
        var sc = cmq.CompOrNull(uid);
        var oc = opcq.CompOrNull(uid);
        if (sc is null || oc is null)
            return false;
        if (!GetContainer(uid, key, out var container))
            return false;
        if (!container.canRemove(target, prediction))
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

        container.remove(target, GetNetEntity(target));
        if (!prediction)
        {
            var ev = new PredContRemoved(target, (uid, oc));
            RaiseLocalEvent(target, ev);
            RaiseLocalEvent(uid, ev);
        }
        Dirty(uid, oc);
        return true;   
    }
}