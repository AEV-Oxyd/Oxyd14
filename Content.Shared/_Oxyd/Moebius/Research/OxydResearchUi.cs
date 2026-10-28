// Eris Moebius research port: BUI contract for the R&D console.
// Eris source: nano/templates/rdconsole.tmpl + code/modules/research/rdconsole.dm Topic().
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Moebius.Research;

[Serializable, NetSerializable]
public enum OxydRdUiKey : byte
{
    Key,
}

/// <summary>Eris rdconsole screens: main (analyzer card + trees summary),
/// protolathe, circuit_imprinter, tech_trees, disk_management, locked.</summary>
[Serializable, NetSerializable]
public enum OxydRdScreen : byte
{
    Main,
    Lathe,
    Trees,
    Disk,
}

[Serializable, NetSerializable]
public sealed class OxydRdDaInfo
{
    /// <summary>A destructive analyzer is linked, even when its input slot is empty.</summary>
    public bool Linked;
    public bool HasItem;
    public string ItemName = string.Empty;
    /// <summary>tree display name -> level the item would grant.</summary>
    public List<string> TechLines = new();
    public bool Busy;
}

[Serializable, NetSerializable]
public sealed class OxydRdDeviceInfo
{
    public NetEntity Uid;
    public string Name = string.Empty;
    public OxydRdDeviceKind Kind;
}

[Serializable, NetSerializable]
public sealed class OxydRdNodeInfo
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Desc = string.Empty;
    public int Cost;
    public float X;
    public float Y;
    /// <summary>Eris science.png icon state (oxydTechNode.iconHint).</summary>
    public string Icon = "rd";
    /// <summary>0 = unavailable (prereq missing), 1 = researchable, 2 = done.</summary>
    public int State;
    /// <summary>Names of missing prerequisite nodes.</summary>
    public List<string> Missing = new();
    public List<string> Requires = new();
    /// <summary>Every prerequisite, met or not, for the Eris side pane.</summary>
    public List<OxydRdReqInfo> Prereqs = new();
    /// <summary>Recipe names this node unlocks (ported designs only).</summary>
    public List<string> Unlocks = new();
}

[Serializable, NetSerializable]
public sealed class OxydRdReqInfo
{
    public string Name = string.Empty;
    public bool Met;
}

[Serializable, NetSerializable]
public sealed class OxydRdDesignInfo
{
    public string RecipeId = string.Empty;
    public string Name = string.Empty;
    /// <summary>"Steel 200" style shortage lines, empty when printable.</summary>
    public string Missing = string.Empty;
    public bool CanPrint;
}

[Serializable, NetSerializable]
public sealed class OxydRdState : BoundUserInterfaceState
{
    public OxydRdScreen Screen;
    public int Points;
    /// <summary>Tree id, name, level, bar color, shown flag.</summary>
    public List<OxydRdTreeInfo> Trees = new();
    public OxydRdDaInfo Da = new();
    public List<OxydRdDeviceInfo> Devices = new();
    /// <summary>Currently selected lathe device for the Lathe screen.</summary>
    public NetEntity? SelectedDevice;
    public List<OxydRdDesignInfo> Designs = new();
    /// <summary>Currently selected tree for the Trees screen.</summary>
    public string? SelectedTree;
    public List<OxydRdNodeInfo> Nodes = new();
    /// <summary>Disk slot state.</summary>
    public bool HasDisk;
    public bool DiskIsPointsDisk;
    public int DiskNodeCount;
    public int DiskDesignCount;
    public int DiskPoints;
    public bool DiskSpent;
    /// <summary>Science tool upload state (stored blocks ready to upload).</summary>
    public int ToolBlocks;
}

[Serializable, NetSerializable]
public sealed class OxydRdTreeInfo
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public int Level;
    public Color Color = Color.White;
    public bool Shown = true;
}

// ---- client -> server messages (Eris rdconsole Topic() commands) ----

[Serializable, NetSerializable]
public sealed class OxydRdScreenMessage : BoundUserInterfaceMessage
{
    public OxydRdScreen Screen;
}

[Serializable, NetSerializable]
public sealed class OxydRdSelectTreeMessage : BoundUserInterfaceMessage
{
    public string Tree = string.Empty;
}

[Serializable, NetSerializable]
public sealed class OxydRdSelectDeviceMessage : BoundUserInterfaceMessage
{
    public NetEntity Device;
}

[Serializable, NetSerializable]
public sealed class OxydRdUnlockMessage : BoundUserInterfaceMessage
{
    public string Node = string.Empty;
}

/// <summary>Eris "Deconstruct" on the main screen.</summary>
[Serializable, NetSerializable]
public sealed class OxydRdDeconstructMessage : BoundUserInterfaceMessage { }

/// <summary>Eris "Eject Item" on the analyzer card.</summary>
[Serializable, NetSerializable]
public sealed class OxydRdEjectItemMessage : BoundUserInterfaceMessage { }

/// <summary>Eris "Sync / rescan devices" button.</summary>
[Serializable, NetSerializable]
public sealed class OxydRdRescanMessage : BoundUserInterfaceMessage { }

/// <summary>Eris lathe design row "Build" link: queue a print on a linked device.</summary>
[Serializable, NetSerializable]
public sealed class OxydRdPrintMessage : BoundUserInterfaceMessage
{
    public string Recipe = string.Empty;
    public NetEntity Device;
    public int Amount = 1;
}

[Serializable, NetSerializable]
public enum OxydRdDiskOp : byte
{
    Eject,
    /// <summary>Eris "Load Data": disk -> console (techs + designs + points).</summary>
    Upload,
    /// <summary>Eris "Save Data": console -> disk.</summary>
    Download,
    /// <summary>Eris "Reset Disk".</summary>
    Clear,
}

[Serializable, NetSerializable]
public sealed class OxydRdDiskMessage : BoundUserInterfaceMessage
{
    public OxydRdDiskOp Op;
}

/// <summary>Eris science_tool upload on the main screen.</summary>
[Serializable, NetSerializable]
public sealed class OxydRdUploadToolMessage : BoundUserInterfaceMessage { }
