using Content.Server.Players.RateLimiting;
using Content.Shared._Oxyd.OxydGunSystem;
using Robust.Shared.Map;

namespace Content.Server._Oxyd.Guns;


/// <summary>
/// This handles...
/// </summary>
public sealed class ServerOxydGunSystem : SharedOxydGunSystem
{
    [Dependency] private readonly PlayerRateLimitManager _playerRateLimitManager = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<ClientSideGunFiredEvent>(OnClientFireGun);
    }

    public void OnClientFireGun(ClientSideGunFiredEvent args)
    {
        EntityUid gun = GetEntity(args.gun);
        EntityUid shooter = GetEntity(args.shooter);
        EntityCoordinates targetCoordinates = GetCoordinates(args.aimedPosition);
        EntityCoordinates firingCoordinates = GetCoordinates(args.shotFrom);
        if (!TryComp<OxydGunComponent>(gun, out var gunComp))
            return;
        fireGun(shooter, (gun, gunComp), firingCoordinates, targetCoordinates.Position);
    }
}
