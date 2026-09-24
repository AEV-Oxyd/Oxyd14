using Content.Shared._Oxyd.NeoTheology.Effects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.NeoTheology;

[Prototype("oxydLitany")]
public sealed partial class LitanyPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    [DataField(required: true)]
    public LocId Name { get; private set; } = string.Empty;

    [DataField(required: true)]
    public LocId Description { get; private set; } = string.Empty;

    /// <summary>Invariant phrase used for recognition. It is deliberately not localized.</summary>
    [DataField(required: true)]
    public string Phrase { get; private set; } = string.Empty;

    /// <summary>
    /// Group-ritual phrase list (Eris <c>rituals/group.dm</c> <c>phrases</c>): index 0 is the
    /// starter phrase, later entries are the follower and advance phrases of each round. Only
    /// <see cref="LitanyTargetMode.Ceremony"/> litanies declare it; the validator requires at
    /// least two entries and a first entry equal to <see cref="Phrase"/>.
    /// </summary>
    [DataField]
    public List<string> CeremonyPhrases { get; private set; } = new();

    [DataField(required: true)]
    public LitanyCategory Category { get; private set; }

    [DataField]
    public List<ProtoId<LitanySetPrototype>> GrantedBy { get; private set; } = new();

    [DataField(required: true)]
    public LitanyEffectKind Effect { get; private set; }

    [DataField(required: true)]
    public LitanyTargetMode TargetMode { get; private set; }

    [DataField]
    public float Range { get; private set; }

    [DataField]
    public double Cost { get; private set; }

    [DataField]
    public string CooldownKey { get; private set; } = string.Empty;

    [DataField]
    public LitanyCooldownScope CooldownScope { get; private set; }

    [DataField]
    public TimeSpan CooldownDuration { get; private set; } = TimeSpan.Zero;

    [DataField]
    public bool IgnoreStuttering { get; private set; }

    [DataField]
    public TimeSpan ExtraDelay { get; private set; } = TimeSpan.Zero;

    [DataField]
    public TimeSpan EffectDuration { get; private set; } = TimeSpan.Zero;

    /// <summary>
    /// The caster picks one of the resolved targets in the book UI before the chant
    /// (Eris <c>pick_disciple_global</c>). Without it the cast uses the deterministic
    /// first target, which manual speech keeps as its fallback.
    /// </summary>
    [DataField]
    public bool SelectTarget { get; private set; }

    /// <summary>
    /// Designations the caster picks in the book UI. Eris Confirmation offers
    /// Acolyte / Agrolyte / Custodian; the YAML lists the profile prototypes.
    /// </summary>
    [DataField]
    public List<ProtoId<NeoTheologyProfilePrototype>> DesignationChoices { get; private set; } = new();

    /// <summary>The caster types a message the effect delivers (Eris Sending).</summary>
    [DataField]
    public bool AllowPlainText { get; private set; }

    /// <summary>
    /// The caster picks one NeoTheology blueprint in the book UI before the chant
    /// (Eris construction.dm "Select construction"). Manual speech fails closed because
    /// it has no choice surface.
    /// </summary>
    [DataField]
    public bool SelectBlueprint { get; private set; }

    /// <summary>
    /// Declarative effect list, instantiated from YAML as <c>!type:</c> entries.
    /// Replaces the legacy parameter-block families.
    /// </summary>
    [DataField]
    public List<LitanyEffect> Effects { get; private set; } = new();

    [DataField]
    public NeoTheologyDependency Dependency { get; private set; }

    /// <summary>Dependency-gated entries remain in the reference catalog but cannot be cast.</summary>
    [DataField]
    public bool Enabled { get; private set; } = true;

    [DataField]
    public LocId? UnavailableReason { get; private set; }

    public bool IsAvailable => Enabled && Dependency == NeoTheologyDependency.None;
}
