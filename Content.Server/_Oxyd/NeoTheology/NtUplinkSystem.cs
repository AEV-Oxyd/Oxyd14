using System.Diagnostics.CodeAnalysis;
using Content.Server.Store.Systems;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.FixedPoint;
using Content.Shared.Implants;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.UserInterface;
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
/// (rituals/inquisitor.dm:291-327). The store BUI lives on the cruciform itself and its
/// telecrystals are held there, so the balance survives a module swap.
/// </summary>
public sealed partial class NtUplinkSystem : EntitySystem
{
    /// <summary>
    /// Store preset prototype copied onto the cruciform on first uplink use: the uplink
    /// catalog plus the NeoTheology category.
    /// </summary>
    public static readonly EntProtoId UplinkStore = "OxydNtUplink";

    private static readonly ProtoId<CoreModulePrototype> UplinkModule = "OxydNtModuleUplink";
    private static readonly ProtoId<CurrencyPrototype> Telecrystal = "Telecrystal";

    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private StoreSystem _store = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

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

        if (!comp.Active || comp.ImplantedEntity is not { } body ||
            !TryComp<CruciformBearerComponent>(body, out var bearer) || bearer.Cruciform != cruciform)
            return false;
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

    /// <summary>
    /// Hosts the hidden uplink store on the cruciform itself — a real entity inside the
    /// bearer — copying the <c>OxydNtUplink</c> preset onto it on first use and restoring
    /// the banked telecrystals.
    /// </summary>
    public EntityUid GetOrCreateStore(EntityUid body, EntityUid cruciform, NtUplinkComponent uplink)
    {
        if (uplink.Store is { } existing && !TerminatingOrDeleted(existing) && HasComp<StoreComponent>(existing))
            return existing;

        var storeComp = EnsureComp<StoreComponent>(cruciform);
        if (_prototypes.Index(UplinkStore).TryGetComponent<StoreComponent>(out var preset, EntityManager.ComponentFactory))
        {
            storeComp.Name = preset.Name;
            storeComp.Categories = new HashSet<ProtoId<StoreCategoryPrototype>>(preset.Categories);
            storeComp.CurrencyWhitelist = new HashSet<ProtoId<CurrencyPrototype>>(preset.CurrencyWhitelist);
        }
        _ui.SetUi(cruciform, StoreUiKey.Key, new InterfaceData("StoreBoundUserInterface", 0f, false));

        if (_mind.TryGetMind(body, out var mindId, out _))
            storeComp.AccountOwner = mindId;

        storeComp.Balance.Clear();
        _store.TryAddCurrency(new() { { Telecrystal, uplink.StoredTelecrystals } }, cruciform, storeComp);
        uplink.Store = cruciform;
        return cruciform;
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

    [SubscribeLocalEvent]
    private void OnActivityChanged(Entity<NtUplinkComponent> ent, ref CruciformActivityChangedEvent args)
    {
        if (!args.Active)
            BankBalance(ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnMindRemoved(Entity<CruciformBearerComponent> ent, ref MindRemovedMessage args)
    {
        if (ent.Comp.Cruciform is { } implant && TryComp<NtUplinkComponent>(implant, out var uplink))
            BankBalance(uplink);
    }

    [SubscribeLocalEvent]
    private void OnStoreMessageAttempt(Entity<StoreComponent> ent, ref BoundUserInterfaceMessageAttempt args)
    {
        var valid = TryComp<CruciformBearerComponent>(args.Actor, out var bearer) &&
            bearer.Cruciform is { } implant && TryGetUplink(implant, out var uplink) && uplink.Store == ent.Owner &&
            _mind.TryGetMind(args.Actor, out var mind, out _) && ent.Comp.AccountOwner == mind;
        if (!valid)
            args.Cancel();
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

        _ui.TryOpenUi(storeUid, StoreUiKey.Key, body);
        _store.UpdateUserInterface(body, storeUid, storeComp);
        args.Handled = true;
    }
}
