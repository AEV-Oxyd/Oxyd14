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
        if (HasComp<OxydHandheldGunComponent>(gun))
        {
            shootingPos = _transformSystem.GetMapCoordinates(shooter.Value);
        }

        var returnedList = TryFireGunAt(gun, shooter.Value, mouseData.mouseMap, shootingPos);
        if (returnedList is null)
        {
            firemodePrototype.currentStep = 0;
            return false;
        }
        Log.Warning($"Trimis event cu datele {gun} , {GetNetEntity(gun)} , {mouseData.mouseMap}, {firemodePrototype.currentStep}");
        _netManager.ClientSendMessage(new FiremodeClientsideFiredEvent()
        {
            gun = GetNetEntity(gun),
            shotFrom = GetNetCoordinates(_transformSystem.ToCoordinates(shootingPos)),
            aimedPosition =  GetNetCoordinates(_transformSystem.ToCoordinates(mouseData.mouseMap)),
            firemodeStep = firemodePrototype.currentStep,
        });
        RaiseLocalEvent(new FiremodeProjectilesFiredEvent()
        {
            projectiles = returnedList,
            shooter = shooter.Value,
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
            firemodePrototype.currentStep = 0;
            return false;
        }

        if (!_mouseSys.mousedDown)
        {
            RemComp<OxydActiveFiremodeUpdatingComponent>(shooter.Value);
            Log.Debug("Stopped fullauto");
            return true;
        }
        var comp = EnsureComp<OxydActiveFiremodeUpdatingComponent>(shooter.Value);
        comp.FiremodePrototype = firemodePrototype;
        comp.gun = gun;
        comp.shooter = shooter.Value;
        Log.Debug("Ensured full auto");
        return true;
    }
}
