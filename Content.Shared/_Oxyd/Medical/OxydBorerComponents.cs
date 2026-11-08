using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Medical;

/// <summary>
/// Eris cortical borer (mob/living/simple_animal/borer). A small sluglike parasite that
/// infests a humanoid through the ear canal and lives in a container inside the host.
/// Stores "chemicals" (a regenerating resource) spent on secreting medicines/drugs into
/// the host's bloodstream, paralyzing victims, and reproduction. Scoped-down port:
/// infestation, secretion, paralysis, reproduction — no mind-control/assumption.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class OxydBorerComponent : Component
{
    /// <summary>Eris `chemicals`: the resource pool all abilities draw from.</summary>
    [DataField, AutoNetworkedField]
    public float Chemicals = 50f;

    /// <summary>Eris `max_chemicals`: pool cap while outside a host.</summary>
    [DataField]
    public float MaxChemicals = 50f;

    /// <summary>Eris `max_chemicals_inhost`: pool cap while inside a host.</summary>
    [DataField]
    public float MaxChemicalsInHost = 250f;

    /// <summary>Eris Life() regen: +1 per tick outside, +level+1 inside (level 0 → +1).</summary>
    [DataField]
    public float ChemRegenPerSecond = 1f;

    /// <summary>Regen per second inside a host (Eris +level+1 at level 0).</summary>
    [DataField]
    public float ChemRegenInHostPerSecond = 1f;

    /// <summary>Brute healed per second while inside a host (Eris adjustBruteLoss(-1) per Life).</summary>
    [DataField]
    public float HostRegenBrutePerSecond = 1f;

    /// <summary>Damage type the host regen heals (Eris adjustBruteLoss(-1) — Brute group).</summary>
    [DataField]
    public ProtoId<DamageTypePrototype> HostRegenDamageType = "Blunt";

    /// <summary>Current host the borer is nested inside.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Host;

    /// <summary>Sugar in the host's blood makes the borer docile (Eris process_host).</summary>
    [DataField, AutoNetworkedField]
    public bool Docile;

    [DataField, AutoNetworkedField]
    public bool HasReproduced;

    /// <summary>Eris `produced_reagents` level-0 list, mapped to this fork's reagent ids.</summary>
    [DataField]
    public List<ProtoId<ReagentPrototype>> ProducedReagents = new()
    {
        "OxydMedAlkysine",    // alkysine
        "Bicaridine",         // bicaridine
        "Kelotane",           // kelotane
        "Dexalin",            // dexalin
        "Dylovene",           // anti_toxin
        "OxydDrugHyperzine",  // hyperzine
        "OxydMedTramadol",    // tramadol
        "SpaceDrugs",         // space_drugs
    };

    /// <summary>Chemical cost of one secretion (Eris chemicals -= 50 → 10u reagent).</summary>
    [DataField]
    public float SecreteCost = 50f;

    /// <summary>Units of reagent pushed into the host bloodstream per secretion.</summary>
    [DataField]
    public float SecreteAmount = 10f;

    /// <summary>Chemical cost of Paralyze Victim (Eris chemicals -= 10).</summary>
    [DataField]
    public float ParalyzeCost = 10f;

    /// <summary>Paralysis duration at level 0 (Eris 10 + level*2).</summary>
    [DataField]
    public TimeSpan ParalyzeDuration = TimeSpan.FromSeconds(10);

    /// <summary>Eris one-minute dominate cooldown.</summary>
    [DataField]
    public TimeSpan ParalyzeCooldown = TimeSpan.FromMinutes(1);

    /// <summary>Time of last paralyze use (Eris used_dominate).</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan LastParalyze = TimeSpan.Zero;

    /// <summary>Eris DEFAULT_INFESTATION_DELAY (2.5s).</summary>
    [DataField]
    public TimeSpan InfestDelay = TimeSpan.FromSeconds(2.5);

    /// <summary>Delay multiplier for protective headgear / NT implant (Eris x3).</summary>
    [DataField]
    public float ArmoredInfestMultiplier = 3f;

    /// <summary>Eris release_host channel (spawn(100) ≈ 10s).</summary>
    [DataField]
    public TimeSpan ReleaseDelay = TimeSpan.FromSeconds(10);

    /// <summary>Fraction of the in-host pool needed to reproduce (Eris 75%).</summary>
    [DataField]
    public float ReproduceFraction = 0.75f;

    /// <summary>Borer prototype spawned by Reproduce (self-reference).</summary>
    [DataField]
    public EntProtoId ReproducePrototype = "OxydBorer";

    /// <summary>Seconds between chem-regen/docile ticks.</summary>
    [DataField]
    public float UpdateInterval = 1f;

    [ViewVariables, AutoPausedField]
    public TimeSpan NextUpdate;

    // ------------------------------------------------------------------
    // Assume control (Eris assume_control / detach / release_control)
    // ------------------------------------------------------------------

    /// <summary>Eris `controlling`: the borer's mind currently drives the host body.</summary>
    [DataField, AutoNetworkedField]
    public bool Controlling;

    /// <summary>The captive-brain entity inside this borer holding the host's mind
    /// while <see cref="Controlling"/> (Eris `host_brain`, a /mob/living/captive_brain
    /// inside the borer).</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? HostBrain;

    /// <summary>Container inside the borer entity that holds the captive brain.</summary>
    public const string CaptiveContainerId = "oxyd_borer_captive";

    [ViewVariables]
    public Container CaptiveContainer = default!;

    /// <summary>Eris assume_control channel: 30 SECONDS + brainloss*5 (no brainloss stat here).</summary>
    [DataField]
    public TimeSpan AssumeControlDelay = TimeSpan.FromSeconds(30);

    /// <summary>Prototype of the captive-brain entity spawned while controlling.</summary>
    [DataField]
    public EntProtoId CaptiveBrainPrototype = "OxydBorerCaptiveBrain";

    /// <summary>Captive-mind resist channel bounds (Eris rand(25s, 30s)+brainloss).</summary>
    [DataField]
    public TimeSpan ResistMinDelay = TimeSpan.FromSeconds(25);

    [DataField]
    public TimeSpan ResistMaxDelay = TimeSpan.FromSeconds(30);

    // ------------------------------------------------------------------
    // Mind read/write side-effects (Eris read_mind/write_mind)
    // ------------------------------------------------------------------

    /// <summary>Eris read_mind: adjustBrainLoss(copied*4) — no stats to copy, flat cellular.</summary>
    [DataField]
    public float ReadMindDamage = 6f;

    /// <summary>Eris write_mind: adjustBrainLoss(copied*2).</summary>
    [DataField]
    public float WriteMindDamage = 3f;

    /// <summary>Jitter/confusion duration applied by Read/Write Mind.</summary>
    [DataField]
    public TimeSpan MindEffectDuration = TimeSpan.FromSeconds(10);

    // ------------------------------------------------------------------
    // Evolution (Eris borer_exp / borer_level / level_up)
    // ------------------------------------------------------------------

    [DataField, AutoNetworkedField]
    public int BorerExp;

    [DataField, AutoNetworkedField]
    public int BorerLevel;

    /// <summary>Evolution tiers (Eris BORER_EXP_LEVEL_* + level_up added_reagents /
    /// max_chemicals bonus). Index 0 unlocks at borer level 1, and so on.
    /// Level 5 psionic reagents have no Oxyd equivalent.</summary>
    [DataField]
    public List<BorerLevel> Levels = new()
    {
        new BorerLevel { Threshold = 20, ChemBonus = 10f,
            Reagents = new() { "Inaprovaline", "Tricordrazine", "Synaptizine", "OxydMedImidazoline", "Hyronalin" } },
        new BorerLevel { Threshold = 40, ChemBonus = 20f,
            Reagents = new() { "OxydMedSpaceacillin", "OxydMedQuickclot", "OxydMedDetox", "OxydMedPurger", "Arithrazine" } },
        new BorerLevel { Threshold = 80, ChemBonus = 30f,
            Reagents = new() { "OxydMedMeralyne", "Dermaline", "DexalinPlus", "OxydMedOxycodone", "OxydMedRyetalyn" } },
        new BorerLevel { Threshold = 160, ChemBonus = 40f,
            Reagents = new() { "OxydMedPeridaxon", "OxydMedRezadone", "OxydMedOssisine", "OxydMedKyphotorin", "OxydMedAminazine" } },
        new BorerLevel { Threshold = 320, ChemBonus = 50f },
    };

    /// <summary>Mind-attack damage type (Eris brainloss).</summary>
    [DataField]
    public ProtoId<DamageTypePrototype> CellularDamage = "Cellular";

    /// <summary>Exp awarded by Reproduce on a humanoid host (Eris borer_add_exp(25)).</summary>
    [DataField]
    public int ReproduceExp = 25;

    [DataField]
    public int ReadMindExp = 10;

    [DataField]
    public int WriteMindExp = 5;

    /// <summary>Hide verb: toggles the borer's draw depth under floor objects (Eris hide()).</summary>
    [DataField, AutoNetworkedField]
    public bool Hidden;
}

/// <summary>One evolution tier of <see cref="OxydBorerComponent.Levels"/>.</summary>
[DataDefinition]
public sealed partial class BorerLevel
{
    /// <summary>BorerExp needed to reach this tier (Eris BORER_EXP_LEVEL_n).</summary>
    [DataField]
    public int Threshold;

    /// <summary>Reagents unlocked on reaching this tier (Eris added_reagents).</summary>
    [DataField]
    public List<ProtoId<ReagentPrototype>> Reagents = new();

    /// <summary>MaxChemicals bonus granted on reaching this tier (Eris max_chemicals += level*10).</summary>
    [DataField]
    public float ChemBonus;
}

/// <summary>The captive mind: an entity inside the borer's container holding the host's
/// mind while the borer controls the host body (Eris /mob/living/captive_brain).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydBorerCaptiveComponent : Component
{
    /// <summary>The host body this captive's mind belongs to.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Host;

    /// <summary>The borer entity this captive lives inside.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Borer;
}

/// <summary>Marks a mob hosting a cortical borer; the borer entity lives in
/// <see cref="BorerContainer"/> inside this entity (Eris head.implants).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydBorerHostComponent : Component
{
    public const string BorerContainerId = "oxyd_borer_host";

    /// <summary>The borer nested inside this host.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Borer;

    [ViewVariables]
    public Container BorerContainer = default!;
}

[Serializable, NetSerializable]
public sealed partial class OxydBorerInfestDoAfterEvent : SimpleDoAfterEvent
{
}

[Serializable, NetSerializable]
public sealed partial class OxydBorerReleaseDoAfterEvent : SimpleDoAfterEvent
{
}

[Serializable, NetSerializable]
public sealed partial class OxydBorerAssumeControlDoAfterEvent : SimpleDoAfterEvent
{
}

[Serializable, NetSerializable]
public sealed partial class OxydBorerResistDoAfterEvent : SimpleDoAfterEvent
{
}

/// <summary>Appearance data keys driven by <see cref="OxydBorerComponent"/>.</summary>
[Serializable, NetSerializable]
public enum OxydBorerVisuals : byte
{
    /// <summary>Hide verb toggled — draw under floor clutter.</summary>
    Hidden,
}
