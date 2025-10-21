using Content.Client._Oxyd.Framework;
using Content.Shared._Oxyd.OxydGunSystem;
using Robust.Shared.Map;

namespace Content.Client._Oxyd.OxydGunSystem;

public sealed partial class ClientOxydGunSystem
{
    public override bool InterpretStep(
        GunFiremodePrototype firemodePrototype,
        OxydGunEffect effect,
        Entity<OxydGunComponent> gun,
        EntityUid? shooter)
    {
        switch (effect)
        {
            case GunEffectCheckHandheld e:
                InterpretStep(firemodePrototype, e, gun, shooter);
                break;

            case GunEffectCheckCuffed e:
                InterpretStep(firemodePrototype, e, gun, shooter);
                break;

            case GunEffectCheckConscious e:
                InterpretStep(firemodePrototype, e, gun, shooter);
                break;

            case GunEffectCheckWielded e:
                InterpretStep(firemodePrototype, e, gun, shooter);
                break;

            case GunEffectWait e:
                InterpretStep(firemodePrototype, e, gun, shooter);
                break;

            case GunEffectTryFireGunDirection e:
                InterpretStep(firemodePrototype, e, gun, shooter);
                break;

            case GunEffectTryFireMouseDirection e:
                InterpretStep(firemodePrototype, e, gun, shooter);
                break;

            case GunEffectRepeatNextTick e:
                InterpretStep(firemodePrototype, e, gun, shooter);
                break;

            case GunEffectRepeatNow e:
                InterpretStep(firemodePrototype, e, gun, shooter);
                break;

            case GunEffectRepeatNextTickIfMouseHeld e:
                InterpretStep(firemodePrototype, e, gun, shooter);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(effect), $"Unknown OxydGunEffect type: {effect.GetType().Name}");
        }

        return true;
    }
    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectTryFireMouseDirection effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (shooter is null)
        {
            firemodePrototype.currentStep = 0;
            return false;
        }
        if(!TryComp<OxydMouseDataComponent>(shooter.Value, out var mouseData))
        {
            firemodePrototype.currentStep = 0;
            return false;
        }

        MapCoordinates shootingPos = _transformSystem.GetMapCoordinates(gun);
        if (TryComp<OxydHandheldGunComponent>(gun, out var handheldComp))
        {
            shootingPos = _transformSystem.GetMapCoordinates(shooter.Value);
        }

        var returnedList = TryFireGunAt(gun, shooter.Value, mouseData.mouseMap, shootingPos);
        if (returnedList is null)
        {
            firemodePrototype.currentStep = 0;
            return false;
        }
        RaiseLocalEvent(new FiremodeProjectilesFiredEvent()
        {
            projectiles = returnedList,
            shooter = shooter.Value,
        });
        RaiseNetworkEvent(new FiremodeClientsideFiredEvent()
        {
            gun = GetNetEntity(gun),
            shotFrom = shootingPos,
            aimedPosition = mouseData.mouseMap,
            firemodeStep = firemodePrototype.currentStep,
        });
        return true;

    }
}
