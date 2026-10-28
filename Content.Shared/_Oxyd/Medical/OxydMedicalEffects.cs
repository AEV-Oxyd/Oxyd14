using Content.Shared._Oxyd.Skills;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Medical;

/// <summary>Marks a reagent as contributing NSA load while in the bloodstream (Eris nerve_system_accumulation).</summary>
public sealed partial class Nsa : EntityEffectBase<Nsa>
{
    /// <summary>NSA contribution while this reagent metabolises (Eris value per tick, scaled by dose).</summary>
    [DataField(required: true)]
    public float Value;

    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) =>
        Loc.GetString("oxyd-medical-effect-nsa", ("value", Value));
}

/// <summary>Temporarily raises the patient's NSA threshold (Eris detox nanites "tolerance").</summary>
public sealed partial class NsaTolerance : EntityEffectBase<NsaTolerance>
{
    [DataField(required: true)]
    public float Value;

    [DataField]
    public float Duration = 30f;

    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) =>
        Loc.GetString("oxyd-medical-effect-nsa-tolerance", ("value", Value));
}

/// <summary>Eris stims: apply a timed stat buff via SharedSkillSystem.SetUniqueBuff while metabolised.</summary>
public sealed partial class Stim : EntityEffectBase<Stim>
{
    [DataField(required: true)]
    public string StimId = string.Empty;

    [DataField(required: true)]
    public ProtoId<SkillPrototype> Skill;

    [DataField(required: true)]
    public int Amount;

    [DataField]
    public float Duration = 10f;

    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) =>
        Loc.GetString("oxyd-medical-effect-stim", ("skill", Skill), ("amount", Amount));
}

/// <summary>Mends a random fractured organ on the body (Eris ossisine).</summary>
public sealed partial class MendBone : EntityEffectBase<MendBone>
{
    [DataField]
    public float HealAmount = 1f;

    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) =>
        Loc.GetString("oxyd-medical-effect-mend-bone");
}

/// <summary>Reduces per-organ damage on the worst wounded organs (Eris peridaxon/veritol bolus organ repair).</summary>
public sealed partial class HealOrganDamage : EntityEffectBase<HealOrganDamage>
{
    [DataField]
    public float Amount = 0.5f;

    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) =>
        Loc.GetString("oxyd-medical-effect-heal-organ");
}

/// <summary>Advances addiction recovery (Eris additol/purger addictions -= N).</summary>
public sealed partial class ReduceAddiction : EntityEffectBase<ReduceAddiction>
{
    [DataField]
    public int Amount = 1;

    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) =>
        Loc.GetString("oxyd-medical-effect-reduce-addiction");
}

/// <summary>Temporarily satisfies all cravings (Eris suppressital/aminazine).</summary>
public sealed partial class SuppressWithdrawal : EntityEffectBase<SuppressWithdrawal>
{
    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) =>
        Loc.GetString("oxyd-medical-effect-suppress-withdrawal");
}

/// <summary>Applies a sanity delta while metabolised (Eris chem sanity effects).</summary>
public sealed partial class ChemSanity : EntityEffectBase<ChemSanity>
{
    [DataField(required: true)]
    public float Amount;

    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) =>
        Loc.GetString("oxyd-medical-effect-chem-sanity", ("amount", Amount));
}

/// <summary>Stops active bleeding (Eris quickclot seals wounds).</summary>
public sealed partial class SealWounds : EntityEffectBase<SealWounds>
{
    [DataField]
    public float BleedReduction = 5f;

    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) =>
        Loc.GetString("oxyd-medical-effect-seal-wounds");
}
