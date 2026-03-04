using System.Collections.Frozen;
using System.Linq;
using System.Numerics;
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
using Robust.Shared.Prototypes;
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


    // Acceptable timing inconsistencies during auto firing.
    public static TimeSpan TimingIncosistencyBuffer = TimeSpan.FromMilliseconds(10);
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
        _netManager.RegisterNetMessage<ClientSideDoneInterpretingFiremode>(OnClientEndInterpret);
        _netManager.RegisterNetMessage<ClientSideInterpretingFiremode>(OnClientInterpret);
        _netManager.RegisterNetMessage<FiremodeClientsideFiredEvent>(OnClientFireGun);
        SubscribeNetworkEvent<FiremodeMouseStatus>(OnClientMouseInform);
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
        target.Comp.jammed = true;
        _audio.PlayEntity()
        Dirty(target);
    }



    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        doStatusTick();
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
