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

public interface OxydResetableEffect
{
    public abstract void Reset();
}
public interface  OxydFiringGunEffect
{

}

public abstract partial class OxydMouseStatusGunEffect : OxydGunEffect
{
    public bool mouseHeld = false;
    public TimeSpan receivedUpdate = TimeSpan.Zero;
    public TimeSpan validDiff = TimeSpan.FromSeconds(1);
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
public sealed partial class GunEffectWait : OxydGunEffect, OxydResetableEffect
{
    // x steps to go back if we want to rerun checks
    [DataField]
    public int stepBack = 0;
    [DataField]
    public TimeSpan waitPeriod = TimeSpan.Zero;
    [ViewVariables]
    public TimeSpan alreadyWaited = TimeSpan.Zero;
    [ViewVariables]
    public GameTick skipTick = GameTick.Zero;
    [ViewVariables]
    public TimeSpan lastNetwork = TimeSpan.Zero;

    public void Reset()
    {
        alreadyWaited = TimeSpan.Zero;
        skipTick = GameTick.Zero;
    }
}
[DataDefinition]
public sealed partial class GunEffectTryFireGunDirection : OxydGunEffect,OxydFiringGunEffect;
[DataDefinition]
public sealed partial class GunEffectTryFireMouseDirection: OxydGunEffect,OxydFiringGunEffect;
[DataDefinition]
public sealed partial class GunEffectRepeatNextTick : OxydGunEffect, OxydResetableEffect
{
    [DataField]
    public int repeatCount = 1;
    [DataField]
    public int stepBack = 0;
    [DataField]
    public TimeSpan triggerTimeout = TimeSpan.FromSeconds(1000);
    [ViewVariables]
    public int timesBack = 0;
    [ViewVariables]
    public TimeSpan lastTrigger = TimeSpan.Zero;

    public void Reset()
    {
        timesBack = 0;
        lastTrigger = TimeSpan.Zero;
    }

}

[DataDefinition]
public sealed partial class GunEffectRepeatNextTickIfMouseHeld : OxydMouseStatusGunEffect, OxydResetableEffect
{
    [DataField]
    public int stepBack = 0;

    [ViewVariables]
    public int missedTicks = 0;

    [DataField]
    public int maxMissed = 5;

    public void Reset()
    {
        missedTicks = 0;
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

