using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:213-230</c> (Baptismal Record): the priest requests the parish
/// record from the altar in front, and a paper listing the active cruciform bearers slides out.
/// The altar lookup and the paper are server-side, so the effect raises
/// <see cref="LitanyBaptismalRecordEvent"/> on the altar and the server <c>AltarSystem</c>
/// writes the record.
/// </summary>
public sealed partial class LitanyBaptismalRecordEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (!context.Targets.Any(system.IsLitanyAltar))
        {
            failure = "oxyd-litany-no-target";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        foreach (var target in context.Targets)
        {
            if (!system.IsLitanyAltar(target))
                continue;

            var record = new LitanyBaptismalRecordEvent(target, false);
            system.RaiseOn(target, ref record);
            if (record.Handled)
                return true;
        }

        return false;
    }
}
