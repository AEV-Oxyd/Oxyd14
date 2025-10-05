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

    // Acceptable timing inconsistencies during auto firing.
    public static TimeSpan TimingIncosistencyBuffer = TimeSpan.FromMilliseconds(15);
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
        // Let  very small inconsistencies slide in , don't want state desyncs!
        if (gunComp.nextFire > _gameTiming.CurTime && (gunComp.nextFire - _gameTiming.CurTime) < TimingIncosistencyBuffer)
            gunComp.nextFire = _gameTiming.CurTime;

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
