using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Guns;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class PlayerRecoilBacktrackerComponent : Component
{
    [ViewVariables]
    public Dictionary<GameTick, List<float>> recoils = new Dictionary<GameTick, List<float>>();
}

