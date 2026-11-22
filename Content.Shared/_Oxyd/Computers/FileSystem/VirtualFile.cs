using System.Linq;
using Content.Shared.Access;
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
    bool IsReadOnly,
    TimeSpan LastModified = default
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
    [DataField] public TimeSpan LastModified = TimeSpan.Zero;

    public string FullPath => (Directory.EndsWith('/') ? Directory : Directory + "/") + Name + "." + Extension;

    public abstract VirtualFile Clone();

    protected void CopyBaseFields(VirtualFile target)
    {
        target.Name = Name;
        target.Extension = Extension;
        target.Directory = Directory;
        target.Size = Size;
        target.IsReadOnly = IsReadOnly;
        target.IsEncrypted = IsEncrypted;
        target.EncryptionKeyHash = EncryptionKeyHash;
        target.LastModified = LastModified;
    }
}

[Serializable, NetSerializable]
public sealed partial class TextVirtualFile : VirtualFile
{
    [DataField] public string TextContent = string.Empty;

    public TextVirtualFile()
    {
        Extension = "txt";
    }

    public override VirtualFile Clone()
    {
        var copy = new TextVirtualFile { TextContent = TextContent };
        CopyBaseFields(copy);
        return copy;
    }
}

[Serializable, NetSerializable]
public sealed partial class RecipeBlueprintFile : VirtualFile
{
    [DataField] public ProtoId<LatheRecipePrototype>? Recipe = null;
    [DataField] public List<ProtoId<LatheRecipePrototype>> Recipes = new();
    [DataField] public int? RemainingUses = null;
    [DataField] public bool DrmLocked = false;

    public RecipeBlueprintFile()
    {
        Extension = "lathe";
    }

    public override VirtualFile Clone()
    {
        var copy = new RecipeBlueprintFile
        {
            Recipe = Recipe,
            Recipes = new List<ProtoId<LatheRecipePrototype>>(Recipes),
            RemainingUses = RemainingUses,
            DrmLocked = DrmLocked
        };
        CopyBaseFields(copy);
        return copy;
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
        var recipes = GetAllRecipes().ToList();
        if (recipes.Count == 0)
            return 512;

        // 512 B header + 2 KB per recipe + 1 KB per DRM point cost so multi-blueprint files scale significantly larger.
        var totalBytes = 512;
        foreach (var r in recipes)
        {
            var pointCost = 0;
            if (proto.TryIndex<LatheRecipePrototype>(r, out var recipeProto) && recipeProto.pointUsage > 0)
                pointCost = recipeProto.pointUsage;

            totalBytes += 2048 + pointCost * 1024;
        }

        return totalBytes;
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

    public override VirtualFile Clone()
    {
        var copy = new DatabaseVirtualFile
        {
            TableName = TableName,
            Data = new Dictionary<string, string>(Data)
        };
        CopyBaseFields(copy);
        return copy;
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
        Extension = "nte";
        Directory = "/bin/";
        Size = 2048;
    }

    public override VirtualFile Clone()
    {
        var copy = new ProgramVirtualFile
        {
            ProgramId = ProgramId,
            Title = Title,
            CpuUsagePercent = CpuUsagePercent,
            RamUsageMb = RamUsageMb
        };
        CopyBaseFields(copy);
        return copy;
    }
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class TaskEntryData
{
    [DataField]
    public int Id;

    [DataField]
    public string Description = string.Empty;

    [DataField]
    public string TaskIsFor = string.Empty;

    [DataField]
    public bool IsTaskDone;

    [DataField]
    public byte Priority = 1;
}

/// <summary>
/// Boot-resistant persistent task checklist stored on hard drives under /tasks/*.tsk.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class TaskListVirtualFile : VirtualFile
{
    [DataField]
    public int NextId = 1;

    [DataField]
    public List<TaskEntryData> Tasks = new();

    public TaskListVirtualFile()
    {
        Extension = "tsk";
        Directory = "/tasks/";
        Size = 512;
    }

    public override VirtualFile Clone()
    {
        var copy = new TaskListVirtualFile
        {
            NextId = NextId,
            Tasks = Tasks.Select(t => new TaskEntryData
            {
                Id = t.Id,
                Description = t.Description,
                TaskIsFor = t.TaskIsFor,
                IsTaskDone = t.IsTaskDone,
                Priority = t.Priority
            }).ToList()
        };
        CopyBaseFields(copy);
        return copy;
    }
}

/// <summary>
/// Serialized ID card access codes built from an ID card via ID Access Encoder
/// and stored in the filesystem (default /keys/*.acc) for remote Terminal authorization.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class AccessCodesVirtualFile : VirtualFile
{
    [DataField]
    public string OwnerName = string.Empty;

    [DataField]
    public string JobTitle = string.Empty;

    [DataField]
    public List<ProtoId<AccessLevelPrototype>> AccessTags = new();

    public AccessCodesVirtualFile()
    {
        Extension = "acc";
        Directory = "/keys/";
        Size = 256;
    }

    public override VirtualFile Clone()
    {
        var copy = new AccessCodesVirtualFile
        {
            OwnerName = OwnerName,
            JobTitle = JobTitle,
            AccessTags = new List<ProtoId<AccessLevelPrototype>>(AccessTags)
        };
        CopyBaseFields(copy);
        return copy;
    }
}
