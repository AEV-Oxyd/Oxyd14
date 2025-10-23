using Content.Server.Players.RateLimiting;
using Content.Shared._Oxyd.Framework;
using Content.Shared._Oxyd.OxydGunSystem;
using Robust.Server.GameStates;
using Robust.Server.Player;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.Server._Oxyd.Guns;


/// <summary>
/// This handles...
/// </summary>
///
public sealed partial class ServerOxydGunSystem : SharedOxydGunSystem
{
    [Dependency] private readonly PlayerRateLimitManager _playerRateLimitManager = default!;
    [Dependency] private readonly PvsOverrideSystem _pvsOverride = default!;
    [Dependency] private readonly IServerNetManager _serverNetManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;


    // Acceptable timing inconsistencies during auto firing.
    public static TimeSpan TimingIncosistencyBuffer = TimeSpan.FromMilliseconds(15);

    public override void Initialize()
    {
        base.Initialize();
        _serverNetManager.RegisterNetMessage<ClientSideDoneInterpretingFiremode>(OnClientEndInterpret);
        _netManager.RegisterNetMessage<ClientSideInterpretingFiremode>(OnClientInterpret);
        _netManager.RegisterNetMessage<FiremodeClientsideFiredEvent>(OnClientFireGun);
    }



    public void OnClientInterpret(ClientSideInterpretingFiremode args)
    {
        EntityUid gun = GetEntity(args.gun);
        EntityUid shooter = GetEntity(args.shooter);
        if (!TryComp<OxydGunComponent>(gun, out var gunComp))
            return;
        // entity desync
        if (TerminatingOrDeleted(gun) || TerminatingOrDeleted(shooter))
            return;
        // state desync - force update to client or something - SPCR 2025
        if (gunComp.selectedFiremodePrototype.currentStep != args.clientsideStartingStep)
        {
            return;
        }
        Log.Warning("Started interp");
        var c = EnsureComp<FiremodeStateHandlerComponent>(gun);
        c.shooterEntity = shooter;
        //c.shooterNetworkId = inp.SenderSession.UserId;
        if(TryExecuteFiremodeCycle(gunComp.selectedFiremodePrototype, (gun, gunComp), shooter))
            c.fullCycle = true;
    }

    public void OnClientEndInterpret(ClientSideDoneInterpretingFiremode args)
    {
        EntityUid gun = GetEntity(args.gun);
        if (TerminatingOrDeleted(gun))
            return;
        if (!TryComp<FiremodeStateHandlerComponent>(gun, out var handler))
        {
            Log.Error($"Sesiunea ---- are packet loss / increase sa termine firemodePrototype-uri neexistente pe arma {gun}");
            return;
        }

        if (!TryComp<OxydGunComponent>(gun, out var gunComp))
            return;
        // state desync
        if (gunComp.selectedFiremodePrototype.currentStep != args.stoppedAt)
        {
            Log.Error($"Sesiunea ------ are un state desync pe arma {gun}");
            return;
        }
        Log.Warning("Ended interp");
        handler.executedFiringSteps.Clear();
        handler.fullCycle = false;
        handler.shooterEntity = EntityUid.Invalid;
    }


    public void OnClientFireGun(FiremodeClientsideFiredEvent args)
    {
        Log.Warning("Primit fire gun -----");
        EntityUid gun = GetEntity(args.gun);
        Log.Warning("Primit fire gun");
        if (TerminatingOrDeleted(gun))
            return;
        if (!TryComp<OxydGunComponent>(gun, out var gunComp))
            return;
        if (!TryComp<FiremodeStateHandlerComponent>(gun, out var handler))
            return;
        if (TerminatingOrDeleted(handler.shooterEntity))
            return;
        Log.Warning("Executat fire gun");
        /*
        if (handler.shooterNetworkId != inp.SenderSession.UserId)
        {
            Log.Error($"Inconsistenta in state handler. Network id mismatch pe {gun} , sesiunea arma {handler.shooterNetworkId} , sesiunea client {inp.SenderSession.UserId}");
        }
        */

        if (handler.executedFiringSteps.Contains(args.firemodeStep))
        {
            Log.Error($"----- a incercat sa duplice fire-events. Cheater?");
            return;
        }

        if (args.firemodeStep > gunComp.selectedFiremodePrototype.currentStep && !handler.fullCycle)
        {
            Log.Error($"----- are un state desync pe arma {gun}, pasul primit {args.firemodeStep} , pasul armei {gunComp.selectedFiremodePrototype.currentStep}");
            return;
        }
        handler.executedFiringSteps.Add(args.firemodeStep);
        // Let  very small inconsistencies slide in , don't want state desyncs!
        if (gunComp.selectedFiremodePrototype.nextFire > _gameTiming.CurTime && (gunComp.selectedFiremodePrototype.nextFire - _gameTiming.CurTime) < TimingIncosistencyBuffer)
            gunComp.selectedFiremodePrototype.nextFire = _gameTiming.CurTime;

        var projectiles = TryFireGunAt((gun, gunComp), handler.shooterEntity, _transformSystem.ToMapCoordinates(args.aimedPosition), _transformSystem.ToMapCoordinates(args.shotFrom));
        if (projectiles is null)
            return;
        if (!_playerManager.TryGetSessionByEntity(handler.shooterEntity, out var session))
            return;
        foreach (var bullet in projectiles)
        {
            var pvsBlk = EnsureComp<ClientsidePleaseIgnoreComponent>(bullet.Owner);
            pvsBlk.forSessions.Add(session.Name);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQuery<OxydActiveFiremodeUpdatingComponent>();
        foreach (var active in query)
        {
            TryExecuteFiremodeCycle(active.FiremodePrototype, active.gun, active.shooter);
        }
    }

}
