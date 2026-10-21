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

    public override void Initialize()
    {
        base.Initialize();
    }

    [SubscribeLocalEvent]
    private void OnOpened(Entity<OxydAccountsDbComponent> ent, ref BoundUIOpenedEvent args)
    {
        _economy.EnsureDepartmentsForRound();
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

        _economy.EnsureDepartmentsForRound();
        var funder = _economy.FindDepartmentAccount(OxydEconomyConstants.EmployerCargo)
                     ?? _economy.FindDepartmentAccount(OxydEconomyConstants.EmployerService);
        if (funder == null)
        {
            // No dept yet — create unfunded personal account.
            var acc = _economy.CreateAccount(name, name, Math.Max(0, args.StartingFunds),
                OxydEconomyConstants.EmployerService, OxydEconomyConstants.WageNone);
            ent.Comp.Creating = false;
            ent.Comp.DetailAccount = acc.AccountNumber;
            Dirty(ent);
            PushState(ent, args.Actor, status: $"Created #{acc.AccountNumber} PIN {acc.Pin}");
            return;
        }

        if (!_economy.TryCreateFundedAccount(name, args.StartingFunds, funder.AccountNumber, "AccountsDB",
                out var created, out var err) || created == null)
        {
            PushState(ent, args.Actor, error: err ?? "Create failed");
            return;
        }

        ent.Comp.Creating = false;
        ent.Comp.DetailAccount = created.AccountNumber;
        Dirty(ent);
        PushState(ent, args.Actor, status: $"Created #{created.AccountNumber} PIN {created.Pin}");
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

        _economy.EnsureDepartmentsForRound();
        var dest = _economy.FindDepartmentAccount(OxydEconomyConstants.EmployerCargo)
                   ?? _economy.FindDepartmentAccount(OxydEconomyConstants.EmployerService);
        if (dest == null)
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
            Accounts = _economy.Accounts.Values
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
