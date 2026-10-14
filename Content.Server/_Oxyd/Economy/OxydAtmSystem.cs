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
        if (!_economy.TryLogin(args.AccountNumber, args.Pin, out _, "ATM"))
        {
            PushState(ent, "Login failed");
            return;
        }

        ent.Comp.SessionAccount = args.AccountNumber;
        Dirty(ent);
        PushState(ent);
    }

    private void OnLogout(Entity<OxydAtmComponent> ent, ref OxydAtmLogoutMessage args)
    {
        ent.Comp.SessionAccount = null;
        Dirty(ent);
        PushState(ent);
    }

    private void OnWithdraw(Entity<OxydAtmComponent> ent, ref OxydAtmWithdrawMessage args)
    {
        if (ent.Comp.SessionAccount is not { } acct || args.Amount <= 0)
        {
            PushState(ent, "Not logged in");
            return;
        }

        if (!_economy.TryAdjust(acct, -args.Amount, "ATM withdraw", "ATM"))
        {
            PushState(ent, "Insufficient funds");
            return;
        }

        _stacks.SpawnAtPosition(args.Amount, CreditStack, Transform(ent).Coordinates);
        _popup.PopupEntity($"Dispensed {args.Amount}cr.", ent, args.Actor);
        PushState(ent);
    }

    private void OnTransfer(Entity<OxydAtmComponent> ent, ref OxydAtmTransferMessage args)
    {
        if (ent.Comp.SessionAccount is not { } from)
        {
            PushState(ent, "Not logged in");
            return;
        }

        if (!_economy.TryTransfer(from, args.TargetAccount, args.Amount, "ATM transfer", "ATM", out var err))
        {
            PushState(ent, err ?? "Transfer failed");
            return;
        }

        PushState(ent);
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
