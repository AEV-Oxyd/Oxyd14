using System.Numerics;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.OxydGunSystem;
[Serializable, NetSerializable, Prototype, DataDefinition]
public abstract class OxydBaseGunFiremode : IPrototype
{
     // TECHNICAL
    [IdDataField]
    public string ID { get; } = default!;

    public SharedOxydGunSystem _gunSystem;
    public int UpdateTicks = 0;
    // prevent changing fire modes whilst this is true.
    public bool Active = false;

    // GAME

    // bullets per second
    [DataField]
    public int FireRate = 60;
    [DataField]
    public TimeSpan nextFire = TimeSpan.Zero;
    [ViewVariables]
    public TimeSpan fireDelay => TimeSpan.FromSeconds(1f/FireRate);

    // firemode specific speed mult
    [DataField]
    public float SpeedMultiplier = 1;

    // firing positions handled here.
    public int shootingPosIndex = 0;
    [DataField]
    public List<Vector2> shootingPosOffsets = new List<Vector2>();

    [DataField]
    // Always added inaccuracy
    public Angle baseInaccuracy = Angle.Zero;
    [DataField]
    // Added depending on chance, from 0 to the value
    public Angle addedInaccuracyMaximum = Angle.FromDegrees(10);



    public OxydBaseGunFiremode(ref SharedOxydGunSystem gun)
    {
        _gunSystem = gun;
    }

    public TimeSpan getFireDelay()
    {
        return TimeSpan.Zero;
    }

    public void Tick(float gameTime)
    {

    }
}
