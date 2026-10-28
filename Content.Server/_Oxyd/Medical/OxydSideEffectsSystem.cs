using Content.Shared.Body.Components;
using Content.Shared._Oxyd.Medical;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Ports Eris human/handle_medical_side_effects(): every 15 ticks newly
/// triggered side effects manifest (trigger reagent present above its
/// threshold, no cure circulating). Active effects grow in strength on a sine
/// cycle; inside the strong half they either get cured, expire past
/// strength 50, or pulse custom_pain-tiered messages every 45 ticks.
/// </summary>
public sealed partial class OxydSideEffectsSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>Eris life_tick % 15 manifest scan interval.</summary>
    private static readonly TimeSpan EvalInterval = TimeSpan.FromSeconds(15);

    /// <summary>Eris life_tick % 45 on_life pulse interval.</summary>
    private static readonly TimeSpan PainPulseInterval = TimeSpan.FromSeconds(45);

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;

        var query = EntityQueryEnumerator<OxydSideEffectsComponent, BloodstreamComponent>();
        while (query.MoveNext(out var uid, out var comp, out var _))
        {
            var eval = now >= comp.NextUpdate;
            if (eval)
                comp.NextUpdate = now + EvalInterval;

            // Idle-cheap: nothing to scan for and no active effects -> skip the
            // bloodstream lookup entirely.
            if (!eval && comp.Active.Count == 0)
                continue;

            if (!_solutions.TryGetSolution(uid, BloodstreamComponent.DefaultBloodSolutionName,
                    out var solEnt, out var blood))
                continue;

            if (eval)
                ScanManifests(uid, comp, blood);

            for (var i = comp.Active.Count - 1; i >= 0; i--)
            {
                var inst = comp.Active[i];
                var proto = _prototypes.Index(inst.Effect);
                var elapsed = (float) (now - inst.Start).TotalSeconds;
                // Eris strength_percent = sin((life_tick - start)/2); a life tick
                // is ~2s, so in seconds that's sin(elapsed/4).
                var percent = MathF.Sin(elapsed / 4f);

                // Effect slowly growing stronger: Eris +0.08 per life tick (~2s)
                // = 0.04/s — one full strength cycle is minutes, not seconds.
                inst.Strength += 0.08f * frameTime * 0.5f;

                if (percent < 0.4f)
                    continue;

                if (Cured(blood, proto))
                {
                    comp.Active.RemoveAt(i);
                    if (proto.CureMessage != default)
                        _popup.PopupEntity(Loc.GetString(proto.CureMessage), uid, uid);
                    continue;
                }

                if (inst.Strength > 50f)
                {
                    comp.Active.RemoveAt(i);
                    continue;
                }

                if (now >= inst.NextPulse)
                {
                    inst.NextPulse = now + PainPulseInterval;
                    Pulse(uid, proto, percent * inst.Strength);
                }
            }
        }
    }

    /// <summary>Eris manifest(): every trigger met -> add/refresh the side effect.</summary>
    private void ScanManifests(EntityUid uid, OxydSideEffectsComponent comp, Solution blood)
    {
        foreach (var proto in _prototypes.EnumeratePrototypes<OxydSideEffectPrototype>())
        {
            if (Cured(blood, proto))
                continue;

            var triggered = false;
            foreach (var (reagent, min) in proto.Triggers)
            {
                if (blood.GetTotalPrototypeQuantity(reagent).Float() >= min)
                {
                    triggered = true;
                    break;
                }
            }
            if (!triggered)
                continue;

            var existing = comp.Active.Find(a => a.Effect == proto.ID);
            if (existing != null)
            {
                // Eris re-manifest: strength = max(strength, 10), restart the cycle.
                existing.Strength = Math.Max(existing.Strength, 10f);
                existing.Start = _timing.CurTime;
            }
            else
            {
                comp.Active.Add(new OxydSideEffectInstance
                {
                    Effect = proto.ID,
                    Strength = 0f,
                    Start = _timing.CurTime,
                    NextPulse = _timing.CurTime + PainPulseInterval,
                });
            }
        }
    }

    private static bool Cured(Solution blood, OxydSideEffectPrototype proto)
    {
        foreach (var cure in proto.Cures)
        {
            if (blood.GetTotalPrototypeQuantity(cure) > 0)
                return true;
        }
        return false;
    }

    /// <summary>Eris on_life: tiered custom_pain by effective strength.</summary>
    private void Pulse(EntityUid uid, OxydSideEffectPrototype proto, float strength)
    {
        var msgs = proto.PainMessages;
        var tier = strength <= 10f ? 0 : strength <= 30f ? 1 : 2;
        if (tier < msgs.Length && msgs[tier] != default)
            _popup.PopupEntity(Loc.GetString(msgs[tier]), uid, uid);

        if (tier == 2)
        {
            if (proto.SevereEmote is { } emote)
                _popup.PopupEntity(Loc.GetString(emote), uid, uid, PopupType.MediumCaution);
            _pain.AddPain(uid, proto.SeverePain);
        }
    }
}
