using Content.Server.Administration;
using Content.Shared._Oxyd.Technomancer;
using Content.Shared._Oxyd.Technomancer.Components;
using Content.Shared.Administration;
using Content.Shared.Atmos.Monitor.Components;
using Content.Shared.Atmos.Piping.Binary.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.UserInterface;
using System.Collections.Generic;
using System.Linq;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Toolshed.Syntax;
using Robust.Shared.Toolshed;

namespace Content.Server._Oxyd.Technomancer;

/// <summary>
/// Debug layout commands for the Technomancer port live-verification matrix:
/// stock spawn:on stacks ~4 copies of everything on one tile under the mob
/// sprite, so these lay each prototype out on distinct tiles instead.
/// Dev/test only — colon subcommands like litany:giveoddity.
/// </summary>
[ToolshedCommand, AdminCommand(AdminFlags.Debug)]
public sealed class TechnomancerCommand : ToolshedCommand
{
    private static readonly EntProtoId[] Machines =
    [
        "OxydSmesUnit", "OxydBreakerBox", "OxydPulsarConsole",
        "OxydPulsarPowerBridge", "OxydPulsarFuelTank", "OxydShipShieldGenerator",
        "OxydRconConsole", "OxydAtmosConsole", "OxydAirAlarm",
        "OxydCanister", "OxydOmniFilter", "OxydOmniMixer",
        "OxydTermoPump", "OxydTermoVolumePump", "OxydPassiveValve",
        "OxydNanoforge",
    ];

    private static readonly EntProtoId[] Items =
    [
        "OxydDuctTape", "OxydCombiDriver", "OxydArmatureCutter",
        "OxydPneumaticCrowbar", "OxydPoweredHammer", "OxydWelderAdv",
        "OxydToolModComplicator",
        "OxydRapidConstructionDevice", "OxydDesignDiskArk",
        "OxydDesignDiskIjiro", "OxydCompressedMatterSingle",
    ];

    /// <summary>
    /// technomancer:grid — spawn every ported machine in a spaced row on the
    /// mob's grid, one per tile, so right-click/Toggle UI works on each.
    /// </summary>
    [CommandImplementation("grid")]
    public void Grid(IInvocationContext ctx)
    {
        var uid = ExecutingEntity(ctx)
            ?? throw new InvalidOperationException("no executing entity");
        GridAt(ctx, uid);
    }

    [CommandImplementation("grid")]
    public void GridPiped(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> mobs)
    {
        foreach (var mob in mobs)
            GridAt(ctx, mob);
    }

    private void GridAt(IInvocationContext ctx, EntityUid uid)
    {
        var coords = Transform(uid).Coordinates;
        var x = coords.X;
        var y = coords.Y;
        for (var i = 0; i < Machines.Length; i++)
        {
            var at = new EntityCoordinates(coords.EntityId, x + (i % 8) * 2f, y + 1.5f + (i / 8) * 2f);
            var ent = Spawn(Machines[i], at);
            ctx.WriteLine($"{Machines[i]} -> {ent}");
        }
    }

    /// <summary>technomancer:items — the handheld gear row.</summary>
    [CommandImplementation("items")]
    public void SpawnItems(IInvocationContext ctx)
    {
        var uid = ExecutingEntity(ctx)
            ?? throw new InvalidOperationException("no executing entity");
        ItemsAt(ctx, uid);
    }

    [CommandImplementation("items")]
    public void SpawnItemsPiped(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> mobs)
    {
        foreach (var mob in mobs)
            ItemsAt(ctx, mob);
    }

    private void ItemsAt(IInvocationContext ctx, EntityUid uid)
    {
        var coords = Transform(uid).Coordinates;
        for (var i = 0; i < Items.Length; i++)
        {
            var at = new EntityCoordinates(coords.EntityId, coords.X + i * 1.5f, coords.Y - 1.5f);
            var ent = Spawn(Items[i], at);
            ctx.WriteLine($"{Items[i]} -> {ent}");
        }
    }

    /// <summary>
    /// ent &lt;uid&gt; | technomancer:bui "&lt;action&gt; [args]" — drives a
    /// ported machine's BUI server-side: raises the exact
    /// BoundUserInterfaceMessage the window's buttons send, so the real
    /// SubscribeLocalEvent handler path executes (no client clicks needed).
    ///   open                          → open the machine's first BUI for the caller
    ///   rcon &lt;targetUid&gt; &lt;input|output|breaker&gt; &lt;on|off&gt;
    ///   forge-eject                   → eject compressed matter
    ///   feed &lt;itemUid&gt;          → insert an item into the forge input slot
    ///   breaker &lt;on|off&gt;        → breaker toggle
    ///   bridge-toggle                 → pulsar bridge link toggle
    ///   pulsar-shield &lt;pct&gt;     → pulsar shield strength
    ///   pulsar-move &lt;dx&gt; &lt;dy&gt; → satellite move
    ///   shield-toggle | shield-mode &lt;id&gt; &lt;on|off&gt;
    ///   shield-size &lt;n&gt; | shield-cap &lt;kw&gt;
    ///   use                           → UseInHandEvent(user=caller) on this item
    ///   mod &lt;toolUid&gt;           → AfterInteractEvent(caller, this mod, tool)
    /// </summary>
    [CommandImplementation("bui")]
    public void Bui(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> ents, string args)
    {
        // ExecutingEntity() reports NotForServerConsoleError when run from the
        // server console; read the attached entity directly so fifo-driven
        // verification still works (the piped mob's own session is used for
        // `open` anyway).
        var exec = ctx.Session?.AttachedEntity;
        foreach (var uid in ents)
            BuiOne(ctx, uid, exec, args.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private void BuiOne(IInvocationContext ctx, EntityUid uid, EntityUid? exec, string[] a)
    {
        if (a.Length == 0)
        {
            ctx.WriteLine("no action");
            return;
        }
        var man = EntityManager;
        // Resolve the actor the way real BUI dispatch does (the sender's
        // session entity). The server console has no session, so fall back to
        // the first attached player entity — handlers like air alarm run
        // AccessCheck(args.Actor) and reject a default actor.
        EntityUid? actor = exec;
        if (actor == null)
        {
            var pm = IoCManager.Resolve<IPlayerManager>();
            foreach (var s in pm.Sessions)
            {
                if (s.AttachedEntity is { } ae)
                {
                    actor = ae;
                    break;
                }
            }
        }
        BoundUserInterfaceMessage Stamp(BoundUserInterfaceMessage m)
        {
            m.Entity = man.GetNetEntity(uid);
            if (actor is { } ac)
                m.Actor = ac;
            return m;
        }
        switch (a[0])
        {
            case "open":
            {
                // "open <machineUid>": piped entity (or executing entity) must
                // carry a player session; the machine's first BUI opens for it.
                var machine = uid;
                var who = exec ?? uid;
                if (a.Length > 1)
                    machine = new EntityUid(int.Parse(a[1]));
                Enum? key = null;
                if (man.TryGetComponent<ActivatableUIComponent>(machine, out var aui))
                    key = aui.Key;
                else if (man.TryGetComponent<UserInterfaceComponent>(machine, out var ui))
                    key = ui.ClientOpenInterfaces.Keys.FirstOrDefault();
                if (key == null)
                {
                    ctx.WriteLine($"{machine}: no BUI key");
                    return;
                }
                var players = IoCManager.Resolve<IPlayerManager>();
                if (!players.TryGetSessionByEntity(who, out var session))
                {
                    // Server console has no attached entity — fall back to the
                    // first player session's mob so `open` still works fifo-side.
                    foreach (var s in players.Sessions)
                    {
                        if (s.AttachedEntity is { } ae)
                        {
                            who = ae;
                            session = s;
                            break;
                        }
                    }
                }
                if (session == null)
                {
                    ctx.WriteLine($"{machine}: {who} has no session");
                    return;
                }
                // BUI opens assert range — pull the machine adjacent to the
                // session entity so wandering mobs can't break the check.
                if (session.AttachedEntity is { } mobEnt)
                {
                    var mc = man.GetComponent<TransformComponent>(mobEnt).Coordinates;
                    man.System<TransformSystem>().SetCoordinates(
                        machine, new EntityCoordinates(mc.EntityId, mc.X + 1f, mc.Y + 1f));
                }
                man.System<UserInterfaceSystem>().OpenUi(machine, key, session);
                ctx.WriteLine($"{machine}: opened {key} for {who}");
                break;
            }
            case "goto":
            {
                // teleport this entity adjacent to the mob so `open` passes
                // the BUI range assert; arg = mob uid (default: caller).
                var near = a.Length > 1 ? new EntityUid(int.Parse(a[1])) : (exec ?? uid);
                var nc = man.GetComponent<TransformComponent>(near).Coordinates;
                var dest = new EntityCoordinates(nc.EntityId, nc.X + 1f, nc.Y + 1f);
                man.System<TransformSystem>().SetCoordinates(uid, dest);
                ctx.WriteLine($"{uid}: moved adjacent to {near}");
                break;
            }
            case "rcon":
            {
                var target = new EntityUid(int.Parse(a[1]));
                var rmsg = new OxydRconToggleMessage { Field = a[2], Value = a[3] == "on" };
                rmsg.Actor = actor ?? uid;
                rmsg.Entity = man.GetNetEntity(target);
                man.EventBus.RaiseLocalEvent(uid, rmsg);
                ctx.WriteLine($"{uid}: rcon {a[2]}={a[3]} on {target}");
                break;
            }
            case "forge-eject":
                man.EventBus.RaiseLocalEvent(uid, Stamp(new OxydForgeEjectMessage()));
                ctx.WriteLine($"{uid}: forge-eject");
                break;
            case "feed":
            {
                var item = new EntityUid(int.Parse(a[1]));
                var user = a.Length > 2 ? new EntityUid(int.Parse(a[2])) : exec;
                var slots = man.System<ItemSlotsSystem>();
                if (slots.TryGetSlot(uid, "input", out var slot))
                    ctx.WriteLine($"{uid}: feed {item} -> {slots.TryInsert(uid, slot, item, user)}");
                else
                    ctx.WriteLine($"{uid}: no input slot");
                break;
            }
            case "breaker":
                man.EventBus.RaiseLocalEvent(uid, Stamp(new OxydBreakerToggleMessage(a[1] == "on")));
                ctx.WriteLine($"{uid}: breaker {a[1]}");
                break;
            case "bridge-toggle":
                man.EventBus.RaiseLocalEvent(uid, Stamp(new OxydPulsarBridgeToggleMessage()));
                ctx.WriteLine($"{uid}: bridge-toggle");
                break;
            case "pulsar-shield":
                man.EventBus.RaiseLocalEvent(uid, Stamp(new OxydPulsarShieldMessage(int.Parse(a[1]))));
                ctx.WriteLine($"{uid}: pulsar-shield {a[1]}");
                break;
            case "pulsar-move":
                man.EventBus.RaiseLocalEvent(uid,
                    Stamp(new OxydPulsarMoveMessage(new Vector2i(int.Parse(a[1]), int.Parse(a[2])))));
                ctx.WriteLine($"{uid}: pulsar-move {a[1]},{a[2]}");
                break;
            case "shield-toggle":
                man.EventBus.RaiseLocalEvent(uid, Stamp(new OxydShieldToggleMessage()));
                ctx.WriteLine($"{uid}: shield-toggle");
                break;
            case "shield-mode":
                man.EventBus.RaiseLocalEvent(uid, Stamp(new OxydShieldModeMessage(a[1], a[2] == "on")));
                ctx.WriteLine($"{uid}: shield-mode {a[1]}={a[2]}");
                break;
            case "shield-size":
                man.EventBus.RaiseLocalEvent(uid, Stamp(new OxydShieldFieldSizeMessage(int.Parse(a[1]))));
                ctx.WriteLine($"{uid}: shield-size {a[1]}");
                break;
            case "shield-cap":
                man.EventBus.RaiseLocalEvent(uid, Stamp(new OxydShieldInputCapMessage(float.Parse(a[1]))));
                ctx.WriteLine($"{uid}: shield-cap {a[1]}");
                break;
            case "use":
            {
                var user = exec ?? uid;
                if (a.Length > 1)
                    user = new EntityUid(int.Parse(a[1]));
                var ev = new UseInHandEvent(user);
                man.EventBus.RaiseLocalEvent(uid, ev);
                ctx.WriteLine($"{uid}: use by {user}");
                break;
            }
            case "mod":
            {
                var user = exec ?? uid;
                var tool = new EntityUid(int.Parse(a[1]));
                if (a.Length > 2)
                    user = new EntityUid(int.Parse(a[2]));
                var ev = new AfterInteractEvent(user, uid, tool,
                    man.GetComponent<TransformComponent>(tool).Coordinates, true);
                man.EventBus.RaiseLocalEvent(uid, ev);
                ctx.WriteLine($"{uid}: mod applied to {tool}");
                break;
            }
            case "canister-valve":
                man.EventBus.RaiseLocalEvent(uid, Stamp(new GasCanisterChangeReleaseValveMessage(a[1] == "on")));
                ctx.WriteLine($"{uid}: canister-valve {a[1]}");
                break;
            case "canister-pressure":
                man.EventBus.RaiseLocalEvent(uid,
                    Stamp(new GasCanisterChangeReleasePressureMessage(float.Parse(a[1]))));
                ctx.WriteLine($"{uid}: canister-pressure {a[1]}");
                break;
            case "airalarm-mode":
                man.EventBus.RaiseLocalEvent(uid,
                    Stamp(new AirAlarmUpdateAlarmModeMessage(Enum.Parse<AirAlarmMode>(a[1], true))));
                ctx.WriteLine($"{uid}: airalarm-mode {a[1]}");
                break;
            case "airalarm-auto":
                man.EventBus.RaiseLocalEvent(uid,
                    Stamp(new AirAlarmUpdateAutoModeMessage(a[1] == "on")));
                ctx.WriteLine($"{uid}: airalarm-auto {a[1]}");
                break;
            default:
                ctx.WriteLine($"unknown action {a[0]}");
                break;
        }
    }
}
