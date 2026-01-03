using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Map;


namespace Content.Shared._Oxyd.OxydGunSystem;


/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydProjectileComponent : Component
{
    // the gun/entity that actually sents out the bullet
    public EntityUid firedFrom;
    // whoever pulled the trigger to cause it to fire.
    public EntityUid shotBy;
    // entities we won't hit
    public HashSet<EntityUid> ignoring = new();
    // where in the world was this initially aimed at
    public MapCoordinates aimedPosition;
    // initial movement to apply when firing
    public Vector2 initialMovement;
    // initial pos to fire from on tick
    [AutoNetworkedField]
    public MapCoordinates initialPosition = MapCoordinates.Nullspace;


}
