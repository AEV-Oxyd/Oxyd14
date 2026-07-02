using Content.Shared._Oxyd.OxydGunSystem;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Guns;

public sealed partial class ServerOxydGunSystem
{
    public override bool InterpretStep(
        GunFiremodePrototype firemodePrototype,
        OxydGunEffect effect,
        Entity<OxydGunComponent> gun,
        EntityUid? shooter)
    {
        if (gun.Comp.jammed)
        {
            ResetFiremode(firemodePrototype, gun, shooter);
            return false;
        }

        Log.Debug($"Interpreting {effect} at {_gameTiming.CurTick}, real {DateTime.UtcNow.ToString("HH:mm:ss.fffffff")}");
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

            case GunEffectRepeat e:
                return InterpretStep(firemodePrototype, e, gun, shooter);

            case GunEffectRepeatMouseHeld e:
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
        GunEffectRepeatMouseHeld effect,
        Entity<OxydGunComponent> gun,
        EntityUid? shooter)
    {
        if (shooter is null || !gun.Comp.selectedFiremodePrototype.Active)
        {
            ResetFiremode(firemodePrototype, gun, shooter);
            return false;
        }


        // keep repeating until message from client comes
        if (effect.updateFromStep != firemodePrototype.currentStep)
        {
            EnsureActiveUpdating(firemodePrototype, gun, shooter);
            return false;
        }
        effect.receivedUpdate = TimeSpan.Zero;
        if (!effect.mouseHeld)
        {
            RemoveActiveUpdating(firemodePrototype, gun, shooter);
            return true;
        }
        EnsureActiveUpdating(firemodePrototype, gun, shooter);
        firemodePrototype.currentStep -= effect.stepBack;
        return false;
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
        // this is clear sign of missing fire from client, mby in future move some of lag compensation
        // here ? SPCR 2026
        if (!stateComp.executedFiringSteps.ContainsKey(firemodePrototype.currentStep))
            stateComp.executedFiringSteps.Add(firemodePrototype.currentStep, new Queue<float>());
        stateComp.executedFiringSteps[firemodePrototype.currentStep].Enqueue(_charge.getMultiplier(gun.Owner));
        Log.Debug($"Executat fireMouseDir effect la {_gameTiming.RealTime}, waiting {stateComp.executedFiringSteps[firemodePrototype.currentStep].Count}");
        return true;
    }

    public override bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectRepeat effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (effect.timesBack < effect.repeatCount)
        {
            effect.timesBack++;
            firemodePrototype.currentStep -= effect.stepBack;
            EnsureActiveUpdating(firemodePrototype, gun, shooter);
            return true;
        }
        effect.timesBack = 0;
        RemoveActiveUpdating(firemodePrototype, gun, shooter);
        return true;
    }

    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectWait effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (gun.Comp.safety || !firemodePrototype.Active)
        {
            ResetFiremode(firemodePrototype, gun, shooter);
            return false;
        }
        if (effect.skip)
        {
            effect.skip = false;
            return true;
        }
        EnsureActiveUpdating(firemodePrototype, gun, shooter);
        var needTime = effect.waitPeriod - effect.alreadyWaited;
        var usedBudget = needTime < firemodePrototype.timeBudget ? needTime : firemodePrototype.timeBudget;
        effect.alreadyWaited += usedBudget;
        firemodePrototype.timeBudget -= usedBudget;
        if (effect.alreadyWaited < effect.waitPeriod)
        {
            return false;
        }
        effect.alreadyWaited = TimeSpan.Zero;
        RemoveActiveUpdating(firemodePrototype, gun, shooter);
        if (effect.stepBack != 0)
        {
            firemodePrototype.currentStep -= effect.stepBack;
            effect.skip = true;
        }
        return true;
    }

}
