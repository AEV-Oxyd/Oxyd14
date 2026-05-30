using Robust.Client.Timing;

namespace Content.Client._Oxyd.Framework;

/// <summary>
/// This handles...
/// </summary>
public sealed partial class ClientOxydHelpers : EntitySystem
{
    [Dependency] private IClientGameTiming _gameTiming = default!;

    // returns how many ticks ahead we are simulating as the client
    public uint getPredTicks()
    {
        return _gameTiming.CurTick.Value - _gameTiming.LastRealTick.Value - 1;
    }
}
