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
        BeginInterpret((obj.Owner, gun), args.user);
    }

    public void BeginInterpret(Entity<OxydGunComponent> gun, EntityUid shooter)
    {
        RaiseNetworkEvent(new ClientSideInterpretingFiremode()
        {
            gun = GetNetEntity(gun),
            shooter = GetNetEntity(shooter),
            clientsideStartingStep = gun.Comp.selectedFiremodePrototype.currentStep,
        });
        if (TryExecuteFiremodeCycle(gun.Comp.selectedFiremodePrototype, gun, shooter) && !HasComp<OxydActiveFiremodeUpdatingComponent>(gun))
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
        var projectiles = base.TryFireGunAt(gun, shooter, targetCoordinates, firingCoordinates);
        if (projectiles is null)
            return null;
        RaiseNetworkEvent(new ClientSideGunFiredEvent()
        {
            aimedPosition = targetCoordinates,
            shotFrom = firingCoordinates,
            gun = GetNetEntity(gun),
        });
        return projectiles;

    }
}
