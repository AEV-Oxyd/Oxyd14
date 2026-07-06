using System.Formats.Tar;
using System.Linq;
using Content.Client.Items.Systems;
using Content.Client.Sound;
using Content.Shared._Oxyd.OxydGunSystem;
using Content.Shared._Oxyd.Predictors;
using Content.Shared.Sound;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
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
    [Dependency] private  AnimationPlayerSystem _animPlayer = default!;
    [Dependency] private  SharedPointLightSystem _lightSystem = default!;
    [Dependency] private ItemSystem items = default!;

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

    public override void doVisUpdate(EntityUid gun)
    {
        updateGunIcon((gun, Comp<OxydGunComponent>(gun)));
    }

    public void updateGunIcon(Entity<OxydGunComponent> target)
    {
        var spriteComp = Comp<SpriteComponent>(target.Owner);
        foreach (var key in Enum.GetValues<GVis>())
        {
            if (!_spriteSystem.LayerMapTryGet(target.Owner, key, out var layer, false))
            {
                var i = spriteComp.AllLayers.Count();
                var l = _spriteSystem.AddBlankLayer((target, spriteComp), i);
                _spriteSystem.LayerMapSet(target.Owner, key, i);
            }
        }

        var magComp = CompOrNull<OxydMagazineChamberComponent>(target.Owner);
        if (magComp is not null)
        {
            var i = _spriteSystem.LayerMapGet(target.Owner, magComp.magAbove ? GVis.MagAbove : GVis.MagUnder);
            _spriteSystem.LayerSetRsiState(target.Owner, i, RSI.StateId.Invalid);
            var magaz = magComp.magazineSlot.FirstOrDefault();
            if (magaz is not null && magaz.HasItem)
            {
                var magent = (EntityUid)magaz.ContainerSlot!.ContainedEntity!;
                // maybe in the future full sprite baking? SPCR 2026
                _spriteSystem.LayerSetRsi(target.Owner, i, _spriteSystem.LayerGetEffectiveRsi(magent, 0), _spriteSystem.LayerGetRsiState(magent, 0));
            }
            else
            {
                _spriteSystem.LayerSetRsiState(target.Owner, i, RSI.StateId.Invalid);
            }
        }

        items.VisualsChanged(target.Owner);
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
