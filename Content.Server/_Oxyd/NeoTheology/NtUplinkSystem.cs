using System.Diagnostics.CodeAnalysis;
using Content.Server.Store.Systems;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.FixedPoint;
using Content.Shared.Implants;
using Content.Shared.Mind;
using Content.Shared.Popups;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// Eris <c>datum/core_module/cruciform/uplink</c> (modules.dm:30-53) and the two inquisitor
/// litanies that use it: Knowledge reads the telecrystal balance, Bounty opens the store
/// (rituals/inquisitor.dm:291-327). The store entity lives in nullspace and its telecrystals
/// are held on the cruciform, so the balance survives a module swap.
/// </summary>
public sealed partial class NtUplinkSystem : EntitySystem
{
    /// <summary>
    /// Spawnable store preset with the uplink catalog and the NeoTheology category.
    /// </summary>
    public static readonly EntProtoId UplinkStore = "OxydNtUplink";

    private static readonly ProtoId<CoreModulePrototype> UplinkModule = "OxydNtModuleUplink";
    private static readonly ProtoId<CurrencyPrototype> Telecrystal = "Telecrystal";

    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StoreSystem _store = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    /// <summary>The module install hook from <see cref="CoreModuleBehaviorSystem"/>.</summary>
    public void OnUplinkInstalled(EntityUid cruciform)
    {
        EnsureComp<NtUplinkComponent>(cruciform);
    }

    /// <summary>
    /// The module uninstall hook: bank the remaining telecrystals on the cruciform and destroy
    /// the store. A later reinstall starts from the banked balance, not a fresh 15.
    /// </summary>
    public void OnUplinkUninstalled(EntityUid cruciform)
    {
        if (!TryComp<NtUplinkComponent>(cruciform, out var uplink))
            return;

        BankBalance(uplink);
    }

    /// <summary>The installed uplink module and its state, or false when the module is absent.</summary>
    public bool TryGetUplink(EntityUid cruciform, [NotNullWhen(true)] out NtUplinkComponent? uplink)
    {
        uplink = null;
        if (!TryComp<CruciformComponent>(cruciform, out var comp) ||
            !comp.InstalledModules.Contains(UplinkModule) ||
            !TryComp<NtUplinkComponent>(cruciform, out var component))
        {
            return false;
        }

        uplink = component;
        return true;
    }

    /// <summary>The live store entity for a cruciform, if one was already created.</summary>
    public bool TryGetStore(EntityUid cruciform, [NotNullWhen(true)] out EntityUid? store)
    {
        store = null;
        if (!TryGetUplink(cruciform, out var uplink) ||
            uplink.Store is not { } storeUid ||
            TerminatingOrDeleted(storeUid) ||
            !HasComp<StoreComponent>(storeUid))
        {
            return false;
        }

        store = storeUid;
        return true;
    }

    /// <summary>Creates the hidden uplink on first use and restores the banked telecrystals.</summary>
    public EntityUid GetOrCreateStore(EntityUid body, EntityUid cruciform, NtUplinkComponent uplink)
    {
        if (uplink.Store is { } existing && !TerminatingOrDeleted(existing) && HasComp<StoreComponent>(existing))
            return existing;

        var storeUid = Spawn(UplinkStore, MapCoordinates.Nullspace);
        var storeComp = EnsureComp<StoreComponent>(storeUid);
        // The hidden uplink is in nullspace, so the default 2 m interaction range would fail the
        // BUI range check. The uplink lives inside the bearer, so range checks do not apply; the
        // BUI subscriber check still limits every message to the interface's actor.
        _ui.SetUi(storeUid, StoreUiKey.Key, new InterfaceData("StoreBoundUserInterface", 0f, false));

        if (_mind.TryGetMind(body, out var mindId, out _))
            storeComp.AccountOwner = mindId;

        storeComp.Balance.Clear();
        _store.TryAddCurrency(new() { { Telecrystal, uplink.StoredTelecrystals } }, storeUid, storeComp);
        uplink.Store = storeUid;
        return storeUid;
    }

    [SubscribeLocalEvent]
    private void OnUplinkShutdown(EntityUid uid, NtUplinkComponent component, ComponentShutdown args)
    {
        BankBalance(component);
        component.Store = null;
    }

    [SubscribeLocalEvent]
    private void OnImplantRemoved(Entity<NtUplinkComponent> ent, ref ImplantRemovedEvent args)
    {
        BankBalance(ent.Comp);
    }

    private void BankBalance(NtUplinkComponent component)
    {
        if (component.Store is not { } storeUid || TerminatingOrDeleted(storeUid))
        {
            component.Store = null;
            return;
        }

        // Revoke access now. Queued deletion alone permits messages until the next tick.
        _ui.CloseUi(storeUid, StoreUiKey.Key);

        if (TryComp<StoreComponent>(storeUid, out var storeComp))
            component.StoredTelecrystals = storeComp.Balance.GetValueOrDefault(Telecrystal);

        QueueDel(storeUid);

        component.Store = null;
    }

    /// <summary>
    /// Knowledge (Eris <c>check_telecrystals</c>): report the remaining telecrystals, or the
    /// absence of an uplink. The validation pass only checks that the module is installed.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnReport(EntityUid body, CruciformBearerComponent bearer, ref LitanyUplinkReportEvent args)
    {
        if (bearer.Cruciform is not { } cruciform || !TryGetUplink(cruciform, out var uplink))
            return;

        args.Handled = true;
        if (args.ValidateOnly)
            return;

        var storeUid = GetOrCreateStore(body, cruciform, uplink);
        if (TryComp<StoreComponent>(storeUid, out var storeComp))
        {
            var count = storeComp.Balance.GetValueOrDefault(Telecrystal);
            _popup.PopupEntity(Loc.GetString("oxyd-litany-knowledge-count", ("count", count)), body, body);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("oxyd-litany-uplink-none"), body, body);
        }
    }

    /// <summary>
    /// Bounty (Eris <c>spawn_item</c>): open the hidden uplink's store interface. The uplink sits
    /// inside the cruciform, so the interface opens from anywhere.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnOpen(EntityUid body, CruciformBearerComponent bearer, ref LitanyUplinkOpenEvent args)
    {
        if (bearer.Cruciform is not { } cruciform || !TryGetUplink(cruciform, out var uplink))
        {
            args.Failure = "oxyd-litany-uplink-none";
            return;
        }

        if (args.ValidateOnly)
        {
            args.Handled = true;
            return;
        }

        var storeUid = GetOrCreateStore(body, cruciform, uplink);
        if (!TryComp<StoreComponent>(storeUid, out var storeComp))
        {
            args.Failure = "oxyd-litany-uplink-none";
            return;
        }

        _ui.OpenUi(storeUid, StoreUiKey.Key, body);
        _store.UpdateUserInterface(body, storeUid, storeComp);
        args.Handled = true;
    }
}
