using System.Numerics;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.OxydGunSystem;
[Serializable, NetSerializable]
public abstract class OxydBaseGunFiremode : IPrototype
{
     // TECHNICAL
    [IdDataField]
    public string ID { get; } = default!;
    [NonSerialized]
    public SharedOxydGunSystem _gunSystem;
    [NonSerialized]
    public readonly EntityUid gun;
    private uint _intUpdateTicks = 0;

    public uint UpdateTicks
    {
        get => _intUpdateTicks;
        set
        {
            _intUpdateTicks = value;
            if(_intUpdateTicks != 0)
                _gunSystem.RegisterFiremodeAsActive(gun,  this);
        }
    }

    // prevent changing fire modes whilst this is true.
    public bool Active = false;

    // SPRITE
    [DataField("icon", required: true)]
    public SpriteSpecifier Icon = default!;

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



    public OxydBaseGunFiremode(ref SharedOxydGunSystem gunSys, EntityUid gunId)
    {
        _gunSystem = gunSys;
        gun = gunId;
    }

    public abstract void Tick(float frameTime);
}

[Prototype]
public partial class FiremodeSemi : OxydBaseGunFiremode
{
    public FiremodeSemi(ref SharedOxydGunSystem gunSys, EntityUid gunId) : base(ref gunSys, gunId) {}

    public override void Tick(float frameTime) {}
}
