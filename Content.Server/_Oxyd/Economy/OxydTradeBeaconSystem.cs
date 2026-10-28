using System.Linq;
using Content.Shared._Oxyd.Economy;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Economy;

/// <summary>Eris trade beacons — receiving (purchase drop) / sending (offer export).</summary>
public sealed partial class OxydTradeBeaconSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private OxydTradeSystem _trade = default!;
    [Dependency] private OxydEconomySystem _economy = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydTradeBeaconSendingComponent, BoundUIOpenedEvent>(OnSendOpened);
        SubscribeLocalEvent<OxydTradeBeaconSendingComponent, OxydTradeBeaconLinkMessage>(OnSendLink);
        SubscribeLocalEvent<OxydTradeBeaconSendingComponent, OxydTradeBeaconExportMessage>(OnExport);
        SubscribeLocalEvent<OxydTradeBeaconSendingComponent, InteractUsingEvent>(OnSendInteract);

        SubscribeLocalEvent<OxydTradeBeaconReceivingComponent, BoundUIOpenedEvent>(OnRecvOpened);
        SubscribeLocalEvent<OxydTradeBeaconReceivingComponent, OxydTradeBeaconLinkMessage>(OnRecvLink);
        SubscribeLocalEvent<OxydTradeBeaconReceivingComponent, OxydTradeBeaconPurchaseMessage>(OnPurchase);
    }

    private void OnSendOpened(Entity<OxydTradeBeaconSendingComponent> ent, ref BoundUIOpenedEvent args)
        => PushSending(ent);

    private void OnRecvOpened(Entity<OxydTradeBeaconReceivingComponent> ent, ref BoundUIOpenedEvent args)
        => PushReceiving(ent);

    private void OnSendLink(Entity<OxydTradeBeaconSendingComponent> ent, ref OxydTradeBeaconLinkMessage args)
    {
        if (!_economy.TryGetAccount(args.AccountNumber, out _))
        {
            PushSending(ent, error: "Unknown account");
            return;
        }

        ent.Comp.LinkedAccountNumber = args.AccountNumber;
        Dirty(ent);
        PushSending(ent, status: $"Linked #{args.AccountNumber}");
    }

    private void OnRecvLink(Entity<OxydTradeBeaconReceivingComponent> ent, ref OxydTradeBeaconLinkMessage args)
    {
        if (!_economy.TryGetAccount(args.AccountNumber, out _))
        {
            PushReceiving(ent, error: "Unknown account");
            return;
        }

        ent.Comp.LinkedAccountNumber = args.AccountNumber;
        Dirty(ent);
        PushReceiving(ent, status: $"Linked #{args.AccountNumber}");
    }

    private void OnExport(Entity<OxydTradeBeaconSendingComponent> ent, ref OxydTradeBeaconExportMessage args)
    {
        if (ent.Comp.NextExportReady > _timing.CurTime)
        {
            PushSending(ent, error: "Beacon cooling down");
            return;
        }

        ResolvePayout(ent.Comp.LinkedAccountNumber, out var acct, out var err);
        if (err != null || acct == null)
        {
            PushSending(ent, error: err ?? "Link an account");
            return;
        }

        var slave = !(acct.IsDepartment && acct.DepartmentId == OxydEconomyConstants.EmployerGuild);
        if (!_trade.TryFulfillOffersNear(ent, ent.Comp.Range, acct.AccountNumber, slave, out var paid, out var exportErr))
        {
            PushSending(ent, error: exportErr);
            return;
        }

        ent.Comp.NextExportReady = _timing.CurTime + ent.Comp.Cooldown;
        Dirty(ent);
        _popup.PopupEntity($"Exported offers for {paid}cr.", ent, args.Actor);
        PushSending(ent, status: $"Paid {paid}cr");
    }

    private void OnPurchase(Entity<OxydTradeBeaconReceivingComponent> ent, ref OxydTradeBeaconPurchaseMessage args)
    {
        if (string.IsNullOrWhiteSpace(args.OrderId))
        {
            PushReceiving(ent, error: "Order id required");
            return;
        }

        if (!_trade.TryFulfillOrder(args.OrderId, Transform(ent).Coordinates, FulfillerAccounts(args.Actor), out var err))
        {
            PushReceiving(ent, error: err);
            return;
        }

        _popup.PopupEntity($"Order {args.OrderId} delivered.", ent, args.Actor);
        PushReceiving(ent, status: $"Delivered {args.OrderId}");
    }

    private void OnSendInteract(Entity<OxydTradeBeaconSendingComponent> ent, ref InteractUsingEvent args)
    {
        // Swipe ID with OxydBankId to link
        var card = args.Used;
        if (TryComp<Content.Shared.PDA.PdaComponent>(card, out var pda) && pda.ContainedId != null)
            card = pda.ContainedId.Value;

        if (!TryComp<OxydBankIdComponent>(card, out var bank))
            return;

        ent.Comp.LinkedAccountNumber = bank.AccountNumber;
        Dirty(ent);
        _popup.PopupEntity($"Linked account #{bank.AccountNumber}.", ent, args.User);
        PushSending(ent, status: $"Linked #{bank.AccountNumber}");
        args.Handled = true;
    }

    private void ResolvePayout(int? linked, out OxydMoneyAccountData? acct, out string? error)
    {
        acct = null;
        error = null;
        _economy.EnsureDepartmentsForRound();
        if (linked is { } num && _economy.TryGetAccount(num, out acct))
            return;

        acct = _economy.FindDepartmentAccount(OxydEconomyConstants.EmployerGuild);
        if (acct == null)
            error = "No payout account";
    }

    private void PushSending(Entity<OxydTradeBeaconSendingComponent> ent, string? error = null, string? status = null)
    {
        var ready = ent.Comp.NextExportReady <= _timing.CurTime;
        _ui.SetUiState(ent.Owner, OxydTradeBeaconUiKey.Key, new OxydTradeBeaconUiState
        {
            Sending = true,
            LinkedAccount = ent.Comp.LinkedAccountNumber,
            Ready = ready,
            Error = error,
            Status = status ?? (ready ? "Ready to export offers" : "Cooling down"),
        });
    }

    /// <summary>Every account the fulfiller controls — personal plus department —
    /// so the trade system can check order ownership before delivering.</summary>
    private int[]? FulfillerAccounts(EntityUid actor)
    {
        var accounts = new List<int>();
        if (TryComp<OxydPersonalAccountComponent>(actor, out var personal))
        {
            accounts.Add(personal.AccountNumber);
            if (personal.DepartmentAccountNumber is { } dept)
                accounts.Add(dept);
        }

        if (accounts.Count == 0 && TryComp<OxydBankIdComponent>(actor, out var bankId))
            accounts.Add(bankId.AccountNumber);

        return accounts.Count == 0 ? null : accounts.ToArray();
    }

    private void PushReceiving(Entity<OxydTradeBeaconReceivingComponent> ent, string? error = null, string? status = null)
    {
        var pending = _trade.Orders.Count(o => !o.Fulfilled);
        _ui.SetUiState(ent.Owner, OxydTradeBeaconUiKey.Key, new OxydTradeBeaconUiState
        {
            Sending = false,
            LinkedAccount = ent.Comp.LinkedAccountNumber,
            Ready = true,
            Error = error,
            Status = status ?? $"{pending} open order(s) — enter order id to purchase",
        });
    }
}
