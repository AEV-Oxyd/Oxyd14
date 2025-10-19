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
        SubscribeNetworkEvent<FiremodeClientsideFiredEvent>(OnClientFireGun);
        SubscribeNetworkEvent<ClientSideInterpretingFiremode>(OnClientInterpret);
        SubscribeNetworkEvent<ClientSideDoneInterpretingFiremode>(OnClientEndInterpret);
    }

    public void OnClientInterpret(ClientSideInterpretingFiremode args, EntitySessionEventArgs inp)
    {
        EntityUid gun = GetEntity(args.gun);
        EntityUid shooter = GetEntity(args.shooter);
        if (!TryComp<OxydGunComponent>(gun, out var gunComp))
            return;
        // entity desync
        if (TerminatingOrDeleted(gun) || TerminatingOrDeleted(shooter))
            return;
        // state desync - force update to client or something - SPCR 2025
        if (gunComp.selectedFiremode.currentStep != args.clientsideStartingStep)
        {
            return;
        }
        var c = EnsureComp<FiremodeStateHandlerComponent>(gun);
        c.shooterEntity = shooter;
        c.shooterNetworkId = inp.SenderSession.UserId;
        if(TryExecuteFiremodeCycle(gunComp.selectedFiremode, (gun, gunComp), shooter))
            c.fullCycle = true;
    }

    public void OnClientEndInterpret(ClientSideDoneInterpretingFiremode args, EntitySessionEventArgs inp)
    {
        EntityUid gun = GetEntity(args.gun);
        if (TerminatingOrDeleted(gun))
            return;
        if (!TryComp<FiremodeStateHandlerComponent>(gun, out var handler))
        {
            Log.Error($"Sesiunea {inp.SenderSession.Name} are packet loss / increase sa termine firemode-uri neexistente pe arma {gun}");
            return;
        }

        if (!TryComp<OxydGunComponent>(gun, out var gunComp))
            return;
        // state desync
        if (gunComp.selectedFiremode.currentStep != args.stoppedAt)
        {
            Log.Error($"Sesiunea {inp.SenderSession.Name} are un state desync pe arma {gun}");
            return;
        }
    }

    public void OnClientFireGun(FiremodeClientsideFiredEvent args,  EntitySessionEventArgs inp)
    {
        EntityUid gun = GetEntity(args.gun);
        if (TerminatingOrDeleted(gun))
            return;
        if (!TryComp<OxydGunComponent>(gun, out var gunComp))
            return;
        if (!TryComp<FiremodeStateHandlerComponent>(gun, out var handler))
            return;
        if (TerminatingOrDeleted(handler.shooterEntity))
            return;
        if (handler.shooterNetworkId != inp.SenderSession.UserId)
        {
            Log.Error($"Inconsistenta in state handler. Network id mismatch pe {gun} , sesiunea arma {handler.shooterNetworkId} , sesiunea client {inp.SenderSession.UserId}");
        }

        if (handler.executedFiringSteps.Contains(args.firemodeStep))
        {
            Log.Error($"{inp.SenderSession.Name} a incercat sa duplice fire-events. Cheater?");
            return;
        }

        if (args.firemodeStep > gunComp.selectedFiremode.currentStep && !handler.fullCycle)
        {
            Log.Error($"{inp.SenderSession.Name} are un state desync pe arma {gun}, pasul primit {args.firemodeStep} , pasul armei {gunComp.selectedFiremode.currentStep}");
            return;
        }
        handler.executedFiringSteps.Add(args.firemodeStep);
        // Let  very small inconsistencies slide in , don't want state desyncs!
        if (gunComp.selectedFiremode.nextFire > _gameTiming.CurTime && (gunComp.selectedFiremode.nextFire - _gameTiming.CurTime) < TimingIncosistencyBuffer)
            gunComp.selectedFiremode.nextFire = _gameTiming.CurTime;

        var projectiles = TryFireGunAt((gun, gunComp), handler.shooterEntity, args.aimedPosition, args.shotFrom);
        if (projectiles is null)
            return;
        foreach (var bullet in projectiles)
        {
            var pvsBlk = EnsureComp<ClientsidePleaseIgnoreComponent>(bullet.Owner);
            pvsBlk.forSessions.Add(inp.SenderSession.Name);
        }
    }
}
