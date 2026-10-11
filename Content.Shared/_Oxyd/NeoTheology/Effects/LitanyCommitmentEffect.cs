using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/base.dm:311-352</c> (install): attach the loose empty cruciform resting on
/// the altar beside the target, then drive 25 Blunt into their chest in a single operation.
/// The implant lands inert — Epiphany is what activates it (§5.2).
/// </summary>
public sealed partial class LitanyCommitmentEffect : LitanyEffect<LitanyCommitmentEffect>, ISerializationHooks
{
    /// <summary>Eris deals five 5-Brute chest hits; ported as one 25-Blunt operation.</summary>
    public const int InstallDamage = 25;

    /// <summary>The pipeline delivers each resolved patient; there is no pick-one fallback.</summary>
    public override bool PerTarget => true;

    /// <summary>§5.2 v1: only this species' humanoids may be committed. Null allows anyone.</summary>
    [DataField]
    public ProtoId<SpeciesPrototype>? RequiredSpecies;

    public LitanyCommitmentEffect()
    {
        // Eris install gate order: eligible victim, no existing cruciform, loose cruciform in
        // reach, living, empty implant. Non-humans are rejected before anything is consumed.
        Conditions =
        [
            new LitanyAliveCondition { FailureId = "oxyd-litany-commitment-too-late" },
            new LitanyNoInstalledCruciformCondition { FailureId = "oxyd-litany-commitment-has-cruciform" },
            new LitanyCanReceiveDamageCondition { FailureId = "oxyd-litany-no-effect" },
            new LitanyProcedureAltarCondition { Undressed = true },
            new LitanyAltarCruciformCondition { FailureId = "oxyd-litany-commitment-no-cruciform" },
        ];
    }

    void ISerializationHooks.AfterDeserialization()
    {
        // The species gate reads a DataField, so it can only be installed once values load.
        // It precedes every other gate to match the Eris check order.
        if (RequiredSpecies is { } species)
        {
            Conditions =
            [
                new LitanySpeciesCondition { Species = species, FailureId = "oxyd-litany-not-human" },
                .. Conditions ?? [],
            ];
        }
    }
}

/// <summary>
/// Commits each resolved patient: re-lookup the altar cruciform at apply time, implant it,
/// then drive the install damage.
/// </summary>
public sealed partial class LitanyCommitmentEntityEffectSystem
    : EntityEffectSystem<DamageableComponent, LitanyCommitmentEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<DamageableComponent> entity, ref EntityEffectEvent<LitanyCommitmentEffect> args)
    {
        // Commit re-runs the deterministic altar lookup: if the altar's loose cruciform moved
        // or was consumed mid-chant, the cast fails instead of grabbing a different item.
        if (!_litany.TryFindAltarCruciform(entity, out _, out var cruciform))
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        if (!_litany.TryImplantLooseCruciform(entity, cruciform))
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        // Eris damages the chest only after the implant is in.
        _litany.ReportApplyResult(args.User,
            _litany.TryApplyDamage(
                entity,
                new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(LitanyCommitmentEffect.InstallDamage) } }));
    }
}
