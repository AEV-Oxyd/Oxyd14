using System.Globalization;
using System.Linq;
using Content.Server._Oxyd.Computers.Network;
using Content.Server._Oxyd.Computers.Servers;
using Content.Server.CriminalRecords.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.CriminalRecords;
using Content.Shared.Lock;
using Content.Shared.Security;
using Content.Shared.StationRecords;
using Content.Shared.StationRecords.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.Programs;

/// <summary>
/// Server runtime for Data-Computer Record Manager programs:
/// - <see cref="StationRecordsProgramComponent"/> (/bin/station_records.nte -> /records/station_records.db)
/// - <see cref="CriminalRecordsProgramComponent"/> (/bin/criminal_records.nte -> /records/criminal_records.db)
/// DataServer HDD .db files are authoritative for all NTOS apps, routed via <see cref="NetworkRouterSystem"/>,
/// cached locally on client HDDs for offline inspection, and synced back to live Security HUD icons on criminal edits.
/// </summary>
public sealed partial class RecordsProgramsSystem : EntitySystem
{
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private NetworkRouterSystem _router = default!;
    [Dependency] private DataServerSystem _dataServer = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private ServerStationSystem _station = default!;
    [Dependency] private StationRecordsSystem _stationRecords = default!;
    [Dependency] private CriminalRecordsSystem _criminalRecords = default!;
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private LockSystem _lock = default!;
    [Dependency] private StationAppsSystem _stationApps = default!;
    [Dependency] private IGameTiming _timing = default!;

    [Dependency] private EntityQuery<ProgramComponent> _programQuery = default!;
    [Dependency] private EntityQuery<OxydComputerComponent> _computerQuery = default!;

    private static readonly ProtoId<AccessLevelPrototype> SecurityAccess = "Security";
    private static readonly ProtoId<AccessLevelPrototype> CommandAccess = "Command";
    private static readonly ProtoId<AccessLevelPrototype> HeadOfPersonnelAccess = "HeadOfPersonnel";

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<StationRecordsProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnStationRecordsBuiOpened);
            subs.Event<StationRecordsActionMessage>(OnStationRecordsAction);
        });

        Subs.BuiEvents<CriminalRecordsProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnCriminalRecordsBuiOpened);
            subs.Event<CriminalRecordsActionMessage>(OnCriminalRecordsAction);
        });
    }

    public void RefreshRecordsForHostLock(EntityUid compUid, bool locked)
    {
        if (!_computerQuery.TryComp(compUid, out var comp))
            return;

        foreach (var progUid in comp.RunningPrograms)
        {
            if (TryComp<StationRecordsProgramComponent>(progUid, out var st))
                SendStationRecordsFromLocalCache(progUid, compUid, st, true, locked ? "Console locked (read-only)." : "Console unlocked (editing enabled).", null);
            if (TryComp<CriminalRecordsProgramComponent>(progUid, out var cr))
                SendCriminalRecordsFromLocalCache(progUid, compUid, cr, true, locked ? "Console locked (read-only)." : "Console unlocked (editing enabled).", null);
        }
    }

    // =========================================================================
    // STATION RECORDS MANAGER (/bin/station_records.nte)
    // =========================================================================

    [SubscribeLocalEvent]
    private void OnStationRecordsStartup(Entity<StationRecordsProgramComponent> ent, ref ProgramStartupEvent args)
    {
        SyncStationRecords(ent.Owner, args.ComputerUid, ent.Comp, null);
    }

    [SubscribeLocalEvent]
    private void OnStationRecordsActivated(Entity<StationRecordsProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        SyncStationRecords(ent.Owner, args.ComputerUid, ent.Comp, null);
    }

    private void OnStationRecordsBuiOpened(Entity<StationRecordsProgramComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (_programQuery.TryComp(ent.Owner, out var prog) && prog.Computer is { } compUid)
            SyncStationRecords(ent.Owner, compUid, ent.Comp, args.Actor);
    }

    private void OnStationRecordsAction(Entity<StationRecordsProgramComponent> ent, ref StationRecordsActionMessage msg)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        switch (msg.Action)
        {
            case StationRecordsAction.Refresh:
                SyncStationRecords(ent.Owner, compUid, ent.Comp, msg.Actor);
                return;

            case StationRecordsAction.Select:
                ent.Comp.SelectedKey = msg.Key;
                Dirty(ent);
                SendStationRecordsFromLocalCache(ent.Owner, compUid, ent.Comp, true, ent.Comp.StatusMessage, msg.Actor);
                return;

            case StationRecordsAction.SaveEntry:
            case StationRecordsAction.CreateEntry:
            case StationRecordsAction.DeleteEntry:
                HandleStationRecordsMutation(ent, compUid, msg);
                return;
        }
    }

    private void HandleStationRecordsMutation(
        Entity<StationRecordsProgramComponent> ent,
        EntityUid compUid,
        StationRecordsActionMessage msg)
    {
        if (!CanModifyStationRecords(compUid, msg.Actor))
        {
            ent.Comp.StatusMessage = "Access denied: requires Command/HoP/Security access or an unlocked records console.";
            Dirty(ent);
            SendStationRecordsFromLocalCache(ent.Owner, compUid, ent.Comp, true, ent.Comp.StatusMessage, msg.Actor);
            return;
        }

        var station = _station.GetOwningStation(compUid) ?? _station.GetStationsSet().FirstOrDefault();
        if (!_dataServer.TryGetLocalRecordCopyFromHost(
                compUid,
                "StationRecordsService",
                StationRecordsProgramComponent.CacheFilePath,
                station,
                out var hostCompUid,
                out var hostCopy,
                out var netErr))
        {
            ent.Comp.StatusMessage = $"Write failed: {netErr}";
            Dirty(ent);
            SendStationRecordsFromLocalCache(ent.Owner, compUid, ent.Comp, false, ent.Comp.StatusMessage, msg.Actor);
            return;
        }

        string actionSummary;
        string targetKey = msg.Key;
        string? affectedName = null;
        string? affectedJob = null;

        if (msg.Action == StationRecordsAction.DeleteEntry)
        {
            if (string.IsNullOrWhiteSpace(targetKey) || !hostCopy.Data.TryGetValue(targetKey, out var oldVal))
            {
                ent.Comp.StatusMessage = "Record not found.";
                Dirty(ent);
                SendStationRecordsFromLocalCache(ent.Owner, compUid, ent.Comp, true, ent.Comp.StatusMessage, msg.Actor);
                return;
            }

            affectedName = oldVal.Split('\t')[0];
            hostCopy.Data.Remove(targetKey);
            actionSummary = $"Deleted record '{affectedName}'.";
            if (ent.Comp.SelectedKey == targetKey)
                ent.Comp.SelectedKey = string.Empty;
        }
        else
        {
            var cleanName = RecordsDbCodec.SanitizeField(msg.Name);
            if (string.IsNullOrWhiteSpace(cleanName))
            {
                ent.Comp.StatusMessage = "Name cannot be empty.";
                Dirty(ent);
                SendStationRecordsFromLocalCache(ent.Owner, compUid, ent.Comp, true, ent.Comp.StatusMessage, msg.Actor);
                return;
            }

            var cleanJob = string.IsNullOrWhiteSpace(msg.JobTitle) ? "Passenger" : RecordsDbCodec.SanitizeField(msg.JobTitle);
            var existingProto = string.Empty;
            var existingIcon = "JobIconUnknown";

            if (msg.Action == StationRecordsAction.CreateEntry || string.IsNullOrWhiteSpace(targetKey) || !hostCopy.Data.ContainsKey(targetKey))
            {
                var nextIdx = FindNextKey(hostCopy.Data, "rec_");
                targetKey = $"rec_{nextIdx}";
            }
            else if (hostCopy.Data.TryGetValue(targetKey, out var existingRaw))
            {
                var cols = existingRaw.Split('\t');
                if (cols.Length > 2) existingProto = cols[2];
                if (cols.Length > 3) existingIcon = cols[3];
            }

            hostCopy.Data[targetKey] = RecordsDbCodec.EncodeStationRecordRow(
                cleanName,
                cleanJob,
                existingProto,
                existingIcon,
                Math.Clamp(msg.Age, 1, 200),
                string.IsNullOrWhiteSpace(msg.Species) ? "Human" : msg.Species,
                string.IsNullOrWhiteSpace(msg.Gender) ? "Epicene" : msg.Gender,
                msg.Fingerprint,
                msg.Dna);

            affectedName = cleanName;
            affectedJob = cleanJob;
            ent.Comp.SelectedKey = targetKey;
            actionSummary = msg.Action == StationRecordsAction.CreateEntry
                ? $"Created record for {cleanName} ({cleanJob})."
                : $"Saved record for {cleanName} ({cleanJob}).";
        }

        hostCopy.Size = Math.Max(128, hostCopy.Data.Count * 96);
        if (!_dataServer.TryWriteRecordCopyToHost(compUid, "StationRecordsService", StationRecordsProgramComponent.CacheFilePath, hostCopy, out hostCompUid, out var writeErr))
        {
            ent.Comp.StatusMessage = $"Write failed: {writeErr}";
            Dirty(ent);
            SendStationRecordsFromLocalCache(ent.Owner, compUid, ent.Comp, false, ent.Comp.StatusMessage, msg.Actor);
            return;
        }

        // Keep Crew Manifest in sync on DataServer when a person's name/job is added, updated, or deleted.
        SyncCrewManifestFromRecordsMutation(compUid, station, affectedName, affectedJob, msg.Action == StationRecordsAction.DeleteEntry);

        // Log routed write packet and refresh local client cache.
        var pkt = BuildStationRecordsPacket(compUid, hostCompUid, hostCopy, $"WRITE: {actionSummary}");
        _router.RecordAndValidatePacket(compUid, hostCompUid, pkt);
        WriteLocalCache(compUid, hostCopy);

        ent.Comp.StatusMessage = actionSummary;
        Dirty(ent);
        SendStationRecordsFromLocalCache(ent.Owner, compUid, ent.Comp, true, actionSummary, msg.Actor);
    }

    private void SyncCrewManifestFromRecordsMutation(EntityUid compUid, EntityUid station, string? name, string? jobTitle, bool deleted)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (!_dataServer.TryGetLocalRecordCopyFromHost(
                compUid,
                "ManifestService",
                CrewManifestProgramComponent.CacheFilePath,
                station,
                out _,
                out var manifestCopy,
                out _))
        {
            return;
        }

        var matchKey = manifestCopy.Data
            .Where(kv => kv.Key.StartsWith("row_") && kv.Value.StartsWith(name + "\t", StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .FirstOrDefault();

        if (deleted)
        {
            if (matchKey == null)
                return;
            manifestCopy.Data.Remove(matchKey);
        }
        else
        {
            var job = string.IsNullOrWhiteSpace(jobTitle) ? "Passenger" : jobTitle;
            var proto = string.Empty;
            var icon = "JobIconUnknown";
            if (matchKey != null)
            {
                var cols = manifestCopy.Data[matchKey].Split('\t');
                if (cols.Length > 2) proto = cols[2];
                if (cols.Length > 3) icon = cols[3];
            }

            var writeKey = matchKey ?? $"row_{FindNextKey(manifestCopy.Data, "row_")}";
            manifestCopy.Data[writeKey] = $"{RecordsDbCodec.SanitizeField(name)}\t{RecordsDbCodec.SanitizeField(job)}\t{proto}\t{icon}";
        }

        manifestCopy.Size = Math.Max(128, manifestCopy.Data.Count * 64);
        _dataServer.TryWriteRecordCopyToHost(compUid, "ManifestService", CrewManifestProgramComponent.CacheFilePath, manifestCopy, out _, out _);
    }

    private void SyncStationRecords(EntityUid progUid, EntityUid compUid, StationRecordsProgramComponent comp, EntityUid? actor)
    {
        var online = false;
        var statusMsg = string.Empty;
        var station = _station.GetOwningStation(compUid) ?? _station.GetStationsSet().FirstOrDefault();

        if (_router.HasActiveNetworkCard(compUid) && _router.IsNetworkConnected(compUid))
        {
            if (_dataServer.TryGetLocalRecordCopyFromHost(
                    compUid,
                    "StationRecordsService",
                    StationRecordsProgramComponent.CacheFilePath,
                    station,
                    out var srvCompUid,
                    out var serverCopy,
                    out var netErr))
            {
                var pkt = BuildStationRecordsPacket(srvCompUid, compUid, serverCopy, $"Station Records [{MetaData(srvCompUid).EntityName}]");
                if (_router.RecordAndValidatePacket(srvCompUid, compUid, pkt))
                {
                    WriteLocalCache(compUid, serverCopy);
                    online = true;
                    statusMsg = $"Synced from {MetaData(srvCompUid).EntityName} [{_router.EnsureDeviceGuid(srvCompUid)}] -> {StationRecordsProgramComponent.CacheFilePath}";
                }
            }
            else
            {
                statusMsg = $"{netErr} — showing local {StationRecordsProgramComponent.CacheFilePath}.";
            }
        }
        else
        {
            statusMsg = $"Offline / no NIC — reading local {StationRecordsProgramComponent.CacheFilePath}.";
        }

        comp.StatusMessage = statusMsg;
        Dirty(progUid, comp);
        SendStationRecordsFromLocalCache(progUid, compUid, comp, online, statusMsg, actor);
    }

    public void SendStationRecordsFromLocalCache(
        EntityUid progUid,
        EntityUid compUid,
        StationRecordsProgramComponent comp,
        bool online,
        string statusMsg,
        EntityUid? actor)
    {
        _fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs);

        var stationName = "Unknown Station";
        var entries = new List<StationRecordEntryDto>();
        TimeSpan? ts = null;

        // Cross-reference cached criminal status by name if available on local drive.
        var crimStatusByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (driveUid != null && fs != null &&
            _fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid.Value, CriminalRecordsProgramComponent.CacheFilePath, out var crimDb, fs))
        {
            foreach (var (k, v) in crimDb.Data)
            {
                if (!k.StartsWith("crim_"))
                    continue;
                if (RecordsDbCodec.TryDecodeCriminalRecordRow(k, v, out var cDto))
                    crimStatusByName[cDto.Name] = cDto.Status.ToString();
            }
        }

        if (driveUid != null && fs != null &&
            _fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid.Value, StationRecordsProgramComponent.CacheFilePath, out var db, fs))
        {
            ts = db.LastModified;
            if (db.Data.TryGetValue("__station", out var sName) && !string.IsNullOrWhiteSpace(sName))
                stationName = sName;

            foreach (var (k, v) in db.Data.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (!k.StartsWith("rec_"))
                    continue;

                var firstCol = v.Split('\t')[0];
                var crimStatus = crimStatusByName.TryGetValue(firstCol, out var st) ? st : "None";
                if (RecordsDbCodec.TryDecodeStationRecordRow(k, v, crimStatus, out var dto))
                    entries.Add(dto);
            }
        }

        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        var selected = comp.SelectedKey;
        if (string.IsNullOrEmpty(selected) && entries.Count > 0)
            selected = entries[0].Key;

        var canEdit = online && CanModifyStationRecords(compUid, actor);

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
        {
            _ui.SetUiState(progUid, ProgramUiKey.Key, new StationRecordsUiState(
                stationName,
                online,
                canEdit,
                statusMsg,
                ts,
                selected,
                entries));
        }
    }

    // =========================================================================
    // CRIMINAL RECORDS MANAGER (/bin/criminal_records.nte)
    // =========================================================================

    [SubscribeLocalEvent]
    private void OnCriminalRecordsStartup(Entity<CriminalRecordsProgramComponent> ent, ref ProgramStartupEvent args)
    {
        SyncCriminalRecords(ent.Owner, args.ComputerUid, ent.Comp, null);
    }

    [SubscribeLocalEvent]
    private void OnCriminalRecordsActivated(Entity<CriminalRecordsProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        SyncCriminalRecords(ent.Owner, args.ComputerUid, ent.Comp, null);
    }

    private void OnCriminalRecordsBuiOpened(Entity<CriminalRecordsProgramComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (_programQuery.TryComp(ent.Owner, out var prog) && prog.Computer is { } compUid)
            SyncCriminalRecords(ent.Owner, compUid, ent.Comp, args.Actor);
    }

    private void OnCriminalRecordsAction(Entity<CriminalRecordsProgramComponent> ent, ref CriminalRecordsActionMessage msg)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        switch (msg.Action)
        {
            case CriminalRecordsAction.Refresh:
                SyncCriminalRecords(ent.Owner, compUid, ent.Comp, msg.Actor);
                return;

            case CriminalRecordsAction.Select:
                ent.Comp.SelectedKey = msg.Key;
                Dirty(ent);
                SendCriminalRecordsFromLocalCache(ent.Owner, compUid, ent.Comp, true, ent.Comp.StatusMessage, msg.Actor);
                return;

            case CriminalRecordsAction.SetStatus:
            case CriminalRecordsAction.AddHistory:
            case CriminalRecordsAction.DeleteHistory:
                HandleCriminalRecordsMutation(ent, compUid, msg);
                return;
        }
    }

    private void HandleCriminalRecordsMutation(
        Entity<CriminalRecordsProgramComponent> ent,
        EntityUid compUid,
        CriminalRecordsActionMessage msg)
    {
        if (!CanModifyCriminalRecords(compUid, msg.Actor))
        {
            ent.Comp.StatusMessage = "Access denied: requires Security clearance or an unlocked Criminal Records console.";
            Dirty(ent);
            SendCriminalRecordsFromLocalCache(ent.Owner, compUid, ent.Comp, true, ent.Comp.StatusMessage, msg.Actor);
            return;
        }

        var station = _station.GetOwningStation(compUid) ?? _station.GetStationsSet().FirstOrDefault();
        if (!_dataServer.TryGetLocalRecordCopyFromHost(
                compUid,
                "StationRecordsService",
                CriminalRecordsProgramComponent.CacheFilePath,
                station,
                out var hostCompUid,
                out var hostCopy,
                out var netErr))
        {
            ent.Comp.StatusMessage = $"Write failed: {netErr}";
            Dirty(ent);
            SendCriminalRecordsFromLocalCache(ent.Owner, compUid, ent.Comp, false, ent.Comp.StatusMessage, msg.Actor);
            return;
        }

        if (string.IsNullOrWhiteSpace(msg.Key) ||
            !hostCopy.Data.TryGetValue(msg.Key, out var rawRow) ||
            !RecordsDbCodec.TryDecodeCriminalRecordRow(msg.Key, rawRow, out var cur))
        {
            ent.Comp.StatusMessage = "Select a valid personnel record first.";
            Dirty(ent);
            SendCriminalRecordsFromLocalCache(ent.Owner, compUid, ent.Comp, true, ent.Comp.StatusMessage, msg.Actor);
            return;
        }

        var actorName = msg.Actor.IsValid() && Exists(msg.Actor) ? MetaData(msg.Actor).EntityName : "Security";
        var newStatus = cur.Status;
        var newReason = cur.Reason;
        var newInitiator = cur.Initiator;
        var newHistory = new List<CrimeHistoryEntryDto>(cur.History);
        string summary;

        if (msg.Action == CriminalRecordsAction.SetStatus)
        {
            newStatus = msg.Status;
            newReason = newStatus == SecurityStatus.None ? string.Empty : RecordsDbCodec.SanitizeField(msg.Text);
            newInitiator = newStatus == SecurityStatus.None ? string.Empty : actorName;
            summary = $"Set {cur.Name} status -> {newStatus}";
        }
        else if (msg.Action == CriminalRecordsAction.AddHistory)
        {
            var note = RecordsDbCodec.SanitizeField(msg.Text);
            if (string.IsNullOrWhiteSpace(note))
            {
                ent.Comp.StatusMessage = "Enter a crime history note first.";
                Dirty(ent);
                SendCriminalRecordsFromLocalCache(ent.Owner, compUid, ent.Comp, true, ent.Comp.StatusMessage, msg.Actor);
                return;
            }

            var ts = TimeSpan.FromSeconds(Math.Truncate(_timing.CurTime.TotalSeconds)).ToString(@"hh\:mm\:ss");
            newHistory.Add(new CrimeHistoryEntryDto(newHistory.Count, ts, note, actorName));
            summary = $"Added crime note for {cur.Name}.";
        }
        else
        {
            if (msg.HistoryIndex < 0 || msg.HistoryIndex >= newHistory.Count)
            {
                ent.Comp.StatusMessage = "Invalid history entry.";
                Dirty(ent);
                SendCriminalRecordsFromLocalCache(ent.Owner, compUid, ent.Comp, true, ent.Comp.StatusMessage, msg.Actor);
                return;
            }

            newHistory.RemoveAt(msg.HistoryIndex);
            summary = $"Removed history entry for {cur.Name}.";
        }

        hostCopy.Data[msg.Key] = RecordsDbCodec.EncodeCriminalRecordRow(
            cur.Name, cur.JobTitle, newStatus, newReason, newInitiator, newHistory);
        hostCopy.Size = Math.Max(128, hostCopy.Data.Count * 96);

        if (!_dataServer.TryWriteRecordCopyToHost(compUid, "StationRecordsService", CriminalRecordsProgramComponent.CacheFilePath, hostCopy, out hostCompUid, out var writeErr))
        {
            ent.Comp.StatusMessage = $"Write failed: {writeErr}";
            Dirty(ent);
            SendCriminalRecordsFromLocalCache(ent.Owner, compUid, ent.Comp, false, ent.Comp.StatusMessage, msg.Actor);
            return;
        }

        // Mirror active wanted/detained/suspected entries to /records/wanted_list.db on DataServer!
        SyncWantedListDbFromCriminalDb(compUid, hostCopy);

        // Sync live Security HUD icon & radio status on StationRecordsComponent if this crewmember exists in StationRecords.
        SyncLiveCriminalRecord(station, cur.Name, msg.Action, newStatus, newReason, actorName, msg.Text, msg.HistoryIndex);

        var pkt = BuildCriminalRecordsPacket(compUid, hostCompUid, hostCopy, $"WRITE: {summary}");
        _router.RecordAndValidatePacket(compUid, hostCompUid, pkt);
        WriteLocalCache(compUid, hostCopy);

        ent.Comp.SelectedKey = msg.Key;
        ent.Comp.StatusMessage = summary;
        Dirty(ent);
        SendCriminalRecordsFromLocalCache(ent.Owner, compUid, ent.Comp, true, summary, msg.Actor);
    }

    private void SyncWantedListDbFromCriminalDb(EntityUid compUid, DatabaseVirtualFile criminalDb)
    {
        var wantedData = new Dictionary<string, string>();
        var idx = 0;
        foreach (var (k, v) in criminalDb.Data.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!k.StartsWith("crim_"))
                continue;
            if (!RecordsDbCodec.TryDecodeCriminalRecordRow(k, v, out var dto))
                continue;
            if (dto.Status == SecurityStatus.None && dto.History.Count == 0)
                continue;

            wantedData[$"rec_{idx++}"] = $"{dto.Name}\t{dto.JobTitle}\t{dto.Status}\t{dto.Reason}\t{dto.Initiator}";
        }

        var wantedFile = new DatabaseVirtualFile
        {
            Name = "wanted_list",
            Extension = "db",
            Directory = "/records/",
            TableName = "wanted_list",
            Data = wantedData,
            Size = Math.Max(128, wantedData.Count * 64)
        };
        _dataServer.TryWriteRecordCopyToHost(compUid, "StationRecordsService", WantedListProgramComponent.CacheFilePath, wantedFile, out _, out _);
    }

    private void SyncLiveCriminalRecord(
        EntityUid station,
        string characterName,
        CriminalRecordsAction action,
        SecurityStatus newStatus,
        string newReason,
        string initiatorName,
        string rawText,
        int historyIndex)
    {
        if (station == EntityUid.Invalid)
            return;

        var recId = _stationRecords.GetRecordByName(station, characterName);
        if (recId == null)
            return;

        var key = new StationRecordKey(recId.Value, station);
        switch (action)
        {
            case CriminalRecordsAction.SetStatus:
                _criminalRecords.TryChangeStatus(key, newStatus, string.IsNullOrWhiteSpace(newReason) ? null : newReason, initiatorName);
                break;

            case CriminalRecordsAction.AddHistory:
                if (!string.IsNullOrWhiteSpace(rawText))
                    _criminalRecords.TryAddHistory(key, rawText.Trim(), initiatorName);
                break;

            case CriminalRecordsAction.DeleteHistory:
                if (historyIndex >= 0)
                    _criminalRecords.TryDeleteHistory(key, (uint) historyIndex);
                break;
        }
    }

    private void SyncCriminalRecords(EntityUid progUid, EntityUid compUid, CriminalRecordsProgramComponent comp, EntityUid? actor)
    {
        var online = false;
        var statusMsg = string.Empty;
        var station = _station.GetOwningStation(compUid) ?? _station.GetStationsSet().FirstOrDefault();

        if (_router.HasActiveNetworkCard(compUid) && _router.IsNetworkConnected(compUid))
        {
            if (_dataServer.TryGetLocalRecordCopyFromHost(
                    compUid,
                    "StationRecordsService",
                    CriminalRecordsProgramComponent.CacheFilePath,
                    station,
                    out var srvCompUid,
                    out var serverCopy,
                    out var netErr))
            {
                var pkt = BuildCriminalRecordsPacket(srvCompUid, compUid, serverCopy, $"Criminal Records [{MetaData(srvCompUid).EntityName}]");
                if (_router.RecordAndValidatePacket(srvCompUid, compUid, pkt))
                {
                    WriteLocalCache(compUid, serverCopy);
                    online = true;
                    statusMsg = $"Synced from {MetaData(srvCompUid).EntityName} [{_router.EnsureDeviceGuid(srvCompUid)}] -> {CriminalRecordsProgramComponent.CacheFilePath}";
                }
            }
            else
            {
                statusMsg = $"{netErr} — showing local {CriminalRecordsProgramComponent.CacheFilePath}.";
            }
        }
        else
        {
            statusMsg = $"Offline / no NIC — reading local {CriminalRecordsProgramComponent.CacheFilePath}.";
        }

        comp.StatusMessage = statusMsg;
        Dirty(progUid, comp);
        SendCriminalRecordsFromLocalCache(progUid, compUid, comp, online, statusMsg, actor);
    }

    public void SendCriminalRecordsFromLocalCache(
        EntityUid progUid,
        EntityUid compUid,
        CriminalRecordsProgramComponent comp,
        bool online,
        string statusMsg,
        EntityUid? actor)
    {
        _fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs);

        var entries = new List<CriminalRecordEntryDto>();
        TimeSpan? ts = null;

        if (driveUid != null && fs != null &&
            _fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid.Value, CriminalRecordsProgramComponent.CacheFilePath, out var db, fs))
        {
            ts = db.LastModified;
            foreach (var (k, v) in db.Data.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (!k.StartsWith("crim_"))
                    continue;
                if (RecordsDbCodec.TryDecodeCriminalRecordRow(k, v, out var dto))
                    entries.Add(dto);
            }
        }

        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        var selected = comp.SelectedKey;
        if (string.IsNullOrEmpty(selected) && entries.Count > 0)
            selected = entries[0].Key;

        var canEdit = online && CanModifyCriminalRecords(compUid, actor);

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
        {
            _ui.SetUiState(progUid, ProgramUiKey.Key, new CriminalRecordsUiState(
                online,
                canEdit,
                statusMsg,
                ts,
                selected,
                entries));
        }
    }

    public void RefreshRecordProgramsFromLocalCache(EntityUid compUid, string filePath)
    {
        if (filePath.Equals(StationRecordsProgramComponent.CacheFilePath, StringComparison.OrdinalIgnoreCase))
        {
            var q = EntityQueryEnumerator<StationRecordsProgramComponent, ProgramComponent>();
            while (q.MoveNext(out var progUid, out var comp, out var prog))
            {
                if (prog.Computer == compUid)
                    SendStationRecordsFromLocalCache(progUid, compUid, comp, true, $"Updated from network packet -> {filePath}", null);
            }
        }
        else if (filePath.Equals(CriminalRecordsProgramComponent.CacheFilePath, StringComparison.OrdinalIgnoreCase))
        {
            var q = EntityQueryEnumerator<CriminalRecordsProgramComponent, ProgramComponent>();
            while (q.MoveNext(out var progUid, out var comp, out var prog))
            {
                if (prog.Computer == compUid)
                    SendCriminalRecordsFromLocalCache(progUid, compUid, comp, true, $"Updated from network packet -> {filePath}", null);
            }
        }
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private bool CanModifyStationRecords(EntityUid compUid, EntityUid? actor)
    {
        if (_computerQuery.TryComp(compUid, out var comp) && comp.HostMachine is { } hostUid)
        {
            if (HasComp<LockComponent>(hostUid))
                return !_lock.IsLocked(hostUid);
        }

        if (actor is { Valid: true } user && Exists(user))
        {
            var tags = _accessReader.FindAccessTags(user);
            if (tags.Contains(CommandAccess) || tags.Contains(HeadOfPersonnelAccess) || tags.Contains(SecurityAccess))
                return true;
        }

        // Allow if the computer is a records console or has a /keys/*.acc file granting Command/HoP/Security.
        if (_fileSystem.TryFindFileSystem(compUid, out _, out var fs))
        {
            foreach (var f in fs.Files.Values)
            {
                if (f is AccessCodesVirtualFile acc &&
                    (acc.AccessTags.Contains(CommandAccess) || acc.AccessTags.Contains(HeadOfPersonnelAccess) || acc.AccessTags.Contains(SecurityAccess)))
                {
                    return true;
                }
            }
        }

        // Standard unlocked Station Records consoles allow personnel viewing & editing.
        return _computerQuery.TryComp(compUid, out var c2) &&
               string.Equals(c2.EmbeddedDefaultAppPath, "/bin/station_records.nte", StringComparison.OrdinalIgnoreCase);
    }

    private bool CanModifyCriminalRecords(EntityUid compUid, EntityUid? actor)
    {
        if (_computerQuery.TryComp(compUid, out var comp) && comp.HostMachine is { } hostUid)
        {
            if (HasComp<LockComponent>(hostUid))
                return !_lock.IsLocked(hostUid);
        }

        if (actor is { Valid: true } user && Exists(user))
        {
            var tags = _accessReader.FindAccessTags(user);
            if (tags.Contains(SecurityAccess) || tags.Contains(CommandAccess))
                return true;
        }

        if (_fileSystem.TryFindFileSystem(compUid, out _, out var fs))
        {
            foreach (var f in fs.Files.Values)
            {
                if (f is AccessCodesVirtualFile acc &&
                    (acc.AccessTags.Contains(SecurityAccess) || acc.AccessTags.Contains(CommandAccess)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void WriteLocalCache(EntityUid compUid, DatabaseVirtualFile serverCopy)
    {
        if (!_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
            return;

        var localFile = (DatabaseVirtualFile) serverCopy.Clone();
        localFile.Data.Remove("__tampered");
        localFile.Data.Remove("__source_hash");
        _fileSystem.TryWriteFile(driveUid.Value, localFile, fs);
    }

    private NetworkDataPacket BuildStationRecordsPacket(EntityUid srcComp, EntityUid dstComp, DatabaseVirtualFile db, string summary)
    {
        var editable = new Dictionary<string, string>();
        foreach (var (k, v) in db.Data)
        {
            if (k.StartsWith("rec_"))
                editable[k] = v;
        }

        return new NetworkDataPacket
        {
            PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            SourceAddress = _router.EnsureDeviceGuid(srcComp),
            TargetAddress = _router.EnsureDeviceGuid(dstComp),
            Protocol = "TCP/STATIONREC",
            PayloadSummary = $"{summary} ({editable.Count} records)",
            RawPayload = string.Join("; ", editable.Select(kv => $"{kv.Key}={kv.Value}")),
            IsPlayerRelevant = true,
            IsEditable = true,
            FilePath = StationRecordsProgramComponent.CacheFilePath,
            EditableEntries = editable,
            ByteSize = Math.Max(128, db.Size),
            Timestamp = _timing.CurTime
        };
    }

    private NetworkDataPacket BuildCriminalRecordsPacket(EntityUid srcComp, EntityUid dstComp, DatabaseVirtualFile db, string summary)
    {
        var editable = new Dictionary<string, string>();
        foreach (var (k, v) in db.Data)
        {
            if (k.StartsWith("crim_"))
                editable[k] = v;
        }

        return new NetworkDataPacket
        {
            PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            SourceAddress = _router.EnsureDeviceGuid(srcComp),
            TargetAddress = _router.EnsureDeviceGuid(dstComp),
            Protocol = "TCP/CRIMINAL",
            PayloadSummary = $"{summary} ({editable.Count} records)",
            RawPayload = string.Join("; ", editable.Select(kv => $"{kv.Key}={kv.Value}")),
            IsPlayerRelevant = true,
            IsEditable = true,
            FilePath = CriminalRecordsProgramComponent.CacheFilePath,
            EditableEntries = editable,
            ByteSize = Math.Max(128, db.Size),
            Timestamp = _timing.CurTime
        };
    }

    private static int FindNextKey(Dictionary<string, string> data, string prefix)
    {
        var max = -1;
        foreach (var k in data.Keys)
        {
            if (!k.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            if (int.TryParse(k[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var num) && num > max)
                max = num;
        }
        return max + 1;
    }
}
