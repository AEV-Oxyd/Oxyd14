using Content.Server._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared._Oxyd.NeoTheology.UI;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// P2.16: armaments printer (Eris <c>datum/armament/purchase</c> + <c>eotp</c>'s armory UI).
/// P3.5: points and purchase counters live on the Eye, so the printer only validates the buyer and
/// routes the debit through <see cref="EyeOfTheProtectorSystem.TrySpendArmaments"/>.
/// </summary>
public sealed partial class ArmamentsPrinterSystem : EntitySystem
{
    [Dependency] private CruciformSystem _cruciform = default!;
    [Dependency] private NeoTheologyMachineSystem _machines = default!;
    [Dependency] private EyeOfTheProtectorSystem _eye = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    /// <summary>
    /// OrderArmaments bridge (Eris <c>rituals/priest.dm:492-520</c>): the priest opens the shop
    /// from the machine; Eris opened the EOTP's own armory UI, the fork's shop is this printer
    /// (P2.16), so the handler opens the printer's existing BUI for the caster.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnEyeOpenArmaments(Entity<EyeOfTheProtectorComponent> eye, ref LitanyOpenArmamentsEvent args)
    {
        if (FindOperationalPrinter(eye.Owner) is not { } printer)
            return;

        var forwarded = args;
        OnLitanyOpenArmaments(printer, Comp<ArmamentsPrinterComponent>(printer), ref forwarded);
        args.Handled = forwarded.Handled;
    }

    /// <summary>How far from the eye a printer may sit and still host its shop.</summary>
    private const float PrinterScanRadius = 25f;

    /// <summary>The nearest operational printer near the eye. The shop UI lives on the printer.</summary>

    private EntityUid? FindOperationalPrinter(EntityUid eye)
    {
        var xform = Transform(eye);
        var origin = _transform.GetWorldPosition(xform);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var printer in _lookup.GetEntitiesInRange<ArmamentsPrinterComponent>(xform.Coordinates, PrinterScanRadius))
        {
            var uid = printer.Owner;
            if (!_machines.IsOperational(uid))
                continue;

            var distance = (_transform.GetWorldPosition(uid) - origin).LengthSquared();
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = uid;
        }

        return best;
    }

    [SubscribeLocalEvent]
    private void OnLitanyOpenArmaments(EntityUid uid, ArmamentsPrinterComponent component, ref LitanyOpenArmamentsEvent args)
    {
        if (!_machines.IsOperational(uid) ||
            !_ui.TryGetInterfaceData(uid, ArmamentsPrinterUiKey.Key, out var data))
            return;

        if (data.RequireInputValidation)
        {
            var attempt = new BoundUserInterfaceMessageAttempt(args.User, uid, ArmamentsPrinterUiKey.Key, new OpenBoundInterfaceMessage());
            RaiseLocalEvent(attempt);
            RaiseLocalEvent(uid, attempt);
            if (attempt.Cancelled)
                return;
        }

        args.Handled = args.ValidateOnly || _ui.TryOpenUi(uid, ArmamentsPrinterUiKey.Key, args.User);
    }

    [SubscribeLocalEvent]
    private void OnUiOpened(EntityUid uid, ArmamentsPrinterComponent component, AfterActivatableUIOpenEvent args)
    {
        UpdateUi(uid);
    }

    /// <summary>
    /// NanoUI auto-refresh equivalent: the open-time state push races the client window and is
    /// dropped, so re-push once a second while the interface stays open (points/debits stay live).
    /// </summary>
    private const float UiRefresh = 1f;
    private float _uiAccum;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _uiAccum += frameTime;
        if (_uiAccum < UiRefresh)
            return;
        _uiAccum = 0f;

        var query = EntityQueryEnumerator<ArmamentsPrinterComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out _, out var ui))
        {
            if (_ui.IsUiOpen((uid, ui), ArmamentsPrinterUiKey.Key))
                UpdateUi(uid);
        }
    }

    [SubscribeLocalEvent]
    private void OnPurchaseMessage(EntityUid uid, ArmamentsPrinterComponent component, PurchaseArmamentMessage args)
    {
        TryPurchase(uid, args.Actor, args.ArmamentId);
        UpdateUi(uid);
    }

    public void UpdateUi(EntityUid uid)
    {
        _ui.SetUiState(uid, ArmamentsPrinterUiKey.Key, BuildState(uid));
    }

    private ArmamentsPrinterState BuildState(EntityUid printer)
    {
        var entries = new List<ArmamentEntry>();

        var points = 0;
        var maxPoints = 0;
        EyeOfTheProtectorComponent? eyeComp = null;
        if (_eye.FindEye(printer) is { } eye)
            TryComp<EyeOfTheProtectorComponent>(eye, out eyeComp);

        if (eyeComp is not null)
        {
            points = eyeComp.ArmamentsPoints;
            maxPoints = eyeComp.MaxArmamentsPoints;
        }

        foreach (var armament in ProtoMan.EnumeratePrototypes<ArmamentPrototype>())
        {
            var cost = eyeComp is not null ? _eye.GetCost(eyeComp, armament) : armament.GetCost(0);
            entries.Add(new ArmamentEntry(
                armament.ID,
                Loc.GetString(armament.Name),
                armament.Desc is { } desc ? Loc.GetString(desc) : string.Empty,
                cost,
                eyeComp is not null && eyeComp.ArmamentsPoints >= cost));
        }

        entries.Sort((left, right) => left.Cost.CompareTo(right.Cost));
        return new ArmamentsPrinterState(points, maxPoints, entries);
    }

    /// <summary>
    /// Eris <c>datum/armament/purchase</c>. Every gate is revalidated here; the BUI message is only
    /// a hint about which armament the player clicked.
    /// </summary>
    public bool TryPurchase(EntityUid printer, EntityUid user, string armamentId)
    {
        if (!_machines.IsOperational(printer) || !TryComp<ArmamentsPrinterComponent>(printer, out var component))
            return false;

        // Eris is_neotheology_disciple: an active cruciform carrying a configured profile.
        if (!_cruciform.TryGetCruciform(user, out _, out var cruciform))
            return false;

        if (_cruciform.GetRules() is not { } rules || !rules.Profiles.Contains(cruciform.Profile))
            return false;

        var printerXform = Transform(printer);
        if (printerXform.MapID != Transform(user).MapID)
            return false;

        var distance = (_transform.GetWorldPosition(printerXform) - _transform.GetWorldPosition(user)).Length();
        if (distance > component.Range)
            return false;

        if (_eye.FindEye(printer) is not { } eye)
            return false;

        if (!_eye.TryBuyArmament(eye, armamentId, out var bought) || bought is null)
            return false;

        SpawnAtPosition(bought.Path, printerXform.Coordinates);
        return true;
    }

    /// <summary>Kept for callers pricing against the Eye's bank; the pricing itself lives there.</summary>
    public int GetCost(EyeOfTheProtectorComponent component, ArmamentPrototype armament)
    {
        return _eye.GetCost(component, armament);
    }
}
