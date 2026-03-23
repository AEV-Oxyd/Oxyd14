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
        foreach (OxydGunEffect eff in fire.Effects)
        {
            if(eff is OxydResetableEffect casted)
                casted.Reset();
        }
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
        if (hasProviderAmmo(gun, firemodePrototype.providerId))
            return true;
        ResetFiremode(firemodePrototype, gun, shooter);
        return false;
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

    public virtual bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectRepeatNextTick effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
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


    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectCheckCharge effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (!TryComp<OxydGunChargeupComponent>(gun.Owner, out var ccomp))
            return true;
        if (ccomp.charge > effect.min && ccomp.charge < effect.max)
            return true;
        ResetFiremode(firemodePrototype, gun, shooter);
        return false;
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



}
