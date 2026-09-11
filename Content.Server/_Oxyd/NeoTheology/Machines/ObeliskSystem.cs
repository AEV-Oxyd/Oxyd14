using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// P2.13: the obelisk's aura. <see cref="Update"/> only paces the aura; the work lives in
/// <see cref="Tick"/> so tests drive it without waiting out <see cref="ObeliskComponent.Interval"/>.
/// </summary>
public sealed class ObeliskSystem : EntitySystem
{
    [Dependency] private readonly CruciformSystem _cruciform = default!;
    [Dependency] private readonly EyeOfTheProtectorSystem _eye = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PlantTraySystem _tray = default!;
    [Dependency] private readonly SanitySystem _sanity = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    [Dependency] private readonly NeoTheologyMachineSystem _machines = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ObeliskComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<ObeliskComponent> ent, ref ComponentShutdown args)
    {
        RefreshRegeneration(ent.Owner);
    }

    /// <summary>Use the strongest operational aura. Never let one obelisk cancel another.</summary>
    private void RefreshRegeneration(EntityUid? excluded = null)
    {
        var implants = EntityQueryEnumerator<CruciformComponent>();
        while (implants.MoveNext(out var implant, out var state))
        {
            var wanted = 1f;
            if (state.Active && state.ImplantedEntity is { } body && !TerminatingOrDeleted(body))
            {
                var xform = Transform(body);
                var obelisks = EntityQueryEnumerator<ObeliskComponent, TransformComponent>();
                while (obelisks.MoveNext(out var uid, out var obelisk, out var source))
                {
                    if (uid == excluded || !_machines.IsOperational(uid) || source.MapID != xform.MapID)
                        continue;
                    if ((source.WorldPosition - xform.WorldPosition).Length() <= obelisk.Radius)
                        wanted = Math.Max(wanted, obelisk.RegenMultiplier);
                }
            }

            if (Math.Abs(state.RegenerationMultiplier - wanted) < 0.001f)
                continue;

            state.RegenerationMultiplier = wanted;
            _cruciform.RecomputeProfile(implant, state);
        }
    }

    /// <summary>How much regeneration a single aura pulse restores, and how much it hurts for.</summary>
    private const float SanityPerPulse = 1f;

    /// <summary>Weed removal per pulse — larger than any <c>MaxWeedLevel</c>, so a tray ends at zero.</summary>
    private const float WeedRemovalPerPulse = 100f;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        RefreshRegeneration();
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ObeliskComponent>();
        while (query.MoveNext(out var uid, out var obelisk))
        {
            if (now < obelisk.NextPulse && _machines.IsOperational(uid))
                continue;

            obelisk.NextPulse = now + obelisk.Interval;
            Tick(uid, obelisk);
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

        // Buff in range, and just as importantly un-buff on the way out: the multiplier is an input
        // to RecomputeProfile, so writing it is all "stop outside the radius" needs.
        var bearers = EntityQueryEnumerator<CruciformBearerComponent, TransformComponent>();
        while (bearers.MoveNext(out var body, out var bearer, out var bodyXform))
        {
            if (bearer.Cruciform is not { } cruciform ||
                !TryComp<CruciformComponent>(cruciform, out var state) ||
                !state.Active ||
                bodyXform.MapID != xform.MapID)
                continue;

            var inRange = (bodyXform.WorldPosition - xform.WorldPosition).Length() <= obelisk.Radius;
            if (inRange)
            {
                faithful++;

                if (TryComp<SanityComponent>(body, out var sanity))
                    _sanity.ApplySanityDelta((body, sanity), SanitySource.Belief, SanityPerPulse);
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
        var damage = new DamageSpecifier();
        damage.DamageDict.Add("Blunt", obelisk.HostileDamage);

        var hit = 0;
        var mobs = EntityQueryEnumerator<MobStateComponent, NpcFactionMemberComponent, TransformComponent>();
        while (mobs.MoveNext(out var mob, out var mobState, out var faction, out var mobXform))
        {
            if (hit >= obelisk.MaxTargets)
                break;
            if (mobXform.MapID != xform.MapID)
                continue;
            if ((mobXform.WorldPosition - xform.WorldPosition).Length() > obelisk.Radius)
                continue;
            if (!_mobState.IsAlive(mob, mobState))
                continue;
            if (HasComp<CruciformBearerComponent>(mob) || HasComp<HumanoidProfileComponent>(mob) ||
                !_factions.IsMemberOfAny((mob, faction), obelisk.HostileFactions))
                continue;

            if (_damageable.TryChangeDamage(mob, damage, origin: uid))
                hit++;
        }
    }

    /// <summary>
    /// Eris tears up the weeds in range. The fork has no free-standing weed entity — weeds are a
    /// level on <see cref="PlantTrayComponent"/> — so trays in range are weeded instead.
    /// </summary>
    private void WeedTrays(TransformComponent xform, ObeliskComponent obelisk)
    {
        var trays = EntityQueryEnumerator<PlantTrayComponent, TransformComponent>();
        while (trays.MoveNext(out var tray, out _, out var trayXform))
        {
            if (trayXform.MapID != xform.MapID)
                continue;
            if ((trayXform.WorldPosition - xform.WorldPosition).Length() > obelisk.Radius)
                continue;

            _tray.AdjustWeed(tray, -WeedRemovalPerPulse);
        }
    }
}
