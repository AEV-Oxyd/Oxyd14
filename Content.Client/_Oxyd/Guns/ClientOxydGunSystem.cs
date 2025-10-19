using Content.Client._Oxyd.Framework;
using Content.Shared._Oxyd.OxydGunSystem;
using Content.Shared.Interaction;
using Robust.Shared.Map;
using Robust.Shared.Timing;


namespace Content.Client._Oxyd.OxydGunSystem;


/// <summary>
/// This handles...
/// </summary>
public sealed class ClientOxydGunSystem : SharedOxydGunSystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydHandheldGunComponent, UsingMouseDownEvent>(HandleHandheldGun);
    }
    public void HandleHandheldGun(Entity<OxydHandheldGunComponent> obj, ref UsingMouseDownEvent args)
    {
        if (!TryComp<OxydGunComponent>(obj, out var gun))
        {
            Log.Error($"Tried to fire handheld gun without gun component {MetaData(obj).EntityName}");
            return;
        }

        TryExecuteFiremodeCycle(gun.selectedFiremode, (obj.Owner, gun), args.user);
    }

    public bool InterpretStep(OxydBaseGunFiremode firemode, GunEffectTryFireMouseDirection effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (shooter is null)
        {
            firemode.currentStep = 0;
            return false;
        }
        if(!TryComp<OxydMouseDataComponent>(shooter.Value, out var mouseData))
        {
            firemode.currentStep = 0;
            return false;
        }

        MapCoordinates shootingPos = _transformSystem.GetMapCoordinates(gun);
        if (TryComp<OxydHandheldGunComponent>(gun, out var handheldComp))
        {
            shootingPos = _transformSystem.GetMapCoordinates(shooter.Value);
        }

        if (TryFireGunAt(gun, shooter.Value, mouseData.mouseMap, shootingPos) is null)
        {
            firemode.currentStep = 0;
            return false;
        }
        return true;

    }

    public override List<Entity<OxydProjectileComponent>>? TryFireGunAt(Entity<OxydGunComponent> gun, EntityUid shooter,
        MapCoordinates targetCoordinates, MapCoordinates firingCoordinates)
    {
        if (!_gameTiming.IsFirstTimePredicted)
            return null;
        var projectiles = base.TryFireGunAt(gun, shooter, targetCoordinates, firingCoordinates);
        if (projectiles is null)
            return null;
        RaiseNetworkEvent(new ClientSideGunFiredEvent()
        {
            aimedPosition = targetCoordinates,
            shotFrom = firingCoordinates,
            gun = GetNetEntity(gun),
            shooter = GetNetEntity(shooter)
        });
        return projectiles;

    }
}
