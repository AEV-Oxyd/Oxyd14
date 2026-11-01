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
    [DataField] public ProtoId<LatheRecipePrototype> Recipe = default!;
    [DataField] public int? RemainingUses = null;
    [DataField] public bool DrmLocked = true;

    public RecipeBlueprintFile()
    {
        Extension = "lathe";
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
