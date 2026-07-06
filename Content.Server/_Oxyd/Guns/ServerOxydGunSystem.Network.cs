using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using Content.Shared._Oxyd.Framework;
using Content.Shared._Oxyd.OxydGunSystem;
using Content.Shared.Actions.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Doors.Electronics;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameStates;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._Oxyd.Guns;

public partial class ServerOxydGunSystem
{
    public void updateMouseStat(OxydGunComponent gun, bool stat)
    {
        Log.Debug($"Mouse:{stat}");
        gun.mouseDown = stat;
        gun.lastNetMouseUpdate = _gameTiming.CurTime;
        gun.stateCounter--;
    }

    public List<EntityUid> extractEntitities(object? variable, List<EntityUid>? lst)
    {
        lst ??= new();
        //Log.Debug($"Extracting {variable}");
        switch( variable)
        {
            case null:
                break;
            case EntityUid c:
                if(c != EntityUid.Invalid)
                    lst.Add(c);
                break;
            case IEnumerable array:
                foreach(var thing in array)
                    extractEntitities(thing, lst);
                break;
            case ItemSlot slot:
                if (slot.Item is null || slot.Item.Value == EntityUid.Invalid)
                    break;
                //Log.Debug($"got item Slot item {slot.Item.Value}");
                lst.Add(slot.Item.Value);
                break;
        }

        return lst;
    }
    public Dictionary<EntityUid, List<IComponent>> getInvolvedComponents(Entity<OxydGunComponent> gun)
    {
        var dict = new Dictionary<EntityUid, List<IComponent>>();
        var firemode = gun.Comp.selectedFiremodePrototype;
        if (!_factory.TryGetRegistration(firemode.providerComp, out var registration))
            return dict;
        if (EntityManager.TryGetComponent(gun.Owner, registration, out var comp))
        {
            var targetType = comp.GetType();
            dict.Add(gun.Owner, new List<IComponent>() {comp});
            foreach (var field in targetType.GetAllFields())
            {
                var indexed = false;
                foreach (var data in field.CustomAttributes)
                {
                    if (data.AttributeType != typeof(CheckForGunUpdateAttribute))
                        continue;
                    if(data.ConstructorArguments.Count == 1 && data.ConstructorArguments[0].Value is bool truth)
                        indexed = truth;
                    goto fieldCheck;
                }
                continue;
            fieldCheck:
                var fieldValue = field.GetValue(comp);
                //Log.Debug($"Got field {field.Name} with value {fieldValue}");
                if (fieldValue is null)
                    continue;
                if (indexed && fieldValue is IEnumerable enumerable)
                {
                    fieldValue = enumerable.Cast<object?>().ToList()[firemode.shootingPosIndex];
                    //Log.Debug($"Is Indexed, casting got us {fieldValue} {fieldValue is IEnumerable}");
                }

                foreach (var thing in extractEntitities(fieldValue, null))
                {
                    getInvolvedComponents(thing, dict);
                }
            }
        }
        return dict;
    }

    public void getInvolvedComponents(EntityUid target, Dictionary<EntityUid, List<IComponent>> dict)
    {
        var comps = EntityManager.GetComponents<OxydGunProvidersComponent>(target);
        dict.TryAdd(target, new List<IComponent>());
        //Log.Debug($"Verifying {target} at second level");
        foreach(var comp in comps)
        {
            //Log.Debug($"Got component {comp}");
            dict[target].Add(comp);
            var targetType = comp.GetType();
            foreach (var field in targetType.GetAllFields())
            {
                foreach (var data in field.CustomAttributes)
                {
                    if (data.AttributeType != typeof(CheckForGunUpdateAttribute))
                        continue;
                    goto fieldCheck;
                }
                continue;
                fieldCheck:
                var fieldValue = field.GetValue(comp);
                //Log.Debug($"Second level indexation got {fieldValue} {field.Name}");
                if (fieldValue is null)
                    continue;
                foreach (var thing in extractEntitities(fieldValue, null))
                {
                    getInvolvedComponents(thing, dict);
                }
            }
        }
    }
    public void onTryStateGun(Entity<OxydGunComponent> ent, ref ComponentGetStateAttemptEvent args)
    {
        //Log.Debug($"getstate {args.Player} {ent.Comp}");
        if (!TryComp<FiremodeStateHandlerComponent>(ent, out var state))
            return;
        // always give state to replay
        if (args.Player is null)
            return;
        if (args.Player == state.shooterSession)
        {
            //Log.Debug($"canceled {args.Player} {ent.Comp}");
            return;
        }

    }

    public void onTryStateGeneric(EntityUid target, IComponent comp, ref ComponentGetStateAttemptEvent args)
    {
        //Log.Debug($"getstate {args.Player} {comp}");
        if (!_help.GetParentWithComp<OxydGunComponent>(target, out var ent))
            return;
        if (comp.CreationTick.Value - _gameTiming.CurTick.Value == 0)
            return;
        if (!TryComp<FiremodeStateHandlerComponent>(ent, out var state))
            return;
        // always give state to replay
        if (args.Player is null)
            return;
        if (args.Player == state.shooterSession)
        {
            //Log.Debug($"canceled {args.Player} {comp}");
            args.Cancelled = true;
            return;
        }
    }
    public void OnClientMouseInform(FiremodeMouseStatus ev)
    {
        //Log.Debug($"Received mouse network at {_gameTiming.RealTime}");
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
        Log.Debug($"Handling mouse status change at {_gameTiming.CurTime}, td {tickDiff}!");
        EntityUid gun = GetEntity(args.gun);
        if (TerminatingOrDeleted(gun))
            return;
        if (!TryComp<OxydGunComponent>(gun, out var gunComp))
            return;
        if (!TryComp<FiremodeStateHandlerComponent>(gun, out var handler))
            return;
        if (TerminatingOrDeleted(handler.shooterEntity))
            return;
        updateMouseStat(gunComp, args.held);

        Log.Debug($"Immediate interpret ran!");
        giveTickInterpTime(gunComp.selectedFiremodePrototype);
        TryExecuteFiremodeCycle(gunComp.selectedFiremodePrototype, (gun, gunComp), handler.shooterEntity);
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
        {
            Log.Debug($"Interpret failed to AttachedEntity");
            return;
        }

        EntityUid shooter = player.AttachedEntity.Value;
        if (!TryComp<OxydGunComponent>(gun, out var gunComp))
        {
            Log.Debug($"Interpret failed to GunComp");
            return;
        }
        // entity desync
        if (TerminatingOrDeleted(gun) || TerminatingOrDeleted(shooter))
        {
            Log.Debug($"Interpret failed to Deletion");
            return;
        }

        if (gunComp.selectedFiremodePrototype.Active)
        {
            Log.Error($"-555- Tried to start a interpret during an active firemode interp");
            return;
        }

        gunComp.jammed = false;
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
        c.shooterSession = player;
        c.executedFiringSteps.Clear();
        c.catchupNeeded = (int)tickDiff;
        gunComp.selectedFiremodePrototype.timeBudget = _gameTiming.TickPeriod *(1+ tickDiff) + TimeSpan.FromMilliseconds(25);
        updateMouseStat(gunComp, args.mouseHeld);
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
        updateMouseStat(gunComp, args.mouseHeld);
        ResetFiremode(gunComp.selectedFiremodePrototype, (gun, gunComp), handler.shooterEntity);
        if(handler.executedFiringSteps.Values.Sum(t => t.Count) != 0)
            Log.Error($"Done Intrepret ended with a bullet never being fired!");
        handler.executedFiringSteps.Clear();
        handler.shooterEntity = EntityUid.Invalid;
        handler.catchupNeeded = 0;
        handler.ticksFoward = 0;
        // this wont get to user since  the state is sessionSpecific handled, just everyone else
        Dirty(gun, gunComp);
        var dict = getInvolvedComponents((gun, gunComp));
        //Log.Debug($"Involved returned {dict.Keys.Count} targets with {dict.Values.Sum(x => x.Count)} components");
        foreach (var (target, components) in dict)
        {
            foreach(var comp in components)
                Dirty(target, comp);
        }
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
        giveTickInterpTime(gunComp.selectedFiremodePrototype);
        TryExecuteFiremodeCycle(gunComp.selectedFiremodePrototype, (gun, gunComp), handler.shooterEntity);
        if (!handler.executedFiringSteps.TryGetValue(args.firemodeStep, out var queue) || queue.Count == 0)
        {
            gunComp.selectedFiremodePrototype.timeBudget += TimeSpan.FromMilliseconds(10);
            TryExecuteFiremodeCycle(gunComp.selectedFiremodePrototype, (gun, gunComp), handler.shooterEntity);
            Log.Debug($"-000- fire was given 10 ms");
            if (!handler.executedFiringSteps.TryGetValue(args.firemodeStep, out var queue2) || queue2.Count == 0)
            {
                Log.Error($"-111- no fire step. step {args.firemodeStep} at {_gameTiming.RealTime}");

                PunishChud((gun, gunComp));
                return;
            }
        }

        var s = gunComp.selectedFiremodePrototype.Effects[args.firemodeStep];
        if (s is not OxydFiringGunEffect step)
        {
            Log.Error($"-222- not fire effect.  step {args.firemodeStep} at {_gameTiming.RealTime}");
            PunishChud((gun, gunComp));
            return;
        }

        if (!handler.executedFiringSteps[args.firemodeStep].TryDequeue(out var damageMult))
        {
            PunishChud((gun, gunComp));
            Log.Error($"-333- no fire queued. step {args.firemodeStep} at {_gameTiming.RealTime}");
            return;
        }
        gunComp.simulateAsTick = _gameTiming.CurTick - tickDiff;
        var projectiles = TryFireGunAt((gun, gunComp), handler.shooterEntity, _transformSystem.ToMapCoordinates(args.aimedPosition), _transformSystem.ToMapCoordinates(args.shotFrom), step.shots);

        if (projectiles is null)
        {
            Log.Debug($"-444- no projectile fired {_gameTiming.RealTime}");
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
                offset.offset = _predictor.PredictWorldPosition(handler.shooterEntity, (uint)predictedTicks) - _transformSystem.GetWorldPosition(handler.shooterEntity);
            }
        }

        if (tickDiff > 0)
        {
            _oxydProjectileSystem.SimulateExtraPhysicsTicks(projectiles, (int)tickDiff);
        }
        Log.Debug($"Fired Gun, {handler.executedFiringSteps[args.firemodeStep].Count}");
    }
}
