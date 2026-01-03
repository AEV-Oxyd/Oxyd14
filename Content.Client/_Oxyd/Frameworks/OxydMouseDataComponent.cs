using Robust.Shared.Map;

namespace Content.Client._Oxyd.Framework;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class OxydMouseDataComponent : Component
{
    public MapCoordinates mouseMap = MapCoordinates.Nullspace;
    // this does not get updated for hovering! This is the entity coordinates version from the player
    public EntityCoordinates mouseEntity = EntityCoordinates.Invalid;
    public EntityUid lastHovered = EntityUid.Invalid;
    public EntityUid lastClicked = EntityUid.Invalid;
}
