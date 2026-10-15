using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._Oxyd.Framework.JobsAndSpawning;
using Content.Server.Cargo.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Oxyd.Economy;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Oxyd.Economy;

/// <summary>
/// Eris-style personal + department money accounts, payday, and job seeding.
/// Department balances bridge to <see cref="StationBankAccountComponent"/> / CargoAccount.
/// </summary>
public sealed partial class OxydEconomySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _protos = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly CargoSystem _cargo = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly ChatSystem _chat = default!;

    private readonly Dictionary<int, OxydMoneyAccountData> _accounts = new();
    private int _nextAccountNumber = 111111;
    private TimeSpan _nextPayday;
    private bool _departmentsReady;

    public IReadOnlyDictionary<int, OxydMoneyAccountData> Accounts => _accounts;

    public override void Initialize()
    {
        base.Initialize();
        _nextPayday = _timing.CurTime + TimeSpan.FromMinutes(OxydEconomyConstants.PaydayIntervalMinutes);
        SubscribeLocalEvent<JobAfterSpawnEvent>(OnJobAfterSpawn);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextPayday)
            return;

        _nextPayday = _timing.CurTime + TimeSpan.FromMinutes(OxydEconomyConstants.PaydayIntervalMinutes);
        RunPayday();
    }

    private void OnJobAfterSpawn(JobAfterSpawnEvent ev)
    {
        SetupAccountForJob(ev.spawned, ev.job);
    }

    public void EnsureDepartmentAccounts(EntityUid station)
    {
        if (!TryComp<StationBankAccountComponent>(station, out var bank))
            return;

        EnsureDepartmentAccounts((station, bank));
    }

    public void EnsureDepartmentAccounts(Entity<StationBankAccountComponent> station)
    {
        Entity<StationBankAccountComponent?> stationNullable = (station.Owner, station.Comp);
        foreach (var (accountId, balance) in _cargo.GetAccounts(stationNullable))
        {
            var deptId = accountId.Id;
            var existing = FindDepartmentAccount(deptId);
            if (existing != null)
            {
                existing.Balance = balance;
                continue;
            }

            var canMake = deptId is OxydEconomyConstants.EmployerCargo or OxydEconomyConstants.EmployerGuild;
            var employer = deptId switch
            {
                OxydEconomyConstants.EmployerSecurity => OxydEconomyConstants.EmployerCargo,
                OxydEconomyConstants.EmployerEngineering => OxydEconomyConstants.EmployerCargo,
                OxydEconomyConstants.EmployerService => OxydEconomyConstants.EmployerCargo,
                _ => null,
            };

            var wage = employer != null ? OxydEconomyConstants.BudgetBase : 0;
            CreateAccount(
                ownerName: $"{deptId} Department",
                accountName: deptId,
                balance: balance,
                employer: employer,
                wage: wage,
                isDepartment: true,
                departmentId: deptId,
                canMakeAccounts: canMake);
        }

        _departmentsReady = true;
    }

    public OxydMoneyAccountData CreateAccount(
        string ownerName,
        string? accountName,
        int balance,
        string? employer,
        int wage,
        bool isDepartment = false,
        string? departmentId = null,
        bool canMakeAccounts = false)
    {
        var number = _nextAccountNumber++;
        while (_accounts.ContainsKey(number))
            number = _nextAccountNumber++;

        var pin = isDepartment ? _random.Next(1111, 111111) : _random.Next(1111, 9999);
        var data = new OxydMoneyAccountData
        {
            AccountNumber = number,
            Pin = pin,
            OwnerName = ownerName,
            AccountName = accountName ?? ownerName,
            Balance = balance,
            Employer = employer,
            Wage = wage,
            WageOriginal = wage,
            IsDepartment = isDepartment,
            DepartmentId = departmentId,
            CanMakeAccounts = canMakeAccounts,
            SecurityLevel = 1,
        };
        _accounts[number] = data;
        LogTransaction(data, "Account creation", balance, "OxydEconomy");
        return data;
    }

    public bool TryGetAccount(int number, [NotNullWhen(true)] out OxydMoneyAccountData? account)
        => _accounts.TryGetValue(number, out account);

    public OxydMoneyAccountData? FindDepartmentAccount(string departmentId)
    {
        foreach (var acc in _accounts.Values)
        {
            if (acc.IsDepartment && acc.DepartmentId == departmentId)
                return acc;
        }

        return null;
    }

    public bool TryLogin(int number, int pin, [NotNullWhen(true)] out OxydMoneyAccountData? account, string terminal = "TaxQuickly")
    {
        account = null;
        if (!_accounts.TryGetValue(number, out var found) || found.Suspended)
            return false;

        if (found.Pin != pin)
        {
            LogTransaction(found, "Failed login", 0, terminal);
            return false;
        }

        account = found;
        return true;
    }

    public bool TryTransfer(int from, int to, int amount, string purpose, string terminal, [NotNullWhen(false)] out string? error)
    {
        error = null;
        if (amount <= 0)
        {
            error = "Invalid amount";
            return false;
        }

        if (!_accounts.TryGetValue(from, out var src) || !_accounts.TryGetValue(to, out var dst))
        {
            error = "Unknown account";
            return false;
        }

        if (src.Suspended || dst.Suspended)
        {
            error = "Account suspended";
            return false;
        }

        if (src.Balance < amount)
        {
            error = "Insufficient funds";
            return false;
        }

        src.Balance -= amount;
        dst.Balance += amount;
        SyncDepartmentToCargo(src);
        SyncDepartmentToCargo(dst);
        LogTransaction(src, purpose, -amount, terminal);
        LogTransaction(dst, purpose, amount, terminal);
        return true;
    }

    public bool TryAdjust(int number, int delta, string purpose, string terminal)
    {
        if (!_accounts.TryGetValue(number, out var acc) || acc.Suspended)
            return false;

        if (delta < 0 && acc.Balance < -delta)
            return false;

        acc.Balance += delta;
        SyncDepartmentToCargo(acc);
        LogTransaction(acc, purpose, delta, terminal);
        return true;
    }

    public bool TrySetBalance(int number, int balance)
    {
        if (!_accounts.TryGetValue(number, out var acc))
            return false;

        acc.Balance = balance;
        SyncDepartmentToCargo(acc);
        return true;
    }

    /// <summary>HoP Accounts DB silent adjust — works even on suspended accounts.</summary>
    public bool TrySilentAdjust(int number, int delta, string purpose, string terminal, int fundCap = OxydAccountsDbComponent.FundCap)
    {
        if (!_accounts.TryGetValue(number, out var acc))
            return false;

        var next = Math.Clamp(acc.Balance + delta, -fundCap, fundCap);
        var applied = next - acc.Balance;
        if (applied == 0 && delta != 0)
            return false;

        acc.Balance = next;
        SyncDepartmentToCargo(acc);
        LogTransaction(acc, purpose, applied, terminal);
        return true;
    }

    public bool TrySetSuspended(int number, bool suspended)
    {
        if (!_accounts.TryGetValue(number, out var acc))
            return false;

        acc.Suspended = suspended;
        LogTransaction(acc, suspended ? "Account suspended" : "Account reinstated", 0, "AccountsDB");
        return true;
    }

    /// <summary>Create a personal account funded from a department (Eris create_account).</summary>
    public bool TryCreateFundedAccount(
        string ownerName,
        int startingFunds,
        int fundingAccount,
        string terminal,
        [NotNullWhen(true)] out OxydMoneyAccountData? created,
        [NotNullWhen(false)] out string? error)
    {
        created = null;
        error = null;
        startingFunds = Math.Clamp(startingFunds, 0, OxydAccountsDbComponent.FundCap);

        if (!_accounts.TryGetValue(fundingAccount, out var funder) || funder.Suspended)
        {
            error = "Funding account unavailable";
            return false;
        }

        if (startingFunds > funder.Balance)
        {
            error = "Insufficient department funds";
            return false;
        }

        if (startingFunds > 0 && !TryAdjust(fundingAccount, -startingFunds, $"New account activation ({ownerName})", terminal))
        {
            error = "Could not debit funding account";
            return false;
        }

        created = CreateAccount(ownerName, ownerName, startingFunds, OxydEconomyConstants.EmployerService, OxydEconomyConstants.WageNone);
        return true;
    }

    /// <summary>Zero a personal account and credit Cargo/station (Eris revoke_payroll).</summary>
    public bool TryRevokePayroll(int number, int destinationAccount, string terminal, [NotNullWhen(false)] out string? error)
    {
        error = null;
        if (!_accounts.TryGetValue(number, out var acc) || acc.IsDepartment)
        {
            error = "Invalid account";
            return false;
        }

        if (!_accounts.TryGetValue(destinationAccount, out _))
        {
            error = "Destination missing";
            return false;
        }

        var funds = acc.Balance;
        if (funds == 0)
            return true;

        acc.Balance = 0;
        SyncDepartmentToCargo(acc);
        LogTransaction(acc, "Revoke payroll", -funds, terminal);
        TrySilentAdjust(destinationAccount, funds, $"Revoke payroll ({acc.OwnerName})", terminal);
        return true;
    }

    /// <summary>Pull department balances from StationBankAccount / CargoAccount.</summary>
    public void SyncDepartmentsFromCargo()
    {
        EnsureDepartmentsForRound();
        foreach (var acc in _accounts.Values)
        {
            if (acc.IsDepartment)
                PullDepartmentFromCargo(acc);
        }
    }

    public void ForcePayday() => RunPayday();

    public TimeSpan NextPayday => _nextPayday;

    private void SyncDepartmentToCargo(OxydMoneyAccountData acc)
    {
        if (!acc.IsDepartment || acc.DepartmentId == null)
            return;

        var station = _station.GetStations().FirstOrNull();
        if (station == null)
            return;

        _cargo.TrySetBankAccount(station.Value, (ProtoId<CargoAccountPrototype>)acc.DepartmentId, acc.Balance, true);
    }

    private void PullDepartmentFromCargo(OxydMoneyAccountData acc)
    {
        if (!acc.IsDepartment || acc.DepartmentId == null)
            return;

        var station = _station.GetStations().FirstOrNull();
        if (station == null)
            return;

        if (_cargo.TryGetAccount(station.Value, acc.DepartmentId, out var bal))
            acc.Balance = bal;
    }

    private void LogTransaction(OxydMoneyAccountData acc, string purpose, int amount, string terminal)
    {
        acc.Transactions.Add(new OxydTransactionData
        {
            TargetName = acc.OwnerName,
            Purpose = purpose,
            Amount = amount,
            SourceTerminal = terminal,
            Time = _timing.CurTime,
        });

        // Cap log size
        if (acc.Transactions.Count > 64)
            acc.Transactions.RemoveRange(0, acc.Transactions.Count - 64);
    }
}
