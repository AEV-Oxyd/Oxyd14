using Content.Shared.Database;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/base.dm:400-430</c> (ejection): a dead bearer's cruciform is ripped out —
/// 15 Blunt into the corpse first, then the same implant entity is dropped beside it. The
/// cruciform is never deleted, so it can be re-installed and scanned for Resurrection.
/// </summary>
public sealed partial class LitanyDeprivationEffect : LitanyEffect<LitanyDeprivationEffect>
{
    /// <summary>Eris ejection: 15 Brute to the chest before the implant comes out.</summary>
    public const int EjectDamage = 15;

    /// <summary>§7.1: the dead-only extraction flow needs Dead candidates resolved.</summary>
    public override bool AllowsDeadTarget => true;

    /// <summary>The pipeline delivers each resolved corpse; there is no pick-one fallback.</summary>
    public override bool PerTarget => true;

    public LitanyDeprivationEffect()
    {
        Conditions =
        [
            // Eris: "Deprivation does not work upon the living."
            new LitanyDeadCondition { FailureId = "oxyd-litany-deprivation-alive" },
            new LitanyInstalledCruciformCondition { FailureId = "oxyd-litany-no-cruciform" },
            new LitanyCanReceiveDamageCondition { FailureId = "oxyd-litany-no-effect" },
        ];
    }
}

/// <summary>Damages each resolved corpse, then rips out its installed cruciform.</summary>
public sealed partial class LitanyDeprivationEntityEffectSystem
    : EntityEffectSystem<DamageableComponent, LitanyDeprivationEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<DamageableComponent> entity, ref EntityEffectEvent<LitanyDeprivationEffect> args)
    {
        // Re-read the installed cruciform at apply time; the same entity is what comes out.
        if (!_litany.TryGetInstalledCruciformEntity(entity, out var cruciform, out _))
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        // Eris order: the 15 Blunt lands first; abort before any mutation if it cannot.
        if (!_litany.TryApplyDamage(
                entity,
                new DamageSpecifier { DamageDict = { [NeoTheologyPrototypes.DamageBlunt] = FixedPoint2.New(LitanyDeprivationEffect.EjectDamage) } }))
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        var extracted = _litany.TryExtractInstalledCruciform(entity, cruciform);
        if (extracted && _litany.GetCastContext(args.User) is { } ctx)
        {
            _litany.AdminLog(LogType.Action, LogImpact.Medium, args.User ?? entity.Owner,
                $"extracted the cruciform via {ctx.Litany.ID} from", entity.Owner);
        }

        _litany.ReportApplyResult(args.User, extracted);
    }
}
