
/// </summary>
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Shared._Oxyd.Framework;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.OxydGunSystem;

[Serializable, NetSerializable]
public sealed class OxydProjectileFiredEvent : EntityEventArgs
{
    public NetCoordinates shootingPosition;
    public NetCoordinates targetPosition;
    public NetEntity weapon;
    public NetEntity shooter;
    public int projectileKey;
}
[Serializable, NetSerializable]
public class HitscanVisualsData(NetCoordinates coordinates, Angle angle, SpriteSpecifier sprite, float scale)
{
    public NetCoordinates coordinates = coordinates;
    public Angle angle = angle;
    public SpriteSpecifier sprite = sprite;
    public float scale = scale;
}
[Serializable, NetSerializable]
public sealed class DrawHitscanEvent : EntityEventArgs
{
    public required List<HitscanVisualsData> data ;
}
/// <summary>
/// This handles...
/// </summary>
public abstract partial class SharedOxydProjectileSystem : EntitySystem
{
    [Dependency] protected  IGameTiming _gameTiming = default!;
    [Dependency] protected  SharedTransformSystem _transform = default!;
    [Dependency] protected  SharedOxydGunSystem _Guns = default!;
    [Dependency] protected  SharedPhysicsSystem _physics = default!;
    [Dependency] protected  RayCastSystem _rayCastSystem = default!;
    [Dependency] protected  INetManager _netmanager = default!;
    [Dependency] protected  DamageableSystem _damage = default!;
    [Dependency] protected  SharedOxydHelpers _help = default!;
    //[Dependency] private  EntityManager _entityManager = default!;
    public List<Entity<OxydProjectileComponent>> FireNextTick =  new List<Entity<OxydProjectileComponent>>();
    private float tickDelay = 0;
    // hitscans process 100m per tick
    public const float HitscanTickRange = 100f;

    private EntityQuery<HitscanBasicVisualsComponent> _visualsQuery;

    public bool ProcessHitscan(Entity<OxydProjectileComponent> ent, float range, out float traveled)
    {
        traveled = float.MinValue;
        if (!TryComp<PhysicsComponent>(ent, out var phys))
            return false;
        Vector2 finalTranslation = ent.Comp.initialMovement.Normalized() * range;
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
                if (ent.Comp.maxHits < ent.Comp.hits.Count)
                {
                    traveled = hit.Fraction * range;
                    return false;
                }
            }
        }

        traveled = range;
        _transform.SetMapCoordinates(ent.Owner, ent.Comp.initialPosition.Offset(finalTranslation));
        return true;

    }

    public override void Initialize()
    {
        base.Initialize();
        tickDelay = 1000.0f/(float)_gameTiming.TickRate;
        _visualsQuery = GetEntityQuery<HitscanBasicVisualsComponent>();
        SubscribeLocalEvent<OxydProjectileComponent, StartCollideEvent>(onCollide);
    }

    public virtual bool shouldTriggerCollide(Entity<OxydProjectileComponent> obj, ref StartCollideEvent args)
    {
        if(!shouldTriggerCollide(obj, args.OtherEntity))
            return false;
        if (!TryComp<FixturesComponent>(obj, out var fixtures))
            return false;
        if (args.OurFixture != fixtures.Fixtures.Values.First())
            return false;
        return true;
    }

    public virtual bool shouldTriggerCollide(Entity<OxydProjectileComponent> obj, EntityUid hitting)
    {
        if (hitting == obj.Comp.shotBy)
            return false;
        return true;
    }

    public virtual void afterBulletCollide(Entity<OxydProjectileComponent> obj, EntityUid other)
    {
        if (obj.Comp.maxHits < obj.Comp.hits.Count)
        {
            _help.QueueDel(obj.Owner);

        }
    }


    public void onCollide(Entity<OxydProjectileComponent> obj, ref StartCollideEvent args)
    {
        onCollide(obj, args.OtherEntity);
    }

    public void onCollide(Entity<OxydProjectileComponent> obj, EntityUid other)
    {
        //Log.Warning("OxydProjectileSystem::onCollide");
        if (!shouldTriggerCollide(obj, other))
            return;
        obj.Comp.hits.Add(other);
        if (TryComp<OxydProjectileApplyDamageComponent>(obj, out var damage))
        {
            _damage.TryChangeDamage(other, damage.DamageSpecifier, false, true);

            if (_netmanager.IsClient)
                Log.Error($"CLIENT - Applying damage to {MetaData(other).EntityName}");
            else
                Log.Error($"SERVER - Applying damage to {MetaData(other).EntityName}");

        }
        afterBulletCollide(obj, other);
    }
    public void queueProjectile(Entity<OxydProjectileComponent> projectile)
    {
        FireNextTick.Add(projectile);
        projectileQueued(projectile);
    }
    // taken from HitscanBasicRaycastSystem but modded to be client/server side
    public bool GetHitscanEffect(EntityCoordinates fromCoordinates, float distance, Angle shotAngle, HitscanBasicVisualsComponent vizComp, out List<HitscanVisualsData> data)
    {
        data = new();
        if (distance == 0)
            return false;
        var fromXform = Transform(fromCoordinates.EntityId);

        // We'll get the effects relative to the grid / map of the firer
        // Look you could probably optimise this a bit with redundant transforms at this point.

        var gridUid = fromXform.GridUid;
        if (gridUid != fromCoordinates.EntityId && TryComp(gridUid, out TransformComponent? gridXform))
        {
            var (_, gridRot, gridInvMatrix) = _transform.GetWorldPositionRotationInvMatrix(gridXform);
            var map = _transform.ToMapCoordinates(fromCoordinates);
            fromCoordinates = new EntityCoordinates(gridUid.Value, Vector2.Transform(map.Position, gridInvMatrix));
            // Dont know why it has to be 2x but 1x was off and i couldn't be bothered to investigate. SPCR 2026
            shotAngle -= gridRot*2;
        }
        else
        {
            shotAngle -= _transform.GetWorldRotation(fromXform);
        }
        if (distance >= 1f)
        {
            if (vizComp.MuzzleFlash != null)
            {
                var coords = fromCoordinates.Offset(shotAngle.ToVec().Normalized() / 2);
                var netCoords = GetNetCoordinates(coords);

                data.Add(new HitscanVisualsData(netCoords, shotAngle, vizComp.MuzzleFlash, 1f));
            }

            if (vizComp.TravelFlash != null)
            {
                var coords = fromCoordinates.Offset(shotAngle.ToVec() * (distance + 0.5f) / 2);
                var netCoords = GetNetCoordinates(coords);

                data.Add(new HitscanVisualsData(netCoords, shotAngle, vizComp.TravelFlash, distance - 1.5f));
            }
        }

        if (vizComp.ImpactFlash != null)
        {
            var coords = fromCoordinates.Offset(shotAngle.ToVec() * distance);
            var netCoords = GetNetCoordinates(coords);
            data.Add(new HitscanVisualsData(netCoords, shotAngle.FlipPositive(), vizComp.ImpactFlash, 1f));
        }

        return true;
    }
    public abstract void projectileQueued(Entity<OxydProjectileComponent> projectile);
    public abstract void processProjectiles(float deltaTime);
    public override void Update(float  deltaTime)
    {
        processProjectiles(deltaTime);
        FireNextTick.Clear();
    }
}
