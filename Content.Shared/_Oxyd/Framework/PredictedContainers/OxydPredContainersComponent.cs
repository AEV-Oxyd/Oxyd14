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
    public byte[] checksums = new byte[16];

    public static byte createHash(EntityUid ent, OxydContainerAction act)
    {
        return (byte)(ent.GetHashCode() + act);
    }
    public bool canPredictedInsert(EntityUid ent)
    {
        if (contained.Contains(ent))
            return true;
        return false;
    }

    public bool canInsert(EntityUid ent)
    {
        if (contained.Contains(ent))
            return false;
        if (capacityLimit is not null && contained.Count >= capacityLimit)
            return false;
        return true;
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