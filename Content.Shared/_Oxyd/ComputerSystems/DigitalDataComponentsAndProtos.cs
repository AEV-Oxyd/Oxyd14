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

[ImplicitDataDefinitionForInheritors]
public partial class DigitalData
{
    [DataField]
    public float size = 1f;
    [DataField]
    public string name = "Program";
    [DataField]
    public ProgramType ftype = ProgramType.Text;
}

public partial class DigitalDataExecutable : DigitalData
{
    [DataField]
    public float ramuse = 1f;
    [DataField]
    public float cpuuse = 1f;
    
    public Enum uiKey = null!;
    public Dictionary<string, object> data = new();
}

public partial class DigitalDataText : DigitalData
{
    [DataField]
    public string text = "";
}

public partial class DigitalDataLathe : DigitalData
{
    [DataField]
    public int? uses = null;
    [DataField]
    public HashSet<ProtoId<LatheRecipePrototype>> recipes = new();
}

/// <summary>
///  Whilst the sane programmer would choose to use Entities for this
///  I require data-hiding and fine-grained control over what gets networked
///  Not to mention Entity-Polution being a real concern SPCR 2026
/// </summary>
[RegisterComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class DigitalDataHolderComponent : Component
{
    /// <summary>
    /// Server-side , true list of all files
    /// Client-side, reconstructed from networked files.
    /// </summary>
    [DataField(required:false)]
    public HashSet<DigitalData> files = new();
    [AutoNetworkedField, ViewVariables]
    public HashSet<DigitalData> networkedFiles = new();
    
    public IEnumerable<T> getFileByData<T>() where T : DigitalData
    {
        foreach (var file in files)
        {
            if (file is T data)
            {
                yield return data;
            }
        }
    }
}