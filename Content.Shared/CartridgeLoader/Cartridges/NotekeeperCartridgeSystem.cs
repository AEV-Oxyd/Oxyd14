using System.Linq;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Content.Shared._Oxyd.Computers.FileSystem;

namespace Content.Shared.CartridgeLoader.Cartridges;

public sealed partial class NotekeeperCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoaderSystem = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private readonly SharedPdaSystem _pdaSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NotekeeperCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
        SubscribeLocalEvent<NotekeeperCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
    }

    /// <summary>
    /// This gets called when the ui fragment needs to be updated for the first time after activating
    /// </summary>
    private void OnUiReady(EntityUid uid, NotekeeperCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        var loaderUid = args.Loader;
        if (component.Notes.Count == 0 && TryComp<FileSystemComponent>(loaderUid, out var fsComp))
        {
            if (_fileSystem.TryGetFile<TextVirtualFile>(loaderUid, "/notes/shift_notes.txt", out var noteFile, fsComp) ||
                _fileSystem.TryGetFile<TextVirtualFile>(loaderUid, "/notes/notes.txt", out noteFile, fsComp))
            {
                if (!string.IsNullOrWhiteSpace(noteFile.TextContent))
                {
                    component.Notes = noteFile.TextContent.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
                    Dirty(uid, component);
                }
            }
        }

        SyncNotesToFile(loaderUid, component.Notes);
        UpdateUiState(uid, loaderUid, component);
        _pdaSystem.UpdatePdaUi(loaderUid);
    }

    /// <summary>
    /// The ui messages received here get wrapped by a CartridgeMessageEvent and are relayed from the <see cref="CartridgeLoaderSystem"/>
    /// </summary>
    private void OnUiMessage(EntityUid uid, NotekeeperCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not NotekeeperUiMessageEvent message)
            return;

        var loaderUid = GetEntity(args.LoaderUid);

        if (message.Action == NotekeeperUiAction.Add)
        {
            component.Notes.Add(message.Note);
            _adminLogger.Add(LogType.PdaInteract, LogImpact.Low,
                $"{ToPrettyString(args.Actor)} added a note to PDA: '{message.Note}' contained on: {ToPrettyString(uid)}");
        }
        else
        {
            component.Notes.Remove(message.Note);
            _adminLogger.Add(LogType.PdaInteract, LogImpact.Low,
                $"{ToPrettyString(args.Actor)} removed a note from PDA: '{message.Note}' was contained on: {ToPrettyString(uid)}");
        }

        Dirty(uid, component);
        SyncNotesToFile(loaderUid, component.Notes);
        UpdateUiState(uid, loaderUid, component);
        _pdaSystem.UpdatePdaUi(loaderUid);
    }

    private void SyncNotesToFile(EntityUid loaderUid, List<string> notes)
    {
        if (!TryComp<FileSystemComponent>(loaderUid, out var fsComp))
            return;

        var content = string.Join("\n", notes);
        var notePath = "/notes/shift_notes.txt";
        if (!_fileSystem.TryGetFile<TextVirtualFile>(loaderUid, notePath, out var noteFile, fsComp))
        {
            notePath = "/notes/notes.txt";
            _fileSystem.TryGetFile<TextVirtualFile>(loaderUid, notePath, out noteFile, fsComp);
        }

        // Variable size directly reflects the notes text length and note count in memory
        var dynamicSize = Math.Max(32, 32 + content.Length + (notes.Count * 16));

        if (noteFile != null)
        {
            var updatedFile = new TextVirtualFile
            {
                Name = noteFile.Name,
                Extension = noteFile.Extension,
                Directory = noteFile.Directory,
                Size = dynamicSize,
                TextContent = content,
                IsReadOnly = noteFile.IsReadOnly
            };
            _fileSystem.TryWriteFile(loaderUid, updatedFile, fsComp);
        }
        else
        {
            var newFile = new TextVirtualFile
            {
                Name = "shift_notes",
                Extension = "txt",
                Directory = "/notes/",
                Size = dynamicSize,
                TextContent = content,
                IsReadOnly = false
            };
            _fileSystem.TryWriteFile(loaderUid, newFile, fsComp);
        }
    }

    private void UpdateUiState(EntityUid uid, EntityUid loaderUid, NotekeeperCartridgeComponent? component)
    {
        if (!Resolve(uid, ref component))
            return;

        var state = new NotekeeperUiState(component.Notes);
        _cartridgeLoaderSystem.UpdateCartridgeUiState(loaderUid, state);
    }
}
