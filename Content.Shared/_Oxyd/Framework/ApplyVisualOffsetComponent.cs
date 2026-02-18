using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Framework;

/// <summary>
/// Apply a visual offset calculated by the server.
/// Use when something requires a predicted offset that wont always be in PVS range
/// or there is no client-side data to deduce it from!
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public partial class ApplyVisualOffsetComponent : Component
{
    [DataField, AutoNetworkedField]
    public Vector2 offset;
}
