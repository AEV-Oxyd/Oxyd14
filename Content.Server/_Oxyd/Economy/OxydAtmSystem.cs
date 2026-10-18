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

    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly OxydEconomySystem _economy = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StackSystem _stacks = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydAtmComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<OxydAtmComponent, OxydAtmLoginMessage>(OnLogin);
        SubscribeLocalEvent<OxydAtmComponent, OxydAtmLogoutMessage>(OnLogout);
        SubscribeLocalEvent<OxydAtmComponent, OxydAtmWithdrawMessage>(OnWithdraw);
        SubscribeLocalEvent<OxydAtmComponent, OxydAtmTransferMessage>(OnTransfer);
        SubscribeLocalEvent<OxydAtmComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnOpened(Entity<OxydAtmComponent> ent, ref BoundUIOpenedEvent args)
    {
        // Auto-login from ID if present on the user.
        if (ent.Comp.SessionAccount == null &&
            TryFindBankId(args.Actor, out var acct) &&
            _economy.TryGetAccount(acct, out _))
        {
            // Still require PIN for security level >= 1 — leave logged out until PIN.
        }

        PushState(ent);
    }

    private void OnLogin(Entity<OxydAtmComponent> ent, ref OxydAtmLoginMessage args)
    {
        if (!TryLogin(ent, args.AccountNumber, args.Pin, out var err))
            PushState(ent, err ?? "Login failed");
    }

    private void OnLogout(Entity<OxydAtmComponent> ent, ref OxydAtmLogoutMessage args)
    {
        TryLogout(ent);
    }

    private void OnWithdraw(Entity<OxydAtmComponent> ent, ref OxydAtmWithdrawMessage args)
    {
        if (!TryWithdraw(ent, args.Amount, args.Actor, out var err))
            PushState(ent, err ?? "Withdraw failed");
    }

    private void OnTransfer(Entity<OxydAtmComponent> ent, ref OxydAtmTransferMessage args)
    {
        if (!TryTransfer(ent, args.TargetAccount, args.Amount, out var err))
            PushState(ent, err ?? "Transfer failed");
    }

    /// <summary>Toolshed-friendly ATM login (avoids BUI LineEdit).</summary>
    public bool TryLogin(Entity<OxydAtmComponent> ent, int accountNumber, int pin, out string? error)
    {
        error = null;
        if (!_economy.TryLogin(accountNumber, pin, out _, "ATM"))
        {
            error = "Login failed";
            PushState(ent, error);
            return false;
        }

        ent.Comp.SessionAccount = accountNumber;
        Dirty(ent);
        PushState(ent);
        return true;
    }

    public void TryLogout(Entity<OxydAtmComponent> ent)
    {
        ent.Comp.SessionAccount = null;
        Dirty(ent);
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

    public bool TryTransfer(Entity<OxydAtmComponent> ent, int targetAccount, int amount, out string? error)
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
        QueueDel(args.Used);
        _economy.TryAdjust(acct, value, "ATM deposit", "ATM");
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
