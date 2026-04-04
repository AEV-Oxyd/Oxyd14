using Content.Client.Sound;
using Content.Shared._Oxyd.OxydGunSystem;
using Content.Shared._Oxyd.Predictors;
using Content.Shared.Sound;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Spawners;

namespace Content.Client._Oxyd.OxydGunSystem;

/// <summary>
/// This handles...
/// </summary>
public partial class ClientOxydGunSystem
{
    [Dependency] private readonly AnimationPlayerSystem _animPlayer = default!;
    [Dependency] private readonly SharedPointLightSystem _lightSystem = default!;

    public void afterFireIndividual(Entity<OxydGunComponent> ent, ref GunAfterFireIndividualProjectileEvent args)
    {
        var effect = Spawn("MuzzleFlashEffect", args.projectile.Comp.initialPosition);
        _transformSystem.SetWorldRotation(effect, args.projectile.Comp.initialMovement.ToAngle());
        var lifetime = 0.4f;

        if (TryComp<TimedDespawnComponent>(args.projectile, out var despawn))
        {
            lifetime = despawn.Lifetime;
        }

        var anim = new Animation()
        {
            Length = TimeSpan.FromSeconds(lifetime),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(1f), 0),
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(0f), lifetime)
                    }
                }
            }
        };

        _animPlayer.Play(effect, anim, $"mf{ent.Comp.timesFired}");

    }

    public void updateGunIcon(Entity<OxydGunComponent> target)
    {
        foreach (var key in Enum.GetValues<GVis>())
        {
            if (!_spriteSystem.LayerMapTryGet(target.Owner, key, out var layer, false))
                continue;
            switch (key)
            {
                case GVis.MagUnder:
                    break;
                case GVis.MagAbove:
                    break;
                case GVis.BoltOpen:
                    break;
                case GVis.BoltClosed:
                    break;
                case GVis.AmmoIndicator:
                    break;
                case GVis.AttStock:
                    break;
                case GVis.AttScope:
                    break;
                case GVis.AttBarrel:
                    break;
                case GVis.AttUnderBarrel:
                    break;
                case GVis.AttInternal:
                    break;

            }

        }
    }


    public void visualUpdate()
    {
        var visquery = EntityQueryEnumerator<GlowOnChargeComponent>();
        while (visquery.MoveNext(out var uid, out var visual))
        {
            var charge = Comp<OxydGunChargeupComponent>(uid);
            if (charge.charge < visual.minCharge)
            {
                _lightSystem.RemoveLightDeferred(uid);
                continue;
            }

            var glow = _lightSystem.EnsureLight(uid);
            var scale = (charge.charge - visual.minCharge + 0.001f) / (charge.maxCharge - visual.minCharge);
            _lightSystem.SetRadius(uid, (visual.maxRadius - visual.minRadius) * scale, glow);
            _lightSystem.SetEnergy(uid, (visual.maxPower - visual.minPower) * scale, glow );
        }
    }
}
