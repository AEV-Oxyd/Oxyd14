using System.Linq;
using System.Text;
using Content.Server.Access.Systems;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.PDA;
using Content.Shared.Popups;
using Content.Shared.StationRecords;
using Content.Shared.StationRecords.Components;
using Content.Shared.StationRecords.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Computers.Programs;

/// <summary>
/// Server logic for the ID Access Encoder &amp; ID Modification Console app
/// (/bin/access_encoder.nte and /bin/id_console.nte).
/// 1) Extracts access codes from an inserted ID card (PDA/computer/console slot) or a swiped ID card
///    and persists them as .acc files in /keys/ on the filesystem.
/// 2) Modifies a target ID card's Full Name, Job Title, and Access Levels, authorized either by
///    a Privileged ID card OR by a .acc file stored on the computer's filesystem.
/// </summary>
public sealed partial class AccessEncoderSystem : EntitySystem
{
    [Dependency] private OxydComputerSystem _computer = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private AccessSystem _accessSystem = default!;
    [Dependency] private IdCardSystem _idCard = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private StationRecordsSystem _stationRecords = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    [Dependency] private EntityQuery<ProgramComponent> _programQuery = default!;
    [Dependency] private EntityQuery<OxydComputerComponent> _computerQuery = default!;
    [Dependency] private EntityQuery<IdCardComponent> _idCardQuery = default!;
    [Dependency] private EntityQuery<AccessComponent> _accessQuery = default!;
    [Dependency] private EntityQuery<IdCardConsoleComponent> _idConsoleQuery = default!;

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
    private void OnHardwareChanged(Entity<AccessEncoderProgramComponent> ent, ref ComputerHardwareChangedEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnHostContainerInserted(Entity<EmbeddedComputerHostComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        RefreshHostAccessPrograms(ent);
    }

    [SubscribeLocalEvent]
    private void OnHostContainerRemoved(Entity<EmbeddedComputerHostComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        RefreshHostAccessPrograms(ent);
    }

    private void RefreshHostAccessPrograms(Entity<EmbeddedComputerHostComponent> host)
    {
        if (host.Comp.ComputerEntity is not { } compUid || !_computerQuery.TryComp(compUid, out var comp))
            return;

        foreach (var progUid in comp.RunningPrograms)
        {
            if (TryComp<AccessEncoderProgramComponent>(progUid, out var enc))
                UpdateUi(progUid, compUid, enc);
        }
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
        ent.Comp.LastScannedCardUid = cardUid;
        ent.Comp.ScannedSourceLabel = $"Swiped ID ({MetaData(cardUid).EntityName})";
        ent.Comp.ScannedOwnerName = ownerName;
        ent.Comp.ScannedJobTitle = jobTitle;
        ent.Comp.ScannedAccessTags = tags;
        ent.Comp.StatusMessage = $"Scanned {tags.Count} access code(s) from {ownerName}. You can save to /keys/*.acc or modify target ID.";
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
                ent.Comp.LastScannedCardUid = null;
                ent.Comp.ScannedSourceLabel = string.Empty;
                ent.Comp.ScannedOwnerName = string.Empty;
                ent.Comp.ScannedJobTitle = string.Empty;
                ent.Comp.ScannedAccessTags.Clear();
                ent.Comp.StatusMessage = "Cleared swiped card buffer.";
                break;

            case AccessEncoderAction.SelectFile:
                ent.Comp.SelectedFilePath = string.IsNullOrWhiteSpace(args.Argument) ? null : args.Argument;
                if (!string.IsNullOrEmpty(ent.Comp.SelectedFilePath))
                    ent.Comp.StatusMessage = $"Selected authorization file '{ent.Comp.SelectedFilePath}'.";
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

            case AccessEncoderAction.TogglePrivilegedSlot:
                HandleToggleHostIdSlot(ent.Comp, compUid, args.Actor, isPrivilegedSlot: true);
                break;

            case AccessEncoderAction.ToggleTargetSlot:
                HandleToggleHostIdSlot(ent.Comp, compUid, args.Actor, isPrivilegedSlot: false);
                break;

            case AccessEncoderAction.WriteTargetIdentity:
                HandleWriteTargetIdentity(ent.Comp, compUid, args.Argument, args.SecondaryArgument, args.Actor);
                break;

            case AccessEncoderAction.ToggleTargetAccessTag:
                HandleToggleTargetAccessTag(ent.Comp, compUid, args.Argument);
                break;

            case AccessEncoderAction.ApplyAccFileToTarget:
                HandleApplyAccFileToTarget(ent.Comp, compUid, args.Argument);
                break;

            case AccessEncoderAction.GrantAllAllowedAccess:
                HandleBulkTargetAccess(ent.Comp, compUid, grant: true);
                break;

            case AccessEncoderAction.RevokeAllAllowedAccess:
                HandleBulkTargetAccess(ent.Comp, compUid, grant: false);
                break;
        }

        Dirty(ent);
        UpdateUi(ent.Owner, compUid, ent.Comp);
    }

    private void HandleToggleHostIdSlot(AccessEncoderProgramComponent comp, EntityUid compUid, EntityUid actor, bool isPrivilegedSlot)
    {
        if (!_computerQuery.TryComp(compUid, out var cComp) ||
            cComp.HostMachine is not { } hostUid ||
            !_idConsoleQuery.TryComp(hostUid, out var idConsole))
        {
            comp.StatusMessage = "This device does not have dedicated dual ID console slots.";
            return;
        }

        var slot = isPrivilegedSlot ? idConsole.PrivilegedIdSlot : idConsole.TargetIdSlot;
        var slotLabel = isPrivilegedSlot ? "Privileged ID" : "Target ID";

        if (slot.HasItem)
        {
            if (_itemSlots.TryEjectToHands(hostUid, slot, actor))
                comp.StatusMessage = $"Ejected {slotLabel} card.";
            else if (_itemSlots.TryEject(hostUid, slot, actor, out _))
                comp.StatusMessage = $"Ejected {slotLabel} card.";
            else
                comp.StatusMessage = $"Unable to eject {slotLabel} card.";
            return;
        }

        if (_itemSlots.TryInsertFromHand(hostUid, slot, actor))
        {
            comp.StatusMessage = $"Inserted {slotLabel} card.";
        }
        else
        {
            comp.StatusMessage = $"Hold a valid ID card in your active hand to insert into {slotLabel} slot.";
        }
    }

    private void HandleWriteTargetIdentity(
        AccessEncoderProgramComponent comp,
        EntityUid compUid,
        string rawFullName,
        string rawJobTitle,
        EntityUid actor)
    {
        if (!TryResolveTargetIdCard(compUid, comp, out var targetCardUid, out _))
        {
            comp.StatusMessage = "Modify failed: No target ID card inserted or scanned.";
            return;
        }

        if (!ResolvePrivilegedPermissions(compUid, comp, out var hasAuth, out var authLabel, out _))
        {
            comp.StatusMessage = "Access denied: Insert/swipe a privileged ID card or select a .acc file in /keys/.";
            return;
        }

        var newName = (rawFullName ?? string.Empty).Trim();
        var newJob = (rawJobTitle ?? string.Empty).Trim();
        if (newName.Length > 48)
            newName = newName[..48];
        if (newJob.Length > 48)
            newJob = newJob[..48];

        if (!string.IsNullOrEmpty(newName))
            _idCard.TryChangeFullName(targetCardUid, newName, player: actor);
        if (!string.IsNullOrEmpty(newJob))
            _idCard.TryChangeJobTitle(targetCardUid, newJob, player: actor);

        if (TryComp<StationRecordKeyStorageComponent>(targetCardUid, out var keyStorage) &&
            keyStorage.Key is { } recKey &&
            _stationRecords.TryGetRecord<GeneralStationRecord>(recKey, out var record))
        {
            if (!string.IsNullOrEmpty(newName))
                record.Name = newName;
            if (!string.IsNullOrEmpty(newJob))
                record.JobTitle = newJob;
            _stationRecords.Synchronize(recKey);
        }

        // If the target card was our scanned card buffer, keep its cached name/title updated too.
        if (comp.LastScannedCardUid == targetCardUid)
        {
            if (!string.IsNullOrEmpty(newName))
                comp.ScannedOwnerName = newName;
            if (!string.IsNullOrEmpty(newJob))
                comp.ScannedJobTitle = newJob;
        }

        comp.StatusMessage = $"Updated ID identity to '{newName}' ({newJob}) via {authLabel}.";
    }

    private void HandleToggleTargetAccessTag(AccessEncoderProgramComponent comp, EntityUid compUid, string rawTag)
    {
        if (string.IsNullOrWhiteSpace(rawTag))
            return;

        if (!TryResolveTargetIdCard(compUid, comp, out var targetCardUid, out _))
        {
            comp.StatusMessage = "Modify failed: No target ID card inserted or scanned.";
            return;
        }

        if (!ResolvePrivilegedPermissions(compUid, comp, out _, out var authLabel, out var allowedTags))
        {
            comp.StatusMessage = "Access denied: Insert/swipe a privileged ID card or select a .acc file in /keys/.";
            return;
        }

        var tagProto = new ProtoId<AccessLevelPrototype>(rawTag.Trim());
        if (!allowedTags.Contains(tagProto))
        {
            comp.StatusMessage = $"Access denied: Current authorization ({authLabel}) does not grant '{tagProto.Id}'.";
            return;
        }

        var currentTags = _accessSystem.TryGetTags(targetCardUid)?.ToList() ?? new List<ProtoId<AccessLevelPrototype>>();
        bool added;
        if (currentTags.Contains(tagProto))
        {
            currentTags.Remove(tagProto);
            added = false;
        }
        else
        {
            currentTags.Add(tagProto);
            added = true;
        }

        _accessSystem.TrySetTags(targetCardUid, currentTags);
        if (comp.LastScannedCardUid == targetCardUid)
            comp.ScannedAccessTags = currentTags.OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase).ToList();

        comp.StatusMessage = $"{(added ? "Granted" : "Revoked")} '{tagProto.Id}' on target ID via {authLabel}.";
    }

    private void HandleApplyAccFileToTarget(AccessEncoderProgramComponent comp, EntityUid compUid, string requestedPath)
    {
        if (!TryResolveTargetIdCard(compUid, comp, out var targetCardUid, out var targetLabel))
        {
            comp.StatusMessage = "Apply failed: No target ID card inserted or scanned.";
            return;
        }

        var path = !string.IsNullOrWhiteSpace(requestedPath) ? requestedPath.Trim() : comp.SelectedFilePath;
        if (!TryGetAccFileFromComputerOrDisk(compUid, path, out var accFile))
        {
            comp.StatusMessage = "Apply failed: Select or save a valid .acc file in /keys/ first.";
            return;
        }

        comp.SelectedFilePath = accFile.FullPath;
        var currentTags = _accessSystem.TryGetTags(targetCardUid)?.ToHashSet() ?? new HashSet<ProtoId<AccessLevelPrototype>>();
        var addedCount = 0;
        foreach (var tag in accFile.AccessTags)
        {
            if (currentTags.Add(tag))
                addedCount++;
        }

        var finalList = currentTags.OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase).ToList();
        _accessSystem.TrySetTags(targetCardUid, finalList);

        if (comp.LastScannedCardUid == targetCardUid)
            comp.ScannedAccessTags = finalList;

        comp.StatusMessage = $"Applied {accFile.FullPath} ({accFile.AccessTags.Count} codes, +{addedCount} new) to {targetLabel}.";
    }

    private void HandleBulkTargetAccess(AccessEncoderProgramComponent comp, EntityUid compUid, bool grant)
    {
        if (!TryResolveTargetIdCard(compUid, comp, out var targetCardUid, out var targetLabel))
        {
            comp.StatusMessage = "Modify failed: No target ID card inserted or scanned.";
            return;
        }

        if (!ResolvePrivilegedPermissions(compUid, comp, out _, out var authLabel, out var allowedTags))
        {
            comp.StatusMessage = "Access denied: Insert/swipe a privileged ID card or select a .acc file in /keys/.";
            return;
        }

        var currentTags = _accessSystem.TryGetTags(targetCardUid)?.ToHashSet() ?? new HashSet<ProtoId<AccessLevelPrototype>>();
        if (grant)
        {
            foreach (var tag in allowedTags)
                currentTags.Add(tag);
        }
        else
        {
            foreach (var tag in allowedTags)
                currentTags.Remove(tag);
        }

        var finalList = currentTags.OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase).ToList();
        _accessSystem.TrySetTags(targetCardUid, finalList);

        if (comp.LastScannedCardUid == targetCardUid)
            comp.ScannedAccessTags = finalList;

        comp.StatusMessage = grant
            ? $"Granted all {allowedTags.Count} authorized access level(s) to {targetLabel} via {authLabel}."
            : $"Revoked authorized access level(s) from {targetLabel} via {authLabel}.";
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

        // Check host IdCardConsole PrivilegedIdSlot or TargetIdSlot first if present
        if (_computerQuery.TryComp(compUid, out var cComp) &&
            cComp.HostMachine is { } hostUid &&
            _idConsoleQuery.TryComp(hostUid, out var idConsole))
        {
            if (idConsole.PrivilegedIdSlot.Item is { Valid: true } privUid && Exists(privUid) && _idCardQuery.HasComp(privUid))
            {
                var creds = ReadCardCredentials(privUid);
                sourceLabel = $"Privileged Slot ({MetaData(privUid).EntityName})";
                ownerName = creds.OwnerName;
                jobTitle = creds.JobTitle;
                tags = creds.Tags;
                return true;
            }

            if (idConsole.TargetIdSlot.Item is { Valid: true } targUid && Exists(targUid) && _idCardQuery.HasComp(targUid))
            {
                var creds = ReadCardCredentials(targUid);
                sourceLabel = $"Target Slot ({MetaData(targUid).EntityName})";
                ownerName = creds.OwnerName;
                jobTitle = creds.JobTitle;
                tags = creds.Tags;
                return true;
            }
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

    private bool TryResolveTargetIdCard(
        EntityUid compUid,
        AccessEncoderProgramComponent comp,
        out EntityUid targetCardUid,
        out string targetLabel)
    {
        if (_computerQuery.TryComp(compUid, out var cComp) &&
            cComp.HostMachine is { } hostUid &&
            _idConsoleQuery.TryComp(hostUid, out var idConsole))
        {
            if (idConsole.TargetIdSlot.Item is { Valid: true } slotTarget && Exists(slotTarget) && _idCardQuery.HasComp(slotTarget))
            {
                targetCardUid = slotTarget;
                targetLabel = $"Target Slot ({MetaData(slotTarget).EntityName})";
                return true;
            }
        }

        if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotIdModule, out var modSlot) &&
            modSlot.Item is { Valid: true } modId &&
            Exists(modId) &&
            _idCardQuery.HasComp(modId))
        {
            targetCardUid = modId;
            targetLabel = $"Computer ID Slot ({MetaData(modId).EntityName})";
            return true;
        }

        if (comp.LastScannedCardUid is { Valid: true } scannedUid && Exists(scannedUid) && _idCardQuery.HasComp(scannedUid))
        {
            targetCardUid = scannedUid;
            targetLabel = $"Swiped ID ({MetaData(scannedUid).EntityName})";
            return true;
        }

        if (_computer.TryGetInsertedIdCard(compUid, out var fallbackId))
        {
            targetCardUid = fallbackId;
            targetLabel = $"Inserted ID ({MetaData(fallbackId).EntityName})";
            return true;
        }

        targetCardUid = EntityUid.Invalid;
        targetLabel = "None";
        return false;
    }

    private bool TryGetAccFileFromComputerOrDisk(
        EntityUid compUid,
        string? requestedPath,
        out AccessCodesVirtualFile accFile)
    {
        var allAccFiles = new List<AccessCodesVirtualFile>();
        if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) && driveUid != null && fs != null)
        {
            allAccFiles.AddRange(_fileSystem.GetFilesByType<AccessCodesVirtualFile>(driveUid.Value, fs));
        }

        if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotDiskModule, out var diskSlot) &&
            diskSlot.Item is { Valid: true } diskUid &&
            TryComp<FileSystemComponent>(diskUid, out var diskFs))
        {
            allAccFiles.AddRange(_fileSystem.GetFilesByType<AccessCodesVirtualFile>(diskUid, diskFs));
        }

        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            var match = allAccFiles.FirstOrDefault(f =>
                string.Equals(f.FullPath, requestedPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f.Name, requestedPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals($"{f.Name}.{f.Extension}", requestedPath, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                accFile = match;
                return true;
            }
        }

        if (allAccFiles.Count > 0)
        {
            accFile = allAccFiles.OrderBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase).First();
            return true;
        }

        accFile = default!;
        return false;
    }

    private bool ResolvePrivilegedPermissions(
        EntityUid compUid,
        AccessEncoderProgramComponent comp,
        out bool hasAuth,
        out string authLabel,
        out HashSet<ProtoId<AccessLevelPrototype>> allowedTags)
    {
        allowedTags = new HashSet<ProtoId<AccessLevelPrototype>>();
        var sources = new List<string>();

        // 1) Host Privileged ID Slot
        if (_computerQuery.TryComp(compUid, out var cComp) &&
            cComp.HostMachine is { } hostUid &&
            _idConsoleQuery.TryComp(hostUid, out var idConsole) &&
            idConsole.PrivilegedIdSlot.Item is { Valid: true } privUid &&
            Exists(privUid))
        {
            foreach (var tag in _access.FindAccessTags(privUid))
                allowedTags.Add(tag);
            sources.Add($"Privileged ID ({MetaData(privUid).EntityName})");
        }

        // 2) Selected or available .acc file on hard drive / disk
        if (TryGetAccFileFromComputerOrDisk(compUid, comp.SelectedFilePath, out var accFile) && !accFile.IsEncrypted)
        {
            comp.SelectedFilePath ??= accFile.FullPath;
            foreach (var tag in accFile.AccessTags)
                allowedTags.Add(tag);
            sources.Add($".acc ({accFile.FullPath})");
        }

        // 3) Swiped card if it has permissions and isn't solely the unprivileged target card
        if (comp.HasScannedCard && comp.ScannedAccessTags.Count > 0)
        {
            foreach (var tag in comp.ScannedAccessTags)
                allowedTags.Add(tag);
            if (sources.Count == 0)
                sources.Add(string.IsNullOrWhiteSpace(comp.ScannedSourceLabel) ? "Swiped ID" : comp.ScannedSourceLabel);
        }

        hasAuth = allowedTags.Count > 0;
        authLabel = sources.Count > 0 ? string.Join(" + ", sources) : "None (Insert Privileged ID or select .acc file)";
        return hasAuth;
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

                if (string.IsNullOrEmpty(comp.SelectedFilePath))
                    comp.SelectedFilePath = f.FullPath;

                if (!string.IsNullOrEmpty(comp.SelectedFilePath) &&
                    string.Equals(f.FullPath, comp.SelectedFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    var tagList = tagStrings.Count > 0 ? string.Join(", ", tagStrings) : "(none)";
                    selectedSummary =
                        $"Active .acc Auth File: {f.FullPath}\n" +
                        $"Holder: {f.OwnerName}{(string.IsNullOrWhiteSpace(f.JobTitle) ? "" : $" ({f.JobTitle})")}\n" +
                        $"Access Codes ({tagStrings.Count}): {tagList}\n" +
                        $"Terminal Usage: auth <guid> {f.FullPath}  |  In SSH: auth {f.FullPath}";
                }
            }
        }

        var hasHostIdSlots = false;
        var hasPrivSlotCard = false;
        var hasTargSlotCard = false;
        var consoleAccessLevels = comp.AccessLevels.Select(a => a.Id).ToList();

        if (_computerQuery.TryComp(compUid, out var cComp) &&
            cComp.HostMachine is { } hostUid &&
            _idConsoleQuery.TryComp(hostUid, out var idConsole))
        {
            hasHostIdSlots = true;
            hasPrivSlotCard = idConsole.PrivilegedIdSlot.HasItem;
            hasTargSlotCard = idConsole.TargetIdSlot.HasItem;
            if (idConsole.AccessLevels.Count > 0)
                consoleAccessLevels = idConsole.AccessLevels.Select(a => a.Id).ToList();
        }

        ResolvePrivilegedPermissions(compUid, comp, out var hasPrivAuth, out var privAuthLabel, out var allowedTagsSet);
        var hasTargetCard = TryResolveTargetIdCard(compUid, comp, out var targetCardUid, out var targetCardLabel);
        var targetFullName = string.Empty;
        var targetJobTitle = string.Empty;
        var targetAccessTags = new List<string>();

        if (hasTargetCard && _idCardQuery.TryComp(targetCardUid, out var targetIdComp))
        {
            targetFullName = targetIdComp.FullName ?? string.Empty;
            targetJobTitle = targetIdComp.LocalizedJobTitle ?? string.Empty;
            if (_accessQuery.TryComp(targetCardUid, out var targetAccComp))
                targetAccessTags = targetAccComp.Tags.Select(t => t.Id).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Ensure any access level present on the auth source or target card is visible in the grid.
        foreach (var allowed in allowedTagsSet)
        {
            if (!consoleAccessLevels.Contains(allowed.Id))
                consoleAccessLevels.Add(allowed.Id);
        }
        consoleAccessLevels.Sort(StringComparer.OrdinalIgnoreCase);

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
            comp.StatusMessage,
            hasPrivAuth,
            privAuthLabel,
            allowedTagsSet.Select(t => t.Id).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
            hasTargetCard,
            targetCardLabel,
            targetFullName,
            targetJobTitle,
            targetAccessTags,
            consoleAccessLevels,
            hasHostIdSlots,
            hasPrivSlotCard,
            hasTargSlotCard);

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, state);
    }
}
