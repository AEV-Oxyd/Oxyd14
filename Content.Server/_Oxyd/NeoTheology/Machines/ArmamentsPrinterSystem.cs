using Content.Server._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.UI;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// P2.16: armaments printer (Eris <c>datum/armament/purchase</c> + <c>eotp</c>'s armory UI).
/// </summary>
public sealed class ArmamentsPrinterSystem : EntitySystem
{
    [Dependency] private readonly CruciformSystem _cruciform = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ArmamentsPrinterComponent, AfterActivatableUIOpenEvent>(OnUiOpened);
        SubscribeLocalEvent<ArmamentsPrinterComponent, PurchaseArmamentMessage>(OnPurchaseMessage);
    }

    private void OnUiOpened(EntityUid uid, ArmamentsPrinterComponent component, AfterActivatableUIOpenEvent args)
    {
        UpdateUi(uid, component);
    }

    private void OnPurchaseMessage(EntityUid uid, ArmamentsPrinterComponent component, PurchaseArmamentMessage args)
    {
        TryPurchase(uid, args.Actor, args.ArmamentId);
        UpdateUi(uid, component);
    }

    public void UpdateUi(EntityUid uid, ArmamentsPrinterComponent component)
    {
        _ui.SetUiState(uid, ArmamentsPrinterUiKey.Key, BuildState(component));
    }

    private ArmamentsPrinterState BuildState(ArmamentsPrinterComponent component)
    {
        var entries = new List<ArmamentsPrinterEntry>();
        foreach (var armament in ProtoMan.EnumeratePrototypes<ArmamentPrototype>())
        {
            var cost = GetCost(component, armament);
            entries.Add(new ArmamentsPrinterEntry(
                armament.ID,
                Loc.GetString(armament.Name),
                armament.Desc is { } desc ? Loc.GetString(desc) : string.Empty,
                cost,
                component.Points >= cost));
        }

        entries.Sort((left, right) => left.Cost.CompareTo(right.Cost));
        return new ArmamentsPrinterState(component.Points, component.MaxPoints, entries);
    }

    /// <summary>
    /// Eris <c>datum/armament/purchase</c>. Every gate is revalidated here; the BUI message is only
    /// a hint about which armament the player clicked.
    /// </summary>
    public bool TryPurchase(EntityUid printer, EntityUid user, string armamentId)
    {
        if (!TryComp<ArmamentsPrinterComponent>(printer, out var component))
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

        var cost = GetCost(component, armament);
        if (component.Points < cost)
            return false;

        component.Points -= cost;
        component.PurchaseCount[armamentId] = GetPurchaseCount(component, armamentId) + 1;

        if (!component.FirstPurchaseMade)
        {
            component.FirstPurchaseMade = true;
            component.MaxPoints += armament.MaxPointsIncrease;
        }

        SpawnAtPosition(armament.Path, printerXform.Coordinates);
        return true;
    }

    public int GetCost(ArmamentsPrinterComponent component, ArmamentPrototype armament)
    {
        return armament.GetCost(GetDiscount(component, armament));
    }

    private int GetPurchaseCount(ArmamentsPrinterComponent component, string armamentId)
    {
        return component.PurchaseCount.GetValueOrDefault(armamentId);
    }

    private int GetDiscount(ArmamentsPrinterComponent component, ArmamentPrototype armament)
    {
        return armament.GetDiscount(GetPurchaseCount(component, armament.ID));
    }
}
