using Content.Shared._Oxyd.NeoTheology.Components;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// The cruciform's attachment slot. All derived stats live in
/// <see cref="CruciformSystem.RecomputeProfile"/>, which reads the installed upgrade —
/// this system only decides whether the slot can be taken or freed.
/// </summary>
public sealed partial class CruciformUpgradeSystem : EntitySystem
{
    [Dependency] private readonly CruciformSystem _cruciform = default!;

    public bool TryInstallUpgrade(EntityUid cruciform, CruciformComponent comp, EntityUid upgradeItem)
    {
        if (!TryComp<CruciformUpgradeComponent>(upgradeItem, out _))
            return false;
        if (comp.Upgrade is not null)
            return false;

        comp.Upgrade = upgradeItem;
        _cruciform.RecomputeProfile(cruciform, comp);
        return true;
    }

    public bool TryUninstallUpgrade(EntityUid cruciform, CruciformComponent comp)
    {
        if (comp.Upgrade is not { } item || !TryComp<CruciformUpgradeComponent>(item, out _))
            return false;

        comp.Upgrade = null;
        _cruciform.RecomputeProfile(cruciform, comp);
        return true;
    }
}
