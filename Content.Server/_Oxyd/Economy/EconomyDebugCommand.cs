using System.Linq;
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Server.Administration;
using Content.Server.Stack;
using Content.Server.Station.Systems;
using Content.Shared._Oxyd.Economy;
using Content.Shared.Administration;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Materials;
using Content.Shared.Mobs.Components;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Toolshed;

namespace Content.Server._Oxyd.Economy;

/// <summary>Toolshed QA surface for Eris economy port (computer-use friendly).</summary>
[ToolshedCommand(Name = "economy"), AdminCommand(AdminFlags.Debug)]
public sealed partial class EconomyDebugCommand : ToolshedCommand
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
            "accountsdb" => "OxydAccountsDbOpen",
            "accountsdb-secure" => "OxydAccountsDb",
            "bench" or "artist" => "OxydArtistBench",
            "art" => "OxydArtworkStatue",
            "send" or "beacon-send" => "OxydTradeBeaconSending",
            "recv" or "beacon-recv" => "OxydTradeBeaconReceiving",
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

            if ((kind is "send" or "beacon-send") && TryComp<OxydTradeBeaconSendingComponent>(ent, out var send))
            {
                send.LinkedAccountNumber = account;
                EntityManager.Dirty(ent, send);
                ctx.WriteLine($"linked send beacon {EntityManager.ToPrettyString(ent)} -> acct={account}");
                return;
            }

            if ((kind is "recv" or "beacon-recv") && TryComp<OxydTradeBeaconReceivingComponent>(ent, out var recv))
            {
                recv.LinkedAccountNumber = account;
                EntityManager.Dirty(ent, recv);
                ctx.WriteLine($"linked recv beacon {EntityManager.ToPrettyString(ent)} -> acct={account}");
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
            case "accountsdb":
                Place(ctx, "accountsdb");
                Place(ctx, "bench");
                Eco.EnsureDepartmentsForRound();
                SeedNearestBench(ctx, uid);
                ctx.WriteLine("accounts uplink + artist bench placed (materials seeded)");
                break;
            case "artist":
                Place(ctx, "bench");
                SeedNearestBench(ctx, uid);
                EntityManager.EnsureComponent<OxydArtistPerkComponent>(uid);
                var san = EntityManager.EnsureComponent<SanityComponent>(uid);
                san.Insight = 100f;
                EntityManager.Dirty(uid, san);
                ctx.WriteLine("artist bench + perk + 100 insight (materials seeded)");
                break;
            case "trade":
                Place(ctx, "send");
                Place(ctx, "recv");
                Eco.EnsureDepartmentsForRound();
                GetSys<OxydTradeSystem>().EnsureSeeded();
                if (Eco.FindDepartmentAccount(OxydEconomyConstants.EmployerGuild) is { } guild)
                {
                    Link(ctx, "send", guild.AccountNumber);
                    Link(ctx, "recv", guild.AccountNumber);
                    ctx.WriteLine($"dept Guild acct={guild.AccountNumber} pin={guild.Pin} bal={guild.Balance}");
                }

                ctx.WriteLine("trade beacons placed (send+recv)");
                break;
            default:
                ctx.WriteLine("usage: economy:setup club-pos|payday-retainer|accountsdb|artist|trade");
                break;
        }
    }

    private void SeedNearestBench(IInvocationContext ctx, EntityUid near)
    {
        var mats = GetSys<SharedMaterialStorageSystem>();
        var lookup = GetSys<EntityLookupSystem>();
        foreach (var ent in lookup.GetEntitiesInRange(Transform(near).Coordinates, 3f))
        {
            if (!TryComp<OxydArtistBenchComponent>(ent, out var bench))
                continue;

            foreach (var mat in bench.SuitableMaterials)
                mats.TryChangeMaterialAmount(ent, mat, 200);

            ctx.WriteLine($"seeded materials on {EntityManager.ToPrettyString(ent)}");
            return;
        }

        ctx.WriteLine("no artist bench in range to seed");
    }

    [CommandImplementation("suspend")]
    public void Suspend(IInvocationContext ctx, int account, bool suspended)
    {
        if (!Eco.TrySetSuspended(account, suspended))
        {
            ctx.WriteLine($"acct={account}: suspend failed");
            return;
        }

        ctx.WriteLine($"acct={account} suspended={suspended}");
    }

    [CommandImplementation("list")]
    public void List(IInvocationContext ctx)
    {
        Eco.EnsureDepartmentsForRound();
        foreach (var acc in Eco.Accounts.Values.OrderBy(a => a.AccountNumber))
        {
            ctx.WriteLine(
                $"#{acc.AccountNumber} {acc.OwnerName} bal={acc.Balance}{(acc.Suspended ? " SUS" : "")}{(acc.IsDepartment ? " DEPT" : "")}");
        }
    }

    [CommandImplementation("sync")]
    public void Sync(IInvocationContext ctx)
    {
        Eco.SyncDepartmentsFromCargo();
        ctx.WriteLine("department accounts synced from cargo bank");
    }

    [CommandImplementation("trade")]
    public void Trade(IInvocationContext ctx, string mode, string? arg = null)
    {
        var trade = GetSys<OxydTradeSystem>();
        trade.EnsureSeeded();
        Eco.EnsureDepartmentsForRound();

        switch (mode.ToLowerInvariant())
        {
            case "stations":
                foreach (var st in trade.Stations.Values)
                {
                    ctx.WriteLine(
                        $"{(st.Discovered ? "*" : " ")} {st.Id} '{st.Name}' goods={st.Goods.Count} offers={st.Offers.Count} wealth={st.Wealth} favor={st.Favor}");
                }

                break;
            case "discover":
                if (string.IsNullOrWhiteSpace(arg))
                {
                    ctx.WriteLine("usage: economy:trade discover <stationId>");
                    return;
                }

                trade.DiscoverStation(arg);
                ctx.WriteLine($"discovered {arg}");
                break;
            case "orders":
                foreach (var o in trade.Orders)
                {
                    ctx.WriteLine(
                        $"{o.OrderId} {(o.Fulfilled ? "DONE" : "OPEN")} acct={o.RequestingAccount} cost={o.Cost}+{o.Fee} {o.ContentsSummary}");
                }

                break;
            case "restock":
                trade.RestockTick();
                ctx.WriteLine("trade restock tick");
                break;
            case "buy":
                // Quick path: buy 1 unit of product from Vermouth as Guild and spawn at feet
                var uid = ExecutingEntity(ctx) ?? throw new InvalidOperationException("no executing entity");
                if (string.IsNullOrWhiteSpace(arg))
                {
                    ctx.WriteLine("usage: economy:trade buy <stationId:product>");
                    return;
                }

                var parts = arg.Split(':', 2);
                if (parts.Length != 2)
                {
                    ctx.WriteLine("usage: economy:trade buy <stationId:product>");
                    return;
                }

                var guild = Eco.FindDepartmentAccount(OxydEconomyConstants.EmployerGuild)
                            ?? throw new InvalidOperationException("no guild");
                var loader = uid; // cart keyed by loader; use self
                trade.ClearCart(loader);
                if (!trade.TryAddToCart(loader, parts[0], parts[1], 1, out var err))
                {
                    ctx.WriteLine($"buy add failed: {err}");
                    return;
                }

                if (!trade.TryPlaceOrder(loader, guild.AccountNumber, "economy:trade buy", out var order, out err) || order == null)
                {
                    ctx.WriteLine($"buy order failed: {err}");
                    return;
                }

                if (!trade.TryFulfillOrder(order.OrderId, Transform(uid).Coordinates, out err))
                {
                    ctx.WriteLine($"buy fulfill failed: {err}");
                    return;
                }

                ctx.WriteLine($"bought {arg} as {order.OrderId}");
                break;
            default:
                ctx.WriteLine("usage: economy:trade stations|discover|orders|restock|buy");
                break;
        }
    }
}
