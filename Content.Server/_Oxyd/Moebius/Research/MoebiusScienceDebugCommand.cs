// Eris Moebius research port: debug commands for live verification.
// Mirrors the LitanyDebugCommand pattern (colon subcommands, uid args).
// Used to drive paths the stock admin verbs can't: simulated blasts for the
// explosion watcher, lathe material feeds, forced node unlocks, point grants.
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared._Oxyd.Moebius.Research;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Toolshed;
using Robust.Shared.Toolshed.Syntax;

namespace Content.Server._Oxyd.Moebius.Research;

[ToolshedCommand, AdminCommand(AdminFlags.Debug)]
public sealed class MoebiusScienceCommand : ToolshedCommand
{
    private DamageableSystem? _dmg;
    private SharedMaterialStorageSystem? _mat;
    private IPrototypeManager? _proto;

    /// <summary>Anchors an entity in place (watcher/console verification).</summary>
    [CommandImplementation("anchor")]
    public void Anchor(IInvocationContext ctx, EntityUid ent)
    {
        Transform(ent).Anchored = true;
        ctx.WriteLine($"moebius: anchored {ent}");
    }

    /// <summary>Simulate a blast on an explosion watcher: deals Blast damage so
    /// the watcher's DamageChanged handler banks points (real explosions need
    /// atmos+bombs; the banking path is identical).</summary>
    [CommandImplementation("blast")]
    public void Blast(IInvocationContext ctx, EntityUid watcher, int amount = 100)
    {
        _dmg ??= GetSys<DamageableSystem>();
        var spec = new DamageSpecifier();
        spec.DamageDict["Structural"] = amount; // explosions deal Structural to inorganic objects
        var done = _dmg.ChangeDamage(watcher, spec);
        var banked = TryComp<OxydExplosionWatcherComponent>(watcher, out var w) ? w.BankedPoints : -1;
        ctx.WriteLine($"moebius: dealt {done.GetTotal()} blast to {watcher}, bankedPoints={banked}");
    }

    /// <summary>Load a linked lathe with steel+glass so Queue can be exercised
    /// from the console BUI without dragging stacks.</summary>
    [CommandImplementation("feed")]
    public void Feed(IInvocationContext ctx, EntityUid lathe, int amount = 1000)
    {
        _mat ??= GetSys<SharedMaterialStorageSystem>();
        var ok = _mat.TryChangeMaterialAmount(lathe, "Steel", amount)
              && _mat.TryChangeMaterialAmount(lathe, "Glass", amount)
              && _mat.TryChangeMaterialAmount(lathe, "Gold", amount);
        ctx.WriteLine(ok ? $"moebius: fed {amount} steel+glass+gold to {lathe}"
                         : $"moebius: {lathe} has no MaterialStorageComponent");
    }

    /// <summary>Force-unlock a tech node on a console (skips point cost and
    /// prereqs) so gated packs can be checked on linked lathes.</summary>
    [CommandImplementation("unlock")]
    public void Unlock(IInvocationContext ctx, EntityUid console, string node)
    {
        if (!TryComp<OxydRdConsoleComponent>(console, out _))
        {
            ctx.WriteLine($"moebius: {console} is not an Oxyd R&D console");
            return;
        }
        GetSys<OxydResearchSystem>().DebugForceUnlock(console, node);
        ctx.WriteLine($"moebius: unlocked {node} on {console} + synced linked lathes");
    }

    /// <summary>Raise UseInHandEvent on an item as if `user` activated it in-hand
    /// (mental imprinter, drone shell deploy).</summary>
    [CommandImplementation("use")]
    public void Use(IInvocationContext ctx, EntityUid item, EntityUid user)
    {
        EntityManager.EventBus.RaiseLocalEvent(item, new UseInHandEvent(user));
        ctx.WriteLine($"moebius: UseInHand {item} by {user}");
    }

    /// <summary>Raise ActivateInWorldEvent on a target (drone fabricator cycle).</summary>
    [CommandImplementation("activate")]
    public void Activate(IInvocationContext ctx, EntityUid target, EntityUid user)
    {
        EntityManager.EventBus.RaiseLocalEvent(target, new ActivateInWorldEvent(user, target, false));
        ctx.WriteLine($"moebius: Activate {target} by {user}");
    }

    /// <summary>Replay the real interact-using chain (SharedInteractionSystem
    /// lines ~1058-1112): InteractUsingEvent on TARGET (slot inserts), then
    /// AfterInteractEvent on the USED item (tool scans/extracts), then
    /// AfterInteractUsingEvent on TARGET.</summary>
    [CommandImplementation("useon")]
    public void UseOn(IInvocationContext ctx, EntityUid item, EntityUid target, EntityUid user)
    {
        var coords = Transform(target).Coordinates;
        var ev = new InteractUsingEvent(user, item, target, coords);
        EntityManager.EventBus.RaiseLocalEvent(target, ev);
        var handled = ev.Handled;
        if (!handled)
        {
            var ae = new AfterInteractEvent(user, item, target, coords, true);
            EntityManager.EventBus.RaiseLocalEvent(item, ae);
            handled = ae.Handled;
            if (!handled)
            {
                var aue = new AfterInteractUsingEvent(user, item, target, coords, true);
                EntityManager.EventBus.RaiseLocalEvent(target, aue);
                handled = aue.Handled;
            }
        }
        ctx.WriteLine($"moebius: UseOn {item} -> {target} by {user} handled={handled}");
    }

    /// <summary>Put an item into the user's free/active hand — needed before
    /// useon steps that gate on holding the tool (wires panel screwdriver,
    /// DoAfter NeedHand checks).</summary>
    [CommandImplementation("hold")]
    public void Hold(IInvocationContext ctx, EntityUid item, EntityUid user)
    {
        var hands = GetSys<SharedHandsSystem>();
        var ok = hands.TryPickupAnyHand(user, item);
        ctx.WriteLine($"moebius: hold {item} by {user} = {ok}");
    }

    /// <summary>Marks a spawned machine powered (spawned entities aren't wired
    /// into an APC net, so ApcPowerReceiver.Powered stays false and lathes/BUIs
    /// refuse to run).</summary>
    [CommandImplementation("power")]
    public void Power(IInvocationContext ctx, EntityUid ent)
    {
        if (TryComp<Content.Server.Power.Components.ApcPowerReceiverComponent>(ent, out var r))
        {
            r.Powered = true;
            ctx.WriteLine($"moebius: powered {ent}");
        }
        else
            ctx.WriteLine($"moebius: {ent} has no ApcPowerReceiverComponent");
    }

    /// <summary>Queues a recipe on a lathe and kicks production, replaying the
    /// console Queue->print path server-side (same API the lathe BUI uses).</summary>
    [CommandImplementation("queue")]
    public void Queue(IInvocationContext ctx, EntityUid lathe, string recipeId)
    {
        _proto ??= IoCManager.Resolve<IPrototypeManager>();
        var latheSys = GetSys<Content.Server.Lathe.LatheSystem>();
        if (!_proto.TryIndex<Content.Shared.Research.Prototypes.LatheRecipePrototype>(recipeId, out var recipe))
        {
            ctx.WriteLine($"moebius: queue on {lathe}: no recipe '{recipeId}'");
            return;
        }
        var queued = latheSys.TryAddToQueue(lathe, recipe, 1);
        var started = queued && latheSys.TryStartProducing(lathe);
        ctx.WriteLine($"moebius: queue '{recipeId}' on {lathe} queued={queued} started={started}");
    }

    /// <summary>Completes the current batch on a lathe now — same code path as
    /// the producing timer's expiry (LatheSystem.FinishProducing), for runs where
    /// no client is connected to tick game time.</summary>
    [CommandImplementation("finish")]
    public void Finish(IInvocationContext ctx, EntityUid lathe)
    {
        var latheSys = GetSys<Content.Server.Lathe.LatheSystem>();
        latheSys.FinishProducing(lathe);
        ctx.WriteLine($"moebius: finish {lathe}");
    }

    /// <summary>Grant research points to a console.</summary>
    [CommandImplementation("points")]
    public void Points(IInvocationContext ctx, EntityUid console, int amount)
    {
        if (!TryComp<OxydRdConsoleComponent>(console, out var comp))
        {
            ctx.WriteLine($"moebius: {console} is not an Oxyd R&D console");
            return;
        }
        comp.Points += amount;
        ctx.WriteLine($"moebius: {console} now has {comp.Points} points");
    }

    /// <summary>Dump the verification-relevant fields of every Oxyd research
    /// component on an entity (server-side evidence without a client).</summary>
    [CommandImplementation("probe")]
    public void Probe(IInvocationContext ctx, EntityUid ent)
    {
        if (TryComp<OxydRdConsoleComponent>(ent, out var con))
            ctx.WriteLine($"probe:console pts={con.Points} unlocked={con.UnlockedNodes.Count} linked={con.LinkedDevices.Count} trees=[{string.Join(", ", con.TreeLevels)}]");
        if (TryComp<OxydDestructiveAnalyzerComponent>(ent, out var da))
            ctx.WriteLine($"probe:da busy={da.Busy} until={da.BusyUntil}");
        if (TryComp<OxydScienceToolComponent>(ent, out var t))
            ctx.WriteLine($"probe:tool blocks={t.Blocks.Count} total={t.TotalValue} first={(t.Blocks.Count > 0 ? t.Blocks[0].Subject : "-")}");
        if (TryComp<OxydExplosionWatcherComponent>(ent, out var w))
            ctx.WriteLine($"probe:watcher banked={w.BankedPoints} n={w.ExplosionsRecorded}");
        if (TryComp<OxydResearchPointsDiskComponent>(ent, out var pd))
            ctx.WriteLine($"probe:pdisk pts={pd.Points} spent={pd.Spent}");
        if (TryComp<OxydResearchDataDiskComponent>(ent, out var dd))
            ctx.WriteLine($"probe:ddisk nodes=[{string.Join(", ", dd.StoredNodes)}] designs={dd.StoredDesigns.Count}");
        if (TryComp<OxydAutopsyReportComponent>(ent, out var ar))
            ctx.WriteLine($"probe:autopsy report='{ar.Report}' v={ar.Value}");
        if (TryComp<OxydOriginTechComponent>(ent, out var ot))
            ctx.WriteLine($"probe:origintech=[{string.Join(", ", ot.Techs)}]");
        if (TryComp<Content.Shared.Containers.ItemSlots.ItemSlotsComponent>(ent, out var isc))
            ctx.WriteLine($"probe:itemslots=[{string.Join(", ", isc.Slots.Keys)}]");
        ctx.WriteLine("probe:done");
    }

    /// <summary>Read the contents of an ItemSlots slot (da input, console disk/tool).</summary>
    [CommandImplementation("slot")]
    public void Slot(IInvocationContext ctx, EntityUid ent, string slotId)
    {
        var slots = GetSys<Content.Shared.Containers.ItemSlots.ItemSlotsSystem>();
        var item = slots.GetItemOrNull(ent, slotId);
        ctx.WriteLine($"probe:slot {ent}[{slotId}] = {(item == null ? "empty" : item.ToString())}");
    }

    /// <summary>Insert an item into an ItemSlots slot directly (the same call
    /// TryInsertFromHand reaches once the hand check passes) — fires the
    /// EntInsertedIntoContainer handlers the real path does.</summary>
    [CommandImplementation("insert")]
    public void Insert(IInvocationContext ctx, EntityUid target, EntityUid item, string slotId)
    {
        var slots = GetSys<Content.Shared.Containers.ItemSlots.ItemSlotsSystem>();
        var ok = slots.TryInsert(target, slotId, item, null);
        ctx.WriteLine($"moebius: insert {item} -> {target}[{slotId}] = {ok}");
    }

    /// <summary>Replay a console BUI message against the same handlers a client
    /// click hits (Actor defaults to the console so popup targets stay valid).
    /// Ops: deconstruct, rescan, uploadtool, ejectitem, diskupload, diskdownload,
    /// diskclear, diskeject, unlockNode=&lt;id&gt;.</summary>
    [CommandImplementation("bui")]
    public void Bui(IInvocationContext ctx, EntityUid console, string op, EntityUid actor)
    {
        BoundUserInterfaceMessage? msg = op switch
        {
            "deconstruct" => new OxydRdDeconstructMessage(),
            "rescan" => new OxydRdRescanMessage(),
            "uploadtool" => new OxydRdUploadToolMessage(),
            "ejectitem" => new OxydRdEjectItemMessage(),
            "diskupload" => new OxydRdDiskMessage { Op = OxydRdDiskOp.Upload },
            "diskdownload" => new OxydRdDiskMessage { Op = OxydRdDiskOp.Download },
            "diskclear" => new OxydRdDiskMessage { Op = OxydRdDiskOp.Clear },
            "diskeject" => new OxydRdDiskMessage { Op = OxydRdDiskOp.Eject },
            _ when op.StartsWith("unlock:") => new OxydRdUnlockMessage { Node = op.Substring(7) },
            _ => null,
        };
        if (msg == null)
        {
            ctx.WriteLine($"moebius: unknown bui op '{op}'");
            return;
        }
        msg.Actor = actor;
        // cast to object: the generic overload would dispatch on the declared
        // base type (BoundUserInterfaceMessage) and no concrete handler would fire.
        EntityManager.EventBus.RaiseLocalEvent(console, (object) msg);
        ctx.WriteLine($"moebius: bui '{op}' on {console}");
    }
}
