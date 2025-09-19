using Content.Server._Oxyd.Framework;
using Content.Server.Players.RateLimiting;
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
        var ent = TryFireGunAt((gun, gunComp), shooter, args.aimedPosition, args.shotFrom);
        if (ent is not null)
        {
            var pvsBlk = EnsureComp<PvsSessionBlockerComponent>(ent.Value.Owner);
            pvsBlk.blacklistedFor.Add(inp.SenderSession.Name);
        }
    }
}
