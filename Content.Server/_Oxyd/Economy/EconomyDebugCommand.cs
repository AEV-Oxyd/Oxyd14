using System.Linq;
using Content.Server.Administration;
using Content.Server.Station.Systems;
using Content.Shared._Oxyd.Economy;
using Content.Shared.Administration;
using Content.Shared.Mobs.Components;
using Robust.Shared.Toolshed;

namespace Content.Server._Oxyd.Economy;

/// <summary>Toolshed QA surface for Eris economy port (computer-use friendly).</summary>
[ToolshedCommand(Name = "economy"), AdminCommand(AdminFlags.Debug)]
public sealed class EconomyDebugCommand : ToolshedCommand
{
    private OxydEconomySystem? _eco;

    private OxydEconomySystem Eco => _eco ??= GetSys<OxydEconomySystem>();

    [CommandImplementation("status")]
    public void StatusSelf(IInvocationContext ctx)
    {
        var uid = ExecutingEntity(ctx) ?? throw new InvalidOperationException("no executing entity");
        Status(ctx, uid);
    }

    [CommandImplementation("status")]
    public void Status(IInvocationContext ctx, [PipedArgument] EntityUid mob)
    {
        if (!HasComp<MobStateComponent>(mob))
        {
            ctx.WriteLine($"{mob}: not a mob, skipped");
            return;
        }

        if (!TryComp<OxydPersonalAccountComponent>(mob, out var personal))
        {
            ctx.WriteLine($"{EntityManager.ToPrettyString(mob)}: no personal account");
            return;
        }

        if (!Eco.TryGetAccount(personal.AccountNumber, out var acc))
        {
            ctx.WriteLine($"{EntityManager.ToPrettyString(mob)}: acct={personal.AccountNumber} MISSING");
            return;
        }

        ctx.WriteLine(
            $"{EntityManager.ToPrettyString(mob)}: acct={acc.AccountNumber} pin={acc.Pin} bal={acc.Balance} wage={acc.Wage} debt={acc.Debt} employer={acc.Employer}"
            + (personal.DepartmentAccountNumber is { } d
                ? $" dept={d} deptPin={personal.DepartmentPin}"
                : string.Empty));
    }

    [CommandImplementation("status")]
    public void StatusMany(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> mobs)
    {
        foreach (var mob in mobs.ToList())
            Status(ctx, mob);
    }

    [CommandImplementation("dept")]
    public void Dept(IInvocationContext ctx, string departmentId)
    {
        var station = GetSys<StationSystem>().GetStations().FirstOrDefault();
        if (station != default)
            Eco.EnsureDepartmentAccounts(station);
        var acc = Eco.FindDepartmentAccount(departmentId);
        if (acc == null)
        {
            ctx.WriteLine($"dept {departmentId}: missing");
            return;
        }

        ctx.WriteLine(
            $"dept={acc.DepartmentId} acct={acc.AccountNumber} pin={acc.Pin} bal={acc.Balance} wage={acc.Wage} employer={acc.Employer} canMake={acc.CanMakeAccounts}");
    }

    [CommandImplementation("create")]
    public void Create(IInvocationContext ctx, string owner, int balance)
    {
        var acc = Eco.CreateAccount(owner, owner, balance, OxydEconomyConstants.EmployerService, OxydEconomyConstants.WageNone);
        ctx.WriteLine($"created acct={acc.AccountNumber} pin={acc.Pin} bal={acc.Balance}");
    }

    [CommandImplementation("set")]
    public void Set(IInvocationContext ctx, int account, int balance)
    {
        var before = Eco.TryGetAccount(account, out var acc) ? acc.Balance : -1;
        if (!Eco.TrySetBalance(account, balance))
        {
            ctx.WriteLine($"acct={account}: set failed");
            return;
        }

        ctx.WriteLine($"acct={account} bal={before}->{balance}");
    }

    [CommandImplementation("adjust")]
    public void Adjust(IInvocationContext ctx, int account, int by)
    {
        var before = Eco.TryGetAccount(account, out var acc) ? acc.Balance : -1;
        if (!Eco.TryAdjust(account, by, "economy:adjust", "Toolshed"))
        {
            ctx.WriteLine($"acct={account}: adjust failed");
            return;
        }

        Eco.TryGetAccount(account, out acc);
        ctx.WriteLine($"acct={account} bal={before}->{acc!.Balance}");
    }

    [CommandImplementation("pin")]
    public void Pin(IInvocationContext ctx, int account)
    {
        if (!Eco.TryGetAccount(account, out var acc))
        {
            ctx.WriteLine($"acct={account}: missing");
            return;
        }

        ctx.WriteLine($"acct={account} pin={acc.Pin}");
    }

    [CommandImplementation("transfer")]
    public void Transfer(IInvocationContext ctx, int from, int to, int amount)
    {
        var fromBefore = Eco.TryGetAccount(from, out var a) ? a.Balance : -1;
        var toBefore = Eco.TryGetAccount(to, out var b) ? b.Balance : -1;
        if (!Eco.TryTransfer(from, to, amount, "economy:transfer", "Toolshed", out var err))
        {
            ctx.WriteLine($"transfer failed: {err}");
            return;
        }

        Eco.TryGetAccount(from, out a);
        Eco.TryGetAccount(to, out b);
        ctx.WriteLine($"transfer {amount}: {from} {fromBefore}->{a!.Balance}; {to} {toBefore}->{b!.Balance}");
    }

    [CommandImplementation("wage")]
    public void Wage(IInvocationContext ctx, int account, int wage)
    {
        if (!Eco.TryGetAccount(account, out var acc))
        {
            ctx.WriteLine($"acct={account}: missing");
            return;
        }

        acc.Wage = wage;
        acc.WageManual = true;
        ctx.WriteLine($"acct={account} wage={wage} manual=true");
    }

    [CommandImplementation("payday")]
    public void Payday(IInvocationContext ctx, string mode)
    {
        if (mode == "force")
        {
            Eco.ForcePayday();
            ctx.WriteLine("economy:payday force complete");
            return;
        }

        if (mode == "interval")
        {
            ctx.WriteLine($"nextPayday={Eco.NextPayday}");
            return;
        }

        ctx.WriteLine("usage: economy:payday force|interval");
    }

    [CommandImplementation("assert")]
    public void AssertBal(IInvocationContext ctx, int account, string op, int value)
    {
        if (!Eco.TryGetAccount(account, out var acc))
            throw new InvalidOperationException($"acct={account} missing");

        var ok = op switch
        {
            ">=" => acc.Balance >= value,
            "<=" => acc.Balance <= value,
            "==" => acc.Balance == value,
            ">" => acc.Balance > value,
            "<" => acc.Balance < value,
            _ => throw new InvalidOperationException($"unknown op {op}"),
        };

        ctx.WriteLine($"assert acct={account} bal={acc.Balance} {op} {value} => {ok}");
        if (!ok)
            throw new InvalidOperationException("economy:assert failed");
    }
}
