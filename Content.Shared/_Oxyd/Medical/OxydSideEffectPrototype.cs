using Content.Shared.Chemistry.Reagent;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Medical;

/// <summary>
/// Eris datum/medical_effect: a medical side effect that manifests while a
/// trigger reagent concentration is met and no cure reagent circulates, ramps
/// in strength on a sine cycle, and hurts via tiered custom_pain messages.
/// </summary>
[Prototype]
public sealed partial class OxydSideEffectPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Reagent id -> minimum bloodstream units that manifests the effect.</summary>
    [DataField]
    public Dictionary<ProtoId<ReagentPrototype>, float> Triggers = new();

    /// <summary>Reagents that cure (and suppress manifesting) the effect.</summary>
    [DataField]
    public List<ProtoId<ReagentPrototype>> Cures = new();

    [DataField]
    public LocId CureMessage;

    /// <summary>Pain messages by tier: [0] strength<=10, [1] <=30, [2] >30 (Eris switch).</summary>
    [DataField]
    public LocId[] PainMessages = new LocId[3];

    /// <summary>Optional emote line broadcast at the top tier (Eris H.emote("me")).</summary>
    [DataField]
    public LocId? SevereEmote;

    /// <summary>Pain amount applied when the severe tier fires (Eris custom_pain power flag).</summary>
    [DataField]
    public float SeverePain = 1f;
}

/// <summary>A currently-active side effect on a mob.</summary>
[DataDefinition]
public sealed partial class OxydSideEffectInstance
{
    [DataField]
    public ProtoId<OxydSideEffectPrototype> Effect;

    [DataField]
    public float Strength;

    /// <summary>Game-time the manifestation (re)started - drives the sine cycle.</summary>
    public TimeSpan Start;

    /// <summary>Game-time of this instance's next on_life pain pulse (Eris life_tick % 45).</summary>
    public TimeSpan NextPulse;
}

/// <summary>Tracks active medical side effects (Eris human.side_effects).</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class OxydSideEffectsComponent : Component
{
    [DataField]
    public List<OxydSideEffectInstance> Active = new();

    /// <summary>Next game-time the manifest scan runs for this mob.</summary>
    [AutoPausedField]
    public TimeSpan NextUpdate;
}
