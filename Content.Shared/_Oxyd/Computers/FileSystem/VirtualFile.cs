using System.Linq;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared.Research.Prototypes;

namespace Content.Shared._Oxyd.Computers.FileSystem;

[Serializable, NetSerializable]
public record struct FileHeader(
    string Path,
    string Name,
    string Extension,
    int Size,
    string MimeType,
    bool IsEncrypted,
    bool IsReadOnly
);

[ImplicitDataDefinitionForInheritors, Serializable, NetSerializable]
public abstract partial class VirtualFile
{
    [DataField] public string Name = "file";
    [DataField] public string Extension = "txt";
    [DataField] public string Directory = "/";
    [DataField] public int Size = 10;
    [DataField] public bool IsReadOnly = false;
    [DataField] public bool IsEncrypted = false;
    [DataField] public string? EncryptionKeyHash = null;

    public string FullPath => (Directory.EndsWith('/') ? Directory : Directory + "/") + Name + "." + Extension;
}

[Serializable, NetSerializable]
public sealed partial class TextVirtualFile : VirtualFile
{
    [DataField] public string TextContent = string.Empty;

    public TextVirtualFile()
    {
        Extension = "txt";
    }
}

[Serializable, NetSerializable]
public sealed partial class RecipeBlueprintFile : VirtualFile
{
    [DataField] public ProtoId<LatheRecipePrototype>? Recipe = null;
    [DataField] public List<ProtoId<LatheRecipePrototype>> Recipes = new();
    [DataField] public int? RemainingUses = null;
    [DataField] public bool DrmLocked = true;

    public RecipeBlueprintFile()
    {
        Extension = "lathe";
    }

    public IEnumerable<ProtoId<LatheRecipePrototype>> GetAllRecipes()
    {
        if (Recipe != null && !string.IsNullOrEmpty(Recipe.Value.Id))
            yield return Recipe.Value;

        foreach (var r in Recipes)
        {
            if (!string.IsNullOrEmpty(r.Id) && (Recipe == null || r != Recipe.Value))
                yield return r;
        }
    }

    public int CalculateSize(IPrototypeManager proto)
    {
        if (Size > 0)
            return Size;

        var recipes = GetAllRecipes().ToList();
        if (recipes.Count == 0)
            return 1024;

        var totalPoints = 0;
        foreach (var r in recipes)
        {
            if (proto.TryIndex<LatheRecipePrototype>(r, out var recipeProto) && recipeProto.pointUsage > 0)
            {
                totalPoints += recipeProto.pointUsage;
            }
            else
            {
                totalPoints += 1;
            }
        }

        // Dynamic size: number of designs multiplied by points cost in KB
        return Math.Max(1024, recipes.Count * totalPoints * 1024);
    }
}

[Serializable, NetSerializable]
public sealed partial class DatabaseVirtualFile : VirtualFile
{
    [DataField] public string TableName = "default";
    [DataField] public Dictionary<string, string> Data = new();

    public DatabaseVirtualFile()
    {
        Extension = "db";
    }
}

[Serializable, NetSerializable]
public sealed partial class ProgramVirtualFile : VirtualFile
{
    [DataField] public string ProgramId = string.Empty;
    [DataField] public string Title = string.Empty;
    [DataField] public float CpuUsagePercent = 4.0f;
    [DataField] public int RamUsageMb = 8;

    public ProgramVirtualFile()
    {
        Extension = "bin";
        Directory = "/bin/";
        Size = 2048;
    }
}
