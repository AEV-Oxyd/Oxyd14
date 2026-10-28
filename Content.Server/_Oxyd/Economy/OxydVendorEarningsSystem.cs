using Content.Server.VendingMachines;
using Content.Shared._Oxyd.Economy;
using Content.Shared.Emag.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.VendingMachines;
using Content.Shared.VendingMachines.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Economy;

/// <summary>
/// Charges the buyer and credits Club/Service (or linked) account on vend attempt.
/// </summary>
public sealed partial class OxydVendorEarningsSystem : EntitySystem
{
    private static readonly ProtoId<StackPrototype> CreditStack = "Credit";

    [Dependency] private OxydEconomySystem _economy = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStackSystem _stacks = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private VendingMachineSystem _vending = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydVendorEarningsComponent, VendingMachineEjectAttemptEvent>(OnEjectAttempt);
    }

    private void OnEjectAttempt(Entity<OxydVendorEarningsComponent> ent, ref VendingMachineEjectAttemptEvent args)
    {
        var price = ent.Comp.DefaultPrice;
        var dest = ResolveDest(ent.Comp);
        if (dest == 0)
        {
            if (!ent.Comp.RequirePayment)
                return;

            args.Cancelled = true;
            args.Reason = "Vendor has no earnings account.";
            _popup.PopupEntity(args.Reason, ent, args.User);
            return;
        }

        // Only charge when the vend will actually happen — access, stock and the
        // in-progress check all live after this attempt event, so repeat them here.
        if (!VendWillSucceed(ent, args))
            return;

        if (!TryChargeBuyer(args.User, price, out var err))
        {
            if (!ent.Comp.RequirePayment)
                return;

            args.Cancelled = true;
            args.Reason = err ?? "Payment failed.";
            _popup.PopupEntity(args.Reason, ent, args.User);
            return;
        }

        _economy.TryAdjust(dest, price, $"Vendor sale {args.ItemId}", "Vending");
        _popup.PopupEntity($"Charged {price}cr.", ent, args.User);
    }

    /// <summary>Mirror of AuthorizedVend's gates: in-stock entry, not already
    /// ejecting, unbroken, and the user is allowed through the access reader.</summary>
    private bool VendWillSucceed(Entity<OxydVendorEarningsComponent> ent, in VendingMachineEjectAttemptEvent args)
    {
        if (!TryComp<VendingMachineComponent>(ent, out var vend) || vend.Broken)
            return false;

        if (TryComp<VendingMachineEjectComponent>(ent, out var eject) && eject.Ejecting)
            return false;

        Dictionary<string, VendingMachineInventoryEntry>? inv = args.Type switch
        {
            InventoryType.Emagged => HasComp<EmaggedComponent>(ent) ? vend.EmaggedInventory : null,
            InventoryType.Contraband => vend.Contraband ? vend.ContrabandInventory : null,
            _ => vend.Inventory,
        };

        if (inv == null || !inv.TryGetValue(args.ItemId, out var entry) || entry.Amount <= 0)
            return false;

        // Same gate the vending system uses — emagged machines authorize anyone,
        // so those vends still get charged rather than coming out free.
        return _vending.IsAuthorized(ent.Owner, args.User, vend);
    }

    private int ResolveDest(OxydVendorEarningsComponent comp)
    {
        if (comp.LinkedAccountNumber != 0 && _economy.TryGetAccount(comp.LinkedAccountNumber, out _))
            return comp.LinkedAccountNumber;

        _economy.EnsureDepartmentsForRound();
        return _economy.FindDepartmentAccount(comp.DepartmentId)?.AccountNumber ?? 0;
    }

    private bool TryChargeBuyer(EntityUid user, int price, out string? error)
    {
        error = null;

        if (_hands.GetActiveItem(user) is { } held &&
            TryComp<StackComponent>(held, out var stack) &&
            stack.StackTypeId == CreditStack.Id)
        {
            if (stack.Count < price)
            {
                error = $"Need {price}cr in hand.";
                return false;
            }

            if (stack.Count == price)
                QueueDel(held);
            else
                _stacks.SetCount(held, stack.Count - price);
            return true;
        }

        if (TryGetPersonalAccount(user, out var acct))
        {
            if (_economy.TryAdjust(acct, -price, "Vendor purchase", "Vending"))
                return true;

            error = "Insufficient account balance.";
            return false;
        }

        error = "Hold credits or wear a linked ID.";
        return false;
    }

    private bool TryGetPersonalAccount(EntityUid user, out int account)
    {
        account = 0;
        if (TryComp<OxydPersonalAccountComponent>(user, out var personal))
        {
            account = personal.AccountNumber;
            return true;
        }

        if (!_inventory.TryGetSlotEntity(user, "id", out var idUid))
            return false;

        var card = idUid.Value;
        if (TryComp<PdaComponent>(idUid, out var pda) && pda.ContainedId != null)
            card = pda.ContainedId.Value;

        if (!TryComp<OxydBankIdComponent>(card, out var bank))
            return false;

        account = bank.AccountNumber;
        return true;
    }
}
