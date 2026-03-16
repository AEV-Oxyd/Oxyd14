using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using Content.Server._Oxyd.Framework;
using Content.Server.Effects;
using Content.Shared._Oxyd.OxydGunSystem;
using Content.Shared._Oxyd.Predictors;
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
    [Dependency] private readonly RayCastSystem _rayCastSystem = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
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
            Vector2 finalTranslation = phys.LinearVelocity * _gameTiming.TickPeriod.Seconds * ticksToSim;
            QueryFilter filter = new QueryFilter()
            {
                MaskBits =  phys.CollisionMask,
                LayerBits = phys.CollisionMask,
            };
            var result = _rayCastSystem.CastRay(ent.Comp.initialPosition.MapId, ent.Comp.initialPosition.Position, finalTranslation, filter);
            if (result.Hit)
            {
                foreach (var hit in result.Results)
                {
                    onCollide(ent, hit.Entity);
                    if (TerminatingOrDeleted(ent))
                        break;
                }
            }

            if (TerminatingOrDeleted(ent))
                continue;
            _transform.SetMapCoordinates(ent.Owner, ent.Comp.initialPosition.Offset(finalTranslation));
        }
    }

    public override void projectileQueued(Entity<OxydProjectileComponent> projectile)
    {

    }

    public override void afterBulletCollide(Entity<OxydProjectileComponent> obj, ref StartCollideEvent args)
    {
        _flashEffectSystem.RaiseEffect(Color.Red, new List<EntityUid>(){args.OtherEntity}, Filter.Pvs(args.OtherEntity));
        base.afterBulletCollide(obj, ref args);
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
                GetHitscanEffect(new EntityCoordinates(projectile.Owner, 0, 0),
                    200,
                    projectile.Comp.initialMovement.ToAngle(),
                    projectile.Owner,
                    out var data);
                var pvsRange = _config.GetCVar(CVars.NetMaxUpdateRange);
                var c = projectile.Comp.initialPosition.Position;
                var m = projectile.Comp.initialMovement;
                var pvsBox = new Box2Rotated(new Box2(c.X,c.Y - pvsRange, c.X + m.X*50, c.Y + pvsRange ), projectile.Comp.initialMovement.ToAngle());
                var targets = _serverHelp.lookupPlayerSessions(projectile.Comp.initialPosition.MapId, pvsBox);
                Filter pvf = Filter.Empty();
                pvf.AddPlayers(targets);
                RaiseNetworkEvent(new DrawHitscanEvent(){data = data}, pvf);
            }
        }
    }
}
