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

    [DataField]
    public LitanyParameters? Parameters { get; private set; }

    [DataField]
    public NeoTheologyDependency Dependency { get; private set; }

    /// <summary>Dependency-gated entries remain in the reference catalog but cannot be cast.</summary>
    [DataField]
    public bool Enabled { get; private set; } = true;

    [DataField]
    public LocId? UnavailableReason { get; private set; }

    public bool IsAvailable => Enabled && Dependency == NeoTheologyDependency.None;
}
