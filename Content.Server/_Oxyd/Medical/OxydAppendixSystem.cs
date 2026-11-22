using Content.Shared._Oxyd.Medical;
using Content.Shared.Body;
using Content.Shared.Medical;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Random.Helpers;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Ports Eris organs/internal/appendix.dm + events/spontaneous_appendicitis.dm:
/// the appendix can spontaneously inflame (rare, Eris random event → per-organ
/// 0.2%/min roll); while inflamed it ticks escalating symptoms - stings,
/// self-damage past 200s, vomiting/weaken past 400s, and ruptures past 600s.
/// Surgical removal (DetachOrgan) is the cure, same as Eris.
/// </summary>
public sealed partial class OxydAppendixSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private VomitSystem _vomit = default!;
    [Dependency] private PainSystem _pain = default!;

    private const float StingStage = 0f;
    private const float DamageStage = 200f;
    private const float VomitStage = 400f;
    private const float RuptureStage = 600f;

    private TimeSpan _nextTick;

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextTick)
            return;
        _nextTick = _timing.CurTime + TimeSpan.FromSeconds(2);

        var query = EntityQueryEnumerator<OxydAppendixComponent, OrganComponent>();
        while (query.MoveNext(out var uid, out var app, out var organ))
        {
            if (organ.Body is not { } body)
                continue;

            // Eris spontaneous_appendicitis is a disabled one-mob event, not a per-organ
            // roll. A healthy appendix stays quiet until something sets Inflamed.
            if (app.Inflamed <= 0f)
                continue;

            // OXYD: tick-scheduled rolls use a predicted stream keyed on the organ
            // so they don't shift the global RNG sequence mid-frame.
            var roll = SharedRandomExtensions.PredictedRandom(_timing, GetNetEntity(uid));

            app.Inflamed += 2f;

            // Eris prob(5) sting + wince at all stages.
            if (roll.Prob(0.05f))
                _popup.PopupEntity(Loc.GetString("oxyd-appendix-sting"), body, body, PopupType.MediumCaution);

            if (app.Inflamed > DamageStage && roll.Prob(0.03f))
            {
                var surg = EnsureComp<OxydOrganSurgeryComponent>(uid);
                OxydWoundSystem.AddOrganDamage(surg, 10f);
                Dirty(uid, surg);
                _pain.AddPain(body, 1f);
                _popup.PopupEntity(Loc.GetString("oxyd-appendix-wince"), body, body, PopupType.SmallCaution);
            }

            if (app.Inflamed > VomitStage && roll.Prob(0.01f))
            {
                _popup.PopupEntity(Loc.GetString("oxyd-appendix-gag"), body, body, PopupType.LargeCaution);
                _stun.TryKnockdown(body, TimeSpan.FromSeconds(10), force: true);
                _vomit.Vomit(body, force: true); // no-op without a stomach, like Eris
            }

            if (app.Inflamed > RuptureStage && roll.Prob(0.01f))
            {
                _popup.PopupEntity(Loc.GetString("oxyd-appendix-rupture"), body, body, PopupType.LargeCaution);
                _stun.TryKnockdown(body, TimeSpan.FromSeconds(10), force: true);
                _pain.AddPain(body, 25f);
                QueueDel(uid); // Eris removed()+qdel: the organ dies and vanishes.
            }
        }
    }
}
