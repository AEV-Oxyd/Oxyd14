using Content.Shared._Oxyd.OxydGunSystem;

namespace Content.Server._Oxyd.Guns;

public sealed partial class ServerOxydGunSystem
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

    public bool InterpretStep(
        GunFiremodePrototype firemodePrototype,
        GunEffectTryFireMouseDirection effect,
        Entity<OxydGunComponent> gun,
        EntityUid? shooter)
    {
        return true;
    }

    public bool InterpretStep(GunFiremodePrototype firemodePrototype,
        GunEffectRepeatNextTickIfMouseHeld effect,
        Entity<OxydGunComponent> gun,
        EntityUid? shooter)
    {
        if (shooter is null)
        {
            firemodePrototype.currentStep = 0;
            return false;
        }

        if (!TryComp<FiremodeStateHandlerComponent>(shooter, out var firemode))
        {
            firemodePrototype.currentStep = 0;
            return false;
        }

        firemode.executedFiringSteps.Clear();
        return true;
    }
}
