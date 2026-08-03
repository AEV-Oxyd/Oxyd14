using Robust.Shared.Serialization;
using Robust.Shared.Utility;
using Robust.Shared.Timing;
using Robust.Shared.Player;

namespace Content.Shared._Oxyd.Framework.RadialMenu;

public abstract partial  class SharedRadialMenuSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    protected float Timer = 0f;

    protected readonly record struct PendingRequest(
        Action<RadialBaseSelection> Callback,
        List<RadialMenuOption> Options,
        TimeSpan CreationTime,
        ICommonSession Player,
        EntityUid? Target = null
    );

    /// <summary>
    /// Show a generic radial menu to <paramref name="player"/>.
    /// </summary>
    public abstract void ShowRadial(ICommonSession player, List<RadialMenuOption> options, Action<RadialBaseSelection> callback, EntityUid? target = null, bool server = true, bool client = true, bool forceChoice = false);

    /// <summary>
    /// Helper to show a radial menu with entity icons.
    /// </summary>
    public void ShowRadial(ICommonSession player, List<NetEntity> entities, Action<RadialItemSelection> callback, EntityUid? target = null)
    {
        var options = new List<RadialMenuOption>();
        foreach (var entity in entities)
        {
            options.Add(new EntityRadialMenuOption
            {
                Entity = entity
            });
        }

        ShowRadial(player, options, sel =>
        {
            if (sel.Index < 0 || sel.Index >= entities.Count)
                return;

            callback(new RadialItemSelection
            {
                Index = sel.Index,
                Entity = entities[sel.Index],
                Options = sel.Options
            });
        }, target);
    }

    protected abstract void OpenMenu(Guid requestId, List<RadialMenuOption> options, bool forceChoice = false, EntityUid? target = null);
}

[Serializable, NetSerializable]
public abstract class RadialMenuOption
{
    public string? Tooltip;
}

[Serializable, NetSerializable]
public sealed class EntityRadialMenuOption : RadialMenuOption
{
    public NetEntity Entity;
}

[Serializable, NetSerializable]
public sealed class SpriteRadialMenuOption : RadialMenuOption
{
    public SpriteSpecifier Sprite = default!;
}

[Serializable, NetSerializable]
public sealed class PrototypeRadialMenuOption : RadialMenuOption
{
    public string Prototype = default!;
}

/// <summary>
/// Represents a selection made by the player in a radial menu, identified by index.
/// </summary>
public class RadialBaseSelection
{
    public int Index;
    public List<RadialMenuOption> Options = default!;
}

/// <summary>
/// A radial selection that also carries the selected entity.
/// </summary>
public sealed class RadialItemSelection : RadialBaseSelection
{
    public NetEntity Entity;
}

/// <summary>
/// Sent server → client to open a radial menu.
/// </summary>
[Serializable, NetSerializable]
public sealed class RadialMenuOpenEvent : EntityEventArgs
{
    public Guid RequestId;
    public List<RadialMenuOption> Options = new();
    public NetEntity? Target;
    public bool ForceChoice = false;
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
