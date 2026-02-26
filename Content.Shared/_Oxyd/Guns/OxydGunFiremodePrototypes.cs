using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Toolshed.TypeParsers;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.OxydGunSystem;
[Prototype]
public sealed partial class GunFiremodePrototype : IPrototype
{
     // TECHNICAL
    [IdDataField]
    public string ID { get; private set; } = default!;
    [ViewVariables]
    public int currentStep = 0;
    [ViewVariables]
    public int maxSteps => Effects.Count;
    // prevent changing fire modes whilst this is true.
    [ViewVariables]
    public bool Active = false;
    // last interpret tick. To not run it multiuple times in the same.
    [ViewVariables]
    public GameTick lastInterpreted = GameTick.Zero;
    [ViewVariables]
    public OxydGunProvidersComponent AmmoProviders = default!;
    // which ammo provider index we pull from(used to set the AmmoProvidersat init)
    [DataField]
    public int providerId = 0;
    // type of ammo provider(same as above)
    [DataField("provider")]
    public string providerComp = "";

    // SPRITE
    [DataField("icon", required: false)]
    public SpriteSpecifier icon = default!;

    // SOUND
    [DataField("fireSound")]
    public SoundSpecifier fireSound = default!;

    // GAME

    // bullets per second
    // bullet sets their own speed , gun can only influence it
    [DataField("firerate")]
    public int FireRate = 60;
    [ViewVariables]
    public TimeSpan nextFire = TimeSpan.Zero;
    [ViewVariables, NonSerialized]
    public TimeSpan firingGaps = TimeSpan.Zero;
    [ViewVariables, NonSerialized]
    public GameTick lastFiredTick = default;

    [ViewVariables, NonSerialized]
    public GameTick lastInterpret = default;
    [ViewVariables]
    public TimeSpan fireDelay => TimeSpan.FromSeconds(1f/FireRate);

    // firemodePrototype specific speed mult
    [DataField]
    public float SpeedMultiplier = 1;
    [ViewVariables]
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

    [DataField("effects"), NonSerialized]
    public List<OxydGunEffect> Effects = new();

    public GunFiremodePrototype createCopy()
    {
        var thing = (GunFiremodePrototype)this.MemberwiseClone();
        thing.Effects = new();
        foreach(var eff in Effects)
        {
            thing.Effects.Add(eff.Clone());
        }

        return thing;
    }

}
