using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using Content.Server._Oxyd.Framework;
using Content.Server.Effects;
using Content.Shared._Oxyd.Framework;
using Content.Shared._Oxyd.OxydGunSystem;
using Content.Shared._Oxyd.Predictors;
using Content.Shared.Weapons.Hitscan.Components;
using Robust.Server.GameObjects;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;


namespace Content.Server._Crescent.HullrotGunSystem;


/// <summary>
/// This handles...
/// </summary>
public sealed class ServerOxydProjectileSystem : SharedOxydProjectileSystem
{
    [Dependency] private readonly PhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly ColorFlashEffectSystem  _flashEffectSystem = default!;
    [Dependency] private readonly ServerOxydHelpers _serverHelp = default!;
    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
    }

    public void SimulateExtraPhysicsTicks(HashSet<Entity<OxydProjectileComponent>> entities, int ticksToSim)
    {
        if (ticksToSim == 0)
            return;
        foreach (var ent in entities)
        {
            if (!TryComp<PhysicsComponent>(ent, out var phys))
                continue;
            ProcessHitscan(ent, (phys.LinearVelocity * _gameTiming.TickPeriod.Seconds * ticksToSim).Length(), out _);
        }
    }

    public override void projectileQueued(Entity<OxydProjectileComponent> projectile)
    {

    }

    public override void afterBulletCollide(Entity<OxydProjectileComponent> obj, EntityUid other)
    {
        _flashEffectSystem.RaiseEffect(Color.Red, new List<EntityUid>(){other}, Filter.Pvs(other));
        base.afterBulletCollide(obj, other);
    }



    public override void processProjectiles(float deltaTime)
    {
        foreach (var projectile in FireNextTick)
        {
            _transform.SetMapCoordinates(projectile.Owner, projectile.Comp.initialPosition);
            if (!HasComp<OxydHitscanProjectileComponent>(projectile.Owner))
            {
                _physicsSystem.SetBodyStatus(projectile.Owner,
                    Comp<PhysicsComponent>(projectile.Owner),
                    BodyStatus.InAir,
                    true);
                _physicsSystem.SetLinearDamping(projectile.Owner, Comp<PhysicsComponent>(projectile.Owner), 0, true);
                _physicsSystem.SetSleepingAllowed(projectile.Owner,
                    Comp<PhysicsComponent>(projectile.Owner),
                    false,
                    true);
                _physicsSystem.SetLinearVelocity(projectile.Owner, projectile.Comp.initialMovement, true);
            }
            else
            {
                var map = _transform.GetMap(projectile.Owner);
                if(map is null || TerminatingOrDeleted(map) || !TryComp<HitscanBasicVisualsComponent>(projectile, out var vizComp))
                    continue;
                Vector2 pos = _transform.GetWorldPosition(projectile.Owner);
                Angle rot = Transform(projectile.Owner).LocalRotation;
                ProcessHitscan(projectile, HitscanTickRange, out float actualTravel);
                //.Error($"Actual travel {actualTravel}");
                GetHitscanEffect(new EntityCoordinates(map.Value, pos),
                    actualTravel,
                    rot,
                    vizComp,
                    out var data);
                var c = projectile.Comp.initialPosition.Position;
                var m = projectile.Comp.initialMovement;
                var box = SharedOxydHelpers.buildWorldBox(c.X, c.Y, c.X + m.X * actualTravel, c.Y);
                var pvsBox = new Box2Rotated(box, rot, box.Center );
                var targets = _serverHelp.lookupPlayerSessions(projectile.Comp.initialPosition.MapId, pvsBox);
                targets.RemoveAll(x => x.AttachedEntity == projectile.Comp.shotBy);
                //Log.Error($"Target count for PVS {targets.Count}");
                Filter pvf = Filter.Empty();
                pvf.AddPlayers(targets);
                RaiseNetworkEvent(new DrawHitscanEvent(){data = data}, pvf);
            }
        }
    }
}
