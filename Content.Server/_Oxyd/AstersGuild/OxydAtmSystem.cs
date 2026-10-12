using Content.Server.UserInterface;
using Content.Shared._Oxyd.AstersGuild;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;

namespace Content.Server._Oxyd.AstersGuild;

/// <summary>
/// Ports Eris economy/ATM.dm: an Asters-branded teller machine tied to the shared guild
/// account. Deposit = use a credits stack on the machine (Eris cash insert); withdraw =
/// button spawns a denomination stack (Eris withdraw dialog). Window is ErisNanoStyle.
/// </summary>
public sealed partial class OxydAtmSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private OxydBankSystem _bank = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>Withdraw amounts offered by the window -> denomination proto spawned.</summary>
    private static readonly Dictionary<int, string> WithdrawDenominations = new()
    {
        [100] = "OxydCredits100",
        [500] = "OxydCredits500",
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydAtmComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<OxydAtmComponent, OxydAtmWithdrawMessage>(OnWithdraw);
        SubscribeLocalEvent<OxydAtmComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnOpened(EntityUid uid, OxydAtmComponent comp, BoundUIOpenedEvent args)
        => PushState(uid);

    /// <summary>Eris cash insert: swallow the held credits stack into the account.</summary>
    private void OnInteractUsing(EntityUid uid, OxydAtmComponent comp, InteractUsingEvent args)
    {
        if (!TryComp(args.Used, out StackComponent? stack) || stack.StackTypeId != "OxydCredits")
            return;
        var value = stack.Count;
        Del(args.Used);
        _bank.Credit(value);
        _popup.PopupEntity($"Deposited {value}cr into the guild account.", uid, args.User);
        PushState(uid);
        args.Handled = true;
    }

    /// <summary>Eris withdraw: pay out a denomination stack if the account covers it.</summary>
    private void OnWithdraw(EntityUid uid, OxydAtmComponent comp, OxydAtmWithdrawMessage msg)
    {
        if (!WithdrawDenominations.TryGetValue(msg.Amount, out var proto))
            return;
        if (!_bank.TrySpend(msg.Amount))
        {
            _popup.PopupEntity("Insufficient funds on the guild account.", uid, msg.Actor);
            return;
        }
        Spawn(proto, Transform(uid).Coordinates);
        _popup.PopupEntity($"Dispensed {msg.Amount}cr.", uid, msg.Actor);
        PushState(uid);
    }

    private void PushState(EntityUid uid)
    {
        if (!_ui.IsUiOpen(uid, OxydAtmUiKey.Key))
            return;
        _ui.SetUiState(uid, OxydAtmUiKey.Key, new OxydAtmState { Balance = _bank.Balance() });
    }
}
