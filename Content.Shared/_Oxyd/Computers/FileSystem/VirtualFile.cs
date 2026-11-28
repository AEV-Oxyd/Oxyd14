using System.Linq;
using System.Text;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Access;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared.Research.Prototypes;

namespace Content.Shared._Oxyd.Computers.FileSystem;

/// <summary>
/// Canonical NTOS QuadByte (QB) formatting helpers (1 QB = 1024 bytes).
/// </summary>
public static class ComputerSizeFormat
{
    public const int BytesPerQb = 1024;

    public static string FormatStorageQb(int bytes)
    {
        if (bytes <= 0)
            return "0 QB";

        if (bytes % BytesPerQb == 0)
            return $"{bytes / BytesPerQb} QB";

        var qb = bytes / (float) BytesPerQb;
        return qb < 0.1f ? "0.1 QB" : $"{qb:0.#} QB";
    }

    public static string FormatRateQb(int bytesPerSec)
    {
        if (bytesPerSec <= 0)
            return "0 QB/s";

        if (bytesPerSec % BytesPerQb == 0)
            return $"{bytesPerSec / BytesPerQb} QB/s";

        var qbs = bytesPerSec / (float) BytesPerQb;
        return qbs < 0.1f ? "0.1 QB/s" : $"{qbs:0.#} QB/s";
    }

    public static string FormatRamQb(int ramUnits)
    {
        return $"{Math.Max(0, ramUnits)} QB";
    }
}

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

    public virtual string FileTypeTag => "VirtualFile";

    public virtual int CalculateRealSize(IPrototypeManager proto, IComponentFactory? compFactory = null) => Math.Max(16, Size);

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

    public override string FileTypeTag => nameof(TextVirtualFile);

    public override int CalculateRealSize(IPrototypeManager proto, IComponentFactory? compFactory = null)
        => Math.Max(16, Encoding.UTF8.GetByteCount(TextContent ?? string.Empty));

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

    public override string FileTypeTag => nameof(RecipeBlueprintFile);

    public override int CalculateRealSize(IPrototypeManager proto, IComponentFactory? compFactory = null) => CalculateSize(proto);

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

    public override string FileTypeTag => nameof(DatabaseVirtualFile);

    public override int CalculateRealSize(IPrototypeManager proto, IComponentFactory? compFactory = null)
    {
        var total = 64 + Encoding.UTF8.GetByteCount(TableName ?? string.Empty);
        foreach (var (k, v) in Data)
        {
            total += Encoding.UTF8.GetByteCount(k ?? string.Empty) + Encoding.UTF8.GetByteCount(v ?? string.Empty) + 8;
        }
        return Math.Max(64, total);
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

    public override string FileTypeTag => nameof(ProgramVirtualFile);

    public override int CalculateRealSize(IPrototypeManager proto, IComponentFactory? compFactory = null)
    {
        if (compFactory != null &&
            !string.IsNullOrEmpty(ProgramId) &&
            proto.TryIndex<EntityPrototype>(ProgramId, out var entProto) &&
            entProto.TryGetComponent<ProgramComponent>(out var progComp, compFactory) &&
            progComp.FileSizeBytes > 0)
        {
            return progComp.FileSizeBytes;
        }

        return Math.Max(512, Size);
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

    public override string FileTypeTag => nameof(TaskListVirtualFile);

    public override int CalculateRealSize(IPrototypeManager proto, IComponentFactory? compFactory = null)
    {
        var total = 64;
        foreach (var t in Tasks)
        {
            total += 24 + Encoding.UTF8.GetByteCount(t.Description ?? string.Empty) + Encoding.UTF8.GetByteCount(t.TaskIsFor ?? string.Empty);
        }
        return Math.Max(128, total);
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

    public override string FileTypeTag => nameof(AccessCodesVirtualFile);

    public override int CalculateRealSize(IPrototypeManager proto, IComponentFactory? compFactory = null)
    {
        var total = 64 + Encoding.UTF8.GetByteCount(OwnerName ?? string.Empty) + Encoding.UTF8.GetByteCount(JobTitle ?? string.Empty);
        foreach (var tag in AccessTags)
            total += Encoding.UTF8.GetByteCount(tag.Id) + 4;
        return Math.Max(128, total);
    }

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

/// <summary>
/// Key-based password file stored under /keys/*.key.
/// Can be bound to a computer's Security Lock and copied to removable disks or remote hosts
/// to authenticate locally on the lock screen or over SSH without typing a passphrase.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class PasswordKeyVirtualFile : VirtualFile
{
    [DataField] public string KeyLabel = "System Key";
    [DataField] public string KeyId = string.Empty;
    [DataField] public string KeySecret = string.Empty;
    [DataField] public string TargetGuid = string.Empty;

    public PasswordKeyVirtualFile()
    {
        Extension = "key";
        Directory = "/keys/";
        Size = 512;
    }

    public override string FileTypeTag => nameof(PasswordKeyVirtualFile);

    public override int CalculateRealSize(IPrototypeManager proto, IComponentFactory? compFactory = null)
    {
        var total = 128 +
                    Encoding.UTF8.GetByteCount(KeyLabel ?? string.Empty) +
                    Encoding.UTF8.GetByteCount(KeyId ?? string.Empty) +
                    Encoding.UTF8.GetByteCount(KeySecret ?? string.Empty) +
                    Encoding.UTF8.GetByteCount(TargetGuid ?? string.Empty);
        return Math.Max(256, total);
    }

    public override VirtualFile Clone()
    {
        var copy = new PasswordKeyVirtualFile
        {
            KeyLabel = KeyLabel,
            KeyId = KeyId,
            KeySecret = KeySecret,
            TargetGuid = TargetGuid
        };
        CopyBaseFields(copy);
        return copy;
    }
}
