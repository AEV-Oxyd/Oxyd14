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
        Log.Debug($"Interpreting {effect} at {_gameTiming.CurTick}");
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

            case GunEffectRepeatNextTickIfMouseHeld e:
                return InterpretStep(firemodePrototype, e, gun, shooter);

            case GunEffectCheckAmmo e:
                return InterpretStep(firemodePrototype, e, gun, shooter);

            case GunEffectCheckCharge e:
                return InterpretStep(firemodePrototype, e, gun, shooter);

            case GunEffectModifyCharge e:
                return InterpretStep(firemodePrototype, e, gun, shooter);

            case GunEffectResetCharge e:
                return InterpretStep(firemodePrototype, e, gun, shooter);



            default:
                throw new ArgumentOutOfRangeException(nameof(effect), $"Unknown OxydGunEffect type: {effect.GetType().Name}");
        }
    }

    public bool InterpretStep(GunFiremodePrototype firemodePrototype,
        GunEffectRepeatNextTickIfMouseHeld effect,
        Entity<OxydGunComponent> gun,
        EntityUid? shooter)
    {
        if (shooter is null)
        {
            ResetFiremode(firemodePrototype, gun, shooter);
            return false;
        }

        if (!TryComp<FiremodeStateHandlerComponent>(gun, out var firemode))
        {
            ResetFiremode(firemodePrototype, gun, shooter);
            return false;
        }

        // keep repeating until message from client comes
        if (_gameTiming.CurTime - effect.receivedUpdate > effect.validDiff)
        {
            EnsureActiveUpdating(firemodePrototype, gun, shooter);
            effect.missedTicks++;
            if(effect.missedTicks > effect.maxMissed)
                ResetFiremode(firemodePrototype, gun, shooter);
            return false;
        }
        effect.receivedUpdate = TimeSpan.Zero;
        effect.missedTicks = 0;
        if (!effect.mouseHeld)
        {
            RemoveActiveUpdating(firemodePrototype, gun, shooter);
            return true;
        }
        EnsureActiveUpdating(firemodePrototype, gun, shooter);
        if(effect.stepBack != 0)
            firemodePrototype.currentStep -= effect.stepBack;
        //  let client handle full firemode cycles instead.
        else if(firemodePrototype.maxSteps == firemodePrototype.currentStep)
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
            ResetFiremode(firemodePrototype, gun, shooter);
            return false;
        }

        if (!TryComp<FiremodeStateHandlerComponent>(gun, out var stateComp))
        {
            ResetFiremode(firemodePrototype, gun, shooter);
            return false;
        }
        Log.Error($"Executat fireMouseDir effect la {_gameTiming.RealTime}");
        // this is clear sign of missing fire from client, mby in future move some of lag compensation
        // here ? SPCR 2026
        if (stateComp.executedFiringSteps.ContainsKey(firemodePrototype.currentStep))
            stateComp.executedFiringSteps[firemodePrototype.currentStep] = _charge.getMultiplier(gun.Owner);
        else
            stateComp.executedFiringSteps.Add(firemodePrototype.currentStep, _charge.getMultiplier((gun.Owner, null)));
        return true;
    }

}
