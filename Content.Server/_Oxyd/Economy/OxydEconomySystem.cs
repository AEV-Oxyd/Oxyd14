using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._Oxyd.Framework.JobsAndSpawning;
using Content.Server.Cargo.Systems;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking.Events;
using Content.Server.Station.Systems;
using Content.Shared._Oxyd.Economy;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Robust.Shared.Map;
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
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private CargoSystem _cargo = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private ChatSystem _chat = default!;

    private static readonly EntProtoId BankingServerProto = "OxydBankingServer";

    /// <summary>PIN brute-force guard: failures are counted per account and per user.
    /// The window is measured from the FIRST failure — 5 inside a minute locks that
    /// side out for a minute.</summary>
    private const int LoginMaxFailures = 5;
    private static readonly TimeSpan LoginFailureWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LoginLockout = TimeSpan.FromMinutes(1);

    private Entity<OxydBankingServerComponent>? _bank;

    /// <summary>The round's banking server. Spawned at round start; also spawned on first use
    /// when no round has started (tests, admin tools).</summary>
    public Entity<OxydBankingServerComponent> Bank => _bank is { } bank && !TerminatingOrDeleted(bank) ? bank : EnsureBank();

    public IReadOnlyDictionary<int, OxydMoneyAccountData> Accounts => Bank.Comp.Accounts;

    public IEnumerable<OxydMoneyAccountData> EnumerateAccounts() => Bank.Comp.Accounts.Values;

    public TimeSpan NextPayday => Bank.Comp.NextPayday;

    [SubscribeLocalEvent]
    private void OnRoundStarting(RoundStartingEvent ev)
    {
        EnsureBank();
    }

    [SubscribeLocalEvent]
    private void OnRoundCleanup(RoundRestartCleanupEvent ev)
    {
        if (_bank is { } bank && !TerminatingOrDeleted(bank))
            Del(bank);
        _bank = null;
    }

    private Entity<OxydBankingServerComponent> EnsureBank()
    {
        var query = EntityQueryEnumerator<OxydBankingServerComponent>();
        if (query.MoveNext(out var existing, out var existingComp))
        {
            _bank = (existing, existingComp);
            return _bank.Value;
        }

        var uid = Spawn(BankingServerProto, MapCoordinates.Nullspace);
        _bank = (uid, Comp<OxydBankingServerComponent>(uid));
        return _bank.Value;
    }

    /// <summary>The only place department accounts are opened: when the banking server starts.</summary>
    [SubscribeLocalEvent]
    private void OnBankStartup(Entity<OxydBankingServerComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.NextAccountNumber = ent.Comp.FirstAccountNumber;
        ent.Comp.NextPayday = _timing.CurTime + ent.Comp.PaydayInterval;

        var station = _station.GetStations().FirstOrNull();
        foreach (var dept in ent.Comp.Departments)
        {
            var balance = dept.StartingBalance;
            if (dept.CargoAccount is { } cargoAccount && station != null &&
                _cargo.TryGetAccount(station.Value, cargoAccount, out var cargoBalance))
                balance = cargoBalance;

            var acc = CreateAccount(ent,
                ownerName: dept.Name ?? $"{dept.Id} Department",
                accountName: dept.Id,
                balance: balance,
                employer: dept.Employer,
                wage: dept.Wage,
                isDepartment: true,
                departmentId: dept.Id,
                canMakeAccounts: dept.CanMakeAccounts);
            ent.Comp.CargoSyncedBalance[acc.AccountNumber] = balance;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_bank is not { } bank || TerminatingOrDeleted(bank) || _timing.CurTime < bank.Comp.NextPayday)
            return;

        bank.Comp.NextPayday = _timing.CurTime + bank.Comp.PaydayInterval;
        RunPayday();
    }

    [SubscribeLocalEvent]
    private void OnJobAfterSpawn(JobAfterSpawnEvent ev)
    {
        SetupAccountForJob(ev.spawned, ev.job);
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
        return CreateAccount(Bank, ownerName, accountName, balance, employer, wage, isDepartment, departmentId, canMakeAccounts);
    }

    private OxydMoneyAccountData CreateAccount(
        Entity<OxydBankingServerComponent> bank,
        string ownerName,
        string? accountName,
        int balance,
        string? employer,
        int wage,
        bool isDepartment,
        string? departmentId,
        bool canMakeAccounts)
    {
        var number = bank.Comp.NextAccountNumber++;
        while (bank.Comp.Accounts.ContainsKey(number))
            number = bank.Comp.NextAccountNumber++;

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
        bank.Comp.Accounts[number] = data;
        LogTransaction(data, "Account creation", balance, "OxydEconomy");
        return data;
    }

    public bool TryGetAccount(int number, [NotNullWhen(true)] out OxydMoneyAccountData? account)
        => Bank.Comp.Accounts.TryGetValue(number, out account);

    public OxydMoneyAccountData? FindDepartmentAccount(string departmentId)
    {
        foreach (var acc in Bank.Comp.Accounts.Values)
        {
            if (acc.IsDepartment && acc.DepartmentId == departmentId)
                return acc;
        }

        return null;
    }

    private static void LoginFailed(OxydLoginThrottle throttle, TimeSpan now)
    {
        if (now - throttle.FirstFailAt > LoginFailureWindow)
        {
            throttle.Count = 0;
            throttle.FirstFailAt = now;
        }

        throttle.Count++;
        if (throttle.Count >= LoginMaxFailures)
        {
            throttle.LockedUntil = now + LoginLockout;
            throttle.Count = 0;
        }
    }

    public bool TryLogin(int number, int pin, [NotNullWhen(true)] out OxydMoneyAccountData? account, string terminal = "TaxQuickly", EntityUid? user = null)
    {
        account = null;
        var now = _timing.CurTime;
        if (!Bank.Comp.Accounts.TryGetValue(number, out var found) || found.Suspended)
            return false;

        if (now < found.Login.LockedUntil)
            return false;
        if (user is { } u && TryComp<OxydLoginFailuresComponent>(u, out var lf) && now < lf.Throttle.LockedUntil)
            return false;

        if (found.Pin != pin)
        {
            LogTransaction(found, "Failed login", 0, terminal);
            LoginFailed(found.Login, now);
            if (user is { } failedUser)
                LoginFailed(EnsureComp<OxydLoginFailuresComponent>(failedUser).Throttle, now);
            return false;
        }

        found.Login.Count = 0;
        if (user is { } loggedIn)
        {
            var throttle = EnsureComp<OxydLoginFailuresComponent>(loggedIn).Throttle;
            throttle.Count = 0;
            throttle.LockedUntil = TimeSpan.Zero;
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

        if (!Bank.Comp.Accounts.TryGetValue(from, out var src) || !Bank.Comp.Accounts.TryGetValue(to, out var dst))
        {
            error = "Unknown account";
            return false;
        }

        if (src.Suspended || dst.Suspended)
        {
            error = "Account suspended";
            return false;
        }

        RefreshDepartment(src);
        RefreshDepartment(dst);
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
        SyncDisplayedBalance(from);
        SyncDisplayedBalance(to);
        return true;
    }

    /// <summary>Cargo-console spending, passive income and bounties mutate
    /// StationBankAccount directly — pull the authoritative balance back before
    /// any op that mutates our copy so it can't overdraw what cargo already spent.</summary>
    private void RefreshDepartment(OxydMoneyAccountData acc)
    {
        if (acc.IsDepartment && acc.DepartmentId != null)
            PullDepartmentFromCargo(acc);
    }

    public bool TryAdjust(int number, int delta, string purpose, string terminal)
    {
        if (!Bank.Comp.Accounts.TryGetValue(number, out var acc) || acc.Suspended)
            return false;

        RefreshDepartment(acc);
        if (delta < 0 && acc.Balance < -delta)
            return false;

        acc.Balance += delta;
        SyncDepartmentToCargo(acc);
        LogTransaction(acc, purpose, delta, terminal);
        SyncDisplayedBalance(number);
        return true;
    }

    public bool TrySetBalance(int number, int balance)
    {
        if (!Bank.Comp.Accounts.TryGetValue(number, out var acc))
            return false;

        RefreshDepartment(acc);
        acc.Balance = balance;
        SyncDepartmentToCargo(acc);
        SyncDisplayedBalance(number);
        return true;
    }

    /// <summary>HoP Accounts DB silent adjust — works even on suspended accounts.</summary>
    public bool TrySilentAdjust(int number, int delta, string purpose, string terminal, int fundCap = OxydAccountsDbComponent.FundCap)
    {
        if (!Bank.Comp.Accounts.TryGetValue(number, out var acc))
            return false;

        RefreshDepartment(acc);
        var next = Math.Clamp(acc.Balance + delta, -fundCap, fundCap);
        var applied = next - acc.Balance;
        if (applied == 0 && delta != 0)
            return false;

        acc.Balance = next;
        SyncDepartmentToCargo(acc);
        LogTransaction(acc, purpose, applied, terminal);
        SyncDisplayedBalance(number);
        return true;
    }

    /// <summary>Copies the server balance onto the owner's networked component for the character window.</summary>
    private void SyncDisplayedBalance(int number)
    {
        var bank = Bank.Comp;
        if (!bank.Accounts.TryGetValue(number, out var acc) ||
            !bank.Holders.TryGetValue(number, out var holder) ||
            !TryComp<OxydPersonalAccountComponent>(holder, out var personal))
            return;

        personal.Balance = acc.Balance;
        personal.OwnerName = acc.OwnerName;
        Dirty(holder, personal);
    }

    public bool TrySetSuspended(int number, bool suspended)
    {
        if (!Bank.Comp.Accounts.TryGetValue(number, out var acc))
            return false;

        acc.Suspended = suspended;
        LogTransaction(acc, suspended ? "Account suspended" : "Account reinstated", 0, "AccountsDB");
        return true;
    }

    /// <summary>Move money from a funding account into an account (Accounts DB funding).</summary>
    public bool TryFundAccount(int account, int fundingAccount, int amount, string terminal, [NotNullWhen(false)] out string? error)
    {
        amount = Math.Clamp(amount, 0, OxydAccountsDbComponent.FundCap);
        if (!Bank.Comp.Accounts.TryGetValue(account, out var target))
        {
            error = "Unknown account";
            return false;
        }

        return TryTransfer(fundingAccount, account, amount, $"Account funding ({target.OwnerName})", terminal, out error);
    }

    /// <summary>Zero a personal account and credit Cargo/station (Eris revoke_payroll).</summary>
    public bool TryRevokePayroll(int number, int destinationAccount, string terminal, [NotNullWhen(false)] out string? error)
    {
        error = null;
        if (!Bank.Comp.Accounts.TryGetValue(number, out var acc) || acc.IsDepartment)
        {
            error = "Invalid account";
            return false;
        }

        if (!Bank.Comp.Accounts.TryGetValue(destinationAccount, out _))
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
        foreach (var acc in Bank.Comp.Accounts.Values)
        {
            if (acc.IsDepartment)
                PullDepartmentFromCargo(acc);
        }
    }

    public void ForcePayday() => RunPayday();

    /// <summary>Push only the change since the last sync — cargo's own income/spend
    /// between syncs is preserved instead of being overwritten by our cached balance.</summary>
    private void SyncDepartmentToCargo(OxydMoneyAccountData acc)
    {
        if (!acc.IsDepartment || acc.DepartmentId == null)
            return;

        var station = _station.GetStations().FirstOrNull();
        if (station == null || !TryComp<StationBankAccountComponent>(station.Value, out var bank))
            return;

        var synced = Bank.Comp.CargoSyncedBalance;
        var delta = acc.Balance - synced.GetValueOrDefault(acc.AccountNumber);
        if (delta == 0)
            return;

        _cargo.UpdateBankAccount((station.Value, bank), delta, (ProtoId<CargoAccountPrototype>)acc.DepartmentId, true);
        synced[acc.AccountNumber] = acc.Balance;
    }

    private void PullDepartmentFromCargo(OxydMoneyAccountData acc)
    {
        if (!acc.IsDepartment || acc.DepartmentId == null)
            return;

        var station = _station.GetStations().FirstOrNull();
        if (station == null)
            return;

        if (_cargo.TryGetAccount(station.Value, acc.DepartmentId, out var bal))
        {
            acc.Balance = bal;
            Bank.Comp.CargoSyncedBalance[acc.AccountNumber] = bal;
        }
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
