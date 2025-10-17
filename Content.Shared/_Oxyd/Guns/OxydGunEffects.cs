using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Content.Shared._Oxyd.OxydGunSystem;

/// <summary>
/// Base class. Actual behaviour is implemented in ClientOxydGunSystem and Server-side version
/// </summary>
[ImplicitDataDefinitionForInheritors]
[MeansImplicitUse]
public abstract partial class OxydGunEffect
{
    private protected string _id => this.GetType().Name;
}

public sealed partial class GunEffectCheckHandheld : OxydGunEffect;

public sealed partial class GunEffectCheckCuffed : OxydGunEffect;

public sealed partial class GunEffectCheckConscious : OxydGunEffect;

public sealed partial class GunEffectCheckWielded : OxydGunEffect;

public sealed partial class GunEffectWait : OxydGunEffect
{
    public TimeSpan alreadyWaited = TimeSpan.Zero;
    [DataField]
    public TimeSpan waitPeriod = TimeSpan.Zero;
}

public sealed partial class GunEffectTryFireGunDirection : OxydGunEffect;

public sealed partial class GunEffectTryFireMouseDirection: OxydGunEffect;

public sealed partial class GunEffectRepeatNextTick : OxydGunEffect
{
    public int timesBack = 0;
    [DataField]
    public int repeatCount = 1;
}

public sealed partial class GunEffectRepeatNow : OxydGunEffect
{
    public int timesBack = 0;
    [DataField]
    public int repeatCount = 1;
}

public sealed partial class GunEffectRepeatNextTickIfMouseHeld : OxydGunEffect;
