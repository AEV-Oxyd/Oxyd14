using Content.Server._Oxyd.Framework.ViewCalc;
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// P2.13: the obelisk's aura. A <see cref="ViewCadenceEvent"/> drives one pulse per second; the
/// work lives in <see cref="Tick"/> so tests drive it without waiting out the cadence.
/// </summary>
public sealed partial class ObeliskSystem : EntitySystem
{
    [Dependency] private readonly CruciformSystem _cruciform = default!;
    [Dependency] private readonly EyeOfTheProtectorSystem _eye = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PlantTraySystem _tray = default!;
    [Dependency] private readonly SanitySystem _sanity = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    [Dependency] private readonly NeoTheologyMachineSystem _machines = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;

    [SubscribeLocalEvent]
    private void OnInit(Entity<ObeliskComponent> ent, ref ComponentInit args)
    {
        var ticker = EnsureComp<ViewTickerComponent>(ent);
        // The aura runs its own broad phase. It does not need the raycast seen set.
        ticker.trackSeen = false;
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<ObeliskComponent> ent, ref ComponentShutdown args)
    {
        RefreshRegeneration(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnCadence(Entity<ObeliskComponent> ent, ref ViewCadenceEvent args)
    {
        Tick(ent);
    }

    /// <summary>
    /// Publish the strongest active aura to every bearer. One lookup per obelisk replaces the
    /// nested scan over every cruciform and every obelisk.
    /// </summary>
    private void RefreshRegeneration(EntityUid? excluded = null)
    {
        var wanted = new Dictionary<EntityUid, float>();
        var obelisks = EntityQueryEnumerator<ObeliskComponent, TransformComponent>();
        while (obelisks.MoveNext(out var uid, out var obelisk, out var source))
        {
            if (uid == excluded || !_machines.IsOperational(uid))
                continue;

            if (obelisk.Radius <= 0)
                continue;

            foreach (var (body, bearer) in _lookup.GetEntitiesInRange<CruciformBearerComponent>(source.Coordinates, obelisk.Radius))
            {
                if (bearer.Cruciform is not { } cruciform ||
                    !TryComp<CruciformComponent>(cruciform, out var state) ||
                    !state.Active)
                    continue;

                wanted[body] = Math.Max(wanted.GetValueOrDefault(body, 1f), obelisk.RegenMultiplier);
            }
        }

        // Reset every bearer that no longer stands in an active aura. RecomputeProfile consumes the value.
        var implants = EntityQueryEnumerator<CruciformComponent>();
        while (implants.MoveNext(out var implant, out var state))
        {
            var value = 1f;
            if (state.ImplantedEntity is { } body && !TerminatingOrDeleted(body))
                value = wanted.GetValueOrDefault(body, 1f);

            if (Math.Abs(state.RegenerationMultiplier - value) < 0.001f)
                continue;

            state.RegenerationMultiplier = value;
            _cruciform.RecomputeProfile(implant, state);
        }
    }

    /// <summary>One aura pulse: buff the faithful in range, punish the hostiles, kill the weeds.</summary>
    public void Tick(EntityUid uid, ObeliskComponent? obelisk = null)
    {
        if (!Resolve(uid, ref obelisk))
            return;

        RefreshRegeneration();
        if (!_machines.IsOperational(uid))
        {
            obelisk.Active = false;
            Dirty(uid, obelisk);
            return;
        }

        var xform = Transform(uid);
        var faithful = 0;

        // Sanctify's forced window lapses on its own; Eris counts force_active down per tick.
        if (obelisk.ForceActiveUntil > TimeSpan.Zero && _timing.CurTime >= obelisk.ForceActiveUntil)
            obelisk.ForceActiveUntil = TimeSpan.Zero;

        var eye = _eye.FindEye(uid);
        if (eye is { } observer)
            _eye.ObserveArea(observer, uid, obelisk.Radius);

        if (obelisk.Radius > 0)
        {
            foreach (var (body, bearer) in _lookup.GetEntitiesInRange<CruciformBearerComponent>(xform.Coordinates, obelisk.Radius))
            {
                if (bearer.Cruciform is not { } cruciform ||
                    !TryComp<CruciformComponent>(cruciform, out var state) ||
                    !state.Active)
                    continue;

                faithful++;

                if (TryComp<SanityComponent>(body, out var sanity))
                    _sanity.ApplySanityDelta((body, sanity), SanitySource.Belief, obelisk.SanityPerSecond);
            }
        }

        obelisk.Active = faithful > 0 || obelisk.ForceActiveUntil > _timing.CurTime;

        Dirty(uid, obelisk);

        if (obelisk.Active)
        {
            DamageHostiles(uid, obelisk, xform);
            WeedTrays(xform, obelisk);
        }
    }

    /// <summary>
    /// Target hostile fauna factions only. Humans and faithful never qualify.
    /// </summary>
    private void DamageHostiles(EntityUid uid, ObeliskComponent obelisk, TransformComponent xform)
    {
        if (obelisk.Radius <= 0)
            return;

        var hit = 0;
        foreach (var (mob, mobState) in _lookup.GetEntitiesInRange<MobStateComponent>(xform.Coordinates, obelisk.Radius))
        {
            if (hit >= obelisk.MaxTargets)
                break;
            if (!_mobState.IsAlive(mob, mobState))
                continue;
            if (!TryComp<NpcFactionMemberComponent>(mob, out var faction))
                continue;
            if (HasComp<CruciformBearerComponent>(mob) || HasComp<HumanoidProfileComponent>(mob) ||
                !_factions.IsMemberOfAny((mob, faction), obelisk.HostileFactions))
                continue;

            if (_damageable.TryChangeDamage(mob, obelisk.HostileDamage, origin: uid))
                hit++;
        }
    }

    /// <summary>
    /// Eris tears up the weeds in range. The fork has no free-standing weed entity — weeds are a
    /// level on <see cref="PlantTrayComponent"/> — so trays in range are weeded instead.
    /// </summary>
    private void WeedTrays(TransformComponent xform, ObeliskComponent obelisk)
    {
        if (obelisk.Radius <= 0)
            return;

        foreach (var (tray, trayComp) in _lookup.GetEntitiesInRange<PlantTrayComponent>(xform.Coordinates, obelisk.Radius))
        {
            if (trayComp.WeedLevel <= 0)
                continue;

            _tray.AdjustWeed((tray, trayComp), -obelisk.WeedRemovalPerSecond);
        }
    }
}
