using Robust.Shared.Map;

namespace Content.Server._Oxyd.Framework.ViewCalc;

/// <summary>
/// This marks a entity to be relevant for view calculations.
/// </summary>
[RegisterComponent]
public sealed partial class ViewRelevantComponent : Component
{

}

/// <summary>
///  Marks a entity as a view ticker, ticking every second
/// </summary>
[RegisterComponent]
public sealed partial class ViewTickerComponent : Component
{
    public TimeSpan lastTickTime = TimeSpan.Zero;
    public MapCoordinates lastTickPosition =  new MapCoordinates();
    public float range = 8f;
    public HashSet<EntityUid> lastSeen;
}
