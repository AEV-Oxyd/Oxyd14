// Eris Moebius research port: prototype declarations.
// Eris source: code/modules/research/research.dm (/datum/tech, /datum/technology).
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Moebius.Research;

/// <summary>Eris /datum/tech: a research tree (engineering, biotech, ...).
/// Holds a display name/color and whether it shows in the console.</summary>
[Prototype("oxydTechTree")]
public sealed partial class OxydTechTreePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    /// <summary>LocId for the tree name.</summary>
    [DataField(required: true)] public LocId Name;

    /// <summary>Eris tech.shown: hidden trees (covert) don't appear in the console.</summary>
    [DataField] public bool Shown = true;

    /// <summary>UI accent color for the tree's level bar.</summary>
    [DataField] public Color Color = Color.White;
}

/// <summary>Eris /datum/technology: one unlockable research node.
/// Cost is paid in research points; prerequisites are other nodes.</summary>
[Prototype("oxydTechNode")]
public sealed partial class OxydTechNodePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    /// <summary>OxydTechTree this node belongs to (Eris tech_type).</summary>
    [DataField(required: true)] public ProtoId<OxydTechTreePrototype> Tree;

    [DataField(required: true)] public LocId Name;
    [DataField] public LocId? Description;

    /// <summary>Eris cost in research points.</summary>
    [DataField] public int Cost;

    /// <summary>Eris NanoUI canvas position (0..1 fractions).</summary>
    [DataField] public float PosX;
    [DataField] public float PosY;

    /// <summary>Eris science.png icon state drawn on the tech-tree node.</summary>
    [DataField] public string? IconHint;

    /// <summary>Eris required_technologies: other node ids that must be unlocked first.</summary>
    [DataField] public List<ProtoId<OxydTechNodePrototype>> RequiredNodes = new();

    /// <summary>Eris unlocks_designs: lathe recipes granted when this node unlocks.</summary>
    [DataField] public List<ProtoId<LatheRecipePrototype>> UnlocksRecipes = new();

    /// <summary>Whole lathe recipe packs granted when this node unlocks —
    /// used to hang stock SS14 packs (mech parts, borg modules) on Eris
    /// robotics nodes without re-authoring their recipes.</summary>
    [DataField] public List<ProtoId<LatheRecipePackPrototype>> UnlocksPacks = new();
}
