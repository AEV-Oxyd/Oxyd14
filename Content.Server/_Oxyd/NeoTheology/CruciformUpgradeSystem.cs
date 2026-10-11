using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared._Oxyd.NeoTheology.Events;
using System.Linq;
using Content.Shared.Interaction;
using Content.Shared.Movement.Systems;
using Robust.Shared.Containers;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// The cruciform's attachment slot. The slot owns where its item lives, the way Eris does:
/// install moves the upgrade inside the cruciform (<c>forceMove(_cruciform)</c>) and uninstall
/// returns it to the bearer's turf (<c>forceMove(get_turf(wearer))</c> — the altar the ritual
/// required). All derived stats live in <see cref="CruciformSystem.RecomputeProfile"/>, which
/// reads the installed upgrade; this system only decides whether the slot can be taken or freed.
/// </summary>
public sealed partial class CruciformUpgradeSystem : EntitySystem
{
    /// <summary>Container inside the cruciform implant that holds the installed attachment.</summary>
    public const string UpgradeContainerId = "cruciform_upgrade";

    [Dependency] private CruciformSystem _cruciform = default!;
    [Dependency] private LitanyEffectSystem _effects = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private CoreModuleSystem _modules = default!;

    [SubscribeLocalEvent]
    private void OnInstallCoreUpgrade(Entity<CruciformCoreUpgradeComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (_cruciform.TryGetCruciformEntity(target, out var implant, out var installed))
            args.Handled = TryInstallCoreUpgrade(implant, installed, ent.Owner, args.User);
        else if (TryComp<CruciformComponent>(target, out var loose))
            args.Handled = TryInstallCoreUpgrade(target, loose, ent.Owner, args.User);
    }

    public bool TryInstallCoreUpgrade(EntityUid cruciform, CruciformComponent comp, EntityUid item, EntityUid? installer = null)
    {
        if (TerminatingOrDeleted(item) || EntityManager.IsQueuedForDeletion(item) ||
            !TryComp<CruciformCoreUpgradeComponent>(item, out var upgrade) ||
            comp.InstalledModules.Contains(upgrade.Module) || comp.CoreUpgrades.ContainsKey(upgrade.Module) ||
            !ProtoMan.HasIndex(upgrade.Module))
            return false;

        var container = _containers.EnsureContainer<Container>(cruciform, "core_upgrades");
        if (!_containers.Insert(item, container))
            return false;

        upgrade.Commander = installer is { } user ? MetaData(user).EntityName : string.Empty;
        comp.CoreUpgrades[upgrade.Module] = item;
        _modules.TryInstall(cruciform, comp, upgrade.Module);
        return true;
    }

    public bool TryRemoveCoreUpgrades(EntityUid cruciform, CruciformComponent comp)
    {
        if (comp.CoreUpgrades.Count == 0)
            return false;

        foreach (var (module, item) in comp.CoreUpgrades.ToArray())
        {
            _modules.TryRemove(cruciform, comp, module);
            // Removing the ascension kit reverses its conversion, not the physical attachment.
            if (module == NeoTheologyPrototypes.PriestConvertModule)
                _cruciform.MakeRank(cruciform, comp, NeoTheologyPrototypes.DiscipleProfile);
            comp.CoreUpgrades.Remove(module);
            QueueDel(item);
        }

        return true;
    }

    /// <summary>
    /// InstallUpgrade bridge: the shared effect cannot see the altar lookup, so it raises
    /// <see cref="LitanyInstallUpgradeEvent"/> on the target. This finds the loose upgrade on the
    /// altar beside them and attaches exactly that item.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnLitanyInstallUpgrade(Entity<CruciformBearerComponent> ent, ref LitanyInstallUpgradeEvent args)
    {
        if (!_cruciform.TryGetCruciformEntity(ent.Owner, out var cruciform, out var component) ||
            component.Upgrade is not null ||
            !_effects.TryFindAltarUpgrade(ent.Owner, out _, out var item))
            return;

        args.Handled = TryInstallUpgrade(cruciform, component, item);
    }

    /// <summary>
    /// UninstallUpgrade bridge: detaches the installed upgrade and returns it to the altar tile.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnLitanyUninstallUpgrade(Entity<CruciformBearerComponent> ent, ref LitanyUninstallUpgradeEvent args)
    {
        if (args.Handled || !_effects.TryGetProcedureAltar(ent.Owner, false, out _, out _) ||
            !_cruciform.TryGetCruciformEntity(ent.Owner, out var cruciform, out var component))
            return;

        args.Handled = TryUninstallUpgrade(cruciform, component);
    }

    public bool TryInstallUpgrade(EntityUid cruciform, CruciformComponent comp, EntityUid upgradeItem)
    {
        if (!TryComp<CruciformUpgradeComponent>(upgradeItem, out _))
            return false;
        if (comp.Upgrade is not null)
            return false;

        // Eris install(): forceMove(_cruciform) — the attachment leaves the altar.
        var container = _containers.EnsureContainer<ContainerSlot>(cruciform, UpgradeContainerId);
        if (!_containers.Insert(upgradeItem, container))
            return false;

        comp.Upgrade = upgradeItem;
        if (comp.ImplantedEntity is { } implanted)
            _cruciform.RefreshUpgradeBehaviors(implanted, comp);
        _cruciform.RecomputeProfile(cruciform, comp);
        RefreshSpeed(comp);
        return true;
    }

    public bool TryUninstallUpgrade(EntityUid cruciform, CruciformComponent comp)
    {
        if (comp.Upgrade is not { } item || !TryComp<CruciformUpgradeComponent>(item, out _))
            return false;

        comp.Upgrade = null;
        if (comp.ImplantedEntity is { } implanted)
            _cruciform.RefreshUpgradeBehaviors(implanted, comp);
        _cruciform.RecomputeProfile(cruciform, comp);
        RefreshSpeed(comp);

        // Eris uninstall(): forceMove(get_turf(wearer)) — right back onto the altar tile.
        var destination = (comp.ImplantedEntity is { } body ? Transform(body) : Transform(cruciform)).Coordinates;
        if (_containers.TryGetContainer(cruciform, UpgradeContainerId, out var container) &&
            container.Contains(item))
        {
            _containers.Remove(item, container, destination: destination);
        }

        return true;
    }

    /// <summary>Applies or removes the movement-speed behaviour the moment the slot changes.</summary>
    private void RefreshSpeed(CruciformComponent comp)
    {
        if (comp.ImplantedEntity is { } body)
            _movement.RefreshMovementSpeedModifiers(body);
    }
}
