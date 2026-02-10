using Content.Client.Sound;
using Content.Shared._Oxyd.OxydGunSystem;
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
    [Dependency] private readonly SharedAudioSystem _soundPlayer = default!;

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
}
