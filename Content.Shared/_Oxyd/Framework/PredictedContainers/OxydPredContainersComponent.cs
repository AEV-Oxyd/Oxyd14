using Robust.Shared.GameStates;

namespace Content.Shared;

/// <summary>
/// This is a predicted container storage
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OxydPredContComponent : Component
{
    public Dictionary<string, OxydContainer> containers = new();
}

public class OxydContainer
{
    public string key = string.Empty;
    public int? capacityLimit = 0;
    public List<EntityUid> contained = new();
    public List<Byte> checksums = new(4);

    public static byte createHash(EntityUid ent, OxydContainerAction act)
    {
        return (byte)(ent.GetHashCode() + act);
    }
    public bool canInsert(EntityUid ent, bool prediction = false)
    {
        if (contained.Contains(ent))
            return prediction;
        if (capacityLimit is not null && contained.Count >= capacityLimit)
            return false;
        return true;
    }

    public bool canRemove(EntityUid ent, bool prediction = false)
    {
        if (prediction)
        {
            var h = createHash(ent, OxydContainerAction.Remove);
            return checksums.Contains(h);
        }
        else
        {
            return contained.Contains(ent);
        }
    }

    public void insert(EntityUid ent)
    {
        if (!contained.Contains(ent))
        {
            contained.Add(ent);
            checksums.Add(createHash(ent, OxydContainerAction.Add));
            if(checksums.Count > 20)
                checksums = checksums.GetRange(10, checksums.Count);
        }
    }

    public void remove(EntityUid ent)
    {
        if (contained.Contains(ent))
        {
            contained.Remove(ent);
            checksums.Add(createHash(ent, OxydContainerAction.Remove));
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
public record PredContInserted(EntityUid uid, Entity<OxydPredContComponent> container);
/// <summary>
///  removed on non-predicted tick
/// </summary>
/// <param name="uid"></param>
/// <param name="container"></param>
public record PredContRemoved(EntityUid uid, Entity<OxydPredContComponent> container);