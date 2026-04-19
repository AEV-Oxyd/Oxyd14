using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Framework.RadialMenu;

/// <summary>
/// Represents a selection made by the player in a radial menu, identified by index.
/// </summary>
public class RadialBaseSelection
{
    public int Index;
}

/// <summary>
/// A radial selection that also carries the selected entity.
/// </summary>
public sealed class RadialItemSelection : RadialBaseSelection
{
    public NetEntity Entity;
}

/// <summary>
/// Sent server → client to open a radial menu with a list of entity options.
/// </summary>
[Serializable, NetSerializable]
public sealed class RadialMenuOpenEvent : EntityEventArgs
{
    public Guid RequestId;
    public List<NetEntity> Options = new();
}

/// <summary>
/// Sent client → server when the player picks an option.
/// </summary>
[Serializable, NetSerializable]
public sealed class RadialMenuSelectionEvent : EntityEventArgs
{
    public Guid RequestId;
    public int SelectedIndex;
}
