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
    [ViewVariables]
    public OxydGunProvidersComponent AmmoProviders = default!;
    // which ammo provider index we pull from(used to set the AmmoProvidersat init)
    [DataField]
    public int providerId = 0;
    // type of ammo provider(same as above)
    [DataField("provider")]
    public string providerComp = "";
    [ViewVariables]
    public TimeSpan totalWait = TimeSpan.Zero;

    [ViewVariables, NonSerialized]
    public TimeSpan nextInterpret = TimeSpan.Zero;
    [ViewVariables, NonSerialized]
    // the time budget we receive ( = tickTime + losses due to networkng/ skipped ticks on client)
    public TimeSpan timeBudget = TimeSpan.Zero;
    // how much budget was spent so far in this tick
    public TimeSpan spentBudget = TimeSpan.Zero;
    [ViewVariables, NonSerialized]
    public TimeSpan lastInterpret = TimeSpan.Zero;


    // SPRITE
    [DataField("icon", required: false)]
    public SpriteSpecifier icon = default!;

    // SOUND
    [DataField("fireSound")]
    public SoundSpecifier fireSound = default!;

    // GAME
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

    [DataField("effects")]
    public List<OxydGunEffect> Effects = new();

    [DataField(required: false)]
    public GunFiremodePrototype? cleanClone = null;

    public void Initialize()
    {
        cleanClone = createCopy();
    }


    public GunFiremodePrototype createCopy()
    {
        var thing = (GunFiremodePrototype)this.MemberwiseClone();
        thing.Effects = new();
        foreach(var eff in Effects)
        {
            thing.Effects.Add(eff.Clone());
        }
        thing.totalWait = SharedOxydGunSystem.getTotalWait(this);
        thing.cleanClone = cleanClone;
        return thing;
    }

    public void ApplyMods(CompoundedModifiers mods)
    {
        if (cleanClone is null)
        {
            throw new InvalidOperationException("Firemode prototype has no clean clone, cannot apply mods");
        }
        providerComp = cleanClone.providerComp;
        providerId = cleanClone.providerId;
        SpeedMultiplier = cleanClone.SpeedMultiplier * mods.speedMult;
        baseInaccuracy = cleanClone.baseInaccuracy;
        addedInaccuracyMaximum = ( cleanClone.addedInaccuracyMaximum + mods.accuracyAdd ) * mods.accuracyMult;
        Effects.Clear();
        foreach (var eff in cleanClone.Effects)
        {
            var cl = eff.Clone();
            Effects.Add(cl);
            if (cl is OxydModdableEffect casted)
            {
                casted.applyMods(mods);
            }
        }
        totalWait = SharedOxydGunSystem.getTotalWait(this);
    }

}
