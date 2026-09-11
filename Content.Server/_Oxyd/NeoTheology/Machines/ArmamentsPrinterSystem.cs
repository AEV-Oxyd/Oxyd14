using Content.Server._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared._Oxyd.NeoTheology.UI;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// P2.16: armaments printer (Eris <c>datum/armament/purchase</c> + <c>eotp</c>'s armory UI).
/// P3.5: points and purchase counters live on the Eye, so the printer only validates the buyer and
/// routes the debit through <see cref="EyeOfTheProtectorSystem.TrySpendArmaments"/>.
/// </summary>
public sealed class ArmamentsPrinterSystem : EntitySystem
{
    [Dependency] private readonly CruciformSystem _cruciform = default!;
    [Dependency] private readonly NeoTheologyMachineSystem _machines = default!;
    [Dependency] private readonly EyeOfTheProtectorSystem _eye = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ArmamentsPrinterComponent, AfterActivatableUIOpenEvent>(OnUiOpened);
        SubscribeLocalEvent<ArmamentsPrinterComponent, PurchaseArmamentMessage>(OnPurchaseMessage);
        SubscribeLocalEvent<ArmamentsPrinterComponent, LitanyOpenArmamentsEvent>(OnLitanyOpenArmaments);
    }

    /// <summary>
    /// OrderArmaments bridge (Eris <c>rituals/priest.dm:492-520</c>): the priest opens the shop
    /// from the machine; Eris opened the EOTP's own armory UI, the fork's shop is this printer
    /// (P2.16), so the handler opens the printer's existing BUI for the caster.
    /// </summary>
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

    private void OnUiOpened(EntityUid uid, ArmamentsPrinterComponent component, AfterActivatableUIOpenEvent args)
    {
        UpdateUi(uid);
    }

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
        var entries = new List<ArmamentsPrinterEntry>();

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
            var cost = eyeComp is not null ? GetCost(eyeComp, armament) : armament.GetCost(0);
            entries.Add(new ArmamentsPrinterEntry(
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

        if (!ProtoMan.TryIndex<ArmamentPrototype>(armamentId, out var armament))
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

        if (_eye.FindEye(printer) is not { } eye ||
            !TryComp<EyeOfTheProtectorComponent>(eye, out var eyeComp))
            return false;

        var cost = GetCost(eyeComp, armament);
        if (!_eye.TrySpendArmaments(eye, cost))
            return false;

        eyeComp.PurchaseCount[armamentId] = GetPurchaseCount(eyeComp, armamentId) + 1;

        if (!eyeComp.FirstPurchaseMade)
        {
            eyeComp.FirstPurchaseMade = true;
            eyeComp.MaxArmamentsPoints += armament.MaxPointsIncrease;
        }

        SpawnAtPosition(armament.Path, printerXform.Coordinates);
        return true;
    }

    public int GetCost(EyeOfTheProtectorComponent component, ArmamentPrototype armament)
    {
        return armament.GetCost(GetDiscount(component, armament));
    }

    private int GetPurchaseCount(EyeOfTheProtectorComponent component, string armamentId)
    {
        return component.PurchaseCount.GetValueOrDefault(armamentId);
    }

    private int GetDiscount(EyeOfTheProtectorComponent component, ArmamentPrototype armament)
    {
        return armament.GetDiscount(GetPurchaseCount(component, armament.ID));
    }
}
