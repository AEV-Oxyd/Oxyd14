using Content.Client._Oxyd.Framework;
using Content.Shared._Oxyd.OxydGunSystem;
using Content.Shared.Interaction;
using Robust.Shared.Map;
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
        if (!gun.Comp.selectedFiremodePrototype.Active)
        {
            RaiseNetworkEvent(new ClientSideInterpretingFiremode()
            {
                gun = GetNetEntity(gun),
                shooter = GetNetEntity(shooter),
                clientsideStartingStep = gun.Comp.selectedFiremodePrototype.currentStep,
            });
        }

        if (TryExecuteFiremodeCycle(gun.Comp.selectedFiremodePrototype, gun, shooter))
        {
            RaiseNetworkEvent(new ClientSideDoneInterpretingFiremode()
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
