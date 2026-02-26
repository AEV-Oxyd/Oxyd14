using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Map;

namespace Content.Shared._Oxyd.OxydGunSystem;

public abstract partial class SharedOxydGunSystem : EntitySystem
{
    [Dependency] private readonly SharedCuffableSystem _cuff = default!;

    public void ResetFiremode(GunFiremodePrototype fire, Entity<OxydGunComponent> gun, EntityUid? shooter )
    {
        fire.currentStep = 0;
        fire.Active = false;
        RemoveActiveUpdating(fire, gun, shooter);
    }
    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectTryFireGunDirection effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        MapCoordinates gunCoords = _transformSystem.GetMapCoordinates(gun.Owner);
        if (TryFireGunAt(gun,
                gun.Owner,
                gunCoords.Offset(_transformSystem.GetWorldRotation(gun).ToWorldVec()),
                gunCoords) is null)
        {
            ResetFiremode(firemodePrototype, gun, shooter);
            return false;
        }

        return true;
    }

    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectCheckAmmo effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        var provider = firemodePrototype.AmmoProviders;
        if (provider is OxydGunAmmoChamberComponent chamber)
        {
            if (chamber.nextBullet[firemodePrototype.providerId] == EntityUid.Invalid)
            {
                ResetFiremode(firemodePrototype, gun, shooter);
                return false;
            }
        }
        if(provider is OxydGunAmmoMagazineChamberComponent mag)
        {
            if (mag.nextBullet[firemodePrototype.providerId] == EntityUid.Invalid)
            {
                ResetFiremode(firemodePrototype, gun, shooter);
                return false;
            }
        }

        return true;
    }

    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectWait effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (gun.Comp.safety)
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
        if (_netManager.IsServer)
        {
            // end time steps 1 tick earlier to stop timing incosistencies
            // overall firing time analysis in TryFireGun will stop people exploiting this anyway
            // SPCR 2026
            if (effect.alreadyWaited + _gameTiming.TickPeriod < effect.waitPeriod)
                return false;
        }
        else if(effect.alreadyWaited < effect.waitPeriod)
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
            ResetFiremode(firemodePrototype, gun, shooter);
            return false;
        }

        if (!TryComp<HandsComponent>(shooter, out var hands))
        {
            ResetFiremode(firemodePrototype, gun, shooter);
            return false;
        }

        var holdings = _handsSystem.EnumerateHeld((shooter.Value, hands));
        foreach (var thing in holdings)
        {
            if (gun.Owner == thing)
                return true;
        }
        ResetFiremode(firemodePrototype, gun, shooter);
        return false;
    }

    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectCheckConscious effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (shooter is null)
        {
            ResetFiremode(firemodePrototype, gun, shooter);
            return false;
        }

        if (!TryComp<MobStateComponent>(shooter.Value, out var comp))
        {
            ResetFiremode(firemodePrototype, gun, shooter);
            return false;
        }

        if (comp.CurrentState == MobState.Alive)
            return true;
        ResetFiremode(firemodePrototype, gun, shooter);
        return false;
    }

    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectRepeatNextTick effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (_gameTiming.CurTime.Ticks - effect.lastTrigger.Ticks> effect.triggerTimeout.Ticks)
        {
            effect.timesBack = 0;
        }
        effect.lastTrigger = _gameTiming.CurTime;
        if (effect.timesBack < effect.repeatCount)
        {
            effect.timesBack++;
            firemodePrototype.currentStep -= effect.stepBack;
            EnsureActiveUpdating(firemodePrototype, gun, shooter);
            return false;
        }
        effect.timesBack = 0;
        RemoveActiveUpdating(firemodePrototype, gun, shooter);
        return true;
    }

    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectCheckCuffed effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (shooter is null)
            return true;
        if (!TryComp<CuffableComponent>(shooter, out var comp))
            return true;
        if (_cuff.IsCuffed((shooter.Value, comp)))
            return false;
        return true;
    }

    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectCheckWielded effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (!TryComp<WieldableComponent>(gun.Owner, out var wcomp))
            return true;
        return wcomp.Wielded;
    }

    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectModifyCharge effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (!TryComp<OxydGunChargeupComponent>(gun.Owner, out var ccomp))
            return true;
        ccomp.charge = Math.Clamp(ccomp.charge + effect.addAmount, 0, ccomp.maxCharge);
        ccomp.lastCharge = _gameTiming.CurTime;
        EnsureComp<ActiveOxydGunChargeupComponent>(gun.Owner);
        return true;
    }

    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectResetCharge effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (!TryComp<OxydGunChargeupComponent>(gun.Owner, out var ccomp))
            return true;
        ccomp.charge = 0;
        ccomp.lastCharge = TimeSpan.Zero;
        RemComp<ActiveOxydGunChargeupComponent>(gun.Owner);
        return true;
    }

    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectCheckCharge effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (!TryComp<OxydGunChargeupComponent>(gun.Owner, out var ccomp))
            return true;
        if (ccomp.charge > effect.min && ccomp.charge < effect.max)
            return true;
        ResetFiremode(firemodePrototype, gun, shooter);
        return false;
    }



}
