using Content.Client._Oxyd.Framework;
using Content.Shared._Oxyd.OxydGunSystem;
using Robust.Shared.Map;

namespace Content.Client._Oxyd.OxydGunSystem;

public sealed partial class ClientOxydGunSystem
{
    [Dependency] private readonly OxydMouseHandlingSystem _mouseSys = default!;
    public override bool InterpretStep(
        GunFiremodePrototype firemodePrototype,
        OxydGunEffect effect,
        Entity<OxydGunComponent> gun,
        EntityUid? shooter)
    {
        Log.Debug($"Interpreting {effect} at {_gameTiming.CurTick}");
        //Log.Debug($"Interpreting effect of type {effect}");
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
    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectTryFireMouseDirection effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (shooter is null)
        {
            ResetFiremode(firemodePrototype, gun, shooter);
            return false;
        }
        if(!TryComp<OxydMouseDataComponent>(shooter.Value, out var mouseData))
        {
            ResetFiremode(firemodePrototype, gun, shooter);
            return false;
        }

        MapCoordinates shootingPos = _transformSystem.GetMapCoordinates(gun);
        if (TryComp<OxydHandheldGunComponent>(gun, out var handheld))
        {
            shootingPos = resolveFiringPosition((gun.Owner, handheld), mouseData.mouseMap, shooter.Value);
        }


        var returnedList = TryFireGunAt(gun, shooter.Value, mouseData.mouseMap, shootingPos);
        if (returnedList is null)
        {
            Log.Debug($"Fail la fire mouse direction");
            ResetFiremode(firemodePrototype, gun, shooter);
            return false;
        }
        _charge.applyMultiplier(returnedList, _charge.getMultiplier(gun.Owner));
        //Log.Warning($"Trimis event cu datele {gun} , {GetNetEntity(gun)} , {mouseData.mouseMap}, {firemodePrototype.currentStep}");
        _netManager.ClientSendMessage(new FiremodeClientsideFiredEvent()
        {
            gun = GetNetEntity(gun),
            shotFrom = GetNetCoordinates(_transformSystem.ToCoordinates(shootingPos)),
            aimedPosition =  GetNetCoordinates(_transformSystem.ToCoordinates(mouseData.mouseMap)),
            firemodeStep = firemodePrototype.currentStep,
            clientTick = _gameTiming.CurTick,
        });

        return true;

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

        _netManager.ClientSendMessage(new FiremodeMouseStatus()
        {
            clientTick = _gameTiming.CurTick,
            gun = GetNetEntity(gun.Owner),
            held = _mouseSys.mousedDown
        });

        if (!_mouseSys.mousedDown)
        {
            RemoveActiveUpdating(firemodePrototype, gun, shooter);
            return true;
        }

        EnsureActiveUpdating(firemodePrototype, gun, shooter);
        firemodePrototype.currentStep -= effect.stepBack;
        return true;
    }

}
