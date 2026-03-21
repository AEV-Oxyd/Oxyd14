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
using Content.Shared.EntityList;
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

    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly ServerOxydProjectileSystem _oxydProjectileSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly BasicPhysicsPredictorSystem _predictor = default!;

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
        SubscribeLocalEvent<OxydMagazineComponent, ComponentInit>(onMagazineInitialized);
        SubscribeLocalEvent<RecoilHandlerComponent, ComponentInit>(onAddRecoil);
        SubscribeLocalEvent<OxydGunComponent, ComponentGetStateAttemptEvent>(onTryStateGun);
        SubscribeLocalEvent<OxydGunAmmoChamberComponent, ComponentGetStateAttemptEvent>(onTryStateGeneric);
        SubscribeLocalEvent<OxydGunAmmoMagazineChamberComponent, ComponentGetStateAttemptEvent>(onTryStateGeneric);
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
            Log.Debug($"Dequeqed {thing} at {_gameTiming.CurTime}");
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
        if (HasComp<OxydHandheldGunComponent>(gun) && !_handsSystem.IsHolding(user, gun.Owner))
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
        var predicedWorldPosition = _predictor.PredictWorldPosition(user, predictedTicks);
        if (HasComp<OxydHandheldGunComponent>(gun) &&
            (predicedWorldPosition- firingPos.Position).Length() >
            acceptableOffset)
        {
            Log.Debug($"Entity {user} failed firingPosition check! using gun {gun}, diff was {(predicedWorldPosition- firingPos.Position).Length()} ");
            return false;
        }
        return true;

    }



    public void onMagazineInitialized(Entity<OxydMagazineComponent> ent, ref ComponentInit args)
    {
        if (!TryComp<OxydMagazineInitializerComponent>(ent.Owner, out var initi))
            return;
        var cnt = _containerSystem.GetContainer(ent, oxydContents);
        foreach (var bulletProto in _prototypeManager.Index<EntityListPrototype>(initi.initialBullets).GetEntities())
        {
            var spawned = Spawn(bulletProto.ID, MapCoordinates.Nullspace);
            ent.Comp.loadedBullets.Push(GetNetEntity(spawned));
            _containerSystem.Insert(spawned, cnt);
            if (ent.Comp.loadedBullets.Count > ent.Comp.maxBullets)
                break;
        }
        Dirty(ent);
    }

    public void PunishChud(Entity<OxydGunComponent> target)
    {
        Log.Error($"Doing total resync");
        target.Comp.jammed = true;
        _audio.PlayPvs(_audio.ResolveSound(getJammedSound(false)), new EntityCoordinates(target.Owner, 0, 0));
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
        MapCoordinates targetCoordinates, MapCoordinates firingCoordinates)
    {
        if (!preFireChecks(gun))
            return null;
        var gfp = gun.Comp.selectedFiremodePrototype;
        if (gfp.nextFire > _gameTiming.CurTime || gfp.lastFiredTick == _gameTiming.CurTick)
        {
            if (gfp.firingGaps < gfp.fireDelay)
                return null;
            // compensare lag
            gfp.firingGaps -= gfp.fireDelay;
            if (gfp.lastFiredTick == _gameTiming.CurTick)
            {
                Log.Debug("Same tick fire compensation");
                gfp.nextFire = _gameTiming.CurTime;
            }
            else
            {
                Log.Debug("Firemode nextFire compensation");
                gfp.nextFire = _gameTiming.CurTime;
            }

            Log.Error("Compensated succesfully");
        }
        return base.TryFireGunAt(gun, shooter, targetCoordinates, firingCoordinates);

    }



    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        doStatusTick();
        foreach (var ent in checkActive)
        {
            Log.Debug($"Running change on {ent.gun.Owner}");
            if( HasComp<OxydActiveFiremodeUpdatingComponent>(ent.gun) != ent.gun.Comp.keepUpdating)
                Log.Debug($"Updated firemode active on entity {ent.gun.Owner} , now is {ent.gun.Comp.keepUpdating}");
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
            TryExecuteFiremodeCycle(active.FiremodePrototype, active.gun, active.shooter);
            //Dirty(active.gun.Owner, active.gun.Comp);
        }
        doMessageTick();
        foreach (var ent in checkActive)
        {
            Log.Debug($"Running change on {ent.gun.Owner}");
            if( HasComp<OxydActiveFiremodeUpdatingComponent>(ent.gun) != ent.gun.Comp.keepUpdating)
                Log.Debug($"Updated firemode active on entity {ent.gun.Owner} , now is {ent.gun.Comp.keepUpdating}");
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
