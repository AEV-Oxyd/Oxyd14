using System.Linq;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared._Oxyd.Skills;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Stunnable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>Source faction-item behavior, implemented with native interaction and DoAfter events.</summary>
public sealed partial class NeoTheologyArtifactSystem : EntitySystem
{
    [Dependency] private CruciformSystem _cruciform = default!;
    [Dependency] private EyeOfTheProtectorSystem _eye = default!;
    [Dependency] private NeoTheologyWorldSystem _world = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedSkillSystem _skills = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damage = default!;

    [SubscribeLocalEvent]
    private void OnSealUsed(Entity<NeoTheologySealComponent> ent, ref OddityUsedEvent args) =>
        EnsureComp<HolyLightComponent>(args.user);

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<HolyLightComponent>();
        while (query.MoveNext(out var body, out var light))
        {
            if (_timing.CurTime < light.NextPulse || !_cruciform.IsActiveBearer(body))
                continue;
            light.NextPulse = _timing.CurTime + light.Interval;
            foreach (var (target, _) in _lookup.GetEntitiesInRange<CruciformBearerComponent>(Transform(body).Coordinates, light.Radius))
                if (_cruciform.TryGetCruciform(target, out _, out var targetCruciform) &&
                    _examine.InRangeUnOccluded(body, target, light.Radius, predicate: null))
                    _damage.TryChangeDamage(target, targetCruciform.HolyLightHealing * light.Healing);
        }
    }

    [SubscribeLocalEvent]
    private void OnSwordDamage(Entity<SwordOfTruthComponent> ent, ref GetMeleeDamageEvent args)
    {
        if (TryComp<NeoTheologyFactionItemComponent>(ent, out var item) && item.CrusadeActivated)
            args.Damage += ent.Comp.CrusadeDamageBonus;
    }

    [SubscribeLocalEvent]
    private void OnSwordHit(Entity<SwordOfTruthComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit || !_hands.IsHolding(args.User, ent.Owner))
            return;
        foreach (var target in args.HitEntities)
            TryDestroyArtifact(ent.Owner, args.User, target);
    }

    public bool TryDestroyArtifact(EntityUid sword, EntityUid user, EntityUid target)
    {
        if (!HasComp<SwordOfTruthComponent>(sword) || !_hands.IsHolding(user, sword) ||
            target == sword || TerminatingOrDeleted(target) || EntityManager.IsQueuedForDeletion(target) ||
            !HasComp<NeoTheologyFactionItemComponent>(target) || !_examine.InRangeUnOccluded(user, target, 1.5f, predicate: null))
            return false;
        var prototype = MetaData(target).EntityPrototype?.ID;
        QueueDel(target);
        if (prototype != null)
            _world.RecordDestruction(user, prototype);
        if (_eye.FindEye(user) is { } eye)
        {
            var state = Comp<EyeOfTheProtectorComponent>(eye);
            _eye.AddObservation(eye, 200);
            state.PowerGainBase *= 2;
            state.MaxObservation *= 1.25f;
            state.ArmamentsRate *= 2;
            state.MaxArmamentsPoints *= 2;
            Dirty(eye, state);
        }
        return true;
    }

    [SubscribeLocalEvent]
    private void OnSwordUse(Entity<SwordOfTruthComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || _timing.CurTime < ent.Comp.NextFlash ||
            !TryComp<WieldableComponent>(ent.Owner, out var wield) || !wield.Wielded)
            return;
        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User,
            TimeSpan.FromSeconds(2.5), new SwordOfTruthFlashEvent(), ent.Owner, used: ent.Owner)
        {
            BreakOnMove = true, BreakOnDamage = true, BreakOnDropItem = true, NeedHand = true,
        });
    }

    [SubscribeLocalEvent]
    private void OnSwordFlash(Entity<SwordOfTruthComponent> ent, ref SwordOfTruthFlashEvent args)
    {
        if (!args.Cancelled && !args.Handled && _hands.IsHolding(args.User, ent.Owner) &&
            TryComp<WieldableComponent>(ent.Owner, out var wield) && wield.Wielded &&
            _timing.CurTime >= ent.Comp.NextFlash)
        {
            ent.Comp.NextFlash = _timing.CurTime + ent.Comp.Cooldown;
            foreach (var (target, _) in _lookup.GetEntitiesInRange<MobStateComponent>(Transform(args.User).Coordinates, ent.Comp.FlashRange))
            {
                if (_mobState.IsDead(target) || _cruciform.IsActiveBearer(target) ||
                    !_examine.InRangeUnOccluded(args.User, target, ent.Comp.FlashRange, predicate: null))
                    continue;
                _stun.TryKnockdown(target, ent.Comp.FlashStunDuration, force: true);
                if (TryComp<MobSkillComponent>(target, out var skills))
                    foreach (var skill in skills.skills.Keys.ToArray())
                        _skills.SetUniqueBuff((target, skills), ent.Comp.FlashBuffId, ent.Comp.FlashSkillPenalty, skill, ent.Comp.FlashDebuffDuration);
            }
        }
        args.Handled = true;
    }

    [SubscribeLocalEvent]
    private void OnShelterUse(Entity<LastShelterComponent> ent, ref UseInHandEvent args)
    {
        if (!args.Handled)
            args.Handled = TryRecover(ent.Owner, args.User, out _);
    }

    /// <summary>Recover an existing loose lost soul; do not steal an implant from a living body or machine.</summary>
    public bool TryRecover(EntityUid shelter, EntityUid user, out EntityUid? recovered)
    {
        recovered = null;
        if (!TryComp<LastShelterComponent>(shelter, out var state) || _timing.CurTime < state.NextRecovery ||
            !_hands.IsHolding(user, shelter))
            return false;
        state.NextRecovery = _timing.CurTime + state.Cooldown;
        var query = EntityQueryEnumerator<CruciformComponent, CruciformSoulComponent>();
        while (query.MoveNext(out var implant, out var comp, out var soul))
        {
            if (!comp.EverActivated || comp.ImplantedEntity != null || !soul.HasSnapshot || soul.MindId == null ||
                TerminatingOrDeleted(implant) || EntityManager.IsQueuedForDeletion(implant) ||
                _containers.TryGetContainingContainer((implant, null, null), out _))
                continue;
            _transform.SetCoordinates(implant, Transform(shelter).Coordinates);
            _hands.TryPickupAnyHand(user, implant);
            recovered = implant;
            return true;
        }
        return false;
    }
}
