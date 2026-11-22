using Content.Shared.Body;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage.Prototypes;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Medical;

public sealed partial class AnalgesicEntityEffectSystem : EntityEffectSystem<PainComponent, Analgesic>
{
    [Dependency] private PainSystem _pain = default!;

    protected override void Effect(Entity<PainComponent> entity, ref EntityEffectEvent<Analgesic> args)
    {
        _pain.SuppressPain(entity, args.Effect.EffectProto, args.Effect.Strength * args.Scale, args.Effect.Duration);
    }
}

/// <summary>Applies an analgesic status effect during normal reagent metabolism.</summary>
public sealed partial class Analgesic : EntityEffectBase<Analgesic>
{
    /// <summary>The analgesic status-effect entity to apply or refresh — one proto per source.</summary>
    [DataField(required: true)]
    public EntProtoId EffectProto = string.Empty;

    [DataField(required: true)]
    public float Strength;

    [DataField]
    public float Duration = 2f;

    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) =>
        Loc.GetString("oxyd-medical-effect-analgesic", ("strength", Strength));
}

/// <summary>Checks dependence during normal metabolism. The server owns addiction state.</summary>
public sealed partial class Addictive : EntityEffectBase<Addictive>
{
    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    [DataField]
    public FixedPoint2 Threshold = 20;

    [DataField]
    public float AddictionChance = 0.1f;

    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) =>
        Loc.GetString("oxyd-medical-effect-addictive");
}

/// <summary>Eris resuscitator affect_blood: wrecks the heart organ each tick;
/// on a dead body within the resuscitation window it restarts the heart
/// (revive to Critical). Injectable-only chem - ingest does nothing.</summary>
public sealed partial class Resuscitate : EntityEffectBase<Resuscitate>
{
    /// <summary>Organ damage dealt to the heart per metabolism tick (Eris 64 TOX).</summary>
    [DataField]
    public float HeartDamage = 30f;

    /// <summary>Dead-state window in minutes during which revival still works
    /// (Eris NECROZTIME = 15 min).</summary>
    [DataField]
    public float ReviveWindowMinutes = 15f;

    /// <summary>Bloodstream units of the reagent consumed on a successful revive
    /// (Eris remove_self(60)).</summary>
    [DataField]
    public FixedPoint2 ReviveDrain = 60;

    /// <summary>Asphyxiation damage is capped to this on revive (Eris setOxyLoss(20)).</summary>
    [DataField]
    public float OxyLossCap = 20f;

    /// <summary>Organ categories that must be present and viable to revive.</summary>
    [DataField]
    public ProtoId<OrganCategoryPrototype> HeartOrgan = "Heart";

    [DataField]
    public ProtoId<OrganCategoryPrototype> BrainOrgan = "Brain";

    /// <summary>Damage type capped by <see cref="OxyLossCap"/>.</summary>
    [DataField]
    public ProtoId<DamageTypePrototype> OxyLossType = "Asphyxiation";

    /// <summary>Reagent drained by <see cref="ReviveDrain"/> on a successful revive.</summary>
    [DataField]
    public ProtoId<ReagentPrototype> ResuscitatorReagent = "OxydChemResuscitator";

    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) =>
        Loc.GetString("entity-effect-guidebook-resuscitate", ("chance", Probability));
}

/// <summary>Adds temporary pain while metabolised (Eris kognim halloss + pain()).</summary>
public sealed partial class InflictPainEntityEffectSystem : EntityEffectSystem<PainComponent, InflictPain>
{
    [Dependency] private PainSystem _pain = default!;

    protected override void Effect(Entity<PainComponent> entity, ref EntityEffectEvent<InflictPain> args)
    {
        _pain.AddPain(entity, args.Effect.Amount * args.Scale);
    }
}

public sealed partial class InflictPain : EntityEffectBase<InflictPain>
{
    [DataField(required: true)]
    public float Amount;

    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) =>
        Loc.GetString("oxyd-medical-effect-inflict-pain");
}
