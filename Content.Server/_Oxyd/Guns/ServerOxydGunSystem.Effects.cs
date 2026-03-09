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
        if (_gameTiming.CurTime - effect.receivedUpdate > effect.validDiff ||
            effect.hardWait && effect.updateFromStep != firemodePrototype.currentStep)
        {
            EnsureActiveUpdating(firemodePrototype, gun, shooter);
            effect.missedTicks++;
            if(effect.missedTicks > effect.maxMissed)
                ResetFiremode(firemodePrototype, gun, shooter);
            return false;
        }
        firemode.catchupNeeded += effect.missedTicks;
        effect.receivedUpdate = TimeSpan.Zero;
        effect.missedTicks = 0;
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
        Log.Error($"Executat fireMouseDir effect la {_gameTiming.RealTime}, waiting {stateComp.executedFiringSteps[firemodePrototype.currentStep].Count}, gap {firemodePrototype.firingGaps}");
        return true;
    }

    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectWait effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (gun.Comp.safety || !firemodePrototype.Active)
        {
            ResetFiremode(firemodePrototype, gun, shooter);
            return false;
        }
        if (!TryComp<FiremodeStateHandlerComponent>(gun, out var stateComp))
        {
            ResetFiremode(firemodePrototype, gun, shooter);
            return false;
        }
        if (effect.skipTick == _gameTiming.CurTick)
        {
            return true;
        }
        EnsureActiveUpdating(firemodePrototype, gun, shooter);
        effect.alreadyWaited += _gameTiming.TickPeriod;
        if (stateComp.catchupNeeded > 0)
        {
            var maxCatch = Math.Min((int)((effect.waitPeriod - effect.alreadyWaited)/_gameTiming.TickPeriod), stateComp.catchupNeeded);
            Log.Error($"Catched up {maxCatch} ticks, total behind {stateComp.catchupNeeded}");
            stateComp.catchupNeeded -= maxCatch;
            effect.alreadyWaited += _gameTiming.TickPeriod * maxCatch;
        }
        // end 1 tick earlier to ensure prediction doesnt miss due to networking
        if (effect.alreadyWaited < effect.waitPeriod)
        {
            if (stateComp.ticksFoward < effect.fowardMax)
            {
                if (effect.alreadyWaited + _gameTiming.TickPeriod < effect.waitPeriod)
                {
                    return false;
                }
                stateComp.ticksFoward++;
            }
            else if (effect.alreadyWaited < effect.waitPeriod)
                return false;
        }


        effect.alreadyWaited = TimeSpan.Zero;
        RemoveActiveUpdating(firemodePrototype, gun, shooter);
        if (effect.stepBack != 0)
        {
            firemodePrototype.currentStep -= effect.stepBack;
            effect.skipTick = _gameTiming.CurTick;
        }
        return true;
    }

}
