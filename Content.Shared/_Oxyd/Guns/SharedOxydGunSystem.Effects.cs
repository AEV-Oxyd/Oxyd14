using Content.Shared.Hands.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;

namespace Content.Shared._Oxyd.OxydGunSystem;

public abstract partial class SharedOxydGunSystem : EntitySystem
{
    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectTryFireGunDirection effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        MapCoordinates gunCoords = _transformSystem.GetMapCoordinates(gun.Owner);
        if (TryFireGunAt(gun,
                gun.Owner,
                gunCoords.Offset(_transformSystem.GetWorldRotation(gun).ToWorldVec()),
                gunCoords) is null)
        {
            firemodePrototype.currentStep = 0;
            return false;
        }

        return true;
    }

    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectCheckAmmo effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (TryComp<OxydGunAmmoChamberComponent>(gun, out var chamberComp) && chamberComp.nextBullet == EntityUid.Invalid)
        {
            firemodePrototype.currentStep = 0;
            RemoveActiveUpdating(firemodePrototype, gun, shooter);
            return false;
        }
        if (TryComp<OxydGunAmmoMagazineChamberComponent>(gun, out var magComp) && magComp.nextBullet == EntityUid.Invalid)
        {
            firemodePrototype.currentStep = 0;
            RemoveActiveUpdating(firemodePrototype, gun, shooter);
            return false;
        }

        return true;
    }

    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectWait effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (effect.skipTick == _gameTiming.CurTick)
        {
            return true;
        }
        EnsureActiveUpdating(firemodePrototype, gun, shooter);
        effect.alreadyWaited += _gameTiming.TickPeriod;
        if (effect.alreadyWaited < effect.waitPeriod)
            return false;
        effect.alreadyWaited = TimeSpan.Zero;
        RemoveActiveUpdating(firemodePrototype, gun, shooter);
        if (effect.stepBack != 0)
        {
            firemodePrototype.currentStep -= effect.stepBack;
            effect.skipTick = _gameTiming.CurTick;
        }
        return true;
    }


    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectCheckHandheld effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (shooter is null)
        {
            firemodePrototype.currentStep = 0;
            return false;
        }

        if (!TryComp<HandsComponent>(shooter, out var hands))
        {
            firemodePrototype.currentStep = 0;
            return false;
        }

        var holdings = _handsSystem.EnumerateHeld((shooter.Value, hands));
        foreach (var thing in holdings)
        {
            if (gun.Owner == thing)
                return true;
        }
        firemodePrototype.currentStep = 0;
        return false;
    }

    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectCheckConscious effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (shooter is null)
        {
            firemodePrototype.currentStep = 0;
            return false;
        }

        if (!TryComp<MobStateComponent>(shooter.Value, out var comp))
        {
            firemodePrototype.currentStep = 0;
            return false;
        }

        if (comp.CurrentState == MobState.Alive)
            return true;
        firemodePrototype.currentStep = 0;
        return false;
    }

    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectRepeatNextTick effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (effect.timesBack < effect.repeatCount)
        {
            effect.timesBack++;
            firemodePrototype.currentStep -= effect.stepsBack;
            EnsureActiveUpdating(firemodePrototype, gun, shooter);
            return false;
        }

        effect.timesBack = 0;
        RemoveActiveUpdating(firemodePrototype, gun, shooter);
        return true;
    }

    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectRepeatNow effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (effect.timesBack < effect.repeatCount)
        {
            effect.timesBack++;
            firemodePrototype.currentStep -= effect.stepsBack;
            return true;
        }
        effect.timesBack = 0;
        return true;
    }


}
