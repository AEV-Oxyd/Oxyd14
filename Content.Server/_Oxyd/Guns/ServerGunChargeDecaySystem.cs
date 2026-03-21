using Content.Shared._Oxyd.OxydGunSystem;
using Robust.Shared.Player;

namespace Content.Server._Oxyd.Guns;

/// <summary>
/// This handles...
/// </summary>
public sealed class ServerGunChargeDecaySystem : GunChargeDecaySystem
{
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var q = EntityQueryEnumerator<ActiveOxydGunChargeupComponent>();
        while (q.MoveNext(out var uid, out var _))
        {
            var c = Comp<OxydGunChargeupComponent>(uid);
            var filter = Filter.Pvs(uid);
            if (TryComp<FiremodeStateHandlerComponent>(uid, out var firemodeStateHandler) && firemodeStateHandler.shooterEntity != EntityUid.Invalid)
                filter = Filter.PvsExcept(firemodeStateHandler.shooterEntity, 2F);
            RaiseNetworkEvent(new SetGunChargeEvent(){charge = c.charge, gun = GetNetEntity(uid)}, filter, true);
        }
    }
}
