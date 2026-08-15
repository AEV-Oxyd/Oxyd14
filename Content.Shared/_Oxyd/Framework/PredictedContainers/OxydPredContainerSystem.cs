using System.Diagnostics.CodeAnalysis;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Shared;

/// <summary>
/// This handles...
/// </summary>
public sealed class OxydPredContainerSystem : EntitySystem
{
    [Dependency] private EntityQuery<OxydPredContComponent> opcq = default!;
    [Dependency] private EntityQuery<ContainerManagerComponent> cmq = default!;
    [Dependency] private SharedContainerSystem containers = default!;
    [Dependency] private IGameTiming gametime = default!;
    [Dependency] private SharedHandsSystem hands = default!;
    public byte GenerateActionChecksum(EntityUid entity, OxydContainerAction action)
    {
        return (byte)(entity.GetHashCode() + action);
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
        container.insert(target);
        if (!prediction)
        {
            var ev = new PredContInserted(target, (uid, oc));
            RaiseLocalEvent(target, ev);
            RaiseLocalEvent(uid, ev);
        }
        return true;
    }
    

    public bool removeEntity(EntityUid uid, string key, EntityUid target, bool prediction = false)
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
        containers.Remove(target, mirror);
        container.remove(target);
        if (!prediction)
        {
            var ev = new PredContRemoved(target, (uid, oc));
            RaiseLocalEvent(target, ev);
            RaiseLocalEvent(uid, ev);
        }
        return true;   
    }
}