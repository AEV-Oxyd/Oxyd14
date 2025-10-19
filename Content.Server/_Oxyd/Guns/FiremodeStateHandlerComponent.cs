using Robust.Shared.Network;

namespace Content.Server._Oxyd.Guns;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class FiremodeStateHandlerComponent : Component
{
    public NetUserId shooterNetworkId;
    public EntityUid shooterEntity;
    //  opreste cheaterii din a trage de mai multe ori
    public HashSet<int> executedFiringSteps;
    public bool fullCycle = false;
}
