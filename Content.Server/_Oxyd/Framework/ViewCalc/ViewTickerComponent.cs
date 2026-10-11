using Robust.Shared.Map;

namespace Content.Server._Oxyd.Framework.ViewCalc;

/// <summary>
///  Marks a entity as a view ticker, ticking every second
/// </summary>
[RegisterComponent]
public sealed partial class ViewTickerComponent : Component
{
    [DataField]
    public float range = 8f;

    /// <summary>Extra range contributed by auras; effective range is max(range, auraRange).</summary>
    public float auraRange;

    public HashSet<EntityUid> lastSeen = new();

    /// <summary>When the last view calculation ran for this ticker.</summary>
    public TimeSpan lastTickTime;

    /// <summary>Where the ticker stood when the last view calculation ran.</summary>
    public MapCoordinates? lastTickPosition;
}
