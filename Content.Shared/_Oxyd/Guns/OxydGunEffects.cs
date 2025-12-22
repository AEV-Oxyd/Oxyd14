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
public sealed partial class GunEffectWait : OxydGunEffect
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
}
[DataDefinition]
public sealed partial class GunEffectTryFireGunDirection : OxydFiringGunEffect;
[DataDefinition]
public sealed partial class GunEffectTryFireMouseDirection: OxydFiringGunEffect;
[DataDefinition]
public sealed partial class GunEffectRepeatNextTick : OxydGunEffect
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

}
[DataDefinition]
public sealed partial class GunEffectRepeatNextTickIfMouseHeld : OxydGunEffect;
