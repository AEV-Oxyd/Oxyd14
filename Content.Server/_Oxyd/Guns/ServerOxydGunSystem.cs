using System.Collections.Frozen;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Server._Crescent.HullrotGunSystem;
using Content.Server.Hands.Systems;
using Content.Server.Players.RateLimiting;
using Content.Shared._Oxyd;
using Content.Shared._Oxyd.Framework;
using Content.Shared._Oxyd.OxydGunSystem;
using Content.Shared._Oxyd.Predictors;
using Content.Shared.Containers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction.Events;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Trigger.Components.Effects;
using Robust.Server.GameStates;
using Robust.Server.Player;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Guns;


/// <summary>
/// This handles...
/// </summary>
///
public sealed partial class ServerOxydGunSystem : SharedOxydGunSystem
{

    [Dependency] private  IPlayerManager _playerManager = default!;
    [Dependency] private  ServerOxydProjectileSystem _oxydProjectileSystem = default!;
    [Dependency] private  IPrototypeManager _prototypeManager = default!;
    [Dependency] private  BasicPhysicsPredictorSystem _predictor = default!;

    public static int MaxTicksIncosistencyBehind = OxydCvars.maxPastTicks.DefaultValue;
    public static int MaxTicksAhead = OxydCvars.maxFutureTicks.DefaultValue;
    public List<Queue<object>> delayedMessages = new List<Queue<object>>();
    public List<Queue<object>> immediateStatus = new List<Queue<object>>();
    public int currentMessagesIndex = 0;
    public int currentImmediateIndex = 0;
    public float acceptableOffset = 2f;

    private EntityQuery<PhysicsComponent> physQ;
    public int predictedTicks = OxydCvars.predictionTicks.DefaultValue;


    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydMagazineComponent, MapInitEvent>(onMagazineInitialized, after: new []{typeof(ContainerFillSystem)});
        SubscribeLocalEvent<RecoilHandlerComponent, ComponentInit>(onAddRecoil);
        SubscribeLocalEvent<OxydGunComponent, ComponentGetStateAttemptEvent>(onTryStateGun);
        SubscribeLocalEvent<OxydChamberComponent, ComponentGetStateAttemptEvent>(onTryStateGeneric);
        SubscribeLocalEvent<OxydMagazineChamberComponent, ComponentGetStateAttemptEvent>(onTryStateGeneric);
        SubscribeLocalEvent<OxydMagazineComponent, ComponentGetStateAttemptEvent>(onTryStateGeneric);
        SubscribeLocalEvent<OxydChamberExtensionComponent, ComponentGetStateAttemptEvent>(onTryStateGeneric);
        SubscribeLocalEvent<OxydRevolvingChamberComponent, ComponentGetStateAttemptEvent>(onTryStateGeneric);
        SubscribeLocalEvent<OxydHandheldGunComponent, DroppedEvent>(onDrop);
        _netManager.RegisterNetMessage<ClientSideDoneInterpretingFiremode>(OnClientEndInterpret);
        _netManager.RegisterNetMessage<ClientSideInterpretingFiremode>(OnClientInterpret);
        _netManager.RegisterNetMessage<FiremodeClientsideFiredEvent>(OnClientFireGun);
        _netManager.RegisterNetMessage<FiremodeMouseStatus>(OnClientMouseInform);
        SubscribeNetworkEvent<FiremodeChangedEvent>(OnClientFiremodeChange);
        SubscribeNetworkEvent<GunSafetyChangedEvent>(OnClientSafetyChange);
        //SubscribeLocalEvent<FiremodeProjectilesFiredEvent>(ev => Dirty(ev.gun));
        for (var i = 0; i < MaxTicksAhead; i++)
        {
            delayedMessages.Add(new Queue<object>());
        }

        for (var i = 0; i < MaxTicksAhead; i++)
        {
            immediateStatus.Add(new Queue<object>());
        }

        physQ = GetEntityQuery<PhysicsComponent>();
    }

    public void onDrop(Entity<OxydHandheldGunComponent> ent, ref DroppedEvent args)
    {
        if (!TryComp<OxydGunComponent>(ent, out var gcomp))
            return;
        var c = EnsureComp<FiremodeStateHandlerComponent>(ent);
        var frd = gcomp.selectedFiremodePrototype;
        if (frd.Active)
        {
            Log.Debug($"Resetting thrown weapon");
            ResetFiremode(frd, (ent.Owner, gcomp), args.User);
            TotalResync((ent, gcomp));
            c.silenceDesyncs = _gameTiming.CurTime + TimeSpan.FromMilliseconds(500);

        }
        else
        {
            Log.Debug($"Thrown but not reset weapon");
        }
    }


    public void doMessageTick()
    {
        while(delayedMessages[currentMessagesIndex].TryDequeue(out var thing))
        {
            switch (thing)
            {
                case ClientSideInterpretingFiremode ev:
                    DoNetMessage(ev, 0);
                    break;
                case ClientSideDoneInterpretingFiremode ev:
                    DoNetMessage(ev, 0);
                    break;
                case  FiremodeClientsideFiredEvent ev:
                    DoNetMessage(ev, 0);
                    break;
                default:
                    Log.Error($"Unimplemented doMessageTick in ServerOxydGunSystem for {thing}");
                    break;
            }

        }
        delayedMessages[currentMessagesIndex].Clear();
        currentMessagesIndex = (currentMessagesIndex + 1) % MaxTicksAhead;
    }

    public void doStatusTick()
    {
        while(immediateStatus[currentImmediateIndex].TryDequeue(out var thing))
        {
            //Log.Debug($"Dequeqed {thing} at {_gameTiming.CurTime}");
            switch (thing)
            {
                case FiremodeMouseStatus ev:
                    DoNetMessage(ev, 0);
                    break;
                default:
                    Log.Error($"Unimplemented STATUS  in ServerOxydGunSystem for {thing}");
                    break;
            }

        }
        immediateStatus[currentImmediateIndex].Clear();
        currentImmediateIndex = (currentImmediateIndex + 1) % MaxTicksAhead;
    }

    public void queueMessage(object thing, int tickDiff)
    {
        delayedMessages[(currentMessagesIndex + tickDiff) % MaxTicksAhead].Enqueue(thing);
    }

    public void queueStatus(object thing, int tickDiff)
    {
        immediateStatus[(currentImmediateIndex + tickDiff) % MaxTicksAhead].Enqueue(thing);
    }


    public void onAddRecoil(Entity<RecoilHandlerComponent> ent, ref ComponentInit args)
    {
        EnsureComp<PlayerRecoilBacktrackerComponent>(ent);
    }
    // validates the user's position to the gun entity
    public bool ValidateUserPosition(Entity<OxydGunComponent> gun, EntityUid user)
    {
        if (HasComp<OxydHandheldGunComponent>(gun) && !_hands.IsHolding(user, gun.Owner))
        {
            Log.Debug($"Entity {user} failed userPosition check! using gun {gun}");
            return false;
        }

        return true;
    }
    // validates the client-side received firing position to the current knonw position of the user
    // add backtracking handling if desyncs too much / false triggers - SPCR 2026
    public bool ValidateFiringPosition(Entity<OxydGunComponent> gun, EntityUid user, MapCoordinates firingPos)
    {
        var predicedWorldPosition = _predictor.PredictWorldPosition(user,(uint)predictedTicks);
        if (HasComp<OxydHandheldGunComponent>(gun) &&
            (predicedWorldPosition- firingPos.Position).Length() >
            acceptableOffset)
        {
            Log.Debug($"Entity {user} failed firingPosition check! using gun {gun}, diff was {(predicedWorldPosition- firingPos.Position).Length()} ");
            return false;
        }
        return true;

    }



    public void onMagazineInitialized(Entity<OxydMagazineComponent> ent, ref MapInitEvent args)
    {
        var cnt = _containerSystem.GetContainer(ent, oxydContents);
        foreach (var bullet in cnt.ContainedEntities)
        {
            ent.Comp.loadedBullets.Push(GetNetEntity(bullet));
        }
        Dirty(ent);
    }

    public void PunishChud(Entity<OxydGunComponent> target)
    {
        Log.Error($"Doing total resync");
        if (!(TryComp<FiremodeStateHandlerComponent>(target, out var state) &&
              state.silenceDesyncs > _gameTiming.CurTime))
        {
            target.Comp.jammed = true;
            _audio.PlayPvs(_audio.ResolveSound(getJammedSound(false)), new EntityCoordinates(target.Owner, 0, 0));
        }

        TotalResync(target);
    }
    // when the gun desync!!
    public void TotalResync(Entity<OxydGunComponent> target)
    {
        if (TryComp<FiremodeStateHandlerComponent>(target, out var state))
        {
            ResetFiremode(target.Comp.selectedFiremodePrototype,target, state.shooterEntity);
            state.shooterSession = null;
        }
        Dirty(target);
        foreach(var comp in EntityManager.GetComponents<OxydGunProvidersComponent>(target.Owner))
            Dirty(target.Owner, comp);
        foreach (var container in _containerSystem.GetAllContainers(target))
        {
            foreach (var ent in container.ContainedEntities)
            {
                foreach(var comp in EntityManager.GetComponents<OxydGunProvidersComponent>(ent))
                    Dirty(ent, comp);
            }
        }
    }

    public override HashSet<Entity<OxydProjectileComponent>>? TryFireGunAt(Entity<OxydGunComponent> gun, EntityUid shooter,
        MapCoordinates targetCoordinates, MapCoordinates firingCoordinates, int shots)
    {
        if (!preFireChecks(gun))
            return null;
        return base.TryFireGunAt(gun, shooter, targetCoordinates, firingCoordinates, shots);

    }



    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        doStatusTick();
        foreach (var ent in checkActive)
        {
            Log.Debug($"Active tick on PRE {ent.gun.Owner}");
            if( HasComp<OxydActiveFiremodeUpdatingComponent>(ent.gun) != ent.gun.Comp.keepUpdating)
                Log.Debug($"Active set for {ent.gun.Owner}, {ent.gun.Comp.keepUpdating}");
            if (ent.gun.Comp.keepUpdating)
            {
                if (HasComp<OxydActiveFiremodeUpdatingComponent>(ent.gun))
                    continue;
                var c = EnsureComp<OxydActiveFiremodeUpdatingComponent>(ent.gun);
                c.gun = ent.gun;
                c.FiremodePrototype = ent.firemode;
                c.shooter = ent.shooter;
            }
            else
            {
                if (!HasComp<OxydActiveFiremodeUpdatingComponent>(ent.gun))
                    continue;
                RemComp<OxydActiveFiremodeUpdatingComponent>(ent.gun);
            }
        }
        checkActive.Clear();
        var query = EntityQuery<OxydActiveFiremodeUpdatingComponent>();
        foreach (var active in query)
        {
            //Log.Error($"Handling active firemode cycle at {_gameTiming.RealTime}!");
            giveTickInterpTime(active.FiremodePrototype);
            TryExecuteFiremodeCycle(active.FiremodePrototype, active.gun, active.shooter);
            //Dirty(active.gun.Owner, active.gun.Comp);
        }
        doMessageTick();
        foreach (var ent in checkActive)
        {
            Log.Debug($"Active tick on POST {ent.gun.Owner}");
            if( HasComp<OxydActiveFiremodeUpdatingComponent>(ent.gun) != ent.gun.Comp.keepUpdating)
                Log.Debug($"Active set for {ent.gun.Owner}, {ent.gun.Comp.keepUpdating}");
            if (ent.gun.Comp.keepUpdating)
            {
                if (HasComp<OxydActiveFiremodeUpdatingComponent>(ent.gun))
                    continue;
                var c = EnsureComp<OxydActiveFiremodeUpdatingComponent>(ent.gun);
                c.gun = ent.gun;
                c.FiremodePrototype = ent.firemode;
                c.shooter = ent.shooter;
            }
            else
            {
                if (!HasComp<OxydActiveFiremodeUpdatingComponent>(ent.gun))
                    continue;
                RemComp<OxydActiveFiremodeUpdatingComponent>(ent.gun);
            }
        }
        checkActive.Clear();
    }

}
