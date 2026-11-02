using System.Linq;
using Content.Shared._Oxyd.Economy;
using Content.Shared.Access.Systems;
using Robust.Server.GameObjects;

namespace Content.Server._Oxyd.Economy;

/// <summary>Eris Accounts uplink terminal against <see cref="OxydEconomySystem"/>.</summary>
public sealed partial class OxydAccountsDbSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private OxydEconomySystem _economy = default!;
    [Dependency] private AccessReaderSystem _access = default!;

    [SubscribeLocalEvent]
    private void OnOpened(Entity<OxydAccountsDbComponent> ent, ref BoundUIOpenedEvent args)
    {
        PushState(ent, args.Actor);
    }

    [SubscribeLocalEvent]
    private void OnSelect(Entity<OxydAccountsDbComponent> ent, ref OxydAccountsDbSelectMessage args)
    {
        if (!HasAccess(ent, args.Actor))
        {
            PushState(ent, args.Actor, error: "Access denied");
            return;
        }

        ent.Comp.DetailAccount = args.AccountNumber;
        ent.Comp.Creating = false;
        Dirty(ent);
        PushState(ent, args.Actor);
    }

    [SubscribeLocalEvent]
    private void OnBack(Entity<OxydAccountsDbComponent> ent, ref OxydAccountsDbBackMessage args)
    {
        ent.Comp.DetailAccount = null;
        ent.Comp.Creating = false;
        Dirty(ent);
        PushState(ent, args.Actor);
    }

    [SubscribeLocalEvent]
    private void OnBeginCreate(Entity<OxydAccountsDbComponent> ent, ref OxydAccountsDbBeginCreateMessage args)
    {
        if (!HasAccess(ent, args.Actor))
        {
            PushState(ent, args.Actor, error: "Access denied");
            return;
        }

        ent.Comp.Creating = true;
        ent.Comp.DetailAccount = null;
        Dirty(ent);
        PushState(ent, args.Actor);
    }

    [SubscribeLocalEvent]
    private void OnCreate(Entity<OxydAccountsDbComponent> ent, ref OxydAccountsDbCreateMessage args)
    {
        if (!HasAccess(ent, args.Actor))
        {
            PushState(ent, args.Actor, error: "Access denied");
            return;
        }

        var name = args.OwnerName.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64)
        {
            PushState(ent, args.Actor, error: "Invalid holder name");
            return;
        }

        // Eris opens accounts empty; money is added afterwards from the funding account.
        var acc = _economy.CreateAccount(name, name, 0, OxydEconomyConstants.EmployerService, OxydEconomyConstants.WageNone);
        ent.Comp.Creating = false;
        ent.Comp.DetailAccount = acc.AccountNumber;
        Dirty(ent);
        PushState(ent, args.Actor, status: $"Created #{acc.AccountNumber} PIN {acc.Pin}");
    }

    [SubscribeLocalEvent]
    private void OnSetFundingAccount(Entity<OxydAccountsDbComponent> ent, ref OxydAccountsDbSetFundingAccountMessage args)
    {
        if (!HasAccess(ent, args.Actor))
        {
            PushState(ent, args.Actor, error: "Access denied");
            return;
        }

        if (!_economy.TryGetAccount(args.AccountNumber, out var acc))
        {
            PushState(ent, args.Actor, error: "Unknown account");
            return;
        }

        ent.Comp.FundingAccount = acc.AccountNumber;
        Dirty(ent);
        PushState(ent, args.Actor, status: $"Funding from #{acc.AccountNumber}");
    }

    [SubscribeLocalEvent]
    private void OnFund(Entity<OxydAccountsDbComponent> ent, ref OxydAccountsDbFundMessage args)
    {
        if (!HasAccess(ent, args.Actor) || ent.Comp.DetailAccount is not { } num)
        {
            PushState(ent, args.Actor, error: "Access denied");
            return;
        }

        if (FundingAccount(ent) is not { } funder)
        {
            PushState(ent, args.Actor, error: "Set a funding account");
            return;
        }

        if (!_economy.TryFundAccount(num, funder.AccountNumber, args.Amount, "AccountsDB", out var err))
        {
            PushState(ent, args.Actor, error: err);
            return;
        }

        PushState(ent, args.Actor, status: $"Funded {args.Amount}cr from #{funder.AccountNumber}");
    }

    [SubscribeLocalEvent]
    private void OnToggleSuspend(Entity<OxydAccountsDbComponent> ent, ref OxydAccountsDbToggleSuspendMessage args)
    {
        if (!HasAccess(ent, args.Actor) || ent.Comp.DetailAccount is not { } num)
        {
            PushState(ent, args.Actor, error: "Access denied");
            return;
        }

        if (!_economy.TryGetAccount(num, out var acc))
        {
            PushState(ent, args.Actor, error: "Unknown account");
            return;
        }

        _economy.TrySetSuspended(num, !acc.Suspended);
        PushState(ent, args.Actor, status: acc.Suspended ? "Suspended" : "Active");
    }

    [SubscribeLocalEvent]
    private void OnAdjust(Entity<OxydAccountsDbComponent> ent, ref OxydAccountsDbAdjustMessage args)
    {
        if (!HasAccess(ent, args.Actor) || ent.Comp.DetailAccount is not { } num)
        {
            PushState(ent, args.Actor, error: "Access denied");
            return;
        }

        if (!_economy.TrySilentAdjust(num, args.Delta, args.Delta >= 0 ? "Silent add funds" : "Silent remove funds",
                "AccountsDB"))
        {
            PushState(ent, args.Actor, error: "Adjust failed");
            return;
        }

        PushState(ent, args.Actor, status: $"Adjusted by {args.Delta}");
    }

    [SubscribeLocalEvent]
    private void OnRevoke(Entity<OxydAccountsDbComponent> ent, ref OxydAccountsDbRevokePayrollMessage args)
    {
        if (!HasAccess(ent, args.Actor) || ent.Comp.DetailAccount is not { } num)
        {
            PushState(ent, args.Actor, error: "Access denied");
            return;
        }

        if (FundingAccount(ent) is not { } dest)
        {
            PushState(ent, args.Actor, error: "No destination department");
            return;
        }

        if (!_economy.TryRevokePayroll(num, dest.AccountNumber, "AccountsDB", out var err))
        {
            PushState(ent, args.Actor, error: err ?? "Revoke failed");
            return;
        }

        PushState(ent, args.Actor, status: "Payroll revoked");
    }

    /// <summary>The chosen funding account, or the configured department's account.</summary>
    private OxydMoneyAccountData? FundingAccount(Entity<OxydAccountsDbComponent> ent)
    {
        if (ent.Comp.FundingAccount is { } number)
            return _economy.TryGetAccount(number, out var chosen) ? chosen : null;

        return ent.Comp.FundingDepartment is { } dept ? _economy.FindDepartmentAccount(dept) : null;
    }

    private bool HasAccess(EntityUid console, EntityUid user)
    {
        // No AccessReader → open for debug/QA spawns. With reader → HoP/Captain gate.
        if (!HasComp<Content.Shared.Access.Components.AccessReaderComponent>(console))
            return true;

        return _access.IsAllowed(user, console);
    }

    private void PushState(Entity<OxydAccountsDbComponent> ent, EntityUid user, string? error = null, string? status = null)
    {
        var state = new OxydAccountsDbUiState
        {
            HasAccess = HasAccess(ent, user),
            Creating = ent.Comp.Creating,
            Error = error,
            Status = status,
            Accounts = _economy.EnumerateAccounts()
                .OrderBy(a => a.AccountNumber)
                .Select(a => new OxydAccountsDbAccountSummary
                {
                    AccountNumber = a.AccountNumber,
                    OwnerName = a.OwnerName,
                    Balance = a.Balance,
                    Suspended = a.Suspended,
                    IsDepartment = a.IsDepartment,
                })
                .ToList(),
        };

        if (FundingAccount(ent) is { } funding)
        {
            state.FundingAccount = funding.AccountNumber;
            state.FundingName = funding.OwnerName;
        }

        if (ent.Comp.DetailAccount is { } num && _economy.TryGetAccount(num, out var detail))
        {
            state.DetailAccount = detail.AccountNumber;
            state.DetailOwner = detail.OwnerName;
            state.DetailBalance = detail.Balance;
            state.DetailSuspended = detail.Suspended;
            state.DetailWage = detail.Wage;
            state.DetailEmployer = detail.Employer;
            state.Transactions = detail.Transactions.TakeLast(24).Reverse().ToList();
        }

        _ui.SetUiState(ent.Owner, OxydAccountsDbUiKey.Key, state);
    }
}
