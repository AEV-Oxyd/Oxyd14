using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared;

[Serializable, NetSerializable]
public sealed partial class PredContState : IComponentState
{
    public Dictionary<string, ContWrap> containers = new();
}
[Serializable, NetSerializable]
public struct ContWrap
{
    public required OxydContainer c;
    public required List<short> s;
}

/// <summary>
/// This is a predicted container storage
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OxydPredContComponent : Component
{
    [ViewVariables] public Dictionary<string, OxydContainer> containers = new();
    [ViewVariables] public TimeSpan lastState = new();
}
[Serializable, NetSerializable]
public class OxydContainer
{
    [NonSerialized, ViewVariables] public string key = string.Empty;
    [ViewVariables] public uint? capacityLimit = 0;
    [ViewVariables] public List<NetEntity> netContained = new();
    [NonSerialized, ViewVariables] public List<EntityUid> contained = new();
    [NonSerialized, ViewVariables] public List<short> checksums = new(4);
    [NonSerialized, ViewVariables] public GameTick lastChange = new();
    [NonSerialized, ViewVariables] public CyclingDictionary<Queue<EntityUid>> predictions = new();

    public static short createHash(NetEntity ent, OxydContainerAction act)
    {
        return (short)(ent.GetHashCode() + act);
    }
    public bool canInsert(EntityUid ent, bool prediction = false)
    {
        if (contained.Contains(ent))
            return prediction;
        if (capacityLimit is not null && contained.Count >= capacityLimit)
            return false;
        return true;
    }

    public bool canRemove(EntityUid ent, NetEntity net, bool prediction = false)
    {
        if (prediction)
        {
            var h = createHash(net, OxydContainerAction.Remove);
            return checksums.Contains(h);
        }
        else
        {
            return contained.Contains(ent);
        }
    }

    public void insert(EntityUid ent, NetEntity netEnt, int? index = null)
    {
        if (!contained.Contains(ent))
        {
            if (index is not null)
            {
                contained.Insert(index.Value, ent);
            }
            else
            {
                contained.Add(ent);
            }

            netContained.Add(netEnt);
            checksums.Add(createHash(netEnt, OxydContainerAction.Add));
            if(checksums.Count > 20)
                checksums = checksums.GetRange(10, checksums.Count);
        }
    }

    public void remove(EntityUid ent, NetEntity netEnt)
    {
        if (contained.Contains(ent))
        {
            contained.Remove(ent);
            netContained.Remove(netEnt);
            checksums.Add(createHash(netEnt, OxydContainerAction.Remove));
            if(checksums.Count > 20)
                checksums = checksums.GetRange(10, checksums.Count);
        }
    }
}
public enum OxydContainerAction
{
    Add = 5823,
    Remove = 1059,
}
/// <summary>
///  inserted a item on a non-predicted tick
/// </summary>
/// <param name="uid"></param>
/// <param name="container"></param>
public record PredContInserted(EntityUid uid, Entity<OxydPredContComponent> container, bool realChange = true);

/// <summary>
///  removed on non-predicted tick
/// </summary>
/// <param name="uid"></param>
/// <param name="container"></param>
public record PredContRemoved(EntityUid uid, Entity<OxydPredContComponent> container, bool realChange = true);
/// <summary>
/// Handled state and we had a reset due to mismatch between server-client , rebuild everything associated.
/// </summary>
/// <param name="container"></param>
/// <param name="resetted"></param>
///

public record PredContStateReset(Entity<OxydPredContComponent> container, Dictionary<string, OxydContainer> resetted);