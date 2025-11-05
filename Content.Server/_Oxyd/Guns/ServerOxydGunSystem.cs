using System.Collections.Frozen;
using System.Linq;
using System.Numerics;
using Content.Server._Crescent.HullrotGunSystem;
using Content.Server.Players.RateLimiting;
using Content.Shared._Oxyd.Framework;
using Content.Shared._Oxyd.OxydGunSystem;
using Robust.Server.GameStates;
using Robust.Server.Player;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

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
    [Dependency] private readonly ServerOxydProjectileSystem _oxydProjectileSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;


    // Acceptable timing inconsistencies during auto firing.
    public static TimeSpan TimingIncosistencyBuffer = TimeSpan.FromMilliseconds(30);
    public static int MaxTicksIncosistencyBehind = 10; // Up to 10 ticks of delta-diff between client-server can and will be simulated to catch up
    public static int MaxTicksAhead = 10;
    public List<Queue<object>> delayedMessages = new List<Queue<object>>();

    public override void Initialize()
    {
        base.Initialize();
        _serverNetManager.RegisterNetMessage<ClientSideDoneInterpretingFiremode>(OnClientEndInterpret);
        _netManager.RegisterNetMessage<ClientSideInterpretingFiremode>(OnClientInterpret);
        _netManager.RegisterNetMessage<FiremodeClientsideFiredEvent>(OnClientFireGun);
        SubscribeLocalEvent<FiremodeProjectilesFiredEvent>(ev => Dirty(ev.gun));
        for (var i = 0; i < MaxTicksAhead; i++)
        {
            delayedMessages.Add(new Queue<object>());
        }

    }

    public void handleLate(ClientSideDoneInterpretingFiremode args)
    {

    }

    public void handleLate(FiremodeClientsideFiredEvent args)
    {
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
        _oxydProjectileSystem.SimulateExtraPhysicsTicks(projectiles, (int)tickDiff);
    }

    public void handleLate(ClientSideInterpretingFiremode args)
    {

    }

    public void doMessageTick()
    {
        while(delayedMessages[0].TryDequeue(out var thing))
        {
            switch (thing)
            {
                case ClientSideInterpretingFiremode ev:
                    OnClientInterpret(ev);
                    break;
                case ClientSideDoneInterpretingFiremode ev:
                    OnClientEndInterpret(ev);
                    break;
                case  FiremodeClientsideFiredEvent ev:
                    OnClientFireGun(ev);
                    break;
                default:
                    Log.Error($"Unimplemented doMessageTick in ServerOxydGunSystem for {thing}");
                    break;
            }

        }

        delayedMessages[MaxTicksAhead - 1] = delayedMessages[0];
        delayedMessages[MaxTicksAhead - 1].Clear();
        for (var i = MaxTicksAhead - 3; i > 0; i--)
        {
            delayedMessages[i] = delayedMessages[i + 1];
        }
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
        if(args.clientTick > _gameTiming.CurTick)
        {
            var aheadBy = (int)(args.clientTick.Value - _gameTiming.CurTick.Value);
            if (aheadBy > MaxTicksAhead)
                return;
            delayedMessages[aheadBy-1].Enqueue(args);
            return;
        }
        var tickDiff = _gameTiming.CurTick.Value - args.clientTick.Value;
        if (tickDiff > 0)
        {
            if (tickDiff > MaxTicksIncosistencyBehind)
                return;
            handleLate(args);
            return;
        }

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
        if(args.clientTick > _gameTiming.CurTick)
        {
            Log.Debug($"Got mismatched FUTURE??? tick! client {args.clientTick} , server {_gameTiming.CurTick}. Likely modified client on {handler.shooterEntity}");
            return;
        }
        var tickDiff = _gameTiming.CurTick.Value - args.clientTick.Value;
        if (tickDiff > 0)
        {
            if (tickDiff > MaxTicksIncosistencyBehind)
                return;
            handleLate(args);
            return;
        }
        handler.executedFiringSteps.Clear();
        handler.fullCycle = false;
        handler.shooterEntity = EntityUid.Invalid;
    }


    public void OnClientFireGun(FiremodeClientsideFiredEvent args)
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
        if(args.clientTick > _gameTiming.CurTick)
        {
            Log.Debug($"Got mismatched FUTURE??? tick! client {args.clientTick} , server {_gameTiming.CurTick}. Likely modified client on {handler.shooterEntity}");
            return;
        }
        var tickDiff = _gameTiming.CurTick.Value - args.clientTick.Value;
        if (tickDiff > 0)
        {
            if (tickDiff > MaxTicksIncosistencyBehind)
                return;
            handleLate(args);
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
        doMessageTick();
        var query = EntityQuery<OxydActiveFiremodeUpdatingComponent>();
        foreach (var active in query)
        {
            TryExecuteFiremodeCycle(active.FiremodePrototype, active.gun, active.shooter);
            //Dirty(active.gun.Owner, active.gun.Comp);
        }
    }

}
