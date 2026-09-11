using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared._Oxyd.NeoTheology.Events;
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

    [Dependency] private readonly CruciformSystem _cruciform = default!;
    [Dependency] private readonly LitanyEffectSystem _effects = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CruciformBearerComponent, LitanyInstallUpgradeEvent>(OnLitanyInstallUpgrade);
        SubscribeLocalEvent<CruciformBearerComponent, LitanyUninstallUpgradeEvent>(OnLitanyUninstallUpgrade);
    }

    /// <summary>
    /// InstallUpgrade bridge: the shared effect cannot see the altar lookup, so it raises
    /// <see cref="LitanyInstallUpgradeEvent"/> on the target. This finds the loose upgrade on the
    /// altar beside them and attaches exactly that item.
    /// </summary>
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
    private void OnLitanyUninstallUpgrade(Entity<CruciformBearerComponent> ent, ref LitanyUninstallUpgradeEvent args)
    {
        if (!_cruciform.TryGetCruciformEntity(ent.Owner, out var cruciform, out var component))
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
        _cruciform.RecomputeProfile(cruciform, comp);
        RefreshSpeed(comp);
        return true;
    }

    public bool TryUninstallUpgrade(EntityUid cruciform, CruciformComponent comp)
    {
        if (comp.Upgrade is not { } item || !TryComp<CruciformUpgradeComponent>(item, out _))
            return false;

        comp.Upgrade = null;
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
