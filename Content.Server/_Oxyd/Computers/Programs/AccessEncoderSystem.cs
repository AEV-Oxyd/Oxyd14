using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.PDA;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Computers.Programs;

/// <summary>
/// Server system for /bin/access_encoder.nte (ID Access Encoder).
/// Reads ID card credentials strictly from an ID Card Slot hardware module (or host PDA ID slot)
/// and encodes / deletes /keys/*.acc files. Does not modify ID cards.
/// </summary>
public sealed partial class AccessEncoderSystem : EntitySystem
{
    [Dependency] private OxydComputerSystem _computer = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private ComputerCardReaderSystem _cardReaders = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    [Dependency] private EntityQuery<ProgramComponent> _programQuery = default!;
    [Dependency] private EntityQuery<OxydComputerComponent> _computerQuery = default!;
    [Dependency] private EntityQuery<IdCardComponent> _idCardQuery = default!;
    [Dependency] private EntityQuery<PdaComponent> _pdaQuery = default!;

    private const string MissingSlotHardwareMessage =
        "No ID Card Slot hardware installed (0/1). Install a modular ID card slot to read IDs.";

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<AccessEncoderProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<AccessEncoderActionMessage>(OnUiAction);
        });
    }

    [SubscribeLocalEvent]
    private void OnStartup(Entity<AccessEncoderProgramComponent> ent, ref ProgramStartupEvent args)
    {
        if (TryGetEncoderSlotCard(args.ComputerUid, out var card, out _))
            PopulateFromCard(ent.Comp, card);

        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnActivated(Entity<AccessEncoderProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        if (TryGetEncoderSlotCard(args.ComputerUid, out var card, out _) && string.IsNullOrWhiteSpace(ent.Comp.LastCardName))
            PopulateFromCard(ent.Comp, card);

        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnDeviceLink(Entity<AccessEncoderProgramComponent> ent, ref ProgramDeviceLinkReceivedEvent args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        UpdateUi(ent.Owner, compUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnHardwareChanged(Entity<AccessEncoderProgramComponent> ent, ref ComputerHardwareChangedEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnCardReaderChanged(Entity<AccessEncoderProgramComponent> ent, ref ComputerCardReaderChangedEvent args)
    {
        if (TryGetEncoderSlotCard(args.ComputerUid, out var card, out _))
            PopulateFromCard(ent.Comp, card);

        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnHostContainerInserted(Entity<EmbeddedComputerHostComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        RefreshHostEncoderPrograms(ent, cardInserted: true);
    }

    [SubscribeLocalEvent]
    private void OnHostContainerRemoved(Entity<EmbeddedComputerHostComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        RefreshHostEncoderPrograms(ent, cardInserted: false);
    }

    [SubscribeLocalEvent]
    private void OnCardScanned(Entity<AccessEncoderProgramComponent> ent, ref ProgramCardScannedEvent scanned)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!HasEncoderSlotHardware(compUid))
        {
            ent.Comp.StatusMessage = MissingSlotHardwareMessage;
            UpdateUi(ent.Owner, compUid, ent.Comp);
            return;
        }

        // Reject transient swipes on workstations when only a scanner module is used; require physical ID slot retention.
        if (!scanned.Retained && !IsHostPda(compUid))
        {
            ent.Comp.StatusMessage = "Insert the ID card into the ID Card Slot module to read credentials.";
            UpdateUi(ent.Owner, compUid, ent.Comp);
            return;
        }

        if (!_idCardQuery.HasComp(scanned.Card))
            return;

        PopulateFromCard(ent.Comp, scanned.Card);
        ent.Comp.StatusMessage = $"Read ID Slot card ({ent.Comp.LastCardName}): {ent.Comp.LastCardAccessTags.Count} access codes.";
        UpdateUi(ent.Owner, compUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnBuiOpened(Entity<AccessEncoderProgramComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (TryGetEncoderSlotCard(compUid, out var card, out _) && string.IsNullOrWhiteSpace(ent.Comp.LastCardName))
            PopulateFromCard(ent.Comp, card);

        UpdateUi(ent.Owner, compUid, ent.Comp);
    }

    private void OnUiAction(Entity<AccessEncoderProgramComponent> ent, ref AccessEncoderActionMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        switch (args.Action)
        {
            case AccessEncoderAction.Refresh:
                if (TryGetEncoderSlotCard(compUid, out var refreshCard, out _))
                    PopulateFromCard(ent.Comp, refreshCard);
                break;

            case AccessEncoderAction.ToggleCardSlot:
                HandleToggleSlot(ent.Comp, compUid, args.Actor);
                break;

            case AccessEncoderAction.ReadSlotCard:
                if (!HasEncoderSlotHardware(compUid))
                {
                    ent.Comp.StatusMessage = MissingSlotHardwareMessage;
                }
                else if (TryGetEncoderSlotCard(compUid, out var slotCard, out _))
                {
                    PopulateFromCard(ent.Comp, slotCard);
                    ent.Comp.StatusMessage = $"Read ID Slot ({ent.Comp.LastCardName}): {ent.Comp.LastCardAccessTags.Count} access codes.";
                }
                else
                {
                    ent.Comp.StatusMessage = "ID slot is empty. Insert an ID card first.";
                }
                break;

            case AccessEncoderAction.ClearScanned:
                ent.Comp.LastCardName = null;
                ent.Comp.LastCardJobTitle = null;
                ent.Comp.LastCardAccessTags.Clear();
                ent.Comp.StatusMessage = "Cleared read buffer.";
                break;

            case AccessEncoderAction.BuildSaveFile:
                HandleBuildSaveFile(ent.Comp, compUid, args.Argument);
                break;

            case AccessEncoderAction.SelectFile:
                ent.Comp.SelectedFilePath = args.Argument.Trim();
                ent.Comp.StatusMessage = $"Selected {ent.Comp.SelectedFilePath}";
                break;

            case AccessEncoderAction.DeleteFile:
                if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) &&
                    _fileSystem.TryDeleteFile(driveUid.Value, args.Argument.Trim(), fs))
                {
                    if (string.Equals(ent.Comp.SelectedFilePath, args.Argument.Trim(), StringComparison.OrdinalIgnoreCase))
                        ent.Comp.SelectedFilePath = null;
                    ent.Comp.StatusMessage = $"Deleted {args.Argument.Trim()}";
                }
                else
                {
                    ent.Comp.StatusMessage = $"Could not delete '{args.Argument}'.";
                }
                break;
        }

        UpdateUi(ent.Owner, compUid, ent.Comp);
    }

    private void RefreshHostEncoderPrograms(Entity<EmbeddedComputerHostComponent> host, bool cardInserted)
    {
        if (host.Comp.ComputerEntity is not { } compUid || !_computerQuery.TryComp(compUid, out var comp))
            return;

        foreach (var progUid in comp.RunningPrograms)
        {
            if (TryComp<AccessEncoderProgramComponent>(progUid, out var enc))
            {
                if (cardInserted && TryGetEncoderSlotCard(compUid, out var card, out _))
                    PopulateFromCard(enc, card);
                UpdateUi(progUid, compUid, enc);
            }
        }
    }

    private void HandleToggleSlot(AccessEncoderProgramComponent comp, EntityUid compUid, EntityUid actor)
    {
        var slotReaders = _cardReaders.GetSlotReaders(compUid);
        if (slotReaders.Count == 0)
        {
            comp.StatusMessage = IsHostPda(compUid)
                ? "Insert or remove your ID directly in the PDA's physical ID slot."
                : MissingSlotHardwareMessage;
            return;
        }

        var slot = slotReaders[0];
        if (_cardReaders.TryGetSlotCard(compUid, 0, out _))
        {
            if (_cardReaders.TryEjectCard(slot, actor, GetDropNear(compUid)))
                comp.StatusMessage = "Ejected ID card from slot.";
            return;
        }

        if (!_hands.TryGetActiveItem(actor, out var held) || !_idCardQuery.HasComp(held.Value))
        {
            comp.StatusMessage = "Hold an ID card in your active hand to insert it.";
            return;
        }

        if (_cardReaders.TryInsertCard(slot, held.Value, actor))
        {
            PopulateFromCard(comp, held.Value);
            comp.StatusMessage = $"Inserted {MetaData(held.Value).EntityName} into ID Slot ({comp.LastCardAccessTags.Count} codes).";
        }
        else
        {
            comp.StatusMessage = "Unable to insert ID card into slot.";
        }
    }

    private void HandleBuildSaveFile(AccessEncoderProgramComponent comp, EntityUid compUid, string requestedName)
    {
        if (!HasEncoderSlotHardware(compUid))
        {
            comp.StatusMessage = MissingSlotHardwareMessage;
            return;
        }

        if (string.IsNullOrWhiteSpace(comp.LastCardName))
        {
            if (!TryGetEncoderSlotCard(compUid, out var card, out _))
            {
                comp.StatusMessage = "Insert an ID card into the ID slot before encoding a .acc file.";
                return;
            }

            PopulateFromCard(comp, card);
        }

        if (!_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
        {
            comp.StatusMessage = "No hard drive installed to store /keys/*.acc files.";
            return;
        }

        var baseName = IdCardProgramHelpers.SanitizeFileName(string.IsNullOrWhiteSpace(requestedName) ? comp.LastCardName : requestedName);
        var accFile = new AccessCodesVirtualFile
        {
            Name = baseName,
            Extension = "acc",
            Directory = AccessEncoderProgramComponent.DefaultDirectory,
            OwnerName = comp.LastCardName!,
            JobTitle = comp.LastCardJobTitle ?? string.Empty,
            AccessTags = new List<ProtoId<AccessLevelPrototype>>(comp.LastCardAccessTags),
            Size = Math.Max(128, comp.LastCardAccessTags.Count * 32)
        };

        if (!_fileSystem.TryWriteFile(driveUid.Value, accFile, fs))
        {
            comp.StatusMessage = "Failed to save .acc file: hard drive full or read-only.";
            return;
        }

        comp.SelectedFilePath = accFile.FullPath;
        _fileSystem.LogSystemActivity(driveUid.Value, "ACCESS-ENC", $"Encoded {accFile.FullPath} ({accFile.AccessTags.Count} tags)");
        comp.StatusMessage = $"Saved {accFile.FullPath} ({accFile.AccessTags.Count} access codes).";
    }

    private bool HasEncoderSlotHardware(EntityUid compUid)
    {
        return _cardReaders.GetSlotReaders(compUid).Count >= 1 || IsHostPda(compUid);
    }

    private bool IsHostPda(EntityUid compUid)
    {
        return _computerQuery.TryComp(compUid, out var comp) &&
               comp.HostMachine is { } hostUid &&
               _pdaQuery.HasComp(hostUid);
    }

    private bool TryGetEncoderSlotCard(EntityUid compUid, out EntityUid cardUid, out bool isModularSlot)
    {
        cardUid = EntityUid.Invalid;
        isModularSlot = false;

        if (_cardReaders.TryGetSlotCard(compUid, 0, out var slotCard) && _idCardQuery.HasComp(slotCard))
        {
            cardUid = slotCard;
            isModularSlot = true;
            return true;
        }

        if (IsHostPda(compUid) && _computer.TryGetInsertedIdCard(compUid, out var pdaCard) && _idCardQuery.HasComp(pdaCard))
        {
            cardUid = pdaCard;
            return true;
        }

        return false;
    }

    private EntityUid? GetDropNear(EntityUid compUid)
    {
        return _computerQuery.TryComp(compUid, out var c) && c.HostMachine is { } h ? h : compUid;
    }

    private void PopulateFromCard(AccessEncoderProgramComponent comp, EntityUid cardUid)
    {
        var (ownerName, jobTitle, tags) = IdCardProgramHelpers.ReadCardCredentials(cardUid, _idCardQuery, _access, EntityManager);
        comp.LastCardName = ownerName;
        comp.LastCardJobTitle = jobTitle;
        comp.LastCardAccessTags = tags;
    }

    public void UpdateUi(EntityUid progUid, EntityUid compUid, AccessEncoderProgramComponent comp)
    {
        if (!_ui.HasUi(progUid, ProgramUiKey.Key))
            return;

        var slotReaders = _cardReaders.GetSlotReaders(compUid);
        var hasModularSlot = slotReaders.Count >= 1;
        var hasSlotHardware = hasModularSlot || IsHostPda(compUid);
        var slotOccupied = TryGetEncoderSlotCard(compUid, out var insertedCard, out _);
        var slotCardLabel = slotOccupied ? MetaData(insertedCard).EntityName : string.Empty;

        var keyFiles = IdCardProgramHelpers.CollectAccFiles(compUid, _itemSlots, _fileSystem, EntityManager, out var hddFiles, out _);
        var selectedPath = comp.SelectedFilePath ?? string.Empty;
        if (string.IsNullOrEmpty(selectedPath) && keyFiles.Count > 0)
        {
            selectedPath = keyFiles[0];
            comp.SelectedFilePath = selectedPath;
        }

        var preview = string.Empty;
        if (!string.IsNullOrEmpty(selectedPath) &&
            IdCardProgramHelpers.TryGetAccFile(compUid, selectedPath, hddFiles, _itemSlots, _fileSystem, EntityManager, out var accFile))
        {
            var tagLine = accFile.AccessTags.Count > 0
                ? string.Join(", ", accFile.AccessTags.Select(t => t.Id))
                : "(no access codes)";
            preview = $"File: {selectedPath}\nHolder: {accFile.OwnerName} ({accFile.JobTitle})\nCodes ({accFile.AccessTags.Count}): {tagLine}";
        }

        var state = new AccessEncoderUiState(
            hasSlotHardware,
            hasModularSlot,
            slotOccupied,
            slotCardLabel,
            comp.LastCardName ?? string.Empty,
            comp.LastCardJobTitle ?? string.Empty,
            comp.LastCardAccessTags.Select(t => t.Id).ToList(),
            selectedPath,
            preview,
            keyFiles,
            hasSlotHardware ? comp.StatusMessage : MissingSlotHardwareMessage);

        _ui.SetUiState(progUid, ProgramUiKey.Key, state);
    }
}

/// <summary>
/// Shared internal helpers for reading ID cards and /keys/*.acc files across
/// Access Encoder and ID Modifier without duplicating filesystem logic.
/// </summary>
internal static class IdCardProgramHelpers
{
    public static (string OwnerName, string JobTitle, List<ProtoId<AccessLevelPrototype>> Tags) ReadCardCredentials(
        EntityUid cardUid,
        EntityQuery<IdCardComponent> idCardQuery,
        AccessReaderSystem access,
        IEntityManager entMan)
    {
        idCardQuery.TryComp(cardUid, out var idCard);
        var owner = !string.IsNullOrWhiteSpace(idCard?.FullName)
            ? idCard.FullName!
            : entMan.GetComponent<MetaDataComponent>(cardUid).EntityName;
        var job = idCard?.LocalizedJobTitle ?? string.Empty;
        var tags = access.FindAccessTags(cardUid)
            .OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return (owner, job, tags);
    }

    public static List<string> CollectAccFiles(
        EntityUid compUid,
        ItemSlotsSystem itemSlots,
        SharedFileSystemSystem fileSystem,
        IEntityManager entMan,
        out Dictionary<string, AccessCodesVirtualFile> filesByPath,
        out EntityUid? hddDriveUid)
    {
        filesByPath = new Dictionary<string, AccessCodesVirtualFile>(StringComparer.OrdinalIgnoreCase);
        hddDriveUid = null;

        if (fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) && driveUid != null && fs != null)
        {
            hddDriveUid = driveUid;
            foreach (var acc in fileSystem.GetFilesByType<AccessCodesVirtualFile>(driveUid.Value, fs))
                filesByPath[acc.FullPath] = acc;
        }

        if (itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotDiskModule, out var diskSlot) &&
            diskSlot.Item is { Valid: true } diskUid &&
            entMan.TryGetComponent<FileSystemComponent>(diskUid, out var diskFs))
        {
            foreach (var acc in fileSystem.GetFilesByType<AccessCodesVirtualFile>(diskUid, diskFs))
            {
                if (!filesByPath.ContainsKey(acc.FullPath))
                    filesByPath[acc.FullPath] = acc;
            }
        }

        return filesByPath.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool TryGetAccFile(
        EntityUid compUid,
        string requestedPath,
        Dictionary<string, AccessCodesVirtualFile> cachedFiles,
        ItemSlotsSystem itemSlots,
        SharedFileSystemSystem fileSystem,
        IEntityManager entMan,
        [NotNullWhen(true)] out AccessCodesVirtualFile? accFile)
    {
        if (cachedFiles.TryGetValue(requestedPath, out accFile))
            return true;

        var normalized = requestedPath.Trim();
        if (!normalized.StartsWith('/'))
            normalized = AccessEncoderProgramComponent.DefaultDirectory + normalized;
        if (!normalized.EndsWith(".acc", StringComparison.OrdinalIgnoreCase))
            normalized += ".acc";

        if (cachedFiles.TryGetValue(normalized, out accFile))
            return true;

        if (fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) &&
            driveUid != null &&
            fs != null &&
            fileSystem.TryGetFile(driveUid.Value, normalized, out accFile, fs))
        {
            return true;
        }

        if (itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotDiskModule, out var diskSlot) &&
            diskSlot.Item is { Valid: true } diskUid &&
            entMan.TryGetComponent<FileSystemComponent>(diskUid, out var diskFs) &&
            fileSystem.TryGetFile(diskUid, normalized, out accFile, diskFs))
        {
            return true;
        }

        accFile = null;
        return false;
    }

    public static string SanitizeFileName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "id_key";

        var clean = new string(raw.Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_')
            .ToArray())
            .Trim('_');

        if (clean.EndsWith("_acc", StringComparison.OrdinalIgnoreCase))
            clean = clean[..^4];
        if (clean.EndsWith(".acc", StringComparison.OrdinalIgnoreCase))
            clean = clean[..^4];

        return string.IsNullOrWhiteSpace(clean) ? "id_key" : clean;
    }
}
