using Content.Shared.Body;
using Content.Shared.DoAfter;
using Content.Shared.Item;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Medical;

/// <summary>
/// Surgical and wound state carried on an organ entity (one per <see cref="Content.Shared.Body.Components.OrganComponent"/>).
/// Ports Eris modules/surgery + organs/external damage flags: incisions, fractures, clamped bleeders and embedded items.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class OxydOrganSurgeryComponent : Component
{
    /// <summary>Whether this organ is machined. Affects which surgical steps apply (Eris "robotic" organs).</summary>
    [DataField, AutoNetworkedField]
    public bool Robotic;

    /// <summary>Eris incision stage: closed skin, open, or held open by a retractor.</summary>
    [DataField, AutoNetworkedField]
    public OxydIncisionStage Incision = OxydIncisionStage.None;

    /// <summary>The incision's bleeders are not yet clamped (Eris incision.healed/clamped state).</summary>
    [DataField, AutoNetworkedField]
    public bool Clamped = true;

    /// <summary>A bone fracture sustained from brute damage or deliberate breakage (Eris is_broken()).</summary>
    [DataField, AutoNetworkedField]
    public bool Fractured;

    /// <summary>A splint item is stabilising a fracture so it can heal (Eris wound splint).</summary>
    [DataField, AutoNetworkedField]
    public bool Splinted;

    /// <summary>Per-organ damage pool that surgery and chems can reduce. Raised by severities of wounds.</summary>
    [DataField, AutoNetworkedField]
    public float OrganDamage;

    /// <summary>Brute share of <see cref="OrganDamage"/> (Eris organ.brute_dam, shown as its own bar).</summary>
    [DataField, AutoNetworkedField]
    public float BruteDamage;

    /// <summary>Burn share of <see cref="OrganDamage"/> (Eris organ.burn_dam, shown as its own bar).</summary>
    [DataField, AutoNetworkedField]
    public float BurnDamage;

    /// <summary>Embedded objects inside this organ (shrapnel, cavity items). Eris wound.embedded_objects.</summary>
    [DataField]
    public List<NetEntity> EmbeddedItems = new();

    /// <summary>Bleed rate contributed by this organ's wounds/incision while it stays open (Eris wound bleeding).</summary>
    [DataField, AutoNetworkedField]
    public float WoundBleedRate;

    /// <summary>The organ's wounds have been diagnosed (medical scanner or a wound probe). Until then the
    /// surgery UI hides wound details, like Eris's undiagnosed limbs.</summary>
    [DataField, AutoNetworkedField]
    public bool Diagnosed;

    /// <summary>Organ has rotted past viability after decaying outside a body (Eris ORGAN_DEAD). Blocks transplant.</summary>
    [DataField, AutoNetworkedField]
    public bool Decayed;

    /// <summary>Step currently running on this organ (shown on its card while the do_after runs).</summary>
    public OxydSurgeryStep? RunningStep;

    /// <summary>Next decay evaluation while outside a body (Eris organ Process tick).</summary>
    [AutoPausedField]
    public TimeSpan NextDecay;

    /// <summary>Display cap for the health bar on an organ (Eris organ.max_damage ~ 60).</summary>
    public const float OrganMaxDamage = 60f;

    /// <summary>Maximum cavity implants per organ (Eris limb.max_volume, simplified to a count).</summary>
    public const int ImplantCavityMax = 3;
}

[Serializable, NetSerializable]
public enum OxydIncisionStage : byte
{
    None,
    Open,
    Retracted,
}

/// <summary>Surgical tool roles. Items can cover several (Eris has e.g. combined items, ghetto substitutes).</summary>
[Serializable, NetSerializable, Flags]
public enum OxydSurgeryTool : uint
{
    None = 0,
    Scalpel = 1 << 0,
    Retractor = 1 << 1,
    Hemostat = 1 << 2,
    Cautery = 1 << 3,
    BoneSetter = 1 << 4,
    Saw = 1 << 5,
    Drill = 1 << 6,
    FixOVein = 1 << 7,
    BoneGel = 1 << 8,
    Screwdriver = 1 << 9,
    Welder = 1 << 10,
    Tray = 1 << 11,
    /// <summary>Brute-repair kits usable surgically: advanced trauma kit / bruise pack (Eris fix_organ, fix_brute).</summary>
    TraumaKit = 1 << 12,
    /// <summary>Burn-repair kits usable surgically: advanced burn kit / ointment (Eris fix_burn).</summary>
    BurnKit = 1 << 13,
    /// <summary>Cable coil for robotic burn repair (Eris wires on robotic organs). Usually applied via the CableCoil tag.</summary>
    CableCoil = 1 << 14,
}

/// <summary>Marks an item as usable as one or more surgical tools (Eris tools/surgery.dm). Speed scales do_after time.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydSurgeryToolComponent : Component
{
    [DataField, AutoNetworkedField]
    public OxydSurgeryTool Tools = OxydSurgeryTool.None;

    /// <summary>Multiplier on step duration; lower is faster (Eris tool tier).</summary>
    [DataField]
    public float Speed = 1f;

    /// <summary>Base success chance 0-100 before step difficulty and self-surgery penalties
    /// (Eris allowed_tools quality values: ~80-100 proper tools, ~20-40 improvised).</summary>
    [DataField]
    public int Quality = 80;
}

/// <summary>Per-surgeon open surgery windows on a patient (the BUI lives on the patient,
/// one session entry per surgeon that opened it).</summary>
[RegisterComponent]
public sealed partial class OxydSurgerySessionComponent : Component
{
    /// <summary>surgeon -> session state.</summary>
    public Dictionary<EntityUid, OxydSurgerySession> Sessions = new();
}

public sealed class OxydSurgerySession
{
    /// <summary>Item the surgeon opened the window with (tool or implant candidate).</summary>
    public EntityUid? Tool;

    public bool SelfSurgery;
}

/// <summary>Surgical steps, mirroring Eris modules/surgery generic steps and organ repair steps.</summary>
[Serializable, NetSerializable]
public enum OxydSurgeryStep : byte
{
    CutOpen,        // scalpel
    RetractSkin,    // retractor (also clamps with hemostat first)
    FixBleeding,    // hemostat
    Cauterize,      // cautery
    MendBone,       // bone setter (fractured)
    BreakBone,      // advanced: deliberately break
    FixBone,        // bone gel to seal a mended bone
    RemoveEmbedded, // hemostat on embedded object
    InsertItem,     // cavity implant
    RemoveItem,     // cavity extract
    AttachOrgan,    // fixovein + organ item
    DetachOrgan,    // scalpel on internal organ
    Amputate,       // saw on external organ
    RoboOpen,       // screwdriver on robotic organ
    RoboFixBrute,   // welder
    RoboFixBurn,    // cable coil (mapped to fixovein slot for now)
    RoboClose,      // screwdriver to close robo shell
    DiagnoseWound,  // surgical tool usage to read wound info
    ExtractShrapnel,// Eris remove_shrapnel: dig embedded objects out on a standing patient
    CloseWounds,    // Eris close_wounds: standing cauterise that seals surface bleeding
    FixOrgan,       // Eris fix_organ/fix_brute: trauma kit on an open site heals organ damage
}

/// <summary>DoAfter payload for a surgery step (Eris surgery step preop/do_surgery flow).</summary>
[Serializable, NetSerializable]
public sealed partial class OxydSurgeryDoAfterEvent : SimpleDoAfterEvent
{
    public NetEntity Patient;
    public NetEntity Organ;
    public OxydSurgeryStep Step;
    public NetEntity Tool;
    /// <summary>Self-surgery penalty applied to the success roll (Eris +20% fail).</summary>
    public bool SelfSurgery;

    public OxydSurgeryDoAfterEvent(NetEntity patient, NetEntity organ, OxydSurgeryStep step, NetEntity tool)
    {
        Patient = patient;
        Organ = organ;
        Step = step;
        Tool = tool;
    }

    private OxydSurgeryDoAfterEvent()
    {
    }
}

/// <summary>OrganCategoryPrototype ids referenced by the medical systems (oxygen-bar
/// organs, pulse source).</summary>
public static class OxydOrganCategories
{
    public static readonly ProtoId<OrganCategoryPrototype> Brain = "Brain";
    public static readonly ProtoId<OrganCategoryPrototype> Lungs = "Lungs";
    public static readonly ProtoId<OrganCategoryPrototype> Heart = "Heart";
}

/// <summary>Item sizes a retracted cavity accepts through the click path (Eris w_class check).</summary>
public static class OxydItemSizes
{
    public static readonly HashSet<ProtoId<ItemSizePrototype>> Implantable = new()
    {
        "Tiny", "Small",
    };
}
