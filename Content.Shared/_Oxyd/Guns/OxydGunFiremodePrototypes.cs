using System.Numerics;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.OxydGunSystem;
[Serializable, NetSerializable, Prototype]
public partial class GunFiremodePrototype : IPrototype
{
     // TECHNICAL
    [IdDataField]
    public string ID { get; } = default!;
    public int currentStep = 0;
    public int maxSteps = 0;
    // prevent changing fire modes whilst this is true.
    public bool Active = false;

    // SPRITE
    [DataField("icon", required: false)]
    public SpriteSpecifier Icon = default!;

    // GAME

    // bullets per second
    [DataField]
    public int FireRate = 60;
    [DataField]
    public TimeSpan nextFire = TimeSpan.Zero;
    [ViewVariables]
    public TimeSpan fireDelay => TimeSpan.FromSeconds(1f/FireRate);

    // firemodePrototype specific speed mult
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
    [DataField("effects"),NonSerialized]
    public List<OxydGunEffect> Effects = new();

    public static GunFiremodePrototype createCopy(GunFiremodePrototype initial)
    {
        var thing = new GunFiremodePrototype()
        {
            Active = initial.Active,
            addedInaccuracyMaximum = initial.addedInaccuracyMaximum,
            baseInaccuracy = initial.baseInaccuracy,
            currentStep = initial.currentStep,

        };
        foreach(var eff in initial.Effects)
        {
            eff.MemberwiseClone()
        }
    }

}
