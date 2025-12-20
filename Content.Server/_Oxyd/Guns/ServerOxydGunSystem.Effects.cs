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
                return InterpretStep(firemodePrototype, e, gun, shooter);

            case GunEffectCheckCuffed e:
                return InterpretStep(firemodePrototype, e, gun, shooter);
            case GunEffectCheckConscious e:
                return InterpretStep(firemodePrototype, e, gun, shooter);

            case GunEffectCheckWielded e:
                return InterpretStep(firemodePrototype, e, gun, shooter);

            case GunEffectWait e:
                return InterpretStep(firemodePrototype, e, gun, shooter);

            case GunEffectTryFireGunDirection e:
                return InterpretStep(firemodePrototype, e, gun, shooter);

            case GunEffectTryFireMouseDirection e:
                return InterpretStep(firemodePrototype, e, gun, shooter);
            case GunEffectRepeatNextTick e:
                return InterpretStep(firemodePrototype, e, gun, shooter);

            case GunEffectRepeatNow e:
                return InterpretStep(firemodePrototype, e, gun, shooter);

            case GunEffectRepeatNextTickIfMouseHeld e:
                return InterpretStep(firemodePrototype, e, gun, shooter);

            case GunEffectCheckAmmo e:
                return InterpretStep(firemodePrototype, e, gun, shooter);

            default:
                throw new ArgumentOutOfRangeException(nameof(effect), $"Unknown OxydGunEffect type: {effect.GetType().Name}");
        }

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

        if (!TryComp<FiremodeStateHandlerComponent>(gun, out var firemode))
        {
            firemodePrototype.currentStep = 0;
            return false;
        }
        RemoveActiveUpdating(firemodePrototype, gun, shooter);
        return true;
    }

    public bool InterpretStep(GunFiremodePrototype firemodePrototype,
        GunEffectTryFireMouseDirection effect,
        Entity<OxydGunComponent> gun,
        EntityUid? shooter)
    {
        if (shooter is null)
        {
            firemodePrototype.currentStep = 0;
            return false;
        }

        if (!TryComp<FiremodeStateHandlerComponent>(gun, out var stateComp))
        {
            firemodePrototype.currentStep = 0;
            return false;
        }
        Log.Error($"Executat fireMouseDir effect la {_gameTiming.RealTime}");
        stateComp.executedFiringSteps.Add(firemodePrototype.currentStep);
        return true;
    }
}
