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
