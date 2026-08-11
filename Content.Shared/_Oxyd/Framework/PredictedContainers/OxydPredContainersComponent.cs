using Robust.Shared.GameStates;

namespace Content.Shared;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OxydPredContainersComponent : Component
{
    public List<EntityUid> containing = new List<EntityUid>();
    public byte[] checksum = new byte[16];
}

public enum OxydContainerAction
{
    Add = 1,
    Remove = 2
}