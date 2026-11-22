using Content.Shared.Damage;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// Eris <c>upgrades.dm</c> installed-upgrade behaviour that the flat deltas on
/// <see cref="CruciformUpgradeComponent"/> cannot carry. The server ticks the aura while the
/// item is installed; installing or removing the item is the only state change.
/// </summary>
[RegisterComponent]
public sealed partial class CruciformUpgradeAuraComponent : Component
{
    /// <summary>Eris processes trays and faithful in <c>oviewers(5, wearer)</c>.</summary>
    [DataField] public float Radius = 5f;

    /// <summary>Heal a faithful only when the matching loss is above this value (Eris 50).</summary>
    [DataField] public float HealThreshold = 50f;

    [DataField] public float BruteHealPerSecond = 0.2f;
    [DataField] public float BurnHealPerSecond = 0.2f;

    [DataField] public float PlantHealPerSecond = 0.1f;
    [DataField] public float WeedReducePerSecond = 0.1f;

    /// <summary>Eris cleansing_presence also wipes blood off the bearer's own tile.</summary>
    [DataField] public bool CleanPuddles;

}

/// <summary>
/// Marks a bearer whose installed upgrade is <see cref="CruciformUpgradeMartyrComponent"/>.
/// The death event drives the burst; no per-tick scan is needed.
/// </summary>
[RegisterComponent]
public sealed partial class CruciformMartyrArmedComponent : Component
{
}

/// <summary>
/// Eris <c>martyr_gift</c>: when the bearer dies the item bursts, burning every non-faithful
/// creature around them, then destroys itself. The cruciform survives.
/// </summary>
[RegisterComponent]
public sealed partial class CruciformUpgradeMartyrComponent : Component
{
    /// <summary>Eris iterates <c>oviewers(6, src)</c>.</summary>
    [DataField] public float Radius = 6f;

    /// <summary>Eris <c>martyr.burn_damage</c>; the burst divides this value by the distance.</summary>
    [DataField] public DamageSpecifier BurstDamage = new()
    {
        DamageDict = { ["Heat"] = 50f },
    };
}

/// <summary>Eris <c>speed_of_the_chosen</c>: the bearer moves faster while the item is installed.</summary>
[RegisterComponent]
public sealed partial class CruciformUpgradeSpeedComponent : Component
{
    /// <summary>Eris subtracts 0.5 from the movement delay tally, so 2x is the faithful reading.</summary>
    [DataField] public float WalkMultiplier = 2f;

    [DataField] public float SprintMultiplier = 2f;
}

/// <summary>Eris <c>wrath_of_god</c>: the bearer deals more melee damage while the item is installed.</summary>
[RegisterComponent]
public sealed partial class CruciformUpgradeMeleeComponent : Component
{
    /// <summary>Eris <c>damage_multiplier</c> 0.2 means the bearer deals 20% more damage.</summary>
    [DataField] public float BonusMultiplier = 0.2f;
}
