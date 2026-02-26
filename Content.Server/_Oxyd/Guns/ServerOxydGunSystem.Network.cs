using Content.Shared._Oxyd.Framework;
using Content.Shared._Oxyd.OxydGunSystem;

namespace Content.Server._Oxyd.Guns;

public partial class ServerOxydGunSystem
{
    public void OnClientMouseInform(FiremodeMouseStatus ev)
    {
        var player = _playerManager.GetSessionByChannel(ev.MsgChannel);
        if (player.AttachedEntity is null)
            return;
        EntityUid gun = GetEntity(ev.gun);
        if (TerminatingOrDeleted(gun))
            return;
        if (!TryComp<OxydGunComponent>(gun, out var gcomp))
            return;
        if (ev.clientTick > _gameTiming.CurTick && ev.clientTick.Value - _gameTiming.CurTick.Value < MaxTicksAhead)
        {
            queueStatus(ev, (int)(ev.clientTick.Value - _gameTiming.CurTick.Value));
            return;
        }
        var tickDiff = _gameTiming.CurTick.Value - ev.clientTick.Value;
        if (tickDiff > MaxTicksIncosistencyBehind)
        {
            return;
        }
        DoNetMessage(ev, tickDiff);
    }

    public void DoNetMessage(FiremodeMouseStatus args, uint tickDiff)
    {
        Log.Error($"Handling mouse status change!");
        EntityUid gun = GetEntity(args.gun);
        if (TerminatingOrDeleted(gun))
            return;
        if (!TryComp<OxydGunComponent>(gun, out var gunComp))
            return;
        if (!TryComp<FiremodeStateHandlerComponent>(gun, out var handler))
            return;
        if (TerminatingOrDeleted(handler.shooterEntity))
            return;
        foreach (var effect in gunComp.selectedFiremodePrototype.Effects)
        {
            if (effect is OxydMouseStatusGunEffect cast)
            {
                cast.mouseHeld = args.held;
                cast.receivedUpdate = _gameTiming.CurTime;
            }
        }
    }

    public void OnClientFiremodeChange(FiremodeChangedEvent ev, EntitySessionEventArgs arg)
    {
        var switcher = GetEntity(ev.switcher);
        var gun = GetEntity(ev.gun);
        if (TerminatingOrDeleted(gun))
            return;
        if (!TryComp<OxydGunComponent>(gun, out var gcomp))
            return;
        if (TerminatingOrDeleted(switcher))
            return;
        if (switcher != arg.SenderSession.AttachedEntity)
        {
            Log.Info($"{arg.SenderSession.Name} {arg.SenderSession.AttachedEntity} has tried to set the firemode for someone else. [EXPLOIT][BUG]");
            return;
        }
        if(!ValidateUserPosition((gun, gcomp), switcher))
            return;
        TryDoFiremodeSwitch((gun, gcomp), switcher);
    }

    public void OnClientSafetyChange(GunSafetyChangedEvent ev, EntitySessionEventArgs arg)
    {
        var switcher = GetEntity(ev.switcher);
        var gun = GetEntity(ev.gun);
        if (TerminatingOrDeleted(gun))
            return;
        if (!TryComp<OxydGunComponent>(gun, out var gcomp))
            return;
        if (TerminatingOrDeleted(switcher))
            return;
        if (switcher != arg.SenderSession.AttachedEntity)
        {
            Log.Info($"{arg.SenderSession.Name} {arg.SenderSession.AttachedEntity} has tried to set the safety for someone else. [EXPLOIT][BUG]");
            return;
        }
        if(!ValidateUserPosition((gun, gcomp), switcher))
            return;
        TryDoSafetySwitch((gun, gcomp), switcher);
        if (ev.newState != gcomp.safety)
        {
            Log.Error($"State desync on switching firearm safety of gun {gun} , by player {switcher}");
            DirtyEntity(gun);
        }
    }





    public void OnClientInterpret(ClientSideInterpretingFiremode args)
    {
        var player = _playerManager.GetSessionByChannel(args.MsgChannel);
        if (player.AttachedEntity is null)
            return;
        EntityUid gun = GetEntity(args.gun);
        if (TerminatingOrDeleted(gun))
            return;
        if (!TryComp<OxydGunComponent>(gun, out var gcomp))
            return;
        if (!ValidateUserPosition((gun, gcomp), player.AttachedEntity.Value))
            return;
        if (args.clientTick > _gameTiming.CurTick && args.clientTick.Value - _gameTiming.CurTick.Value < MaxTicksAhead)
        {
            queueMessage(args, (int)(args.clientTick.Value - _gameTiming.CurTick.Value));
            return;
        }
        var tickDiff = _gameTiming.CurTick.Value - args.clientTick.Value;
        if (tickDiff > MaxTicksIncosistencyBehind)
        {
            DirtyEntity(gun);
            return;
        }
        DoNetMessage(args, tickDiff);
    }

    public void DoNetMessage(ClientSideInterpretingFiremode args, uint tickDiff)
    {
        Log.Error($"Interpreting Client-Firemode at {_gameTiming.RealTime}");
        EntityUid gun = GetEntity(args.gun);
        var player = _playerManager.GetSessionByChannel(args.MsgChannel);
        if (player.AttachedEntity is null)
            return;
        EntityUid shooter = player.AttachedEntity.Value;
        if (!TryComp<OxydGunComponent>(gun, out var gunComp))
            return;
        // entity desync
        if (TerminatingOrDeleted(gun) || TerminatingOrDeleted(shooter))
            return;
        // state desync - force update to client or something - SPCR 2025
        if (gunComp.selectedFiremodePrototype.currentStep != args.clientsideStartingStep)
        {
            Log.Debug($"State desync - interpret {gunComp.selectedFiremodePrototype.currentStep} != {args.clientsideStartingStep}");
            DirtyEntity(gun);

            return;
        }
        gunComp.simulateAsTick = _gameTiming.CurTick - tickDiff;
        var c = EnsureComp<FiremodeStateHandlerComponent>(gun);
        c.shooterEntity = shooter;
        c.shooterNetworkId = args.MsgChannel.UserId;
        c.executedFiringSteps.Clear();
        TryExecuteFiremodeCycle(gunComp.selectedFiremodePrototype, (gun, gunComp), shooter);
    }

    public void OnClientEndInterpret(ClientSideDoneInterpretingFiremode args)
    {
        Log.Error($"Ending Client-Firemode at {_gameTiming.RealTime}");
        var player = _playerManager.GetSessionByChannel(args.MsgChannel);
        if (player.AttachedEntity is null)
            return;
        EntityUid gun = GetEntity(args.gun);
        if (TerminatingOrDeleted(gun))
            return;
        if (!TryComp<OxydGunComponent>(gun, out var gcomp))
            return;
        if (!ValidateUserPosition((gun, gcomp), player.AttachedEntity.Value))
            return;
        if(args.clientTick >= _gameTiming.CurTick && args.clientTick.Value - _gameTiming.CurTick.Value < MaxTicksAhead)
        {
            queueMessage(args, (int)(args.clientTick.Value - _gameTiming.CurTick.Value));
            return;
        }
        var tickDiff = _gameTiming.CurTick.Value - args.clientTick.Value;
        if (tickDiff > MaxTicksIncosistencyBehind)
        {
            return;
        }
        DoNetMessage(args, tickDiff);
    }

    public void DoNetMessage(ClientSideDoneInterpretingFiremode args, uint tickDiff)
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
            Log.Error($"Sesiunea ------ are un state desync pe arma {gun}, {gunComp.selectedFiremodePrototype.currentStep} != {args.stoppedAt}");
            return;
        }
        handler.executedFiringSteps.Clear();
        handler.shooterEntity = EntityUid.Invalid;

        RaiseNetworkEvent(new GunCompareFired(){firedCount = (int)gunComp.timesFired, target = args.gun});
    }


    public void OnClientFireGun(FiremodeClientsideFiredEvent args)
    {
        Log.Error($"Interpreting fire gun at {_gameTiming.RealTime}");
        var player = _playerManager.GetSessionByChannel(args.MsgChannel);
        if (player.AttachedEntity is null)
            return;
        EntityUid gun = GetEntity(args.gun);
        if (TerminatingOrDeleted(gun))
            return;
        if (!TryComp<OxydGunComponent>(gun, out var gcomp))
            return;
        if (!ValidateUserPosition((gun, gcomp), player.AttachedEntity.Value))
            return;
        if(!ValidateFiringPosition((gun, gcomp), player.AttachedEntity.Value, _transformSystem.ToMapCoordinates(args.shotFrom)))
            return;
        if(args.clientTick >= _gameTiming.CurTick && args.clientTick.Value - _gameTiming.CurTick.Value < MaxTicksAhead)
        {
            queueMessage(args, (int)(args.clientTick.Value - _gameTiming.CurTick.Value));
            return;
        }
        var tickDiff = _gameTiming.CurTick.Value - args.clientTick.Value;
        if (tickDiff > MaxTicksIncosistencyBehind)
        {
            return;
        }
        DoNetMessage(args, tickDiff);
    }

    public void DoNetMessage(FiremodeClientsideFiredEvent args, uint tickDiff)
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
        if (!handler.executedFiringSteps.Contains(args.firemodeStep))
        {
            Log.Error($"----- a incercat sa duplice fire-events. Cheater? step {args.firemodeStep} la  {_gameTiming.RealTime}");
            return;
        }
        handler.executedFiringSteps.Remove(args.firemodeStep);
        gunComp.simulateAsTick = _gameTiming.CurTick - tickDiff;
        // Let  very small inconsistencies slide in , don't want state desyncs!
        var savedFire = gunComp.selectedFiremodePrototype.nextFire;
        if (gunComp.selectedFiremodePrototype.nextFire > _gameTiming.CurTime && (gunComp.selectedFiremodePrototype.nextFire - _gameTiming.CurTime) < TimingIncosistencyBuffer)
            gunComp.selectedFiremodePrototype.nextFire = _gameTiming.CurTime;
        if (tickDiff > 0)
        {
            gunComp.selectedFiremodePrototype.nextFire = _gameTiming.CurTime;
        }

        var projectiles = TryFireGunAt((gun, gunComp), handler.shooterEntity, _transformSystem.ToMapCoordinates(args.aimedPosition), _transformSystem.ToMapCoordinates(args.shotFrom));

        if (projectiles is null)
        {
            Log.Debug($"fara proiectil {_gameTiming.RealTime}");
            return;
        }

        if (!_playerManager.TryGetSessionByEntity(handler.shooterEntity, out var session))
            return;
        foreach (var bullet in projectiles)
        {
            var pvsBlk = EnsureComp<ClientsidePleaseIgnoreComponent>(bullet.Owner);
            pvsBlk.forSessions.Add(session.Name);
            if (TryComp<OxydHandheldGunComponent>(gun, out var handheld))
            {
                if (!physQ.TryGetComponent(handler.shooterEntity, out var physicsComponent))
                    continue;
                var offset = EnsureComp<ApplyVisualOffsetComponent>(bullet.Owner);
                offset.offset = _predictor.PredictWorldPosition(handler.shooterEntity, predictedTicks) - _transformSystem.GetWorldPosition(handler.shooterEntity);
            }
        }

        if (tickDiff > 0)
        {
            gunComp.selectedFiremodePrototype.nextFire = savedFire;
            _oxydProjectileSystem.SimulateExtraPhysicsTicks(projectiles, (int)tickDiff);
        }
        Log.Debug("Fired Gun");
    }

}
