using Content.Server.Stack;
using Content.Shared._Oxyd.Economy;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Economy;

/// <summary>
/// Cash register, EFTPOS, tip jar — Eris interaction-driven POS against OxydMoneyAccount / Credit.
/// </summary>
public sealed partial class OxydPosDevicesSystem : EntitySystem
{
    private static readonly ProtoId<StackPrototype> CreditStack = "Credit";

    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly OxydEconomySystem _economy = default!;
    [Dependency] private readonly StackSystem _stacks = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydCashRegisterComponent, InteractHandEvent>(OnRegisterHand);
        SubscribeLocalEvent<OxydCashRegisterComponent, InteractUsingEvent>(OnRegisterUsing);
        SubscribeLocalEvent<OxydCashRegisterComponent, ExaminedEvent>(OnRegisterExamined);
        SubscribeLocalEvent<OxydCashRegisterComponent, GetVerbsEvent<AlternativeVerb>>(OnRegisterVerbs);

        SubscribeLocalEvent<OxydEftposComponent, UseInHandEvent>(OnEftposUse);
        SubscribeLocalEvent<OxydEftposComponent, InteractUsingEvent>(OnEftposUsing);
        SubscribeLocalEvent<OxydEftposComponent, ExaminedEvent>(OnEftposExamined);

        SubscribeLocalEvent<OxydTipJarComponent, InteractUsingEvent>(OnTipUsing);
        SubscribeLocalEvent<OxydTipJarComponent, InteractHandEvent>(OnTipHand);
        SubscribeLocalEvent<OxydTipJarComponent, ExaminedEvent>(OnTipExamined);
    }

    private int ResolveLinkedAccount(int linked, string departmentId)
    {
        if (linked != 0 && _economy.TryGetAccount(linked, out _))
            return linked;

        _economy.EnsureDepartmentsForRound();
        return _economy.FindDepartmentAccount(departmentId)?.AccountNumber ?? 0;
    }

    private void OnRegisterHand(Entity<OxydCashRegisterComponent> ent, ref InteractHandEvent args)
    {
        ent.Comp.DrawerOpen = !ent.Comp.DrawerOpen;
        Dirty(ent);
        _popup.PopupEntity(ent.Comp.DrawerOpen
                ? $"You open the cash drawer — {ent.Comp.Stored}cr inside."
                : "You close the cash drawer.",
            ent, args.User);
        args.Handled = true;
    }

    private void OnRegisterUsing(Entity<OxydCashRegisterComponent> ent, ref InteractUsingEvent args)
    {
        if (!TryComp(args.Used, out StackComponent? stack) || stack.StackTypeId != CreditStack.Id)
            return;

        if (!ent.Comp.DrawerOpen)
        {
            _popup.PopupEntity("The drawer is closed.", ent, args.User);
            return;
        }

        ent.Comp.Stored += stack.Count;
        Dirty(ent);
        _popup.PopupEntity($"You drop {stack.Count}cr into the till.", ent, args.User);
        QueueDel(args.Used);
        args.Handled = true;
    }

    private void OnRegisterExamined(Entity<OxydCashRegisterComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(ent.Comp.DrawerOpen
            ? $"The drawer is open — {ent.Comp.Stored}cr inside."
            : "The drawer is closed.");
        var linked = ResolveLinkedAccount(ent.Comp.LinkedAccountNumber, ent.Comp.DepartmentId);
        if (linked != 0)
            args.PushMarkup($"Linked account #{linked} ({ent.Comp.DepartmentId}).");
    }

    private void OnRegisterVerbs(Entity<OxydCashRegisterComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = "Bank till",
            Act = () => TryBankTill(ent, user),
        });
    }

    /// <summary>Bank till contents into the linked department account. Used by verbs and Toolshed QA.</summary>
    public bool TryBankTill(Entity<OxydCashRegisterComponent> ent, EntityUid user, out string? error)
    {
        error = null;
        if (ent.Comp.Stored <= 0)
        {
            error = "The till is empty.";
            _popup.PopupEntity(error, ent, user);
            return false;
        }

        var acct = ResolveLinkedAccount(ent.Comp.LinkedAccountNumber, ent.Comp.DepartmentId);
        if (acct == 0)
        {
            error = "No linked account.";
            _popup.PopupEntity(error, ent, user);
            return false;
        }

        var amount = ent.Comp.Stored;
        if (!_economy.TryAdjust(acct, amount, "Cash register bank", "CashRegister"))
        {
            error = "Could not credit linked account.";
            _popup.PopupEntity(error, ent, user);
            return false;
        }

        ent.Comp.Stored = 0;
        Dirty(ent);
        _popup.PopupEntity($"Banked {amount}cr to account #{acct}.", ent, user);
        return true;
    }

    private void TryBankTill(Entity<OxydCashRegisterComponent> ent, EntityUid user)
        => TryBankTill(ent, user, out _);

    /// <summary>Arm EFTPOS charge amount for the next swipe (Toolshed / verb parity).</summary>
    public void ArmEftpos(Entity<OxydEftposComponent> ent, int charge)
    {
        ent.Comp.Charge = Math.Max(0, charge);
        ent.Comp.Charging = ent.Comp.Charge > 0;
        Dirty(ent);
    }

    /// <summary>Charge <paramref name="payer"/> account into the EFTPOS linked destination.</summary>
    public bool TryChargeEftpos(Entity<OxydEftposComponent> ent, int payer, out string? error)
    {
        error = null;
        if (!ent.Comp.Charging || ent.Comp.Charge <= 0)
        {
            error = "EFTPOS idle";
            return false;
        }

        var dest = ResolveLinkedAccount(ent.Comp.LinkedAccountNumber, ent.Comp.DepartmentId);
        if (dest == 0)
        {
            error = "EFTPOS has no linked account.";
            return false;
        }

        if (!_economy.TryTransfer(payer, dest, ent.Comp.Charge, "EFTPOS charge", "EFTPOS", out error))
            return false;

        ClearCharge(ent);
        return true;
    }

    private void OnEftposUse(Entity<OxydEftposComponent> ent, ref UseInHandEvent args)
    {
        ent.Comp.Charge = ent.Comp.Charge switch { 0 => 100, 100 => 250, 250 => 500, _ => 0 };
        ent.Comp.Charging = ent.Comp.Charge > 0;
        Dirty(ent);
        _popup.PopupEntity(ent.Comp.Charging
                ? $"EFTPOS armed — charging {ent.Comp.Charge}cr on next swipe."
                : "EFTPOS idle.",
            ent, args.User);
        args.Handled = true;
    }

    private void OnEftposUsing(Entity<OxydEftposComponent> ent, ref InteractUsingEvent args)
    {
        if (!ent.Comp.Charging || ent.Comp.Charge <= 0)
            return;

        var dest = ResolveLinkedAccount(ent.Comp.LinkedAccountNumber, ent.Comp.DepartmentId);
        if (dest == 0)
        {
            _popup.PopupEntity("EFTPOS has no linked account.", ent, args.User);
            return;
        }

        // Pay with cash stack
        if (TryComp(args.Used, out StackComponent? stack) && stack.StackTypeId == CreditStack.Id)
        {
            if (stack.Count < ent.Comp.Charge)
            {
                _popup.PopupEntity($"Insufficient credits — need {ent.Comp.Charge}cr.", ent, args.User);
                return;
            }

            if (stack.Count == ent.Comp.Charge)
                QueueDel(args.Used);
            else
                _stacks.SetCount(args.Used, stack.Count - ent.Comp.Charge);

            _economy.TryAdjust(dest, ent.Comp.Charge, "EFTPOS charge", "EFTPOS");
            _popup.PopupEntity($"Charged {ent.Comp.Charge}cr.", ent, args.User);
            ClearCharge(ent);
            args.Handled = true;
            return;
        }

        // Pay with ID-linked personal account (card or PDA)
        if (TryGetAccountFromItem(args.Used, out var payer) || TryGetAccountFromItem(args.User, out payer))
        {
            if (!_economy.TryTransfer(payer, dest, ent.Comp.Charge, "EFTPOS charge", "EFTPOS", out var err))
            {
                _popup.PopupEntity(err ?? "Charge failed.", ent, args.User);
                return;
            }

            _popup.PopupEntity($"Charged {ent.Comp.Charge}cr from account #{payer}.", ent, args.User);
            ClearCharge(ent);
            args.Handled = true;
        }
    }

    private void ClearCharge(Entity<OxydEftposComponent> ent)
    {
        ent.Comp.Charge = 0;
        ent.Comp.Charging = false;
        Dirty(ent);
    }

    private void OnEftposExamined(Entity<OxydEftposComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(ent.Comp.Charging
            ? $"Charging {ent.Comp.Charge}cr — swipe credits or ID to pay."
            : "Idle — activate in hand to set a charge.");
    }

    private void OnTipUsing(Entity<OxydTipJarComponent> ent, ref InteractUsingEvent args)
    {
        if (!TryComp(args.Used, out StackComponent? stack) || stack.StackTypeId != CreditStack.Id)
            return;

        ent.Comp.Stored += stack.Count;
        Dirty(ent);
        _popup.PopupEntity($"You tip {stack.Count}cr.", ent, args.User);
        QueueDel(args.Used);
        args.Handled = true;
    }

    private void OnTipHand(Entity<OxydTipJarComponent> ent, ref InteractHandEvent args)
    {
        if (ent.Comp.Stored <= 0)
        {
            _popup.PopupEntity("The tip jar is empty.", ent, args.User);
            return;
        }

        var amount = ent.Comp.Stored;
        ent.Comp.Stored = 0;
        Dirty(ent);
        _stacks.SpawnAtPosition(amount, CreditStack, Transform(ent).Coordinates);
        _popup.PopupEntity($"You empty {amount}cr from the tip jar.", ent, args.User);
        args.Handled = true;
    }

    private void OnTipExamined(Entity<OxydTipJarComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(ent.Comp.Stored > 0
            ? $"A tip jar with about {ent.Comp.Stored}cr inside."
            : "An empty tip jar.");
    }

    private bool TryGetAccountFromItem(EntityUid itemOrUser, out int account)
    {
        account = 0;
        if (TryComp<OxydBankIdComponent>(itemOrUser, out var bank))
        {
            account = bank.AccountNumber;
            return true;
        }

        if (TryComp<OxydPersonalAccountComponent>(itemOrUser, out var personal))
        {
            account = personal.AccountNumber;
            return true;
        }

        if (TryComp<PdaComponent>(itemOrUser, out var pda) && pda.ContainedId is { } id &&
            TryComp<OxydBankIdComponent>(id, out bank))
        {
            account = bank.AccountNumber;
            return true;
        }

        if (_inventory.TryGetSlotEntity(itemOrUser, "id", out var slot) &&
            TryComp<PdaComponent>(slot, out pda) && pda.ContainedId is { } card &&
            TryComp<OxydBankIdComponent>(card, out bank))
        {
            account = bank.AccountNumber;
            return true;
        }

        if (_inventory.TryGetSlotEntity(itemOrUser, "id", out slot) &&
            TryComp<OxydBankIdComponent>(slot, out bank))
        {
            account = bank.AccountNumber;
            return true;
        }

        return false;
    }
}
