using Content.Server.Atmos.Monitor.Components;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Power.SMES;
using Content.Shared._Oxyd.Technomancer;
using Content.Shared._Oxyd.Technomancer.Components;
using Content.Shared.Containers;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Materials;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Stacks;
using Content.Shared.Tools.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Technomancer;

/// <summary>
/// Server systems for the second Technomancer wave — Eris rcon_console.dm
/// (remote SMES/breaker control), atmos_control.tmpl (air-alarm monitor list),
/// autolathe.dm nanoforge (items -> compressed matter), tools/mods mod items,
/// and the IJIRO RPD + engineering design disks.
/// </summary>
public sealed class TechnomancerExtrasSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private SharedBatterySystem _battery = default!;
    [Dependency] private PopupSystem _popup = default!;

    private TimeSpan _nextPush = TimeSpan.Zero;

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydRconComponent, OxydRconToggleMessage>(OnRconToggle);
        SubscribeLocalEvent<OxydRconComponent, BoundUIOpenedEvent>(OnRconOpened);
        SubscribeLocalEvent<OxydAtmosConsoleComponent, BoundUIOpenedEvent>(OnAtmosOpened);
        SubscribeLocalEvent<OxydNanoforgeComponent, EntInsertedIntoContainerMessage>(OnForgeInsert);
        SubscribeLocalEvent<OxydNanoforgeComponent, OxydForgeEjectMessage>(OnForgeEject);
        SubscribeLocalEvent<OxydToolModComponent, AfterInteractEvent>(OnToolModApply);
        SubscribeLocalEvent<OxydDesignDiskComponent, UseInHandEvent>(OnDiskUse);
        SubscribeLocalEvent<OxydNanoforgeComponent, BoundUIOpenedEvent>(OnForgeOpened);
    }

    // ---- RCON (Eris rcon_console.dm: SMES rows + breaker rows) ----

    private void OnRconOpened(Entity<OxydRconComponent> ent, ref BoundUIOpenedEvent args)
    {
        PushRcon(ent);
    }

    private void PushRcon(Entity<OxydRconComponent> ent)
    {
        var state = new OxydRconUiState();
        var smes = EntityQueryEnumerator<SmesComponent>();
        while (smes.MoveNext(out var uid, out _))
        {
            if (!OnConsoleMap(ent.Owner, uid))
                continue;
            var row = new RconRow
            {
                Entity = GetNetEntity(uid),
                Name = MetaData(uid).EntityName,
                Group = "SMES",
            };
            if (TryComp<BatteryComponent>(uid, out var bat) && bat.MaxCharge > 0)
                row.ChargePercent = (int)(_battery.GetCharge(uid) / bat.MaxCharge * 100f);
            if (TryComp<PowerNetworkBatteryComponent>(uid, out var net))
            {
                row.InputEnabled = net.CanCharge;
                row.OutputEnabled = net.CanDischarge;
            }
            state.Rows.Add(row);
        }
        var breakers = EntityQueryEnumerator<OxydBreakerBoxComponent>();
        while (breakers.MoveNext(out var uid, out var br))
        {
            if (!OnConsoleMap(ent.Owner, uid))
                continue;
            state.Rows.Add(new RconRow
            {
                Entity = GetNetEntity(uid),
                Name = MetaData(uid).EntityName,
                Group = "BREAKER",
                InputEnabled = br.Enabled,
                OutputEnabled = br.Enabled,
            });
        }
        _ui.SetUiState(ent.Owner, OxydRconUiKey.Key, state);
    }

    private bool OnConsoleMap(EntityUid console, EntityUid target)
        => Transform(console).MapID == Transform(target).MapID;

    private void OnRconToggle(Entity<OxydRconComponent> ent, ref OxydRconToggleMessage msg)
    {
        // The client names the target, so only act on what this console lists:
        // SMES units and breaker boxes on the console's own map.
        if (!TryGetEntity(msg.Entity, out var maybeTarget) || maybeTarget is not { } target ||
            !Exists(target) || !OnConsoleMap(ent.Owner, target))
            return;
        switch (msg.Field)
        {
            // Same fields the stock SMES BUI toggles (BatteryInterfaceSystem) —
            // set directly: re-raised BUI messages lose UiKey and get filtered.
            case "input":
                if (HasComp<SmesComponent>(target) && TryComp<PowerNetworkBatteryComponent>(target, out var netIn))
                    netIn.CanCharge = msg.Value;
                break;
            case "output":
                if (HasComp<SmesComponent>(target) && TryComp<PowerNetworkBatteryComponent>(target, out var netOut))
                    netOut.CanDischarge = msg.Value;
                break;
            case "breaker":
                if (HasComp<OxydBreakerBoxComponent>(target))
                    RaiseLocalEvent(target, new OxydBreakerToggleMessage(msg.Value));
                break;
        }
        PushRcon(ent);
    }

    // ---- Atmos control console (Eris atmos_control.tmpl alarm list) ----

    private void OnAtmosOpened(Entity<OxydAtmosConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        var state = new OxydAtmosUiState();
        var alarms = EntityQueryEnumerator<AirAlarmComponent>();
        while (alarms.MoveNext(out var uid, out var alarm))
        {
            var pos = _xform.GetMapCoordinates(uid);
            state.Rows.Add(new AtmosRow
            {
                Entity = GetNetEntity(uid),
                Name = MetaData(uid).EntityName,
                Area = $"grid {pos.MapId} ({pos.Position.X:0},{pos.Position.Y:0})",
                Status = alarm.State.ToString(),
            });
        }
        _ui.SetUiState(ent.Owner, OxydAtmosUiKey.Key, state);
    }

    // ---- Nanoforge (Eris autolathe.dm /obj/machinery/autolathe/nanoforge) ----

    private void OnForgeOpened(Entity<OxydNanoforgeComponent> ent, ref BoundUIOpenedEvent args)
    {
        PushForge(ent);
    }

    private void OnForgeInsert(Entity<OxydNanoforgeComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        // Eris: items consumed -> compressed matter, 0.6 efficiency.
        var item = args.Entity;
        var worth = 5;
        if (TryComp<PhysicalCompositionComponent>(item, out var comp) && comp.MaterialComposition.Count > 0)
        {
            worth = 0;
            foreach (var v in comp.MaterialComposition.Values)
                worth += (int)(v * 0.6f);
            worth = Math.Max(worth, 1);
        }
        ent.Comp.StoredMatter = Math.Min(ent.Comp.StoredMatter + worth, ent.Comp.Capacity);
        QueueDel(item);
        PushForge(ent);
        Dirty(ent);
    }

    private void OnForgeEject(Entity<OxydNanoforgeComponent> ent, ref OxydForgeEjectMessage msg)
    {
        if (ent.Comp.StoredMatter < 10)
            return;
        var sheets = ent.Comp.StoredMatter / 10;
        ent.Comp.StoredMatter -= sheets * 10;
        var stack = Spawn("OxydCompressedMatterSingle", Transform(ent.Owner).Coordinates);
        EntityManager.System<SharedStackSystem>().SetCount(stack, sheets);
        PushForge(ent);
        Dirty(ent);
    }

    private void PushForge(Entity<OxydNanoforgeComponent> ent)
    {
        _ui.SetUiState(ent.Owner, OxydForgeUiKey.Key,
            new OxydForgeUiState { StoredMatter = ent.Comp.StoredMatter, Capacity = ent.Comp.Capacity });
    }

    // ---- Tool mod (Eris tools/mods Adapt: use on a tool -> grants quality) ----

    private void OnToolModApply(Entity<OxydToolModComponent> ent, ref AfterInteractEvent args)
    {
        if (!args.CanReach || args.Target is not { } target || !Exists(target))
            return;
        if (!TryComp<ToolComponent>(target, out var tool))
            return;
        if (tool.Qualities.Contains(ent.Comp.GrantsQuality))
            return;
        tool.Qualities.Add(ent.Comp.GrantsQuality);
        Dirty(target, tool);
        var modded = EnsureComp<OxydToolModdedComponent>(target);
        modded.AppliedQualities.Add(ent.Comp.GrantsQuality.Id);
        QueueDel(ent.Owner);
        args.Handled = true;
    }

    // ---- Design disks (readout of licensed designs) ----

    private void OnDiskUse(Entity<OxydDesignDiskComponent> ent, ref UseInHandEvent args)
    {
        foreach (var d in ent.Comp.Designs)
            _popup.PopupEntity(d, args.User, args.User);
        args.Handled = true;
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        if (now < _nextPush)
            return;
        _nextPush = now + TimeSpan.FromSeconds(3);
        var rcon = EntityQueryEnumerator<OxydRconComponent>();
        while (rcon.MoveNext(out var uid, out var comp))
            PushRcon((uid, comp));
        var atmos = EntityQueryEnumerator<OxydAtmosConsoleComponent>();
        while (atmos.MoveNext(out var uid, out var comp))
        {
            var state = new OxydAtmosUiState();
            var alarms = EntityQueryEnumerator<AirAlarmComponent>();
            while (alarms.MoveNext(out var auid, out var alarm))
            {
                var pos = _xform.GetMapCoordinates(auid);
                state.Rows.Add(new AtmosRow
                {
                    Entity = GetNetEntity(auid),
                    Name = MetaData(auid).EntityName,
                    Area = $"grid {pos.MapId} ({pos.Position.X:0},{pos.Position.Y:0})",
                    Status = alarm.State.ToString(),
                });
            }
            _ui.SetUiState(uid, OxydAtmosUiKey.Key, state);
        }
    }
}
