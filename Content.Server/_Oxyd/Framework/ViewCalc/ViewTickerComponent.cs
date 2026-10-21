using Robust.Shared.Map;

namespace Content.Server._Oxyd.Framework.ViewCalc;

/// <summary>
///  Marks a entity as a view ticker, ticking every second
/// </summary>
[RegisterComponent]
public sealed partial class ViewTickerComponent : Component
{
    public TimeSpan lastTickTime = TimeSpan.Zero;

    public MapCoordinates lastTickPosition = new();

    /// <summary>View radius. Aura upgrades add to this and subtract the same amount when removed.</summary>
    [DataField]
    public float range = 8f;

    public HashSet<EntityUid> lastSeen = new();
}
