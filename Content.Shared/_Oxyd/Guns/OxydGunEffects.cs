using System.Threading.Tasks;
using JetBrains.Annotations;
using Robust.Shared.Timing;

namespace Content.Shared._Oxyd.OxydGunSystem;

/// <summary>
/// Base class. Actual behaviour is implemented in ClientOxydGunSystem and Server-side version
/// </summary>
[ImplicitDataDefinitionForInheritors]
[MeansImplicitUse]
public abstract partial class OxydGunEffect
{
    private protected string _id => this.GetType().Name;

    public OxydGunEffect Clone()
    {
        return (OxydGunEffect)MemberwiseClone();
    }
}

public abstract partial class OxydFiringGunEffect : OxydGunEffect
{
    [DataField]
    public int shots = 1;

}

public interface OxydResetableEffect
{
    public abstract void Reset();
}

public interface OxydModdableEffect
{
    public void applyMods(CompoundedModifiers mods);
}

public interface OxydImmediateInterpret
{
    public bool shouldInterpretImmediately();
}
public abstract partial class OxydMouseStatusGunEffect : OxydGunEffect, OxydResetableEffect
{
    public bool mouseHeld = false;
    public TimeSpan receivedUpdate = TimeSpan.Zero;
    public TimeSpan validDiff = TimeSpan.FromMilliseconds(250);
    public int updateFromStep = 0;

    public void Reset()
    {
        mouseHeld = false;
        receivedUpdate = TimeSpan.Zero;
        updateFromStep = 0;
    }
}
[DataDefinition]
public sealed partial class GunEffectCheckHandheld : OxydGunEffect;
[DataDefinition]
public sealed partial class GunEffectCheckCuffed : OxydGunEffect;
[DataDefinition]
public sealed partial class GunEffectCheckConscious : OxydGunEffect;
[DataDefinition]
public sealed partial class GunEffectCheckWielded : OxydGunEffect;

[DataDefinition]
public sealed partial class GunEffectCheckAmmo : OxydGunEffect;
[DataDefinition]
public sealed partial class GunEffectWait : OxydGunEffect, OxydResetableEffect, OxydModdableEffect
{
    // x steps to go back if we want to rerun checks
    [DataField]
    public int stepBack = 0;
    [DataField]
    public TimeSpan waitPeriod = TimeSpan.Zero;
    [ViewVariables]
    public TimeSpan alreadyWaited = TimeSpan.Zero;

    [ViewVariables]
    public bool skip = false;
    [ViewVariables]
    public TimeSpan lastNetwork = TimeSpan.Zero;


    public void Reset()
    {
        alreadyWaited = TimeSpan.Zero;
        skip = false;
    }

    public void applyMods(CompoundedModifiers mods)
    {
        waitPeriod = (waitPeriod + mods.waitAdd ) * mods.waitMult;
    }
}
[DataDefinition]
public sealed partial class GunEffectTryFireGunDirection : OxydFiringGunEffect;
[DataDefinition]
public sealed partial class GunEffectTryFireMouseDirection: OxydFiringGunEffect;
[DataDefinition]
public sealed partial class GunEffectRepeat : OxydGunEffect, OxydResetableEffect
{
    [DataField]
    public int repeatCount = 1;
    [DataField]
    public int stepBack = 0;
    [ViewVariables]
    public int timesBack = 0;

    public void Reset()
    {
        timesBack = 0;
    }

}

[DataDefinition]
public sealed partial class GunEffectRepeatMouseHeld : OxydMouseStatusGunEffect, OxydImmediateInterpret
{
    [DataField]
    public int stepBack = 0;

    public bool shouldInterpretImmediately()
    {
        return true;
    }


}

[DataDefinition]
public sealed partial class GunEffectModifyCharge : OxydGunEffect
{
    [DataField]
    public float addAmount = 0;
}

[DataDefinition]
public sealed partial class GunEffectResetCharge : OxydGunEffect;


[DataDefinition]
public sealed partial class GunEffectCheckCharge : OxydGunEffect
{
    [DataField]
    public float min = float.NegativeInfinity;

    [DataField]
    public float max = float.PositiveInfinity;
}



