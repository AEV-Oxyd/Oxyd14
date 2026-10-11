using Content.Shared.EntityEffects;
using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Crusade (Eris <c>rituals/group.dm:220-245</c>): with at least six followers the rite teaches
/// the crusader litanies to every bearer who took part. Eris stores this in the implant's
/// <c>known_rituals</c>; the fork grants the <c>OxydLitanyCrusader</c> set instead. Eris also
/// flips the world crusade flag on faction items; the fork has no equivalent (named divergence).
/// </summary>
public sealed partial class LitanyCrusadeEffect : LitanyCeremonyEffect<LitanyCrusadeEffect>
{
    /// <summary>Eris <c>success()</c> floor for teaching the crusader litanies.</summary>
    public const int MinimumParticipants = 6;

    /// <summary>The set the rite reveals: Eternal Brotherhood, Call to Battle, Searing Revelation.</summary>
    public static readonly ProtoId<LitanySetPrototype> CrusaderSet = "OxydLitanyCrusader";

    // Begin resolves no candidates: the followers join while the rite runs, so the effect
    // carries no target conditions at all.
}

/// <summary>
/// Cast-scoped: with enough participants, fires the crusade world flag and grants the
/// crusader set to every participant bearer.
/// </summary>
public sealed partial class LitanyCrusadeEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyCrusadeEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyCrusadeEffect> args)
    {
        if (_litany.GetCastContext(args.User) is not { } ctx)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        if (ctx.CeremonyParticipants < LitanyCrusadeEffect.MinimumParticipants)
        {
            // Eris completes the rite and only skips the payload below the floor.
            _litany.ReportApplyResult(args.User, true);
            return;
        }

        var crusade = new NeoTheologyCrusadeEvent();
        _litany.RaiseOn(entity.Owner, ref crusade);

        var handled = false;
        foreach (var target in ctx.Targets)
        {
            var grant = new LitanyGrantLitanySetEvent(target, LitanyCrusadeEffect.CrusaderSet, false);
            _litany.RaiseOn(target, ref grant);
            handled |= grant.Handled;
        }

        _litany.ReportApplyResult(args.User, handled);
    }
}
