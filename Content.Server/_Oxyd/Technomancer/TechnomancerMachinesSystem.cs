using Content.Shared.Emag.Components;
using Content.Server.Power.EntitySystems;
using Robust.Server.GameObjects;
using Content.Server.Power.Components;
using Content.Shared._Oxyd.Technomancer.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Technomancer;

/// <summary>
/// Server systems for Technomancer machines, one file per machine family:
/// pulsar console + bridge + fuel tank (CEV-Eris code/modules/pulsar_engine),
/// ship shield generator (shield_generators/shield_generator.dm), and the
/// breaker box (machinery/power/breakerbox.dm). Eris procs are cited inline.
/// </summary>
public sealed class TechnomancerMachinesSystem : EntitySystem
{
    [Dependency] private IRobustRandom _rand = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private FixtureSystem _fixtures = default!;

    private TimeSpan _nextUpdate = TimeSpan.Zero;

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydPulsarComponent, OxydPulsarMoveMessage>(OnPulsarMove);
        SubscribeLocalEvent<OxydPulsarComponent, OxydPulsarShieldMessage>(OnPulsarShield);
        SubscribeLocalEvent<OxydPulsarBridgeComponent, OxydPulsarBridgeToggleMessage>(OnBridgeToggle);
        SubscribeLocalEvent<OxydShipShieldComponent, OxydShieldToggleMessage>(OnShieldToggle);
        SubscribeLocalEvent<OxydShipShieldComponent, PreventCollideEvent>(OnShieldPreventCollide);
        SubscribeLocalEvent<OxydShipShieldComponent, StartCollideEvent>(OnShieldCollide);
        SubscribeLocalEvent<OxydShipShieldComponent, OxydShieldModeMessage>(OnShieldMode);
        SubscribeLocalEvent<OxydShipShieldComponent, OxydShieldFieldSizeMessage>(OnShieldFieldSize);
        SubscribeLocalEvent<OxydShipShieldComponent, OxydShieldInputCapMessage>(OnShieldInputCap);
        SubscribeLocalEvent<OxydBreakerBoxComponent, OxydBreakerToggleMessage>(OnBreakerToggle);
        SubscribeLocalEvent<OxydPulsarComponent, MapInitEvent>(OnPulsarInit);
        SubscribeLocalEvent<OxydPulsarBridgeComponent, MapInitEvent>(OnBridgeInit);
    }

    // --- Pulsar console (Eris: pulsar_consoles.dm interactions) ---

    private void OnPulsarInit(Entity<OxydPulsarComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Beams.Count == 0)
            RegenBeams(ent.Comp);
    }

    private void RegenBeams(OxydPulsarComponent comp)
    {
        // Eris ship effect re-rolls beam origin points on the PULSAR grid.
        comp.Beams.Clear();
        var n = _rand.Next(1, 3);
        for (var i = 0; i < n; i++)
            comp.Beams.Add(new Vector2i(_rand.Next(comp.GridSize), _rand.Next(comp.GridSize)));
        DirtyField();
    }

    private void DirtyField()
    {
    }

    private void OnPulsarMove(Entity<OxydPulsarComponent> ent, ref OxydPulsarMoveMessage msg)
    {
        var c = ent.Comp;
        if (c.Fuel <= 0)
            return;
        // One tile per move; the client picks the direction, not the distance.
        if (msg.Dir == Vector2i.Zero || Math.Abs(msg.Dir.X) > 1 || Math.Abs(msg.Dir.Y) > 1)
            return;
        var newPos = c.SatellitePos + msg.Dir;
        if (newPos.X < 0 || newPos.Y < 0 || newPos.X >= c.GridSize || newPos.Y >= c.GridSize)
            return;
        // Eris: each move consumes thrust_cost fuel and resets the orbit decay.
        c.SatellitePos = newPos;
        c.Fuel = Math.Max(0, c.Fuel - c.FuelPerMove);
        c.OrbitDecaySeconds = 900f;
        Dirty(ent, c);
    }

    private void OnPulsarShield(Entity<OxydPulsarComponent> ent, ref OxydPulsarShieldMessage msg)
    {
        ent.Comp.ShieldPower = Math.Clamp(msg.Pct, 0, 100);
        Dirty(ent, ent.Comp);
    }

    private float EffectivePower(OxydPulsarComponent c, out float shieldNeeded)
    {
        // produced * (1 - shield_power * shield_cost) — Eris console math.
        var produced = OxydPulsarMath.ProducedFraction(c.SatellitePos, c.Beams, c.GridSize);
        shieldNeeded = c.ShieldCost * c.ShieldPower / 100f;
        return produced * (1f - shieldNeeded);
    }

    // --- Power bridge (Eris: disconnect/reconnect + RECONNECT_CD) ---

    private void OnBridgeInit(Entity<OxydPulsarBridgeComponent> ent, ref MapInitEvent args)
    {
    }

    private void OnBridgeToggle(Entity<OxydPulsarBridgeComponent> ent, ref OxydPulsarBridgeToggleMessage msg)
    {
        var c = ent.Comp;
        if (c.Connected)
        {
            c.Connected = false;
            c.ReconnectCooldown = c.ReconnectCooldownMax;
        }
        else if (c.ReconnectCooldown <= 0f)
        {
            c.Connected = true;
        }
        Dirty(ent, c);
    }

    // --- Shield generator (Eris: idle_power/update_upkeep equivalents) ---

    private void OnShieldToggle(Entity<OxydShipShieldComponent> ent, ref OxydShieldToggleMessage msg)
    {
        ent.Comp.Running = !ent.Comp.Running;
        if (ent.Comp.Running && ent.Comp.Integrity <= 0)
            ent.Comp.Running = false;
        Dirty(ent, ent.Comp);
        RefreshShieldField(ent);
        PushShieldState(ent);
    }

    private void OnShieldPreventCollide(Entity<OxydShipShieldComponent> ent, ref PreventCollideEvent args)
    {
        if ((args.OurFixture.CollisionLayer & (int) CollisionGroup.BulletImpassable) == 0)
            return;

        if (!ShieldBlocks(ent.Comp, args.OtherEntity))
            args.Cancelled = true;
    }

    private void OnShieldCollide(Entity<OxydShipShieldComponent> ent, ref StartCollideEvent args)
    {
        if (args.OurFixtureId != "shield" || !HasComp<ProjectileComponent>(args.OtherEntity))
            return;

        ent.Comp.Integrity = Math.Max(0f, ent.Comp.Integrity - 40f);
        if (ent.Comp.Integrity <= 0f)
            ent.Comp.Running = false;

        Dirty(ent, ent.Comp);
        RefreshShieldField(ent);
        PushShieldState(ent);
    }

    private bool ShieldBlocks(OxydShipShieldComponent shield, EntityUid other)
    {
        if (!shield.Running || shield.Integrity <= 0f)
            return false;

        if (HasComp<ProjectileComponent>(other))
            return shield.EnabledModes.Contains("hyperkinetic");

        if (HasComp<BorgChassisComponent>(other))
            return shield.EnabledModes.Contains("silicons");

        if (HasComp<MobStateComponent>(other))
        {
            var id = MetaData(other).EntityPrototype?.ID ?? string.Empty;
            var human = id.Contains("Human", StringComparison.Ordinal);
            return human
                ? shield.EnabledModes.Contains("humanoids")
                : shield.EnabledModes.Contains("nonhumans");
        }

        return false;
    }

    private void RefreshShieldField(Entity<OxydShipShieldComponent> ent)
    {
        _fixtures.DestroyFixture(ent.Owner, "shield");
        if (!ent.Comp.Running || ent.Comp.Integrity <= 0f || ent.Comp.FieldSize <= 0)
            return;

        var radius = Math.Clamp(ent.Comp.FieldSize, 1, 40);
        _fixtures.TryCreateFixture(
            ent.Owner,
            new PhysShapeCircle(radius),
            "shield",
            density: 1000f,
            hard: true,
            collisionLayer: (int) (CollisionGroup.BulletImpassable | CollisionGroup.MidImpassable | CollisionGroup.HighImpassable));
    }

    private void PushShieldState(Entity<OxydShipShieldComponent> ent)
    {
        if (!_ui.IsUiOpen(ent.Owner, OxydShieldUiKey.Key))
            return;

        var shield = ent.Comp;
        _ui.SetUiState(ent.Owner, OxydShieldUiKey.Key, new OxydShieldState
        {
            Running = shield.Running,
            Integrity = shield.Integrity,
            Capacity = shield.Capacity,
            MitigationEm = shield.EnabledModes.Contains("photonic") ? 60f : 0f,
            MitigationPh = shield.EnabledModes.Contains("hyperkinetic") ? 60f : 0f,
            MitigationHe = shield.EnabledModes.Contains("atmos") ? 60f : 0f,
            MitigationMax = 60f,
            FieldSize = shield.FieldSize,
            MaxFieldSize = shield.MaxFieldSize,
            InputCap = shield.InputCap,
            EnabledModes = new List<string>(shield.EnabledModes),
            EventLog = new List<string>(shield.EventLog),
            Hacked = HasComp<EmaggedComponent>(ent.Owner),
        });
    }

    private void OnShieldMode(Entity<OxydShipShieldComponent> ent, ref OxydShieldModeMessage msg)
    {
        if (msg.On)
        {
            // Only known modes; hacked-only ones need a hacked (emagged) generator.
            var known = false;
            foreach (var def in OxydShieldModes.All)
            {
                if (def.Id != msg.ModeId)
                    continue;
                known = !def.HackedOnly || HasComp<EmaggedComponent>(ent);
                break;
            }
            if (!known)
                return;
            ent.Comp.EnabledModes.Add(msg.ModeId);
        }
        else
            ent.Comp.EnabledModes.Remove(msg.ModeId);
        Dirty(ent, ent.Comp);
        RefreshShieldField(ent);
        PushShieldState(ent);
    }

    private void OnShieldFieldSize(Entity<OxydShipShieldComponent> ent, ref OxydShieldFieldSizeMessage msg)
    {
        ent.Comp.FieldSize = Math.Clamp(msg.Size, 0, ent.Comp.MaxFieldSize);
        Dirty(ent, ent.Comp);
        RefreshShieldField(ent);
        PushShieldState(ent);
    }

    private void OnShieldInputCap(Entity<OxydShipShieldComponent> ent, ref OxydShieldInputCapMessage msg)
    {
        ent.Comp.InputCap = Math.Max(0f, msg.Kw);
        Dirty(ent, ent.Comp);
    }

    // --- Breaker box ---

    private void OnBreakerToggle(Entity<OxydBreakerBoxComponent> ent, ref OxydBreakerToggleMessage msg)
    {
        ent.Comp.Enabled = msg.On;
        if (TryComp<ApcPowerReceiverComponent>(ent, out var rec))
            rec.PowerDisabled = !msg.On;
        Dirty(ent, ent.Comp);
    }

    // --- Periodic update: orbit decay, cooldowns, bridge feed, shield upkeep ---

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;
        _nextUpdate = now + TimeSpan.FromSeconds(1);
        var dt = 1f;

        var pulsars = EntityQueryEnumerator<OxydPulsarComponent>();
        var pulsarList = new List<(EntityUid, OxydPulsarComponent)>();
        while (pulsars.MoveNext(out var pu, out var pc0))
            pulsarList.Add((pu, pc0));
        foreach (var (_, c) in pulsarList)
        {
            // Eris: orbit decayed → satellite drifts off beam and power drops.
            c.OrbitDecaySeconds = Math.Max(0f, c.OrbitDecaySeconds - dt);
            if (c.OrbitDecaySeconds <= 0f)
            {
                RegenBeams(c);
                c.OrbitDecaySeconds = 900f;
            }
        }

        var bridgeQuery = EntityQueryEnumerator<OxydPulsarBridgeComponent>();
        while (bridgeQuery.MoveNext(out var uid, out var bridge))
        {
            bridge.ReconnectCooldown = Math.Max(0f, bridge.ReconnectCooldown - dt);
            EntityUid consoleUid;
            OxydPulsarComponent? consoleComp;
            if (bridge.Console is { } cu && TryComp<OxydPulsarComponent>(cu, out var pc))
            { consoleUid = cu; consoleComp = pc; }
            else
            {
                // nearest-console fallback for spawn-and-run MVP
                consoleUid = EntityUid.Invalid; consoleComp = null;
                foreach (var (cu2, pc2) in pulsarList)
                { consoleUid = cu2; consoleComp = pc2; bridge.Console = cu2; break; }
            }
            var target = 0f;
            if (consoleComp != null && bridge.Connected)
            {
                var eff = EffectivePower(consoleComp, out _);
                target = consoleComp.MaxPowerWatts * eff;
            }
            bridge.InjectedWatts = bridge.Connected ? target : 0f;
            if (TryComp<PowerNetworkBatteryComponent>(uid, out var pnb))
                pnb.MaxSupply = bridge.InjectedWatts;
            _ui.SetUiState(uid, OxydPulsarBridgeUiKey.Key, new OxydPulsarBridgeState
            {
                Connected = bridge.Connected,
                InjectedWatts = bridge.InjectedWatts,
                ReconnectCooldown = bridge.ReconnectCooldown,
                HasConsole = bridge.Console != null,
            });
        }

        var shieldQuery = EntityQueryEnumerator<OxydShipShieldComponent>();
        while (shieldQuery.MoveNext(out var uid, out var shield))
        {
            var mult = 1f;
            foreach (var m in shield.EnabledModes)
                foreach (var def in OxydShieldModes.All)
                    if (def.Id == m) mult *= def.Mult;
            var upkeepWatts = shield.UpkeepWattsPerUnit * shield.Integrity * mult;
            if (shield.InputCap > 0f)
                upkeepWatts = Math.Min(upkeepWatts, shield.InputCap * 1000f);

            var hasReceiver = TryComp<ApcPowerReceiverComponent>(uid, out var rec);
            if (shield.Running)
            {
                // Reviewer: upkeep must CONSUME power — declare the draw as the
                // receiver's network load and only regenerate integrity while the
                // grid actually feeds the generator (no free regen).
                if (hasReceiver && rec != null)
                {
                    // Floor the draw so an empty (0 integrity) shield still needs a live grid.
                    rec.Load = Math.Max(upkeepWatts, shield.UpkeepWattsPerUnit);
                    if (rec.Powered)
                        shield.Integrity = Math.Min(shield.Capacity, shield.Integrity + 2f);
                }
                else
                {
                    shield.Integrity = Math.Min(shield.Capacity, shield.Integrity + 2f);
                }
                Dirty(uid, shield);
            }
            else if (hasReceiver && rec != null && rec.Load != 0f)
            {
                // A stopped generator draws nothing.
                rec.Load = 0f;
            }
            if (_ui.IsUiOpen(uid, OxydShieldUiKey.Key))
            {
                _ui.SetUiState(uid, OxydShieldUiKey.Key, new OxydShieldState
                {
                    HasConduit = hasReceiver,
                    Running = shield.Running,
                    Integrity = shield.Integrity,
                    Capacity = shield.Capacity,
                    MitigationEm = shield.EnabledModes.Contains("photonic") ? 60f : 0f,
                    MitigationPh = shield.EnabledModes.Contains("hyperkinetic") ? 60f : 0f,
                    MitigationHe = shield.EnabledModes.Contains("atmos") ? 60f : 0f,
                    MitigationMax = 60f,
                    UpkeepKw = upkeepWatts / 1000f,
                    TotalUseKw = hasReceiver && rec != null ? rec.PowerReceived / 1000f : 0f,
                    FieldSize = shield.FieldSize,
                    MaxFieldSize = shield.MaxFieldSize,
                    InputCap = shield.InputCap,
                    EnabledModes = new List<string>(shield.EnabledModes),
                    EventLog = new List<string>(shield.EventLog),
                    Hacked = HasComp<EmaggedComponent>(uid),
                });
            }
        }
        // Pulsar console state push
        foreach (var (uid, c) in pulsarList)
        {
            var eff = EffectivePower(c, out var need);
            _ui.SetUiState(uid, OxydPulsarUiKey.Key, new OxydPulsarState
            {
                ProducedFraction = OxydPulsarMath.ProducedFraction(c.SatellitePos, c.Beams, c.GridSize),
                Fuel = c.Fuel,
                FuelPerMove = c.FuelPerMove,
                ShieldPower = c.ShieldPower,
                ShieldNeededPct = need * 100f,
                EffectiveFraction = eff,
                OrbitDecaySeconds = c.OrbitDecaySeconds,
                SatellitePos = c.SatellitePos,
                Beams = new List<Vector2i>(c.Beams),
                GridSize = c.GridSize,
            });
        }
        var breakerQuery = EntityQueryEnumerator<OxydBreakerBoxComponent>();
        while (breakerQuery.MoveNext(out var uid, out var br))
        {
            var load = 0f;
            if (TryComp<ApcPowerReceiverComponent>(uid, out var rec2)) load = rec2.Load;
            _ui.SetUiState(uid, OxydBreakerUiKey.Key, new OxydBreakerState { Enabled = br.Enabled, LoadWatts = load });
        }
    }
}
