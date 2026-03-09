using Content.Shared._Oxyd.Framework;
using Content.Shared._Oxyd.OxydGunSystem;
using Content.Shared.Doors.Electronics;

namespace Content.Server._Oxyd.Guns;

public partial class ServerOxydGunSystem
{
    public void OnClientMouseInform(FiremodeMouseStatus ev)
    {
        Log.Debug($"Received mouse network at {_gameTiming.RealTime}");
        var player = _playerManager.GetSessionByChannel(ev.MsgChannel);
        if (player.AttachedEntity is null)
            return;
        EntityUid gun = GetEntity(ev.gun);
        if (TerminatingOrDeleted(gun))
            return;
        if (!TryComp<OxydGunComponent>(gun, out var gcomp))
            return;
        if (ev.clientTick >= _gameTiming.CurTick && ev.clientTick.Value - _gameTiming.CurTick.Value < MaxTicksAhead)
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
        Log.Debug($"Handling mouse status change at {_gameTiming.RealTime}, td {tickDiff}!");
        EntityUid gun = GetEntity(args.gun);
        if (TerminatingOrDeleted(gun))
            return;
        if (!TryComp<OxydGunComponent>(gun, out var gunComp))
            return;
        if (!TryComp<FiremodeStateHandlerComponent>(gun, out var handler))
            return;
        if (TerminatingOrDeleted(handler.shooterEntity))
            return;
        Log.Debug($"Mouse status succesfull , from step {args.fromStep}");
        var immediateInterpret = false;
        for(var i = 0; i < gunComp.selectedFiremodePrototype.Effects.Count; i++)
        {
            var effect = gunComp.selectedFiremodePrototype.Effects[i];
            if (effect is OxydMouseStatusGunEffect cast)
            {
                Log.Debug($"Trying to apply to {i}");
                if (_gameTiming.CurTime - cast.receivedUpdate < cast.validDiff && args.fromStep != i)
                    continue;
                Log.Debug($"Succesfully applied to  step {i}");
                cast.mouseHeld = args.held;
                cast.receivedUpdate = _gameTiming.CurTime;
                cast.updateFromStep = args.fromStep;
                if (effect is OxydImmediateInterpret second && second.shouldInterpretImmediately())
                    immediateInterpret = true;
            }
        }

        if (immediateInterpret)
        {
            Log.Debug($"Immediate interpret ran!");
            TryExecuteFiremodeCycle(gunComp.selectedFiremodePrototype, (gun, gunComp), handler.shooterEntity);
        }
        /*
        // handle immediate ticking
        if (tickDiff > 0)
        {
            TryExecuteFiremodeCycle(gunComp.selectedFiremodePrototype, (gun, gunComp), handler.shooterEntity);
        }
        */
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
        Log.Debug($"Receiving client interpret at {_gameTiming.RealTime}");
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
        if (args.clientTick >= _gameTiming.CurTick && args.clientTick.Value - _gameTiming.CurTick.Value < MaxTicksAhead)
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
        Log.Debug($"Interpreting Client-Firemode at {_gameTiming.RealTime}, td {tickDiff}");
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
        gunComp.jammed = false;
        if (gunComp.selectedFiremodePrototype.nextFire == TimeSpan.Zero)
        {
            gunComp.selectedFiremodePrototype.nextFire = _gameTiming.CurTime - _gameTiming.TickPeriod;
        }
        // state desync - force update to client or something - SPCR 2025
        if (gunComp.selectedFiremodePrototype.currentStep != args.clientsideStartingStep)
        {
            Log.Error($"State desync - interpret {gunComp.selectedFiremodePrototype.currentStep} != {args.clientsideStartingStep}");
            PunishChud((gun, gunComp));
            return;
        }
        gunComp.simulateAsTick = _gameTiming.CurTick - tickDiff;
        var c = EnsureComp<FiremodeStateHandlerComponent>(gun);
        c.shooterEntity = shooter;
        c.shooterNetworkId = args.MsgChannel.UserId;
        c.executedFiringSteps.Clear();
        c.catchupNeeded = (int)tickDiff;
        TryExecuteFiremodeCycle(gunComp.selectedFiremodePrototype, (gun, gunComp), shooter);
    }

    public void OnClientEndInterpret(ClientSideDoneInterpretingFiremode args)
    {
        Log.Debug($"Receiving end interpret at {_gameTiming.RealTime}");
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
        Log.Debug($"Ending Client-Firemode at {_gameTiming.RealTime}, td {tickDiff}");
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
        }
        ResetFiremode(gunComp.selectedFiremodePrototype, (gun, gunComp), handler.shooterEntity);
        handler.executedFiringSteps.Clear();
        handler.shooterEntity = EntityUid.Invalid;
        handler.catchupNeeded = 0;
        handler.ticksFoward = 0;
        gunComp.selectedFiremodePrototype.firingGaps = TimeSpan.Zero;
        gunComp.selectedFiremodePrototype.nextFire = TimeSpan.Zero;

        RaiseNetworkEvent(new GunCompareFired(){firedCount = (int)gunComp.timesFired, target = args.gun});
    }


    public void OnClientFireGun(FiremodeClientsideFiredEvent args)
    {
        Log.Debug($"Receiving fire gun at {_gameTiming.RealTime}");
        var player = _playerManager.GetSessionByChannel(args.MsgChannel);
        if (player.AttachedEntity is null)
            return;
        EntityUid gun = GetEntity(args.gun);
        if (TerminatingOrDeleted(gun))
            return;
        if (!TryComp<OxydGunComponent>(gun, out var gcomp))
            return;
        if (!ValidateUserPosition((gun, gcomp), player.AttachedEntity.Value))
        {
            Log.Error($"Failed to validate user pos");
            PunishChud((gun, gcomp));
            return;
        }

        if(!ValidateFiringPosition((gun, gcomp), player.AttachedEntity.Value, _transformSystem.ToMapCoordinates(args.shotFrom)))
        {
            Log.Error($"Did not fire due to failed position validation");
            PunishChud((gun, gcomp));
            return;
        }
        if(args.clientTick >= _gameTiming.CurTick && args.clientTick.Value - _gameTiming.CurTick.Value < MaxTicksAhead)
        {
            queueMessage(args, (int)(args.clientTick.Value - _gameTiming.CurTick.Value));
            return;
        }
        var tickDiff = _gameTiming.CurTick.Value - args.clientTick.Value;
        if (tickDiff > MaxTicksIncosistencyBehind)
        {
            Log.Error($"Message discarded due to many ticks behind");
            PunishChud((gun, gcomp));
            return;
        }
        DoNetMessage(args, tickDiff);
    }

    public void DoNetMessage(FiremodeClientsideFiredEvent args, uint tickDiff)
    {
        Log.Debug($"Interpreting fire gun at {_gameTiming.RealTime}, td {tickDiff}");
        EntityUid gun = GetEntity(args.gun);
        if (TerminatingOrDeleted(gun))

            return;
        if (!TryComp<OxydGunComponent>(gun, out var gunComp))
            return;
        if (!TryComp<FiremodeStateHandlerComponent>(gun, out var handler))
        {
            Log.Error("Missing state handler");
            PunishChud((gun, gunComp));
            return;
        }
        if (TerminatingOrDeleted(handler.shooterEntity))
            return;
        if (!handler.executedFiringSteps.ContainsKey(args.firemodeStep))
        {
            Log.Error($"-111- a incercat sa duplice fire-events. Cheater? step {args.firemodeStep} la  {_gameTiming.RealTime}");

            PunishChud((gun, gunComp));
            return;
        }

        if (!handler.executedFiringSteps[args.firemodeStep].TryDequeue(out var damageMult))
        {
            PunishChud((gun, gunComp));
            Log.Error($"-222- a incercat sa duplice fire-events. Cheater? step {args.firemodeStep} la  {_gameTiming.RealTime}");
            return;
        }
        gunComp.simulateAsTick = _gameTiming.CurTick - tickDiff;
        var projectiles = TryFireGunAt((gun, gunComp), handler.shooterEntity, _transformSystem.ToMapCoordinates(args.aimedPosition), _transformSystem.ToMapCoordinates(args.shotFrom));

        if (projectiles is null)
        {
            Log.Debug($"fara proiectil {_gameTiming.RealTime}");
            return;
        }

        if (!_playerManager.TryGetSessionByEntity(handler.shooterEntity, out var session))
            return;
        _charge.applyMultiplier(projectiles, damageMult);
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
            _oxydProjectileSystem.SimulateExtraPhysicsTicks(projectiles, (int)tickDiff);
        }
        Log.Debug("Fired Gun");
    }

}
