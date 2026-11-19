using System.Linq;
using System.Text;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Interaction;
using Content.Shared.PDA;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Computers.Programs;

/// <summary>
/// Server logic for the ID Access Encoder app (/bin/access_encoder.nte).
/// Extracts access codes from an inserted ID card (PDA/computer slot) or an ID card
/// swiped on the computer, and persists them as .acc files in /keys/ on the filesystem.
/// </summary>
public sealed partial class AccessEncoderSystem : EntitySystem
{
    [Dependency] private OxydComputerSystem _computer = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    [Dependency] private EntityQuery<ProgramComponent> _programQuery = default!;
    [Dependency] private EntityQuery<IdCardComponent> _idCardQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<AccessEncoderProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnBuiOpened);
            subs.Event<AccessEncoderActionMessage>(OnUiAction);
        });
    }

    [SubscribeLocalEvent]
    private void OnStartup(Entity<AccessEncoderProgramComponent> ent, ref ProgramStartupEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnActivated(Entity<AccessEncoderProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnDeviceLink(Entity<AccessEncoderProgramComponent> ent, ref ProgramDeviceLinkReceivedEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnRelayedInteractUsing(Entity<AccessEncoderProgramComponent> ent, ref ProgramRelayedEvent<InteractUsingEvent> relayed)
    {
        if (relayed.Args.Handled)
            return;

        if (!TryExtractSwipedCard(relayed.Args.Used, out var cardUid))
            return;

        var (ownerName, jobTitle, tags) = ReadCardCredentials(cardUid);
        ent.Comp.HasScannedCard = true;
        ent.Comp.ScannedSourceLabel = $"Swiped ID ({MetaData(cardUid).EntityName})";
        ent.Comp.ScannedOwnerName = ownerName;
        ent.Comp.ScannedJobTitle = jobTitle;
        ent.Comp.ScannedAccessTags = tags;
        ent.Comp.StatusMessage = $"Scanned {tags.Count} access code(s) from {ownerName}. Click Build & Save (.acc) to persist to /keys/.";
        Dirty(ent);

        _audio.PlayPvs(ent.Comp.SoundScan, relayed.Args.Target);
        _popup.PopupEntity($"Scanned ID card ({tags.Count} access codes).", relayed.Args.Target, relayed.Args.User);
        relayed.Args.Handled = true;

        UpdateUi(ent.Owner, relayed.ComputerUid, ent.Comp);
    }

    private void OnBuiOpened(Entity<AccessEncoderProgramComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        UpdateUi(ent.Owner, compUid, ent.Comp);
    }

    private void OnUiAction(Entity<AccessEncoderProgramComponent> ent, ref AccessEncoderActionMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        switch (args.Action)
        {
            case AccessEncoderAction.Refresh:
                ent.Comp.StatusMessage = "Refreshed ID slot and /keys/ filesystem.";
                break;

            case AccessEncoderAction.ClearScanned:
                ent.Comp.HasScannedCard = false;
                ent.Comp.ScannedSourceLabel = string.Empty;
                ent.Comp.ScannedOwnerName = string.Empty;
                ent.Comp.ScannedJobTitle = string.Empty;
                ent.Comp.ScannedAccessTags.Clear();
                ent.Comp.StatusMessage = "Cleared swiped card buffer.";
                break;

            case AccessEncoderAction.SelectFile:
                ent.Comp.SelectedFilePath = string.IsNullOrWhiteSpace(args.Argument) ? null : args.Argument;
                break;

            case AccessEncoderAction.DeleteFile:
                if (_fileSystem.TryFindFileSystem(compUid, out var delDrive, out var delFs) &&
                    !string.IsNullOrWhiteSpace(args.Argument))
                {
                    if (_fileSystem.TryDeleteFile(delDrive.Value, args.Argument, delFs))
                    {
                        if (string.Equals(ent.Comp.SelectedFilePath, args.Argument, StringComparison.OrdinalIgnoreCase))
                            ent.Comp.SelectedFilePath = null;
                        ent.Comp.StatusMessage = $"Deleted '{args.Argument}'.";
                    }
                    else
                    {
                        ent.Comp.StatusMessage = $"Could not delete '{args.Argument}'.";
                    }
                }
                break;

            case AccessEncoderAction.BuildSaveFile:
                HandleBuildSaveFile(ent.Comp, compUid, args.Argument);
                break;
        }

        Dirty(ent);
        UpdateUi(ent.Owner, compUid, ent.Comp);
    }

    private void HandleBuildSaveFile(AccessEncoderProgramComponent comp, EntityUid compUid, string requestedName)
    {
        if (!_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
        {
            comp.StatusMessage = "Save failed: No hard drive installed.";
            return;
        }

        if (!TryResolveActiveSource(compUid, comp, out _, out var ownerName, out var jobTitle, out var tags))
        {
            comp.StatusMessage = "No ID card detected. Insert an ID into your PDA or swipe an ID card on this computer.";
            return;
        }

        var safeName = SanitizeFileName(requestedName);
        if (string.IsNullOrEmpty(safeName))
            safeName = SanitizeFileName(!string.IsNullOrWhiteSpace(jobTitle) ? jobTitle : ownerName);
        if (string.IsNullOrEmpty(safeName))
            safeName = "id_access";

        var file = new AccessCodesVirtualFile
        {
            Name = safeName,
            Extension = "acc",
            Directory = AccessEncoderProgramComponent.DefaultDirectory,
            OwnerName = ownerName,
            JobTitle = jobTitle,
            AccessTags = new List<ProtoId<AccessLevelPrototype>>(tags),
            Size = Math.Max(128, 128 + tags.Count * 24)
        };

        if (_fileSystem.TryWriteFile(driveUid.Value, file, fs))
        {
            comp.SelectedFilePath = file.FullPath;
            comp.StatusMessage = $"Saved {tags.Count} access code(s) to {file.FullPath}.";
            _fileSystem.LogSystemActivity(driveUid.Value, "ACCESS-ENC", $"Encoded {tags.Count} access codes -> {file.FullPath}");
        }
        else
        {
            comp.StatusMessage = "Save failed: Hard drive full or target file is read-only.";
        }
    }

    private bool TryExtractSwipedCard(EntityUid used, out EntityUid cardUid)
    {
        if (_idCardQuery.HasComp(used))
        {
            cardUid = used;
            return true;
        }

        if (TryComp<PdaComponent>(used, out var pda))
        {
            var pdaCard = pda.IdSlot.Item ?? pda.ContainedId;
            if (pdaCard is { Valid: true } idUid && Exists(idUid) && _idCardQuery.HasComp(idUid))
            {
                cardUid = idUid;
                return true;
            }
        }

        cardUid = EntityUid.Invalid;
        return false;
    }

    private (string OwnerName, string JobTitle, List<ProtoId<AccessLevelPrototype>> Tags) ReadCardCredentials(EntityUid cardUid)
    {
        var owner = string.Empty;
        var job = string.Empty;
        if (_idCardQuery.TryComp(cardUid, out var idCard))
        {
            owner = idCard.FullName ?? string.Empty;
            job = idCard.LocalizedJobTitle ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(owner))
            owner = MetaData(cardUid).EntityName;

        var tags = _access.FindAccessTags(cardUid)
            .OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (owner, job, tags);
    }

    private bool TryResolveActiveSource(
        EntityUid compUid,
        AccessEncoderProgramComponent comp,
        out string sourceLabel,
        out string ownerName,
        out string jobTitle,
        out List<ProtoId<AccessLevelPrototype>> tags)
    {
        if (comp.HasScannedCard)
        {
            sourceLabel = string.IsNullOrWhiteSpace(comp.ScannedSourceLabel) ? "Swiped ID Card" : comp.ScannedSourceLabel;
            ownerName = comp.ScannedOwnerName;
            jobTitle = comp.ScannedJobTitle;
            tags = comp.ScannedAccessTags;
            return true;
        }

        if (_computer.TryGetInsertedIdCard(compUid, out var insertedCardUid))
        {
            var creds = ReadCardCredentials(insertedCardUid);
            sourceLabel = $"Inserted ID ({MetaData(insertedCardUid).EntityName})";
            ownerName = creds.OwnerName;
            jobTitle = creds.JobTitle;
            tags = creds.Tags;
            return true;
        }

        sourceLabel = "None";
        ownerName = string.Empty;
        jobTitle = string.Empty;
        tags = new List<ProtoId<AccessLevelPrototype>>();
        return false;
    }

    private static string SanitizeFileName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var trimmed = raw.Trim();
        var slash = trimmed.LastIndexOf('/');
        if (slash >= 0)
            trimmed = trimmed[(slash + 1)..];
        if (trimmed.EndsWith(".acc", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4];

        var sb = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
                sb.Append(char.ToLowerInvariant(ch));
            else if (ch == ' ')
                sb.Append('_');
        }

        return sb.ToString().Trim('_');
    }

    public void UpdateUi(EntityUid progUid, EntityUid compUid, AccessEncoderProgramComponent comp)
    {
        var hasHdd = _fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs);
        var hasSource = TryResolveActiveSource(compUid, comp, out var sourceLabel, out var ownerName, out var jobTitle, out var sourceTags);

        var savedFiles = new List<AccessCodeFileEntryDto>();
        string? selectedSummary = null;

        if (hasHdd && driveUid != null && fs != null)
        {
            foreach (var f in _fileSystem.GetFilesByType<AccessCodesVirtualFile>(driveUid.Value, fs).OrderBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase))
            {
                var tagStrings = f.AccessTags.Select(t => t.Id).ToList();
                savedFiles.Add(new AccessCodeFileEntryDto(
                    f.FullPath,
                    $"{f.Name}.{f.Extension}",
                    f.OwnerName,
                    f.JobTitle,
                    tagStrings));

                if (!string.IsNullOrEmpty(comp.SelectedFilePath) &&
                    string.Equals(f.FullPath, comp.SelectedFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    var tagList = tagStrings.Count > 0 ? string.Join(", ", tagStrings) : "(none)";
                    selectedSummary =
                        $"File: {f.FullPath}\n" +
                        $"Holder: {f.OwnerName}{(string.IsNullOrWhiteSpace(f.JobTitle) ? "" : $" ({f.JobTitle})")}\n" +
                        $"Access Codes ({tagStrings.Count}): {tagList}\n" +
                        $"Terminal Usage: auth <guid> {f.FullPath}  |  In SSH: auth {f.FullPath}";
                }
            }
        }

        var state = new AccessEncoderUiState(
            hasHdd,
            hasSource,
            comp.HasScannedCard,
            sourceLabel,
            ownerName,
            jobTitle,
            sourceTags.Select(t => t.Id).ToList(),
            savedFiles,
            comp.SelectedFilePath,
            selectedSummary,
            comp.StatusMessage);

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, state);
    }
}
