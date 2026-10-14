using System.Linq;
using Content.Server.Administration;
using Content.Server.Stack;
using Content.Server.Station.Systems;
using Content.Shared._Oxyd.Economy;
using Content.Shared.Administration;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
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

    [CommandImplementation("givecash")]
    public void GiveCash(IInvocationContext ctx, int amount)
    {
        var uid = ExecutingEntity(ctx) ?? throw new InvalidOperationException("no executing entity");
        var cash = GetSys<StackSystem>().SpawnAtPosition(amount, (ProtoId<StackPrototype>)"Credit", Transform(uid).Coordinates);
        GetSys<SharedHandsSystem>().TryPickupAnyHand(uid, cash, checkActionBlocker: false);
        ctx.WriteLine($"gave {amount} Credit to {EntityManager.ToPrettyString(uid)}");
    }

    [CommandImplementation("place")]
    public void Place(IInvocationContext ctx, string what)
    {
        var uid = ExecutingEntity(ctx) ?? throw new InvalidOperationException("no executing entity");
        var proto = what.ToLowerInvariant() switch
        {
            "atm" => "OxydAtm",
            "register" => "OxydCashRegister",
            "eftpos" => "OxydEftpos",
            "tipjar" => "OxydTipJar",
            "vendor" => "OxydVendingClubSnacks",
            _ => what,
        };
        var ent = Spawn(proto, Transform(uid).Coordinates);
        ctx.WriteLine($"placed {EntityManager.ToPrettyString(ent)}");
    }

    [CommandImplementation("link")]
    public void Link(IInvocationContext ctx, string kind, int account)
    {
        var uid = ExecutingEntity(ctx) ?? throw new InvalidOperationException("no executing entity");
        // Link nearest matching device within 2m
        var lookup = GetSys<EntityLookupSystem>();
        var xform = Transform(uid);
        foreach (var ent in lookup.GetEntitiesInRange(xform.Coordinates, 2f))
        {
            if (kind == "register" && TryComp<OxydCashRegisterComponent>(ent, out var reg))
            {
                reg.LinkedAccountNumber = account;
                EntityManager.Dirty(ent, reg);
                ctx.WriteLine($"linked register {EntityManager.ToPrettyString(ent)} -> acct={account}");
                return;
            }

            if (kind == "eftpos" && TryComp<OxydEftposComponent>(ent, out var eft))
            {
                eft.LinkedAccountNumber = account;
                EntityManager.Dirty(ent, eft);
                ctx.WriteLine($"linked eftpos {EntityManager.ToPrettyString(ent)} -> acct={account}");
                return;
            }

            if (kind == "vendor" && TryComp<OxydVendorEarningsComponent>(ent, out var vend))
            {
                vend.LinkedAccountNumber = account;
                EntityManager.Dirty(ent, vend);
                ctx.WriteLine($"linked vendor {EntityManager.ToPrettyString(ent)} -> acct={account}");
                return;
            }
        }

        ctx.WriteLine($"no {kind} in range");
    }

    [CommandImplementation("setup")]
    public void Setup(IInvocationContext ctx, string scenario)
    {
        var uid = ExecutingEntity(ctx) ?? throw new InvalidOperationException("no executing entity");
        Eco.EnsureDepartmentsForRound();
        var service = Eco.FindDepartmentAccount(OxydEconomyConstants.EmployerService);
        switch (scenario)
        {
            case "club-pos":
                Place(ctx, "register");
                Place(ctx, "eftpos");
                Place(ctx, "tipjar");
                if (service != null)
                {
                    Link(ctx, "register", service.AccountNumber);
                    Link(ctx, "eftpos", service.AccountNumber);
                    ctx.WriteLine($"dept Service acct={service.AccountNumber} pin={service.Pin} bal={service.Balance}");
                }
                break;
            case "payday-retainer":
                if (Eco.FindDepartmentAccount(OxydEconomyConstants.EmployerCargo) is { } cargo)
                    Eco.TrySetBalance(cargo.AccountNumber, 50000);
                if (service != null)
                    Eco.TrySetBalance(service.AccountNumber, 500);
                ctx.WriteLine("payday-retainer seeded (Cargo rich, Service poor)");
                break;
            default:
                ctx.WriteLine("usage: economy:setup club-pos|payday-retainer");
                break;
        }
    }
}
