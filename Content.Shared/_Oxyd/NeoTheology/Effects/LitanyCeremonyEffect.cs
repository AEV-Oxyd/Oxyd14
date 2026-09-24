namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Base for the group-ritual payloads (Eris <c>rituals/group.dm</c>). Ceremony effects run at
/// the end of the phrase round, once for the starter and every recorded participant.
/// </summary>
public abstract partial class LitanyCeremonyEffect : LitanyEffect
{
    /// <summary>
    /// Eris <c>/datum/ritual/group/cruciform/high_ritual</c>: a priest or inquisitor must start
    /// the rite. Sanctify overrides this because Eris marks it <c>high_ritual = FALSE</c>.
    /// </summary>
    public virtual bool RequiresClergy => true;

    /// <summary>Eris <c>GLOB.miracle_points--</c> and <c>eotp.addObservation(25)</c> in the stat rite.</summary>
    public virtual bool ConsumesMiraclePoint => false;
}
