using Robust.Shared.GameStates;

namespace Content.Shared;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OxydPredContainersComponent : Component
{
    public List<EntityUid> containing = new List<EntityUid>();
    // client stores each checksum, server only sends its latest one. SPCR 2026
    // state reset is done if server's checksum is not present at all.
    public byte[] checksum = new byte[16];
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
public record PredContInserted(EntityUid uid, Entity<OxydPredContainersComponent> container);
/// <summary>
///  removed on non-predicted tick
/// </summary>
/// <param name="uid"></param>
/// <param name="container"></param>
public record PredContRemoved(EntityUid uid, Entity<OxydPredContainersComponent> container);
/// <summary>
/// a state reset was triggered due to mismatching checksums. rebuild everything!
/// </summary>
/// <param name="container"></param>
public record PredContStateReset(Entity<OxydPredContainersComponent> container);