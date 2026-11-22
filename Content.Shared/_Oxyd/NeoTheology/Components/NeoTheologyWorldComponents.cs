using Content.Shared.Damage;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Maths;

namespace Content.Shared._Oxyd.NeoTheology.Components;

[RegisterComponent]
public sealed partial class NeoTheologySanctifiedAreaComponent : Component
{
    // ponytail: native room areas are absent; sanctify a bounded 7 m patch, replace with room polygons when available.
    [DataField] public HashSet<Vector2i> Tiles = new();
}

[RegisterComponent]
public sealed partial class NeoTheologyFactionItemComponent : Component
{
    [DataField] public bool CrusadeActivated;
    [DataField] public bool Church = true;
}

/// <summary>Explicit source Carrion/Blitz/Borer threat hook. Native traitors are not guessed to be one.</summary>
[RegisterComponent]
public sealed partial class NeoTheologyThreatComponent : Component;

[RegisterComponent]
public sealed partial class SwordOfTruthComponent : Component
{
    [DataField] public TimeSpan Cooldown = TimeSpan.FromMinutes(1);
    public TimeSpan NextFlash;

    /// <summary>Extra damage added to the blade while the wielder's crusade blessing is active.</summary>
    [DataField]
    public DamageSpecifier CrusadeDamageBonus = new()
    {
        DamageDict = { ["Slash"] = 8f },
    };

    /// <summary>Reach of the blinding flash.</summary>
    [DataField] public float FlashRange = 7f;

    [DataField] public TimeSpan FlashStunDuration = TimeSpan.FromSeconds(5);

    /// <summary>Skill debuff the flash puts on every skill of a non-believer.</summary>
    [DataField] public int FlashSkillPenalty = -40;

    [DataField] public TimeSpan FlashDebuffDuration = TimeSpan.FromSeconds(45);

    /// <summary>Unique-buff id so a repeated flash refreshes rather than stacks.</summary>
    [DataField] public string FlashBuffId = "SwordOfTruth";
}

[RegisterComponent]
public sealed partial class NeoTheologySealComponent : Component;

[RegisterComponent]
public sealed partial class HolyLightComponent : Component
{
    [DataField] public TimeSpan Interval = TimeSpan.FromSeconds(1);
    [DataField] public float Radius = 7f;
    [DataField] public float Healing = 0.1f;
    public TimeSpan NextPulse;
}

[RegisterComponent]
public sealed partial class LastShelterComponent : Component
{
    [DataField] public TimeSpan Cooldown = TimeSpan.FromMinutes(15);
    public TimeSpan NextRecovery;
}

[RegisterComponent]
public sealed partial class NeoTheologyObjectiveComponent : Component
{
    [DataField] public NeoTheologyObjectiveKind Kind;
    [DataField] public EntityUid? TargetMind;
    [DataField] public string? TargetPrototype;
    [DataField] public EntityUid? TargetGrid;
    [DataField] public Vector2i TargetTile;
    [DataField] public bool Completed;
}

public enum NeoTheologyObjectiveKind : byte { Convert, Reveal, Sanctify, Destroy }
