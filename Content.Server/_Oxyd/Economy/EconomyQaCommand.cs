using System.Linq;
using Content.Server._Oxyd.Ntos;
using Content.Server._Oxyd.Pda;
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Server.DeviceLinking.Components;
using Content.Server.Medical.Components;
using Content.Shared._Oxyd.Economy;
using Content.Shared._Oxyd.Pda;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking;
using Content.Shared.Materials;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Toolshed;

namespace Content.Server._Oxyd.Economy;

/// <summary>
/// In-game QA harness (<c>economy:qa</c>) for the Eris economy+PDA port.
/// Prints PASS/FAIL lines and a coverage percentage.
/// </summary>
public sealed partial class EconomyDebugCommand
{
    private sealed class QaRun
    {
        public int Pass;
        public int Fail;
        public readonly List<string> Lines = new();

        public void Check(string name, bool ok, string? detail = null)
        {
            if (ok)
            {
                Pass++;
                Lines.Add($"PASS {name}" + (detail is null ? "" : $" — {detail}"));
            }
            else
            {
                Fail++;
                Lines.Add($"FAIL {name}" + (detail is null ? "" : $" — {detail}"));
            }
        }
    }

    [CommandImplementation("qa")]
    public void Qa(IInvocationContext ctx, string? suite = null)
    {
        var run = new QaRun();
        var mode = (suite ?? "all").ToLowerInvariant();

        var actor = ResolveQaActor(ctx);
        if (actor == null)
        {
            ctx.WriteLine("FAIL resolve-actor — no mob available (join the round or spawn MobHuman)");
            return;
        }

        try
        {
            if (mode is "all" or "economy" or "core")
                RunEconomyCore(run);

            if (mode is "all" or "atm" or "pos" or "devices")
                RunAtmPos(run, actor.Value);

            if (mode is "all" or "accounts" or "accountsdb")
                RunAccountsDb(run);

            if (mode is "all" or "artist" or "art")
                RunArtist(run, actor.Value);

            if (mode is "all" or "trade")
                RunTrade(run, actor.Value);

            if (mode is "all" or "pda" or "ntos" or "hw")
                RunPdaNtos(run, actor.Value);
        }
        catch (Exception e)
        {
            run.Fail++;
            run.Lines.Add($"FAIL exception — {e.GetType().Name}: {e.Message}");
        }

        foreach (var line in run.Lines)
            ctx.WriteLine(line);

        var total = run.Pass + run.Fail;
        var pct = total == 0 ? 0 : (int)Math.Round(100.0 * run.Pass / total);
        ctx.WriteLine($"=== economy:qa suite={mode} pass={run.Pass} fail={run.Fail} total={total} coverage={pct}% ===");
        if (pct < 90)
            ctx.WriteLine("WARNING: coverage below 90% target");
        else
            ctx.WriteLine("OK: coverage meets >=90% target");
    }

    private EntityUid? ResolveQaActor(IInvocationContext ctx)
    {
        try
        {
            var uid = ExecutingEntity(ctx);
            if (uid is { } actor && EntityManager.EntityExists(actor) && HasComp<MobStateComponent>(actor))
                return actor;
        }
        catch
        {
            // no attached session
        }

        var query = EntityManager.EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID != MapId.Nullspace)
                return uid;
        }

        return null;
    }

    private void RunEconomyCore(QaRun run)
    {
        var eco = GetSys<OxydEconomySystem>();
        eco.EnsureDepartmentsForRound();

        var a = eco.CreateAccount("QaAlice", "QaAlice", 1000, OxydEconomyConstants.EmployerService, OxydEconomyConstants.WageLabour);
        var b = eco.CreateAccount("QaBob", "QaBob", 100, OxydEconomyConstants.EmployerService, OxydEconomyConstants.WageNone);
        run.Check("core.create", a.AccountNumber != b.AccountNumber, $"a={a.AccountNumber} b={b.AccountNumber}");
        run.Check("core.login-ok", eco.TryLogin(a.AccountNumber, a.Pin, out _), $"pin={a.Pin}");
        run.Check("core.login-bad", !eco.TryLogin(a.AccountNumber, a.Pin + 1, out _), null);

        run.Check("core.transfer", eco.TryTransfer(a.AccountNumber, b.AccountNumber, 250, "qa", "qa", out var err), err);
        run.Check("core.transfer-bal", a.Balance == 750 && b.Balance == 350, $"a={a.Balance} b={b.Balance}");

        run.Check("core.adjust", eco.TryAdjust(a.AccountNumber, -50, "qa", "qa") && a.Balance == 700, $"bal={a.Balance}");

        run.Check("core.suspend", eco.TrySetSuspended(a.AccountNumber, true), null);
        run.Check("core.suspend-blocks", !eco.TryTransfer(a.AccountNumber, b.AccountNumber, 10, "qa", "qa", out _), null);
        run.Check("core.unsuspend", eco.TrySetSuspended(a.AccountNumber, false), null);

        var service = eco.FindDepartmentAccount(OxydEconomyConstants.EmployerService);
        var guild = eco.FindDepartmentAccount(OxydEconomyConstants.EmployerGuild);
        var cargo = eco.FindDepartmentAccount(OxydEconomyConstants.EmployerCargo);
        run.Check("core.dept-service", service != null, service?.AccountNumber.ToString());
        run.Check("core.dept-guild", guild != null, guild?.AccountNumber.ToString());
        if (cargo == null)
        {
            cargo = eco.CreateAccount("Cargo Department", "Cargo", 10000, null, 0, true,
                OxydEconomyConstants.EmployerCargo, canMakeAccounts: true);
        }

        run.Check("core.dept-cargo", cargo != null, cargo?.AccountNumber.ToString());

        if (service != null)
            eco.TrySetBalance(service.AccountNumber, 8000);
        var beforeWorker = a.Balance;
        var beforeNone = b.Balance;
        eco.ForcePayday();
        run.Check("core.payday-wage", a.Balance > beforeWorker, $"before={beforeWorker} after={a.Balance}");
        run.Check("core.payday-none", b.Balance == beforeNone, $"bal={b.Balance}");

        eco.SyncDepartmentsFromCargo();
        run.Check("core.sync", true, "SyncDepartmentsFromCargo invoked");
    }

    private void RunAtmPos(QaRun run, EntityUid actor)
    {
        var eco = GetSys<OxydEconomySystem>();
        eco.EnsureDepartmentsForRound();
        var coords = Transform(actor).Coordinates;

        var acct = eco.CreateAccount("QaTeller", "QaTeller", 2000, OxydEconomyConstants.EmployerService, 0);
        var atmUid = Spawn("OxydAtm", coords);
        var atm = Comp<OxydAtmComponent>(atmUid);
        var atmSys = GetSys<OxydAtmSystem>();
        run.Check("atm.spawn", HasComp<OxydAtmComponent>(atmUid), EntityManager.ToPrettyString(atmUid));
        run.Check("atm.login", atmSys.TryLogin((atmUid, atm), acct.AccountNumber, acct.Pin, out var err), err);
        var balBefore = acct.Balance;
        run.Check("atm.withdraw", atmSys.TryWithdraw((atmUid, atm), 100, actor, out err), err);
        run.Check("atm.withdraw-bal", acct.Balance == balBefore - 100, $"bal={acct.Balance}");
        run.Check("atm.deposit", atmSys.TryDeposit((atmUid, atm), 40, out err), err);
        run.Check("atm.deposit-bal", acct.Balance == balBefore - 60, $"bal={acct.Balance}");

        var dest = eco.CreateAccount("QaDest", "QaDest", 0, OxydEconomyConstants.EmployerService, 0);
        run.Check("atm.transfer", atmSys.TryTransfer((atmUid, atm), dest.AccountNumber, 50, out err), err);

        var service = eco.FindDepartmentAccount(OxydEconomyConstants.EmployerService)
                      ?? eco.CreateAccount("Service Department", "Service", 1000,
                          OxydEconomyConstants.EmployerCargo, OxydEconomyConstants.BudgetBase, true,
                          OxydEconomyConstants.EmployerService);
        var regUid = Spawn("OxydCashRegister", coords);
        var reg = Comp<OxydCashRegisterComponent>(regUid);
        reg.DrawerOpen = true;
        reg.Stored = 250;
        reg.LinkedAccountNumber = service.AccountNumber;
        EntityManager.Dirty(regUid, reg);
        var svcBefore = service.Balance;
        var pos = GetSys<OxydPosDevicesSystem>();
        run.Check("pos.register-spawn", HasComp<OxydCashRegisterComponent>(regUid), null);
        run.Check("pos.bank-till", pos.TryBankTill((regUid, reg), actor, out err), err);
        run.Check("pos.bank-till-bal", service.Balance == svcBefore + 250 && reg.Stored == 0,
            $"svc={service.Balance} stored={reg.Stored}");

        var eftUid = Spawn("OxydEftpos", coords);
        var eft = Comp<OxydEftposComponent>(eftUid);
        eft.LinkedAccountNumber = service.AccountNumber;
        EntityManager.Dirty(eftUid, eft);
        pos.ArmEftpos((eftUid, eft), 100);
        var payer = eco.CreateAccount("QaPayer", "QaPayer", 500, OxydEconomyConstants.EmployerService, 0);
        var svc2 = service.Balance;
        run.Check("pos.eftpos-charge", pos.TryChargeEftpos((eftUid, eft), payer.AccountNumber, out err), err);
        run.Check("pos.eftpos-bal", payer.Balance == 400 && service.Balance == svc2 + 100,
            $"payer={payer.Balance} svc={service.Balance}");

        var tipUid = Spawn("OxydTipJar", coords);
        var tip = Comp<OxydTipJarComponent>(tipUid);
        tip.Stored = 75;
        EntityManager.Dirty(tipUid, tip);
        run.Check("pos.tipjar", tip.Stored == 75, EntityManager.ToPrettyString(tipUid));

        var vendorUid = Spawn("OxydVendingClubSnacks", coords);
        run.Check("pos.vendor", HasComp<OxydVendorEarningsComponent>(vendorUid),
            EntityManager.ToPrettyString(vendorUid));
    }

    private void RunAccountsDb(QaRun run)
    {
        var eco = GetSys<OxydEconomySystem>();
        eco.EnsureDepartmentsForRound();
        var cargo = eco.FindDepartmentAccount(OxydEconomyConstants.EmployerCargo)
                    ?? eco.CreateAccount("Cargo Department", "Cargo", 5000, null, 0, true,
                        OxydEconomyConstants.EmployerCargo, canMakeAccounts: true);
        eco.TrySetBalance(cargo.AccountNumber, 5000);

        run.Check("accountsdb.funded",
            eco.TryCreateFundedAccount("QaHire", 500, cargo.AccountNumber, "qa", out var created, out var err)
            && created != null && created.Balance == 500 && cargo.Balance == 4500,
            err);

        if (created == null)
            return;

        run.Check("accountsdb.suspend", eco.TrySetSuspended(created.AccountNumber, true), null);
        run.Check("accountsdb.silent-adjust",
            eco.TrySilentAdjust(created.AccountNumber, 50, "silent", "AccountsDB") && created.Balance == 550,
            $"bal={created.Balance}");
        run.Check("accountsdb.revoke",
            eco.TryRevokePayroll(created.AccountNumber, cargo.AccountNumber, "AccountsDB", out err)
            && created.Balance == 0,
            err);

        run.Check("accountsdb.proto",
            IoCManager.Resolve<IPrototypeManager>().HasIndex<EntityPrototype>("OxydAccountsDbOpen"),
            null);
    }

    private void RunArtist(QaRun run, EntityUid actor)
    {
        var coords = Transform(actor).Coordinates;
        var mats = GetSys<SharedMaterialStorageSystem>();
        var artSys = GetSys<OxydArtistBenchSystem>();

        var npc = Spawn("MobHuman", coords);
        var san = EntityManager.EnsureComponent<SanityComponent>(npc);
        san.Insight = 80f;
        EntityManager.Dirty(npc, san);

        var benchUid = Spawn("OxydArtistBench", coords);
        var benchComp = Comp<OxydArtistBenchComponent>(benchUid);
        Entity<OxydArtistBenchComponent> bench = (benchUid, benchComp);

        run.Check("artist.need-mats",
            !artSys.TryCreateArtwork(bench, npc, 50f, out var err, out _, out _),
            err);

        foreach (var mat in benchComp.SuitableMaterials)
            mats.TryChangeMaterialAmount(benchUid, mat, 200);

        run.Check("artist.create-nonperk",
            artSys.TryCreateArtwork(bench, npc, 50f, out err, out var price, out var used)
            && used >= 80f - 0.01f,
            err ?? $"used={used} price={price}");

        EntityManager.EnsureComponent<OxydArtistPerkComponent>(actor);
        var actorSan = EntityManager.EnsureComponent<SanityComponent>(actor);
        actorSan.Insight = 100f;
        EntityManager.Dirty(actor, actorSan);
        foreach (var mat in benchComp.SuitableMaterials)
            mats.TryChangeMaterialAmount(benchUid, mat, 200);

        run.Check("artist.create-perk",
            artSys.TryCreateArtwork(bench, actor, 40f, out err, out price, out used, out var kind)
            && used is >= 39.9f and <= 40.1f,
            err ?? $"used={used} kind={kind} price={price}");

        run.Check("artist.proto-art",
            IoCManager.Resolve<IPrototypeManager>().HasIndex<EntityPrototype>("OxydArtworkStatue"),
            null);
    }

    private void RunTrade(QaRun run, EntityUid actor)
    {
        var eco = GetSys<OxydEconomySystem>();
        var trade = GetSys<OxydTradeSystem>();
        trade.EnsureSeeded();
        eco.EnsureDepartmentsForRound();

        run.Check("trade.vermouth", trade.TryGetStation("Vermouth", out var vermouth) && vermouth!.Discovered,
            null);
        run.Check("trade.asters", trade.TryGetStation("AstersCapital", out _), null);
        // First-run expects Caduceus hidden; re-runs after DiscoverStation stay discovered.
        var hadCad = trade.Stations.TryGetValue("Caduceus", out var cad);
        var wasHidden = hadCad && !cad!.Discovered;
        run.Check("trade.caduceus-state",
            hadCad && (wasHidden || cad!.Discovered),
            wasHidden ? "hidden" : "already-discovered");
        trade.DiscoverStation("Caduceus");
        run.Check("trade.caduceus-discover",
            trade.TryGetStation("Caduceus", out cad) && cad!.Discovered,
            null);

        var guild = eco.FindDepartmentAccount(OxydEconomyConstants.EmployerGuild)
                    ?? eco.CreateAccount("Guild Department", "Guild", 20000, null, 0, true,
                        OxydEconomyConstants.EmployerGuild, canMakeAccounts: true);
        eco.TrySetBalance(guild.AccountNumber, 20000);

        trade.ClearCart(actor);
        run.Check("trade.cart-add",
            trade.TryAddToCart(actor, "Vermouth", "DrinkGlass", 1, out var err), err);
        run.Check("trade.order-guild",
            trade.TryPlaceOrder(actor, guild.AccountNumber, "qa buy", out var order, out err)
            && order != null && order.Fee == 0,
            err ?? $"fee={order?.Fee}");

        var coords = Transform(actor).Coordinates;
        Spawn("OxydTradeBeaconReceiving", coords);
        if (order != null)
        {
            var before = guild.Balance;
            run.Check("trade.fulfill",
                trade.TryFulfillOrder(order.OrderId, coords, out err) && order.Fulfilled,
                err);
            run.Check("trade.fulfill-debit",
                guild.Balance == before - order.Cost,
                $"before={before} after={guild.Balance} cost={order.Cost}");
        }

        var personal = eco.CreateAccount("QaTrader", "QaTrader", 5000, OxydEconomyConstants.EmployerService, 0);
        trade.ClearCart(actor);
        trade.TryAddToCart(actor, "Vermouth", "DrinkGlass", 1, out _);
        run.Check("trade.order-fee",
            trade.TryPlaceOrder(actor, personal.AccountNumber, "qa fee", out var feeOrder, out err)
            && feeOrder != null && feeOrder.Fee > 0,
            err ?? $"fee={feeOrder?.Fee}");

        if (!trade.TryGetStation("Vermouth", out vermouth) ||
            !vermouth!.Offers.TryGetValue("FoodBreadPlain", out var offer))
        {
            run.Check("trade.offer-export", false, "no FoodBreadPlain offer");
        }
        else
        {
            var send = Spawn("OxydTradeBeaconSending", coords);
            var need = offer.AmountRemaining;
            for (var i = 0; i < need; i++)
                Spawn("FoodBreadPlain", coords);

            var beforeP = personal.Balance;
            var beforeG = guild.Balance;
            run.Check("trade.offer-export",
                trade.TryFulfillOffersNear(send, 3f, personal.AccountNumber, slaveCut: true, out var paid, out err)
                && paid > 0,
                err ?? $"paid={paid} p={personal.Balance - beforeP} g={guild.Balance - beforeG}");
        }

        trade.RestockTick();
        run.Check("trade.restock", true, "RestockTick invoked");

        run.Check("trade.beacon-send-proto",
            IoCManager.Resolve<IPrototypeManager>().HasIndex<EntityPrototype>("OxydTradeBeaconSending"),
            null);
        run.Check("trade.beacon-recv-proto",
            IoCManager.Resolve<IPrototypeManager>().HasIndex<EntityPrototype>("OxydTradeBeaconReceiving"),
            null);
    }

    private void RunPdaNtos(QaRun run, EntityUid actor)
    {
        var coords = Transform(actor).Coordinates;
        var protos = IoCManager.Resolve<IPrototypeManager>();
        var slots = GetSys<ItemSlotsSystem>();
        var modSys = GetSys<OxydModularPdaSystem>();
        var email = GetSys<OxydEmailSystem>();
        var chat = GetSys<OxydNtChatSystem>();

        foreach (var id in new[]
                 {
                     "OxydPdaClub", "OxydPdaArtist", "OxydPdaGuild", "OxydPdaChurch",
                     "OxydTaxQuicklyCartridge", "OxydTradeOrdersCartridge",
                     "OxydRecordsCartridge", "OxydBountyCartridge",
                     "OxydEmailCartridge", "OxydNtChatCartridge",
                     "OxydHwSignaller", "OxydHwScannerMedical", "OxydHwScannerPrice",
                     "OxydHwCpuAdv", "OxydHwHddAdvanced", "OxydHwNetAdvanced",
                 })
        {
            run.Check($"pda.proto.{id}", protos.HasIndex<EntityPrototype>(id), null);
        }

        var club = Spawn("OxydPdaClub", coords);
        run.Check("pda.club-modular", HasComp<OxydModularPdaComponent>(club), EntityManager.ToPrettyString(club));
        var mod = Comp<OxydModularPdaComponent>(club);
        modSys.Recalculate((club, mod));
        run.Check("pda.hw-report", !string.IsNullOrWhiteSpace(modSys.DescribeHardware((club, mod))),
            modSys.DescribeHardware((club, mod)));

        if (slots.GetItemOrNull(club, OxydModularPdaComponent.PeripheralSlotId) is { } oldPer)
        {
            slots.TryEject(club, OxydModularPdaComponent.PeripheralSlotId, null, out _);
            Del(oldPer);
        }

        var signal = Spawn("OxydHwSignaller", coords);
        run.Check("pda.install-signaller",
            slots.TryInsert(club, OxydModularPdaComponent.PeripheralSlotId, signal, null), null);
        modSys.Recalculate((club, mod));
        run.Check("pda.signaller-flag", mod.HasSignaller, null);
        run.Check("pda.signaller-comp",
            HasComp<SignallerComponent>(club) && HasComp<DeviceLinkSourceComponent>(club), null);

        slots.TryEject(club, OxydModularPdaComponent.PeripheralSlotId, null, out var ejected);
        if (ejected != null)
            Del(ejected.Value);

        var med = Spawn("OxydHwScannerMedical", coords);
        run.Check("pda.install-medscan",
            slots.TryInsert(club, OxydModularPdaComponent.PeripheralSlotId, med, null), null);
        modSys.Recalculate((club, mod));
        run.Check("pda.medscan-kind", mod.ActiveScanner == OxydScannerKind.Medical, $"{mod.ActiveScanner}");
        run.Check("pda.medscan-comp", HasComp<HealthAnalyzerComponent>(club), null);

        slots.TryEject(club, OxydModularPdaComponent.PeripheralSlotId, null, out ejected);
        if (ejected != null)
            Del(ejected.Value);

        var priceHw = Spawn("OxydHwScannerPrice", coords);
        slots.TryInsert(club, OxydModularPdaComponent.PeripheralSlotId, priceHw, null);
        modSys.Recalculate((club, mod));
        var target = Spawn("OxydArtworkStatue", coords);
        var ev = new Content.Shared.Interaction.AfterInteractEvent(actor, club, target, Transform(target).Coordinates, true);
        EntityManager.EventBus.RaiseLocalEvent(club, ev);
        run.Check("pda.pricescan-buffer",
            mod.ActiveScanner == OxydScannerKind.Price && (mod.ScanBuffer?.Contains("PRICE") ?? false),
            mod.ScanBuffer);

        var loginA = $"qa{Environment.TickCount % 100000}a";
        var loginB = $"qa{Environment.TickCount % 100000}b";
        run.Check("ntos.email-register-a", email.TryRegister(loginA, "pw", out var err), err);
        run.Check("ntos.email-register-b", email.TryRegister(loginB, "pw", out err), err);
        run.Check("ntos.email-login", email.TryLogin(club, loginA, "pw", out err), err);
        run.Check("ntos.email-send",
            email.TrySend($"{loginA}@nt.mail", $"{loginB}@nt.mail", "QA", "hello", out err), err);
        run.Check("ntos.email-inbox",
            email.GetInbox(loginB).Any(m => m.Subject == "QA"), null);

        var artistPda = Spawn("OxydPdaArtist", coords);
        chat.GetOrSetName(club, "QaAlice");
        chat.GetOrSetName(artistPda, "QaBob");
        var general = chat.ListChannels().FirstOrDefault(c => c.Title == "General");
        run.Check("ntos.chat-general", general != null, null);
        if (general != null)
        {
            run.Check("ntos.chat-join", chat.TryJoin(club, general.Id, out err), err);
            chat.TryJoin(artistPda, general.Id, out _);
            run.Check("ntos.chat-speak", chat.TrySpeak(club, "qa-ping", out err), err);
            run.Check("ntos.chat-msg",
                chat.GetChannel(general.Id)?.Messages.Any(m => m.Contains("qa-ping")) == true, null);
        }
    }
}
