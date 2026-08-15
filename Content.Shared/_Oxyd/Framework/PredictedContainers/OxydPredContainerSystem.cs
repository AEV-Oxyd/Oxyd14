using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Containers;

namespace Content.Shared;

/// <summary>
/// This handles...
/// </summary>
public sealed class OxydPredContainerSystem : EntitySystem
{
    [Dependency] private EntityQuery<OxydPredContComponent> opcq = default!;
    [Dependency] private EntityQuery<ContainerManagerComponent> cmq = default!;
    [Dependency] private SharedContainerSystem containers = default!;
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
        
    }
}