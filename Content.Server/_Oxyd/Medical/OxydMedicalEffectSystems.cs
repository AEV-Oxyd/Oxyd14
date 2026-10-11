using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Shared._Oxyd.Medical;
using Content.Shared._Oxyd.Skills;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.EntityEffects;

namespace Content.Server._Oxyd.Medical;

/// <summary>Applies NSA tolerance from detox nanites and similar chems.</summary>
public sealed partial class NsaToleranceEntityEffectSystem : EntityEffectSystem<BloodstreamComponent, NsaTolerance>
{
    [Dependency] private OxydNsaSystem _nsa = default!;

    protected override void Effect(Entity<BloodstreamComponent> entity, ref EntityEffectEvent<NsaTolerance> args)
    {
        _nsa.ApplyTolerance(entity, args.Effect.Value, args.Effect.Duration);
    }
}

/// <summary>Eris stims: timed skill buffs while the stim metabolises.</summary>
public sealed partial class StimEntityEffectSystem : EntityEffectSystem<BloodstreamComponent, Stim>
{
    [Dependency] private SharedSkillSystem _skills = default!;

    protected override void Effect(Entity<BloodstreamComponent> entity, ref EntityEffectEvent<Stim> args)
    {
        _skills.SetUniqueBuff((entity.Owner, EnsureComp<MobSkillComponent>(entity.Owner)),
            args.Effect.StimId, args.Effect.Amount, args.Effect.Skill,
            TimeSpan.FromSeconds(args.Effect.Duration));
    }
}

/// <summary>Ossisine-style bone mending while metabolised.</summary>
public sealed partial class MendBoneEntityEffectSystem : EntityEffectSystem<BloodstreamComponent, MendBone>
{
    [Dependency] private OxydWoundSystem _wounds = default!;

    protected override void Effect(Entity<BloodstreamComponent> entity, ref EntityEffectEvent<MendBone> args)
    {
        _wounds.MendRandomFracture(entity);
    }
}

/// <summary>Peridaxon-style organ repair while metabolised.</summary>
public sealed partial class HealOrganDamageEntityEffectSystem : EntityEffectSystem<BloodstreamComponent, HealOrganDamage>
{
    [Dependency] private OxydWoundSystem _wounds = default!;

    protected override void Effect(Entity<BloodstreamComponent> entity, ref EntityEffectEvent<HealOrganDamage> args)
    {
        _wounds.HealOrganDamage(entity, args.Effect.Amount * args.Scale);
    }
}

/// <summary>Advances addiction recovery (Eris additol / purger).</summary>
public sealed partial class ReduceAddictionEntityEffectSystem : EntityEffectSystem<BloodstreamComponent, ReduceAddiction>
{
    [Dependency] private AddictionSystem _addiction = default!;

    protected override void Effect(Entity<BloodstreamComponent> entity, ref EntityEffectEvent<ReduceAddiction> args)
    {
        _addiction.AdvanceRecovery(entity, (int) (args.Effect.Amount * args.Scale));
    }
}

/// <summary>Suppressital/aminazine: satisfied-feeling resets cravings (Eris suppressWithdrawal).</summary>
public sealed partial class SuppressWithdrawalEntityEffectSystem : EntityEffectSystem<BloodstreamComponent, SuppressWithdrawal>
{
    protected override void Effect(Entity<BloodstreamComponent> entity, ref EntityEffectEvent<SuppressWithdrawal> args)
    {
        if (!TryComp<AddictionComponent>(entity, out var comp))
            return;

        // Eris suppression: the chemical fills the craving so dependence resets to "sated".
        foreach (var state in comp.Reagents.Values)
        {
            if (state.Progress is > -15)
                state.Progress = -15;
        }
    }
}

/// <summary>Chemical sanity deltas (Eris chem sanity effects).</summary>
public sealed partial class ChemSanityEntityEffectSystem : EntityEffectSystem<BloodstreamComponent, ChemSanity>
{
    [Dependency] private SanitySystem _sanity = default!;

    protected override void Effect(Entity<BloodstreamComponent> entity, ref EntityEffectEvent<ChemSanity> args)
    {
        if (TryComp<SanityComponent>(entity, out var sanity))
            _sanity.ApplySanityDelta((entity.Owner, sanity), SanitySource.Chemical, args.Effect.Amount * args.Scale);
    }
}

/// <summary>Quickclot-style wound sealing while metabolised.</summary>
public sealed partial class SealWoundsEntityEffectSystem : EntityEffectSystem<BloodstreamComponent, SealWounds>
{
    [Dependency] private BloodstreamSystem _bloodstream = default!;

    protected override void Effect(Entity<BloodstreamComponent> entity, ref EntityEffectEvent<SealWounds> args)
    {
        _bloodstream.TryModifyBleedAmount(entity.AsNullable(), -args.Effect.BleedReduction * args.Scale);
    }
}
