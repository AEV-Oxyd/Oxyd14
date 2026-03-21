using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Oxyd.Guns;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class FiremodeStateHandlerComponent : Component
{
    [ViewVariables]
    public TimeSpan lastAction = TimeSpan.Zero;
    [ViewVariables]
    public ICommonSession? shooterSession;
    [ViewVariables]
    public EntityUid shooterEntity;
    //  opreste cheaterii din a trage de mai multe ori
    [ViewVariables]
    public Dictionary<int, Queue<float>> executedFiringSteps = new();
    // total ticks ahead of client due to Wait early ending!
    [ViewVariables]
    public int ticksFoward = 0;
    // total ticks known to need to be executed faster due to message arriving late
    [ViewVariables]
    public int catchupNeeded = 0;
}
