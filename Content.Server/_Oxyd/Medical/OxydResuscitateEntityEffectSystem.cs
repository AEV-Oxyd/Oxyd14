using Content.Shared._Oxyd.Medical;
using Content.Shared.Damage.Systems;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.EntityEffects;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Ports Eris resuscitator affect_blood + human/resuscitate(): each tick the
/// heart organ takes cardiac damage (Eris take_damage(64, TOX) scaled to our
/// numeric organ pools); if the body is dead but resuscitable - heart and
/// brain present and not decayed, inside the 15-minute window, total damage
/// below the dead threshold after the asphyxiation cap - the mob is brought
/// back to Critical and the dose is drained (Eris remove_self(60)).
/// </summary>
public sealed partial class OxydResuscitateEntityEffectSystem : EntityEffectSystem<BloodstreamComponent, Resuscitate>
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _threshold = default!;
    [Dependency] private OxydWoundSystem _wounds = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
        {
            var tod = EnsureComp<OxydTimeOfDeathComponent>(args.Target);
            if (args.OldMobState != MobState.Dead)
                tod.DiedAt = _timing.CurTime;
        }
        else
        {
            RemComp<OxydTimeOfDeathComponent>(args.Target);
        }
    }

    protected override void Effect(Entity<BloodstreamComponent> entity, ref EntityEffectEvent<Resuscitate> args)
    {
        var uid = entity.Owner;

        // Cardiac stimulant wrecks the heart every tick it circulates
        // (Eris heart.take_damage(64, TOX)). A missing heart is a failed check later.
        var heart = OrganByCategory(uid, args.Effect.HeartOrgan);
        if (heart is { } h && !h.Surgery.Robotic)
        {
            OxydWoundSystem.AddOrganDamage(h.Surgery, args.Effect.HeartDamage * args.Scale);
            Dirty(h.Uid, h.Surgery);
        }

        if (!_mobState.IsDead(uid))
            return;

        // ---- Eris resuscitate() preconditions ----
        var brain = OrganByCategory(uid, args.Effect.BrainOrgan);
        if (!Viable(heart) || !Viable(brain))
            return;

        // No time-of-death record (spawned dead, or died before the tracker
        // saw a state change) is treated as unknown -> allow.
        if (TryComp<OxydTimeOfDeathComponent>(uid, out var tod) &&
            _timing.CurTime - tod.DiedAt > TimeSpan.FromMinutes(args.Effect.ReviveWindowMinutes))
            return;

        // ---- Revive ----
        // Cap asphyxiation first (Eris setOxyLoss(20)), then refuse the revive
        // only when the *total* damage still reaches the dead threshold -
        // a body too mangled to hold life is left dead instead of being set
        // Critical only to die again on the next damage update.
        if (TryComp<DamageableComponent>(uid, out var dmg))
        {
            var spec = _damage.GetPositiveDamage((uid, dmg));
            if (spec.DamageDict.TryGetValue(args.Effect.OxyLossType, out var oxy) &&
                oxy.Float() > args.Effect.OxyLossCap)
            {
                var heal = new DamageSpecifier();
                heal.DamageDict[args.Effect.OxyLossType] = -(oxy.Float() - args.Effect.OxyLossCap);
                _damage.TryChangeDamage(uid, heal);
                spec = _damage.GetPositiveDamage((uid, dmg));
            }

            if (_threshold.TryGetDeadThreshold(uid, out var deadThresh) &&
                deadThresh is { } th && spec.GetTotal() >= th)
                return;
        }

        _mobState.ChangeMobState(uid, MobState.Critical);
        _popup.PopupEntity(Loc.GetString("oxyd-resuscitate-twitch", ("name", Name(uid))),
            uid, PopupType.Medium);

        // Eris remove_self(60): the dose burns itself out on a successful revive.
        if (_solutions.TryGetSolution(uid, BloodstreamComponent.DefaultBloodSolutionName,
                out var bloodEnt, out var blood))
        {
            _solutions.RemoveReagent(bloodEnt.Value, args.Effect.ResuscitatorReagent,
                args.Effect.ReviveDrain);
        }
    }

    private (EntityUid Uid, OrganComponent Organ, OxydOrganSurgeryComponent Surgery)? OrganByCategory(
        EntityUid body, ProtoId<OrganCategoryPrototype> category)
    {
        foreach (var entry in _wounds.GetOrgans(body))
        {
            if (entry.Organ.Category == category)
                return entry;
        }
        return null;
    }

    /// <summary>Eris is_broken(): present, organic, below the damage cap, not decayed.</summary>
    private static bool Viable(
        (EntityUid Uid, OrganComponent Organ, OxydOrganSurgeryComponent Surgery)? entry)
    {
        if (entry is not { } e)
            return false;
        return !e.Surgery.Robotic && !e.Surgery.Decayed &&
               e.Surgery.OrganDamage < OxydOrganSurgeryComponent.OrganMaxDamage;
    }
}
