using Content.Client._Oxyd.Framework;
using Content.Shared._Oxyd.OxydGunSystem;
using Content.Shared.Interaction;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;


namespace Content.Client._Oxyd.OxydGunSystem;


/// <summary>
/// This handles...
/// </summary>
public sealed partial class ClientOxydGunSystem : SharedOxydGunSystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydHandheldGunComponent, UsingMouseDownEvent>(HandleHandheldGun);
        _netManager.RegisterNetMessage<ClientSideDoneInterpretingFiremode>();
        _netManager.RegisterNetMessage<ClientSideInterpretingFiremode>();
        _netManager.RegisterNetMessage<FiremodeClientsideFiredEvent>();
    }
    public void HandleHandheldGun(Entity<OxydHandheldGunComponent> obj, ref UsingMouseDownEvent args)
    {
        if (!_gameTiming.IsFirstTimePredicted)
            return;
        if (!TryComp<OxydGunComponent>(obj, out var gun))
        {
            Log.Error($"Tried to fire handheld gun without gun component {MetaData(obj).EntityName}");
            return;
        }
        DoInterpret((obj.Owner, gun), args.user);
    }

    public void DoInterpret(Entity<OxydGunComponent> gun, EntityUid shooter)
    {
        if (gun.Comp.selectedFiremodePrototype.nextFire > _gameTiming.CurTime)
            return;

        if (!gun.Comp.selectedFiremodePrototype.Active)
        {
            _netManager.ClientSendMessage(new ClientSideInterpretingFiremode()
            {
                gun = GetNetEntity(gun),
                shooter = GetNetEntity(shooter),
                clientsideStartingStep = gun.Comp.selectedFiremodePrototype.currentStep,
            });
        }

        var value = TryExecuteFiremodeCycle(gun.Comp.selectedFiremodePrototype, gun, shooter);
        if (value)
        {
            _netManager.ClientSendMessage(new ClientSideDoneInterpretingFiremode()
            {
                gun = GetNetEntity(gun),
                stoppedAt = gun.Comp.selectedFiremodePrototype.currentStep,
            });
        }
    }

    public override List<Entity<OxydProjectileComponent>>? TryFireGunAt(Entity<OxydGunComponent> gun, EntityUid shooter,
        MapCoordinates targetCoordinates, MapCoordinates firingCoordinates)
    {
        if (!_gameTiming.IsFirstTimePredicted)
            return null;
        return base.TryFireGunAt(gun, shooter, targetCoordinates, firingCoordinates);

    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_gameTiming.IsFirstTimePredicted)
            return;
        var query = EntityQuery<OxydActiveFiremodeUpdatingComponent>();
        foreach (var active in query)
        {
            if(active.shooter is not null)
                DoInterpret(active.gun, active.shooter.Value);
        }
    }
}
