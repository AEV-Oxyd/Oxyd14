using System.Numerics;
using Content.Client._Oxyd.Framework;
using Content.Client.Effects;
using Content.Client.Projectiles;
using Content.Shared._Oxyd.Framework;
using Content.Shared._Oxyd.OxydGunSystem;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Physics;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;


namespace Content.Client._Oxyd.OxydGunSystem;


/// <summary>
/// This handles...
/// </summary>
public sealed partial class ClientOxydProjectileSystem : SharedOxydProjectileSystem
{
    [Dependency] private  ColorFlashEffectSystem _colorFlashEffect = default!;
    [Dependency] private  FixClientsidePhysicsSystem _patcher = default!;
    [Dependency] private  OxydClientsidePleaseIgnoreSystem _ignore = default!;
    [Dependency] private  SpriteSystem _sprite = default!;
    [Dependency] private  AnimationPlayerSystem _animPlayer = default!;

    public static  EntProtoId HitscanProto = "HitscanEffect";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<DrawHitscanEvent>(doDraw);
    }

    public override bool shouldTriggerCollide(Entity<OxydProjectileComponent> obj, ref StartCollideEvent args)
    {
        if (!base.shouldTriggerCollide(obj, ref args))
            return false;
        if (_ignore.shouldIgnore(obj.Owner))
            return false;
        return true;

    }

    public void doDraw(DrawHitscanEvent msg)
    {
        DrawHitscans(msg.data);
    }

    public override void afterBulletCollide(Entity<OxydProjectileComponent> obj, EntityUid other)
    {
        _colorFlashEffect.RaiseEffect(Color.Red, new List<EntityUid>(){other}, Filter.Local());
        base.afterBulletCollide(obj, other);
    }

    public override void projectileQueued(Entity<OxydProjectileComponent> projectile)
    {

    }
    // taken from GunSystem.cs
    private void DrawHitscans(List<HitscanVisualsData> scans)
    {
        // ALL I WANT IS AN ANIMATED EFFECT

        // TODO EFFECTS
        // This is very jank
        // because the effect consists of three unrelatd entities, the hitscan beam can be split appart.
        // E.g., if a grid rotates while part of the beam is parented to the grid, and part of it is parented to the map.
        // Ideally, there should only be one entity, with one sprite that has multiple layers
        // Or at the very least, have the other entities parented to the same entity to make sure they stick together.
        foreach (var a in scans)
        {
            if (a.sprite is not SpriteSpecifier.Rsi rsi)
                continue;

            var coords = GetCoordinates(a.coordinates);

            if (!TryComp(coords.EntityId, out TransformComponent? relativeXform))
                continue;

            var ent = Spawn(HitscanProto, coords);
            var sprite = Comp<SpriteComponent>(ent);

            var xform = Transform(ent);
            var targetWorldRot = a.angle + _transform.GetWorldRotation(relativeXform);
            var delta = targetWorldRot - _transform.GetWorldRotation(xform);
            _transform.SetLocalRotationNoLerp(ent, xform.LocalRotation + delta, xform);

            sprite[EffectLayers.Unshaded].AutoAnimated = false;
            _sprite.LayerSetSprite((ent, sprite), EffectLayers.Unshaded, rsi);
            _sprite.LayerSetRsiState((ent, sprite), EffectLayers.Unshaded, rsi.RsiState);
            _sprite.SetScale((ent, sprite), new Vector2(a.scale, 1f));
            sprite[EffectLayers.Unshaded].Visible = true;

            var anim = new Animation()
            {
                Length = TimeSpan.FromSeconds(0.48f),
                AnimationTracks =
                {
                    new AnimationTrackSpriteFlick()
                    {
                        LayerKey = EffectLayers.Unshaded,
                        KeyFrames =
                        {
                            new AnimationTrackSpriteFlick.KeyFrame(rsi.RsiState, 0f),
                        }
                    }
                }
            };

            _animPlayer.Play(ent, anim, "hitscan-effect");
        }
    }
    public override void processProjectiles(float deltaTime)
    {
        foreach (var projectile in FireNextTick)
        {
            _transform.SetMapCoordinates(projectile.Owner, projectile.Comp.initialPosition);
            if (!HasComp<OxydHitscanProjectileComponent>(projectile.Owner))
            {
                _transform.SetMapCoordinates(projectile.Owner, projectile.Comp.initialPosition);
                _physics.SetBodyStatus(projectile.Owner,
                    Comp<PhysicsComponent>(projectile.Owner),
                    BodyStatus.InAir,
                    false);
                _physics.SetLinearDamping(projectile.Owner, Comp<PhysicsComponent>(projectile.Owner), 0f, false);
                _physics.SetAngularDamping(projectile.Owner, Comp<PhysicsComponent>(projectile.Owner), 0f, false);
                _physics.SetFriction(projectile.Owner, Comp<PhysicsComponent>(projectile.Owner), 0f, false);
                _physics.SetLinearVelocity(projectile.Owner, projectile.Comp.initialMovement);
                _patcher.startForcedPrediction(projectile.Owner);
            }
            else
            {
                var map = _transform.GetMap(projectile.Owner);
                if(map is null || TerminatingOrDeleted(map) || !TryComp<HitscanBasicVisualsComponent>(projectile, out var vizComp))
                    continue;
                Vector2 pos = _transform.GetWorldPosition(projectile.Owner);
                Angle rot = Transform(projectile.Owner).LocalRotation;
                ProcessHitscan(projectile, HitscanTickRange, out float actualTravel);
                GetHitscanEffect(new EntityCoordinates(map.Value, pos),
                    actualTravel,
                    rot,
                    vizComp,
                    out var data);
                DrawHitscans(data);
            }

        }
    }
}
