using Content.Client._Oxyd.Framework;
using Content.Shared._Oxyd.OxydGunSystem;
using Content.Shared.Interaction;
using Robust.Shared.Map;
using Robust.Shared.Timing;


namespace Content.Client._Oxyd.OxydGunSystem;


/// <summary>
/// This handles...
/// </summary>
public sealed class ClientOxydGunSystem : SharedOxydGunSystem
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
        if (!TryComp<OxydGunComponent>(obj, out var gun))
        {
            Log.Error($"Tried to fire handheld gun without gun component {MetaData(obj).EntityName}");
            return;
        }

        var targetPos = _transformSystem.ToMapCoordinates(args.clickCoords);
        TryFireGunAt((obj.Owner, gun), args.user, targetPos, resolveFiringPosition(obj, targetPos, args.user));
    }

    public new void TryFireGunAt(Entity<OxydGunComponent> gun, EntityUid shooter,
        MapCoordinates targetCoordinates, MapCoordinates firingCoordinates)
    {
        if (!_gameTiming.IsFirstTimePredicted)
            return;
        base.TryFireGunAt(gun,shooter, targetCoordinates, firingCoordinates);
        RaiseNetworkEvent(new ClientSideGunFiredEvent()
        {
            aimedPosition = targetCoordinates,
            shotFrom = firingCoordinates,
            gun = GetNetEntity(gun),
            shooter = GetNetEntity(shooter)
        });

    }
}
