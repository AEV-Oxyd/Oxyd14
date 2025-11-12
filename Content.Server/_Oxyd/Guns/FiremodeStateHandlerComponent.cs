using Robust.Shared.Network;

namespace Content.Server._Oxyd.Guns;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class FiremodeStateHandlerComponent : Component
{
    [ViewVariables]
    public NetUserId shooterNetworkId;
    [ViewVariables]
    public EntityUid shooterEntity;
    //  opreste cheaterii din a trage de mai multe ori
    [ViewVariables]
    public HashSet<int> executedFiringSteps = new();
}
