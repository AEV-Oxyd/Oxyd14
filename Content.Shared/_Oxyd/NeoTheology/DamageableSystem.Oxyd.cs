using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;

namespace Content.Shared.Damage.Systems;

/// <summary>
/// Oxyd extensions to the shared damage system. <see cref="DamageableComponent.Damage"/> is
/// restricted to this system, so group-aware consumers read a positive copy through here.
/// </summary>
public sealed partial class DamageableSystem
{
    /// <summary>Copies the damage types that currently have a positive value. The caller owns the copy.</summary>
    public DamageSpecifier GetPositiveDamage(DamageableComponent component)
    {
        var result = new DamageSpecifier();
        foreach (var (type, value) in component.Damage.DamageDict)
        {
            if (value > FixedPoint2.Zero)
                result.DamageDict[type] = value;
        }

        return result;
    }

    /// <summary>True when the entity's total damage has reached <paramref name="threshold"/>.</summary>
    public bool IsAtLeastTotalDamage(EntityUid uid, FixedPoint2 threshold)
    {
        return TryComp<DamageableComponent>(uid, out var damageable) && damageable.TotalDamage >= threshold;
    }
}
