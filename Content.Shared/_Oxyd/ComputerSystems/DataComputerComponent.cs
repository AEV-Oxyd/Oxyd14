using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd;

[Flags]
public enum ProgramType
{
    Text = 0<<0, // plain doc
    Executable = 1<<0, // runnable program
    LatheData = 1<<1, // data for printing an item
    
}

public class CompData
{
    public float size = 1f;
    public string name = "Program";
    public ProgramType ftype = ProgramType.Text;
}

public class CompDataExecutable : CompData
{
    public float ramuse = 1f;
    public float cpuuse = 1f;
    public Enum uiKey = null!;
    public Dictionary<string, object> data = new();
}

public class CompDataText : CompData
{
    public string text = "";
}

public class CompDataLathe : CompData
{
    public int? uses = null;
    public List<ProtoId<LatheRecipePrototype>> recipes = new();
}
/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class DataComputerComponent : Component
{
    
}