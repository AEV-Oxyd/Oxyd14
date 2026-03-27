using Robust.Client.Timing;

namespace Content.Client._Oxyd.Framework;

/// <summary>
/// This handles...
/// </summary>
public sealed class ClientOxydHelpers : EntitySystem
{
    [Dependency] private readonly IClientGameTiming _gameTiming = default!;

    // returns how many ticks ahead we are simulating as the client
    public uint getPredTicks()
    {
        return _gameTiming.CurTick.Value - _gameTiming.LastRealTick.Value - 1;
    }
}
