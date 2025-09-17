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
        SubscribeLocalEvent<OxydHandheldGunComponent, UsingMouseDownEvent>(TryUseGun);
    }



    public void onEmptyShootAttempt()
    {

    }

    public void onInvalidShootAttempt()
    {

    }

    public void TryUseGun(Entity<OxydHandheldGunComponent> gun, ref UsingMouseDownEvent args)
    {
        if (!TryComp<OxydGunComponent>(gun, out var gunComp))
            return;
        if (!gunComp.ammoProvider.getAmmo(out var bullet, out var itemSlot))
        {
            onEmptyShootAttempt();
            return;
        }

        if (!TryComp<OxydBulletComponent>(bullet, out var chambered))
        {
            onInvalidShootAttempt();
            return;
        }
        if (!_gameTiming.IsFirstTimePredicted)
            return;
        var firingGrid = Transform(args.user).GridUid;
        var firingCoordinates = new EntityCoordinates(args.user, 0, 0);
        if (firingGrid is not null)
        {
            var pos = _transformSystem.GetGridOrMapTilePosition(args.user);
            firingCoordinates = new EntityCoordinates(firingGrid.Value, pos.X, pos.Y);
        }

        fireGun(args.user, (gun.Owner, gunComp), firingCoordinates, args.clickCoords.Position);
        RaiseNetworkEvent(new ClientSideGunFiredEvent()
        {
            aimedPosition = GetNetCoordinates(args.clickCoords),
            shotFrom = GetNetCoordinates(firingCoordinates),
            gun = GetNetEntity(gun),
            shooter = GetNetEntity(args.user)
        });

    }
}
