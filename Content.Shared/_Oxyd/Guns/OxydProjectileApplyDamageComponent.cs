using Content.Shared.Damage;

namespace Content.Shared._Oxyd.OxydGunSystem;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class OxydProjectileApplyDamageComponent : Component
{
    [DataField("damage")]
    public DamageSpecifier DamageSpecifier = default!;
}
