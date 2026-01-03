using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using Content.Server.Effects;
using Content.Shared._Oxyd.OxydGunSystem;
using Robust.Server.GameObjects;
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
    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
    }

    public void SimulateExtraPhysicsTicks(List<Entity<OxydProjectileComponent>> entities, int ticksToSim)
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
            Log.Debug($"Speed is {projectile.Comp.initialMovement}");
            _transform.SetMapCoordinates(projectile.Owner, projectile.Comp.initialPosition);
            _physicsSystem.SetBodyStatus(projectile.Owner,Comp<PhysicsComponent>(projectile.Owner), BodyStatus.InAir, true);
            _physicsSystem.SetLinearDamping(projectile.Owner,Comp<PhysicsComponent>(projectile.Owner), 0, true);
            _physicsSystem.SetSleepingAllowed(projectile.Owner,Comp<PhysicsComponent>(projectile.Owner), false, true);
            _physicsSystem.SetLinearVelocity(projectile.Owner, projectile.Comp.initialMovement, true);
        }
    }
}
