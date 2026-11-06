using Content.Server.Stack;
using Content.Shared._Oxyd.Economy;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Economy;

/// <summary>Eris ATM against OxydMoneyAccount — Credit deposit/withdraw + PIN login.</summary>
public sealed partial class OxydAtmSystem : EntitySystem
{
    private static readonly ProtoId<StackPrototype> CreditStack = "Credit";

    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private OxydEconomySystem _economy = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private InventorySystem _inventory = default!;

    // Eris ATMs stay logged in until someone presses Logout. Walking away
    // leaves the session open for whoever uses the machine next.
    [SubscribeLocalEvent]
    private void OnOpened(Entity<OxydAtmComponent> ent, ref BoundUIOpenedEvent args)
    {
        PushState(ent);
    }

    [SubscribeLocalEvent]
    private void OnLogin(Entity<OxydAtmComponent> ent, ref OxydAtmLoginMessage args)
    {
        if (!TryLogin(ent, args.AccountNumber, args.Pin, args.Actor, out var err))
            PushState(ent, err ?? "Login failed");
    }

    [SubscribeLocalEvent]
    private void OnLogout(Entity<OxydAtmComponent> ent, ref OxydAtmLogoutMessage args)
    {
        TryLogout(ent);
    }

    [SubscribeLocalEvent]
    private void OnWithdraw(Entity<OxydAtmComponent> ent, ref OxydAtmWithdrawMessage args)
    {
        if (!TryWithdraw(ent, args.Amount, args.Actor, out var err))
            PushState(ent, err ?? "Withdraw failed");
    }

    [SubscribeLocalEvent]
    private void OnTransfer(Entity<OxydAtmComponent> ent, ref OxydAtmTransferMessage args)
    {
        if (!TryTransfer(ent, args.TargetAccount, args.Amount, args.Actor, out var err))
            PushState(ent, err ?? "Transfer failed");
    }

    /// <summary>Toolshed-friendly ATM login (avoids BUI LineEdit).</summary>
    public bool TryLogin(Entity<OxydAtmComponent> ent, int accountNumber, int pin, EntityUid? actor, out string? error)
    {
        error = null;
        if (!_economy.TryLogin(accountNumber, pin, out _, "ATM", actor))
        {
            error = "Login failed";
            PushState(ent, error);
            return false;
        }

        ent.Comp.SessionAccount = accountNumber;
        PushState(ent);
        return true;
    }

    public void TryLogout(Entity<OxydAtmComponent> ent)
    {
        ent.Comp.SessionAccount = null;
        PushState(ent);
    }

    public bool TryWithdraw(Entity<OxydAtmComponent> ent, int amount, EntityUid actor, out string? error)
    {
        error = null;
        if (ent.Comp.SessionAccount is not { } acct || amount <= 0)
        {
            error = "Not logged in";
            PushState(ent, error);
            return false;
        }

        if (!_economy.TryAdjust(acct, -amount, "ATM withdraw", "ATM"))
        {
            error = "Insufficient funds";
            PushState(ent, error);
            return false;
        }

        _stacks.SpawnAtPosition(amount, CreditStack, Transform(ent).Coordinates);
        _popup.PopupEntity($"Dispensed {amount}cr.", ent, actor);
        PushState(ent);
        return true;
    }

    public bool TryTransfer(Entity<OxydAtmComponent> ent, int targetAccount, int amount, EntityUid actor, out string? error)
    {
        error = null;
        if (ent.Comp.SessionAccount is not { } from)
        {
            error = "Not logged in";
            PushState(ent, error);
            return false;
        }

        if (!_economy.TryTransfer(from, targetAccount, amount, "ATM transfer", "ATM", out error))
        {
            error ??= "Transfer failed";
            PushState(ent, error);
            return false;
        }

        PushState(ent);
        return true;
    }

    public bool TryDeposit(Entity<OxydAtmComponent> ent, int amount, out string? error)
    {
        error = null;
        if (ent.Comp.SessionAccount is not { } acct)
        {
            error = "Not logged in";
            return false;
        }

        if (amount <= 0)
        {
            error = "Bad amount";
            return false;
        }

        if (!_economy.TryAdjust(acct, amount, "ATM deposit", "ATM"))
        {
            error = "Deposit failed";
            return false;
        }

        PushState(ent);
        return true;
    }

    [SubscribeLocalEvent]
    private void OnInteractUsing(Entity<OxydAtmComponent> ent, ref InteractUsingEvent args)
    {
        if (!TryComp(args.Used, out StackComponent? stack) || stack.StackTypeId != CreditStack.Id)
            return;

        if (ent.Comp.SessionAccount is not { } acct)
        {
            _popup.PopupEntity("Log in before depositing.", ent, args.User);
            return;
        }

        var value = stack.Count;
        if (!_economy.TryAdjust(acct, value, "ATM deposit", "ATM"))
        {
            _popup.PopupEntity("Deposit failed — account unavailable.", ent, args.User);
            return;
        }

        QueueDel(args.Used);
        _popup.PopupEntity($"Deposited {value}cr.", ent, args.User);
        PushState(ent);
        args.Handled = true;
    }

    private void PushState(Entity<OxydAtmComponent> ent, string? error = null)
    {
        var state = new OxydAtmUiState { Error = error };
        if (ent.Comp.SessionAccount is { } acct && _economy.TryGetAccount(acct, out var data))
        {
            state.LoggedIn = true;
            state.AccountNumber = data.AccountNumber;
            state.OwnerName = data.OwnerName;
            state.Balance = data.Balance;
        }

        foreach (var account in _economy.EnumerateAccounts())
        {
            if (account.Suspended)
                continue;

            state.Accounts.Add(new OxydAtmAccountEntry
            {
                Number = account.AccountNumber,
                Name = string.IsNullOrWhiteSpace(account.OwnerName) ? account.AccountName : account.OwnerName,
            });
        }

        _ui.SetUiState(ent.Owner, OxydAtmUiKey.Key, state);
    }

    private bool TryFindBankId(EntityUid user, out int account)
    {
        account = 0;
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
