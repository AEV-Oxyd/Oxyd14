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
        var firingCoordinates = resolveFiringPosition(gun, _transformSystem.ToMapCoordinates(args.clickCoords), args.user);
        Log.Debug($"MouseClick pos at {args.clickCoords.Position}");
        fireGun(args.user, (gun.Owner, gunComp), firingCoordinates, _transformSystem.ToMapCoordinates(args.clickCoords));
        RaiseNetworkEvent(new ClientSideGunFiredEvent()
        {
            aimedPosition = _transformSystem.ToMapCoordinates(args.clickCoords),
            shotFrom = firingCoordinates,
            gun = GetNetEntity(gun),
            shooter = GetNetEntity(args.user)
        });

    }
}
