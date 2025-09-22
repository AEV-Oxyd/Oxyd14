using Content.Server.Players.RateLimiting;
using Content.Shared._Oxyd.Framework;
using Content.Shared._Oxyd.OxydGunSystem;
using Robust.Server.GameStates;
using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Server._Oxyd.Guns;


/// <summary>
/// This handles...
/// </summary>
public sealed class ServerOxydGunSystem : SharedOxydGunSystem
{
    [Dependency] private readonly PlayerRateLimitManager _playerRateLimitManager = default!;
    [Dependency] private readonly PvsOverrideSystem _pvsOverride = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<ClientSideGunFiredEvent>(OnClientFireGun);
    }

    public void OnClientFireGun(ClientSideGunFiredEvent args,  EntitySessionEventArgs inp)
    {
        EntityUid gun = GetEntity(args.gun);
        EntityUid shooter = GetEntity(args.shooter);
        if (!TryComp<OxydGunComponent>(gun, out var gunComp))
            return;
        var projectiles = TryFireGunAt((gun, gunComp), shooter, args.aimedPosition, args.shotFrom);
        if (projectiles is null)
            return;
        foreach (var bullet in projectiles)
        {
            var pvsBlk = EnsureComp<ClientsidePleaseIgnoreComponent>(bullet.Owner);
            pvsBlk.forSessions.Add(inp.SenderSession.Name);
        }
    }
}
