using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Medical;

/// <summary>Damage-type sets matching the stock damageGroup prototypes (Brute/Burn/Toxin/
/// Airloss), used for display grouping without touching the obsolete group prototype.</summary>
public static class OxydDamageTypes
{
    public static readonly HashSet<ProtoId<DamageTypePrototype>> Brute = new()
        { "Blunt", "Slash", "Piercing" };

    public static readonly HashSet<ProtoId<DamageTypePrototype>> Burn = new()
        { "Heat", "Shock", "Cold", "Caustic" };

    public static readonly HashSet<ProtoId<DamageTypePrototype>> Toxin = new()
        { "Poison", "Radiation" };

    public static readonly HashSet<ProtoId<DamageTypePrototype>> Airloss = new()
        { "Asphyxiation", "Bloodloss" };

    /// <summary>Sum of all damage amounts whose type is in <paramref name="types"/>.</summary>
    public static float Sum(DamageSpecifier spec, HashSet<ProtoId<DamageTypePrototype>> types)
    {
        var total = 0f;
        foreach (var (type, amount) in spec.DamageDict)
        {
            if (types.Contains(type))
                total += amount.Float();
        }
        return total;
    }
}
