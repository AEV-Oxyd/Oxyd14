using Robust.Shared.Timing;

namespace Content.Shared._Oxyd.OxydGunSystem;

/// <summary>
/// This handles...
/// </summary>
public sealed class GunChargeDecaySystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _time = default!;
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var q = EntityQueryEnumerator<ActiveOxydGunChargeupComponent>();
        while (q.MoveNext(out var uid, out var _))
        {
            var c = Comp<OxydGunChargeupComponent>(uid);
            if (c.lastCharge + c.chargeDecayBegin > _time.CurTime)
                continue;
            if (c.lastDecay + c.decayDelay > _time.CurTime)
                continue;
            c.lastDecay = _time.CurTime;
            c.charge = Math.Clamp(c.charge - c.amountPerDecay, 0, c.maxCharge);

        }
    }
}
