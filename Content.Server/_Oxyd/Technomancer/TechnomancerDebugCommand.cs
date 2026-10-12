using Content.Server.Administration;
using Content.Shared._Oxyd.Technomancer;
using Content.Shared._Oxyd.Technomancer.Components;
using Content.Shared.Administration;
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
        "OxydToolModComplicator", "OxydRapidPipeDispenser",
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
        var exec = ExecutingEntity(ctx);
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
        switch (a[0])
        {
            case "open":
            {
                Enum? key = null;
                if (man.TryGetComponent<ActivatableUIComponent>(uid, out var aui))
                    key = aui.Key;
                else if (man.TryGetComponent<UserInterfaceComponent>(uid, out var ui))
                    key = ui.ClientOpenInterfaces.Keys.FirstOrDefault();
                if (exec == null || key == null)
                {
                    ctx.WriteLine($"{uid}: no BUI key / caller");
                    return;
                }
                var players = IoCManager.Resolve<IPlayerManager>();
                if (!players.TryGetSessionByEntity(exec.Value, out var session))
                {
                    ctx.WriteLine($"{uid}: caller has no session");
                    return;
                }
                man.System<UserInterfaceSystem>().OpenUi(uid, key, session);
                ctx.WriteLine($"{uid}: opened {key}");
                break;
            }
            case "rcon":
            {
                var target = new EntityUid(int.Parse(a[1]));
                man.EventBus.RaiseLocalEvent(uid, new OxydRconToggleMessage
                {
                    Entity = man.GetNetEntity(target), Field = a[2], Value = a[3] == "on",
                });
                ctx.WriteLine($"{uid}: rcon {a[2]}={a[3]} on {target}");
                break;
            }
            case "forge-eject":
                man.EventBus.RaiseLocalEvent(uid, new OxydForgeEjectMessage());
                ctx.WriteLine($"{uid}: forge-eject");
                break;
            case "feed":
            {
                var item = new EntityUid(int.Parse(a[1]));
                var slots = man.System<ItemSlotsSystem>();
                if (slots.TryGetSlot(uid, "input", out var slot))
                    ctx.WriteLine($"{uid}: feed {item} -> {slots.TryInsert(uid, slot, item, exec)}");
                else
                    ctx.WriteLine($"{uid}: no input slot");
                break;
            }
            case "breaker":
                man.EventBus.RaiseLocalEvent(uid, new OxydBreakerToggleMessage(a[1] == "on"));
                ctx.WriteLine($"{uid}: breaker {a[1]}");
                break;
            case "bridge-toggle":
                man.EventBus.RaiseLocalEvent(uid, new OxydPulsarBridgeToggleMessage());
                ctx.WriteLine($"{uid}: bridge-toggle");
                break;
            case "pulsar-shield":
                man.EventBus.RaiseLocalEvent(uid, new OxydPulsarShieldMessage(int.Parse(a[1])));
                ctx.WriteLine($"{uid}: pulsar-shield {a[1]}");
                break;
            case "pulsar-move":
                man.EventBus.RaiseLocalEvent(uid,
                    new OxydPulsarMoveMessage(new Vector2i(int.Parse(a[1]), int.Parse(a[2]))));
                ctx.WriteLine($"{uid}: pulsar-move {a[1]},{a[2]}");
                break;
            case "shield-toggle":
                man.EventBus.RaiseLocalEvent(uid, new OxydShieldToggleMessage());
                ctx.WriteLine($"{uid}: shield-toggle");
                break;
            case "shield-mode":
                man.EventBus.RaiseLocalEvent(uid, new OxydShieldModeMessage(a[1], a[2] == "on"));
                ctx.WriteLine($"{uid}: shield-mode {a[1]}={a[2]}");
                break;
            case "shield-size":
                man.EventBus.RaiseLocalEvent(uid, new OxydShieldFieldSizeMessage(int.Parse(a[1])));
                ctx.WriteLine($"{uid}: shield-size {a[1]}");
                break;
            case "shield-cap":
                man.EventBus.RaiseLocalEvent(uid, new OxydShieldInputCapMessage(float.Parse(a[1])));
                ctx.WriteLine($"{uid}: shield-cap {a[1]}");
                break;
            case "use":
            {
                if (exec == null) { ctx.WriteLine("no caller"); return; }
                var ev = new UseInHandEvent(exec.Value);
                man.EventBus.RaiseLocalEvent(uid, ev);
                ctx.WriteLine($"{uid}: use by {exec}");
                break;
            }
            case "mod":
            {
                if (exec == null) { ctx.WriteLine("no caller"); return; }
                var tool = new EntityUid(int.Parse(a[1]));
                var ev = new AfterInteractEvent(exec.Value, uid, tool,
                    man.GetComponent<TransformComponent>(tool).Coordinates, true);
                man.EventBus.RaiseLocalEvent(uid, ev);
                ctx.WriteLine($"{uid}: mod applied to {tool}");
                break;
            }
            default:
                ctx.WriteLine($"unknown action {a[0]}");
                break;
        }
    }
}
