using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._Oxyd.Computers.Hardware;
using Content.Server.Access.Systems;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Roles;
using Content.Shared.StationRecords;
using Content.Shared.StationRecords.Components;
using Content.Shared.StationRecords.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Computers.Programs;

/// <summary>
/// Server system for /bin/id_console.nte (ID Access Modifier).
/// Requires 2 ID Card Slot modules (Slot #1 = Modifying ID, Slot #2 = Target ID)
/// and supports authenticating modifications via Modifying ID in Slot #1 or /keys/*.acc files.
/// </summary>
public sealed partial class IdModifierSystem : EntitySystem
{
    [Dependency] private OxydComputerSystem _computer = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private AccessSystem _accessSystem = default!;
    [Dependency] private IdCardSystem _idCard = default!;
    [Dependency] private ComputerCardReaderSystem _cardReaders = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private StationRecordsSystem _stationRecords = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    [Dependency] private EntityQuery<ProgramComponent> _programQuery = default!;
    [Dependency] private EntityQuery<OxydComputerComponent> _computerQuery = default!;
    [Dependency] private EntityQuery<IdCardComponent> _idCardQuery = default!;
    [Dependency] private EntityQuery<AccessComponent> _accessQuery = default!;

    private const string MissingDualSlotMessage =
        "Requires 2 ID Card Slot hardware modules (Modifying ID + Target ID).";

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<IdModifierProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<IdModifierActionMessage>(OnUiAction);
        });
    }

    [SubscribeLocalEvent]
    private void OnStartup(Entity<IdModifierProgramComponent> ent, ref ProgramStartupEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnActivated(Entity<IdModifierProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnDeviceLink(Entity<IdModifierProgramComponent> ent, ref ProgramDeviceLinkReceivedEvent args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        UpdateUi(ent.Owner, compUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnHardwareChanged(Entity<IdModifierProgramComponent> ent, ref ComputerHardwareChangedEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnCardReaderChanged(Entity<IdModifierProgramComponent> ent, ref ComputerCardReaderChangedEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnCardScanned(Entity<IdModifierProgramComponent> ent, ref ProgramCardScannedEvent scanned)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        var slotCount = _cardReaders.GetSlotReaders(compUid).Count;
        if (slotCount < 2)
        {
            ent.Comp.StatusMessage = $"{MissingDualSlotMessage} Currently {slotCount}/2 installed.";
        }
        else if (scanned.Retained)
        {
            ent.Comp.StatusMessage = $"Updated ID slot ({MetaData(scanned.Card).EntityName}).";
        }

        UpdateUi(ent.Owner, compUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnBuiOpened(Entity<IdModifierProgramComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        UpdateUi(ent.Owner, compUid, ent.Comp);
    }

    private void OnUiAction(Entity<IdModifierProgramComponent> ent, ref IdModifierActionMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        switch (args.Action)
        {
            case IdModifierAction.Refresh:
                ent.Comp.StatusMessage = "ID Modifier state refreshed.";
                break;

            case IdModifierAction.TogglePrivilegedSlot:
                HandleToggleSlot(ent.Comp, compUid, args.Actor, slotIndex: 0);
                break;

            case IdModifierAction.ToggleTargetSlot:
                HandleToggleSlot(ent.Comp, compUid, args.Actor, slotIndex: 1);
                break;

            case IdModifierAction.SwapSlots:
                if (!HasDualSlots(compUid, out var slotCount))
                {
                    ent.Comp.StatusMessage = $"{MissingDualSlotMessage} Currently {slotCount}/2 installed.";
                }
                else
                {
                    ent.Comp.StatusMessage = _cardReaders.TrySwapSlotCards(compUid, args.Actor)
                        ? "Swapped Modifying ID and Target ID slots."
                        : "Both physical ID Card Slots must be installed to swap cards.";
                }
                break;

            case IdModifierAction.SelectAccFile:
                var trimmed = args.Argument.Trim();
                if (args.SecondaryArgument == "toggle" &&
                    string.Equals(ent.Comp.SelectedAccFilePath, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    ent.Comp.SelectedAccFilePath = null;
                }
                else
                {
                    ent.Comp.SelectedAccFilePath = string.IsNullOrEmpty(trimmed) ? null : trimmed;
                }
                ent.Comp.StatusMessage = string.IsNullOrEmpty(ent.Comp.SelectedAccFilePath)
                    ? "Cleared .acc authorization file selection."
                    : $"Selected {ent.Comp.SelectedAccFilePath} as authorization source.";
                break;

            case IdModifierAction.WriteTargetIdentity:
                HandleWriteTargetIdentity(ent.Comp, compUid, args.Actor, args.Argument, args.SecondaryArgument, args.TertiaryArgument);
                break;

            case IdModifierAction.ToggleTargetAccessTag:
                HandleToggleTargetAccessTag(ent.Comp, compUid, args.Argument);
                break;

            case IdModifierAction.ApplyAccFileToTarget:
                HandleApplyAccFileToTarget(ent.Comp, compUid, args.Argument);
                break;

            case IdModifierAction.GrantAllAllowedAccess:
                HandleBulkTargetAccess(ent.Comp, compUid, grant: true);
                break;

            case IdModifierAction.RevokeAllAllowedAccess:
                HandleBulkTargetAccess(ent.Comp, compUid, grant: false);
                break;
        }

        UpdateUi(ent.Owner, compUid, ent.Comp);
    }

    private void HandleToggleSlot(IdModifierProgramComponent comp, EntityUid compUid, EntityUid actor, int slotIndex)
    {
        var slotReaders = _cardReaders.GetSlotReaders(compUid);
        if (slotIndex == 1 && slotReaders.Count < 2)
        {
            comp.StatusMessage = $"{MissingDualSlotMessage} Currently {slotReaders.Count}/2 installed.";
            return;
        }

        if (slotIndex >= slotReaders.Count)
        {
            comp.StatusMessage = "ID Card Slot hardware is not installed.";
            return;
        }

        var slot = slotReaders[slotIndex];
        var slotName = slotIndex == 0 ? "Slot #1 (Modifying ID)" : "Slot #2 (Target ID)";
        var dropNear = _computerQuery.TryComp(compUid, out var cComp) && cComp.HostMachine is { } h ? h : compUid;

        if (_cardReaders.TryGetSlotCard(compUid, slotIndex, out _))
        {
            if (_cardReaders.TryEjectCard(slot, actor, dropNear))
                comp.StatusMessage = $"Ejected card from {slotName}.";
            return;
        }

        if (!_hands.TryGetActiveItem(actor, out var held) || !_idCardQuery.HasComp(held.Value))
        {
            comp.StatusMessage = $"Hold an ID card in your active hand to insert into {slotName}.";
            return;
        }

        if (_cardReaders.TryInsertCard(slot, held.Value, actor))
            comp.StatusMessage = $"Inserted {MetaData(held.Value).EntityName} into {slotName}.";
        else
            comp.StatusMessage = $"Failed to insert ID card into {slotName}.";
    }

    private void HandleWriteTargetIdentity(
        IdModifierProgramComponent comp,
        EntityUid compUid,
        EntityUid actor,
        string fullName,
        string jobTitle,
        string jobProtoId)
    {
        if (!TryValidateModificationContext(comp, compUid, out var targetCardUid, out _, out var authName))
            return;

        var newName = fullName.Trim();
        var newJob = jobTitle.Trim();
        if (!string.IsNullOrEmpty(newName))
            _idCard.TryChangeFullName(targetCardUid, newName, player: actor);
        if (!string.IsNullOrEmpty(newJob))
            _idCard.TryChangeJobTitle(targetCardUid, newJob, player: actor);

        JobPrototype? resolvedJob = null;
        if (!string.IsNullOrWhiteSpace(jobProtoId) && _proto.TryIndex<JobPrototype>(jobProtoId.Trim(), out resolvedJob))
        {
            if (_proto.Resolve(resolvedJob.Icon, out var jobIcon))
            {
                _idCard.TryChangeJobIcon(targetCardUid, jobIcon, player: actor);
                _idCard.TryChangeJobDepartment(targetCardUid, resolvedJob);
            }

            if (_idCardQuery.TryComp(targetCardUid, out var targetIdComp))
                targetIdComp.JobPrototype = resolvedJob.ID;
        }

        if (TryComp<StationRecordKeyStorageComponent>(targetCardUid, out var keyStorage) &&
            keyStorage.Key is { } key &&
            _stationRecords.TryGetRecord<GeneralStationRecord>(key, out var record))
        {
            if (!string.IsNullOrEmpty(newName))
                record.Name = newName;
            if (!string.IsNullOrEmpty(newJob))
                record.JobTitle = newJob;
            if (resolvedJob != null)
            {
                record.JobPrototype = resolvedJob.ID;
                record.JobIcon = resolvedJob.Icon;
            }
            _stationRecords.Synchronize(key);
        }

        comp.StatusMessage = $"Updated Target ID identity (authorized by {authName}).";
    }

    private void HandleToggleTargetAccessTag(IdModifierProgramComponent comp, EntityUid compUid, string rawTag)
    {
        var tagId = rawTag.Trim();
        if (string.IsNullOrEmpty(tagId))
            return;

        if (!TryValidateModificationContext(comp, compUid, out var targetCardUid, out var allowedTags, out var authName))
            return;

        if (!allowedTags.Contains(tagId))
        {
            comp.StatusMessage = $"Denied: modifying authority ({authName}) lacks '{tagId}'.";
            return;
        }

        var current = new HashSet<ProtoId<AccessLevelPrototype>>(_access.FindAccessTags(targetCardUid));
        var protoTag = new ProtoId<AccessLevelPrototype>(tagId);
        var granted = !current.Contains(protoTag);
        if (granted)
            current.Add(protoTag);
        else
            current.Remove(protoTag);

        _accessSystem.TrySetTags(targetCardUid, current);
        comp.StatusMessage = $"{(granted ? "Granted" : "Revoked")} '{tagId}' on Target ID.";
    }

    private void HandleApplyAccFileToTarget(IdModifierProgramComponent comp, EntityUid compUid, string requestedPath)
    {
        if (!HasDualSlots(compUid, out var count))
        {
            comp.StatusMessage = $"{MissingDualSlotMessage} Currently {count}/2 installed.";
            return;
        }

        if (!TryGetTargetCard(compUid, out var targetCardUid))
        {
            comp.StatusMessage = "No Target ID card inserted in Slot #2.";
            return;
        }

        var path = string.IsNullOrWhiteSpace(requestedPath) ? comp.SelectedAccFilePath : requestedPath.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            comp.StatusMessage = "Select a /keys/*.acc file first.";
            return;
        }

        IdCardProgramHelpers.CollectAccFiles(compUid, _itemSlots, _fileSystem, EntityManager, out var files, out _);
        if (!IdCardProgramHelpers.TryGetAccFile(compUid, path, files, _itemSlots, _fileSystem, EntityManager, out var accFile))
        {
            comp.StatusMessage = $"Access file '{path}' not found.";
            return;
        }

        if (!ResolvePrivilegedAuthority(comp, compUid, out var allowedTags, out var authName))
        {
            comp.StatusMessage = "Insert a Modifying ID in Slot #1 or select an authorizing .acc file.";
            return;
        }

        var current = new HashSet<ProtoId<AccessLevelPrototype>>(_access.FindAccessTags(targetCardUid));
        var added = 0;
        var denied = 0;
        foreach (var tag in accFile.AccessTags)
        {
            if (allowedTags.Contains(tag.Id))
            {
                if (current.Add(tag))
                    added++;
            }
            else
            {
                denied++;
            }
        }

        _accessSystem.TrySetTags(targetCardUid, current);
        comp.StatusMessage = denied > 0
            ? $"Applied {added} permitted codes from {accFile.Name}.acc via {authName} ({denied} exceeded authority)."
            : $"Applied {added} access codes from {accFile.Name}.acc via {authName}.";
    }

    private void HandleBulkTargetAccess(IdModifierProgramComponent comp, EntityUid compUid, bool grant)
    {
        if (!TryValidateModificationContext(comp, compUid, out var targetCardUid, out var allowedTags, out var authName))
            return;

        var current = new HashSet<ProtoId<AccessLevelPrototype>>(_access.FindAccessTags(targetCardUid));
        var changed = 0;
        foreach (var tagId in allowedTags)
        {
            if (!_proto.TryIndex<AccessLevelPrototype>(tagId, out var accProto) || !accProto.CanAddToIdCard)
                continue;

            var tag = new ProtoId<AccessLevelPrototype>(tagId);
            if (grant)
            {
                if (current.Add(tag))
                    changed++;
            }
            else
            {
                if (current.Remove(tag))
                    changed++;
            }
        }

        _accessSystem.TrySetTags(targetCardUid, current);
        comp.StatusMessage = grant
            ? $"Granted {changed} permitted access tags onto Target ID (via {authName})."
            : $"Revoked {changed} access tags from Target ID (via {authName}).";
    }

    private bool HasDualSlots(EntityUid compUid, out int count)
    {
        count = _cardReaders.GetSlotReaders(compUid).Count;
        return count >= 2;
    }

    private bool TryGetTargetCard(EntityUid compUid, out EntityUid targetCard)
    {
        return _cardReaders.TryGetSlotCard(compUid, 1, out targetCard) && _idCardQuery.HasComp(targetCard);
    }

    private bool TryValidateModificationContext(
        IdModifierProgramComponent comp,
        EntityUid compUid,
        out EntityUid targetCardUid,
        out HashSet<string> allowedTags,
        out string authName)
    {
        targetCardUid = EntityUid.Invalid;
        allowedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        authName = string.Empty;

        if (!HasDualSlots(compUid, out var count))
        {
            comp.StatusMessage = $"{MissingDualSlotMessage} Currently {count}/2 installed.";
            return false;
        }

        if (!TryGetTargetCard(compUid, out targetCardUid))
        {
            comp.StatusMessage = "Insert Target ID into Slot #2.";
            return false;
        }

        if (!ResolvePrivilegedAuthority(comp, compUid, out allowedTags, out authName))
        {
            comp.StatusMessage = "No modifying authority. Insert Modifying ID into Slot #1 or select a /keys/*.acc file.";
            return false;
        }

        return true;
    }

    private bool ResolvePrivilegedAuthority(
        IdModifierProgramComponent comp,
        EntityUid compUid,
        out HashSet<string> allowedTags,
        out string authorityName)
    {
        allowedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<string>();

        if (_cardReaders.TryGetSlotCard(compUid, 0, out var slot1Card) && _idCardQuery.HasComp(slot1Card))
        {
            var (owner, job, tags) = IdCardProgramHelpers.ReadCardCredentials(slot1Card, _idCardQuery, _access, EntityManager);
            sources.Add(string.IsNullOrWhiteSpace(job) ? $"{owner} (Slot #1)" : $"{owner} [{job}]");
            foreach (var t in tags)
                allowedTags.Add(t.Id);
        }

        if (!string.IsNullOrWhiteSpace(comp.SelectedAccFilePath))
        {
            IdCardProgramHelpers.CollectAccFiles(compUid, _itemSlots, _fileSystem, EntityManager, out var files, out _);
            if (IdCardProgramHelpers.TryGetAccFile(compUid, comp.SelectedAccFilePath, files, _itemSlots, _fileSystem, EntityManager, out var accFile))
            {
                sources.Add($"{accFile.OwnerName} (.acc)");
                foreach (var t in accFile.AccessTags)
                    allowedTags.Add(t.Id);
            }
        }

        authorityName = string.Join(" + ", sources);
        return sources.Count > 0;
    }

    public void UpdateUi(EntityUid progUid, EntityUid compUid, IdModifierProgramComponent comp)
    {
        if (!_ui.HasUi(progUid, ProgramUiKey.Key))
            return;

        var slotReaders = _cardReaders.GetSlotReaders(compUid);
        var slotCount = slotReaders.Count;
        var hasDualSlots = slotCount >= 2;

        var slot1Occupied = _cardReaders.TryGetSlotCard(compUid, 0, out var slot1Card) && _idCardQuery.HasComp(slot1Card);
        var slot1Label = slot1Occupied ? MetaData(slot1Card).EntityName : string.Empty;

        var slot2Occupied = false;
        EntityUid targetCard = default;
        if (hasDualSlots && TryGetTargetCard(compUid, out targetCard))
            slot2Occupied = true;
        var slot2Label = slot2Occupied ? MetaData(targetCard).EntityName : string.Empty;

        var accFiles = IdCardProgramHelpers.CollectAccFiles(compUid, _itemSlots, _fileSystem, EntityManager, out _, out _);
        ResolvePrivilegedAuthority(comp, compUid, out var privTags, out var privOwner);

        var targetCardName = string.Empty;
        var targetFullName = string.Empty;
        var targetJobTitle = string.Empty;
        var targetJobProto = string.Empty;
        var targetTags = new List<string>();

        if (slot2Occupied && _idCardQuery.TryComp(targetCard, out var tId))
        {
            targetCardName = MetaData(targetCard).EntityName;
            targetFullName = tId.FullName ?? string.Empty;
            targetJobTitle = tId.LocalizedJobTitle ?? string.Empty;
            targetJobProto = tId.JobPrototype?.Id ?? string.Empty;
            if (_accessQuery.TryComp(targetCard, out var accComp))
                targetTags = accComp.Tags.Select(t => t.Id).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        }

        var state = new IdModifierUiState(
            slotCount,
            hasDualSlots,
            slot1Occupied,
            slot1Label,
            slot2Occupied,
            slot2Label,
            privOwner,
            privTags.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
            comp.SelectedAccFilePath ?? string.Empty,
            accFiles,
            slot2Occupied,
            targetCardName,
            targetFullName,
            targetJobTitle,
            targetJobProto,
            targetTags,
            hasDualSlots ? comp.StatusMessage : $"{MissingDualSlotMessage} Currently {slotCount}/2 installed.");

        _ui.SetUiState(progUid, ProgramUiKey.Key, state);
    }
}
