using System.Globalization;
using System.Linq;
using Content.Server._Oxyd.Computers.Programs;
using Content.Server._Oxyd.Computers.Servers;
using Content.Server.CriminalRecords.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.Cameras;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared._Oxyd.Computers.Servers;
using Content.Shared.CriminalRecords;
using Content.Shared.CriminalRecords.Systems;
using Content.Shared.Forensics.Components;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Security;
using Content.Shared.StationRecords;
using Content.Shared.StationRecords.Events;
using Content.Shared.StationRecords.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.Network;

/// <summary>
/// Server runtime for the Station Network Core OS (<see cref="NetworkCoreOsProgramComponent"/>)
/// running on <c>StationNetworkRouter</c>.
/// Automatically appends newly spawned/joined crew to authoritative DataServer VFS databases
/// (<c>/records/crew_manifest.db</c>, <c>/records/station_records.db</c>, <c>/records/criminal_records.db</c>)
/// without overwriting existing/edited/tampered rows, and removes departed crew from untampered tables.
/// </summary>
public sealed partial class NetworkCoreOsSystem : EntitySystem
{
    [Dependency] private OxydComputerSystem _computer = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private NetworkRouterSystem _router = default!;
    [Dependency] private DataServerSystem _dataServer = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private ServerStationSystem _station = default!;
    [Dependency] private StationRecordsSystem _records = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    [Dependency] private EntityQuery<ProgramComponent> _programQuery = default!;
    [Dependency] private EntityQuery<OxydComputerComponent> _computerQuery = default!;
    [Dependency] private EntityQuery<ComputerStatsComponent> _statsQuery = default!;
    [Dependency] private EntityQuery<NetworkRouterComponent> _routerQuery = default!;
    [Dependency] private EntityQuery<DataServerComponent> _dataServerQuery = default!;

    private const int MaxSyncLogs = 40;
    private const string DefaultRouterAppPath = "/bin/router.nte";

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<NetworkCoreOsProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnBuiOpened);
            subs.Event<NetworkCoreOsActionMessage>(OnActionMessage);
        });
    }

    [SubscribeLocalEvent]
    private void OnStartup(Entity<NetworkCoreOsProgramComponent> ent, ref ProgramStartupEvent args)
    {
        if (!ent.Comp.DefaultAppsLaunched)
        {
            ent.Comp.DefaultAppsLaunched = true;
            _computer.TryLaunchExecutable(args.ComputerUid, DefaultRouterAppPath, out _, out _);
        }

        AppendSyncLog(ent.Comp, "Network Core OS initialized. Automatic record provisioning armed.");
        UpdateCoreUi(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnActivated(Entity<NetworkCoreOsProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdateCoreUi(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnComputerHardwareChanged(Entity<NetworkCoreOsProgramComponent> ent, ref ComputerHardwareChangedEvent args)
    {
        UpdateCoreUi(ent.Owner);
    }

    private void OnBuiOpened(Entity<NetworkCoreOsProgramComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateCoreUi(ent.Owner);
    }

    private void OnActionMessage(Entity<NetworkCoreOsProgramComponent> ent, ref NetworkCoreOsActionMessage msg)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computer.CanExecuteProgramAction(ent.Owner, msg.Actor, out _))
            return;

        switch (msg.Action)
        {
            case NetworkCoreOsAction.Refresh:
                if (TryResolveRouterHost(compUid, out var rEnt))
                {
                    _router.RefreshDnsDirectory(rEnt);
                    _router.RefreshRouterDevices(rEnt);
                }
                break;

            case NetworkCoreOsAction.SetTab:
                ent.Comp.CurrentTab = Math.Clamp(msg.TabIndex, 0, 2);
                Dirty(ent);
                break;

            case NetworkCoreOsAction.ToggleAutoSync:
                ent.Comp.AutoSyncRecords = !ent.Comp.AutoSyncRecords;
                AppendSyncLog(ent.Comp, ent.Comp.AutoSyncRecords
                    ? "Auto-provisioning of joining crew ENABLED."
                    : "Auto-provisioning of joining crew PAUSED.");
                Dirty(ent);
                break;

            case NetworkCoreOsAction.ForceSyncAllRecords:
                var station = _station.GetOwningStation(compUid) ?? _station.GetStationsSet().FirstOrDefault();
                var added = SyncAllStationRecordsToDataServers(ent, compUid, station);
                ent.Comp.LastSyncSummary = $"Manual resync completed ({added} new entries provisioned).";
                AppendSyncLog(ent.Comp, ent.Comp.LastSyncSummary);
                Dirty(ent);
                break;

            case NetworkCoreOsAction.LaunchProgram:
                if (_computer.TryLaunchExecutable(compUid, msg.ExecutablePath, out _, out _, msg.Actor))
                {
                    ent.Comp.CurrentTab = 1;
                    Dirty(ent);
                }
                break;

            case NetworkCoreOsAction.SwitchProgram:
                if (msg.ProgramEntity is { } targetNet && TryGetEntity(targetNet, out var target))
                {
                    if (_computer.SwitchActiveProgram(compUid, target.Value, actor: msg.Actor))
                    {
                        ent.Comp.CurrentTab = 1;
                        Dirty(ent);
                    }
                }
                break;

            case NetworkCoreOsAction.CloseProgram:
                if (msg.ProgramEntity is { } closeNet && TryGetEntity(closeNet, out var closeTarget))
                {
                    _computer.CloseProgram(compUid, closeTarget.Value);
                }
                break;

            case NetworkCoreOsAction.ShutDown:
                _computer.ShutDownComputer(compUid, manualShutdown: true);
                return;
        }

        UpdateCoreUi(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (TerminatingOrDeleted(ev.Mob) || ev.Station == EntityUid.Invalid)
            return;

        var core = FindOperationalAutoSyncCore();
        if (core == null)
            return;

        var (coreProgEnt, coreCompUid, routerHostUid) = core.Value;
        var profile = ev.Profile;
        var name = !string.IsNullOrWhiteSpace(profile?.Name) ? profile.Name : MetaData(ev.Mob).EntityName;
        var age = profile?.Age ?? 25;
        var species = profile?.Species ?? "Human";
        var gender = profile?.Gender.ToString() ?? "Epicene";
        var jobProtoId = ev.JobId ?? string.Empty;
        var jobTitle = "Passenger";
        var jobIcon = "JobIconUnknown";
        uint? existingRecordId = null;

        if (!string.IsNullOrWhiteSpace(jobProtoId) && _proto.TryIndex<JobPrototype>(jobProtoId, out var jobProto))
        {
            jobTitle = jobProto.LocalizedName;
            jobIcon = jobProto.Icon;
        }

        var fingerprint = TryComp<FingerprintComponent>(ev.Mob, out var fp) ? fp.Fingerprint ?? string.Empty : string.Empty;
        var dna = TryComp<DnaComponent>(ev.Mob, out var dnaComp) ? dnaComp.DNA ?? string.Empty : string.Empty;

        // If StationRecordsSystem already created a record for this player, reuse its exact ID & data.
        var existingId = _records.GetRecordByName(ev.Station, name);
        if (existingId != null)
        {
            existingRecordId = existingId.Value;
            var key = new StationRecordKey(existingId.Value, ev.Station);
            if (_records.TryGetRecord(key, out GeneralStationRecord? gen))
            {
                name = gen.Name;
                jobTitle = gen.JobTitle;
                jobProtoId = gen.JobPrototype;
                jobIcon = gen.JobIcon;
                age = gen.Age;
                species = gen.Species;
                gender = gen.Gender.ToString();
                if (!string.IsNullOrEmpty(gen.Fingerprint))
                    fingerprint = gen.Fingerprint;
                if (!string.IsNullOrEmpty(gen.DNA))
                    dna = gen.DNA;
            }
        }

        var appended = AppendCrewEntryToDataServers(
            coreProgEnt,
            coreCompUid,
            routerHostUid,
            ev.Station,
            existingRecordId,
            name,
            jobTitle,
            jobProtoId,
            jobIcon,
            age,
            species,
            gender,
            fingerprint,
            dna);

        if (appended)
        {
            coreProgEnt.Comp.AutoSyncCount++;
            coreProgEnt.Comp.LastSyncSummary = $"Auto-provisioned {name} ({jobTitle})";
            AppendSyncLog(coreProgEnt.Comp, $"[JOIN] Appended {name} ({jobTitle}) to DataServer records.");
            Dirty(coreProgEnt);
            UpdateCoreUi(coreProgEnt.Owner);
        }
    }

    [SubscribeLocalEvent]
    private void OnRecordModified(ref RecordModifiedEvent ev)
    {
        var core = FindOperationalAutoSyncCore();
        if (core == null)
            return;

        if (!_records.TryGetRecord(ev.Key, out GeneralStationRecord? gen))
            return;

        var (coreProgEnt, coreCompUid, routerHostUid) = core.Value;
        UpdateOrAppendRecordAcrossServers(
            coreCompUid,
            routerHostUid,
            ev.Key.OriginStation,
            ev.Key.Id,
            gen,
            overwriteIfUntampered: true);

        AppendSyncLog(coreProgEnt.Comp, $"[SYNC] Updated record for {gen.Name} ({gen.JobTitle}).");
        Dirty(coreProgEnt);
        UpdateCoreUi(coreProgEnt.Owner);
    }

    [SubscribeLocalEvent]
    private void OnCriminalRecordChanged(ref CriminalRecordChangedEvent ev)
    {
        var core = FindOperationalAutoSyncCore();
        if (core == null)
            return;

        var (coreProgEnt, coreCompUid, routerHostUid) = core.Value;
        if (!_dataServer.TryGetCoreRecordDrive(routerHostUid, "StationRecordsService", out var hostCompUid, out var driveUid, out var fs))
            return;

        // If the criminal records file is not tampered, keep it aligned with live console changes.
        if (fs.Files.TryGetValue(CriminalRecordsProgramComponent.CacheFilePath, out var raw) &&
            raw is DatabaseVirtualFile crimDb &&
            (!crimDb.Data.TryGetValue("__tampered", out var tVal) || tVal != "1"))
        {
            var station = _station.GetOwningStation(routerHostUid) ?? _station.GetStationsSet().FirstOrDefault();
            if (station != EntityUid.Invalid)
            {
                foreach (var (recId, crim) in _records.GetRecordsOfType<CriminalRecord>(station))
                {
                    if (crim != ev.Record)
                        continue;

                    var key = new StationRecordKey(recId, station);
                    if (!_records.TryGetRecord(key, out GeneralStationRecord? gen))
                        break;

                    var hist = new List<CrimeHistoryEntryDto>();
                    for (var i = 0; i < crim.History.Count; i++)
                    {
                        var h = crim.History[i];
                        var ts = TimeSpan.FromSeconds(Math.Truncate(h.AddTime.TotalSeconds)).ToString(@"hh\:mm\:ss");
                        hist.Add(new CrimeHistoryEntryDto(i, ts, h.Crime, h.InitiatorName ?? string.Empty));
                    }

                    crimDb.Data[$"crim_{recId}"] = RecordsDbCodec.EncodeCriminalRecordRow(
                        gen.Name,
                        gen.JobTitle,
                        crim.Status,
                        crim.Reason ?? string.Empty,
                        crim.InitiatorName ?? string.Empty,
                        hist);
                    crimDb.Size = Math.Max(128, crimDb.Data.Count * 96);
                    _fileSystem.TryWriteFile(driveUid.Value, crimDb, fs);
                    break;
                }
            }
        }

        UpdateCoreUi(coreProgEnt.Owner);
    }

    [SubscribeLocalEvent]
    private void OnRecordRemoved(ref RecordRemovedEvent ev)
    {
        var core = FindOperationalAutoSyncCore();
        if (core == null)
            return;

        var (coreProgEnt, _, routerHostUid) = core.Value;
        var keySuffix = ev.Key.Id.ToString(CultureInfo.InvariantCulture);
        string? removedName = null;

        if (_dataServer.TryGetCoreRecordDrive(routerHostUid, "StationRecordsService", out _, out var recDrive, out var recFs))
        {
            if (recFs.Files.TryGetValue(StationRecordsProgramComponent.CacheFilePath, out var rawSt) &&
                rawSt is DatabaseVirtualFile stDb &&
                (!stDb.Data.TryGetValue("__tampered", out var stTamp) || stTamp != "1"))
            {
                if (stDb.Data.TryGetValue($"rec_{keySuffix}", out var oldRow))
                {
                    var cols = oldRow.Split('\t');
                    if (cols.Length > 0)
                        removedName = cols[0];
                    stDb.Data.Remove($"rec_{keySuffix}");
                    _fileSystem.TryWriteFile(recDrive.Value, stDb, recFs);
                }
            }

            if (recFs.Files.TryGetValue(CriminalRecordsProgramComponent.CacheFilePath, out var rawCr) &&
                rawCr is DatabaseVirtualFile crDb &&
                (!crDb.Data.TryGetValue("__tampered", out var crTamp) || crTamp != "1"))
            {
                if (crDb.Data.Remove($"crim_{keySuffix}"))
                    _fileSystem.TryWriteFile(recDrive.Value, crDb, recFs);
            }
        }

        if (!string.IsNullOrEmpty(removedName) &&
            _dataServer.TryGetCoreRecordDrive(routerHostUid, "ManifestService", out _, out var manDrive, out var manFs) &&
            manFs.Files.TryGetValue(CrewManifestProgramComponent.CacheFilePath, out var rawMan) &&
            rawMan is DatabaseVirtualFile manDb &&
            (!manDb.Data.TryGetValue("__tampered", out var manTamp) || manTamp != "1"))
        {
            var keysToRemove = manDb.Data
                .Where(kv => kv.Key.StartsWith("row_") && kv.Value.StartsWith(removedName + "\t", StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();
            if (keysToRemove.Count > 0)
            {
                foreach (var k in keysToRemove)
                    manDb.Data.Remove(k);
                _fileSystem.TryWriteFile(manDrive.Value, manDb, manFs);
            }
        }

        if (!string.IsNullOrEmpty(removedName))
        {
            AppendSyncLog(coreProgEnt.Comp, $"[DEPART] Removed {removedName} from untampered records.");
            Dirty(coreProgEnt);
            UpdateCoreUi(coreProgEnt.Owner);
        }
    }

    public int SyncAllStationRecordsToDataServers(Entity<NetworkCoreOsProgramComponent> coreProgEnt, EntityUid coreCompUid, EntityUid station)
    {
        if (station == EntityUid.Invalid)
            return 0;

        var routerHostUid = _computerQuery.TryComp(coreCompUid, out var comp) && comp.HostMachine is { } hm ? hm : coreCompUid;
        var added = 0;

        foreach (var (recId, gen) in _records.GetRecordsOfType<GeneralStationRecord>(station))
        {
            if (AppendCrewEntryToDataServers(
                coreProgEnt,
                coreCompUid,
                routerHostUid,
                station,
                recId,
                gen.Name,
                gen.JobTitle,
                gen.JobPrototype,
                gen.JobIcon,
                gen.Age,
                gen.Species,
                gen.Gender.ToString(),
                gen.Fingerprint ?? string.Empty,
                gen.DNA ?? string.Empty))
            {
                added++;
            }
        }

        return added;
    }

    private bool AppendCrewEntryToDataServers(
        Entity<NetworkCoreOsProgramComponent> coreProgEnt,
        EntityUid coreCompUid,
        EntityUid routerHostUid,
        EntityUid station,
        uint? recordId,
        string name,
        string jobTitle,
        string jobProto,
        string jobIcon,
        int age,
        string species,
        string gender,
        string fingerprint,
        string dna)
    {
        var anyAppended = false;
        var stationName = Exists(station) ? MetaData(station).EntityName : "Unknown Station";

        // 1) Incrementally append to /records/crew_manifest.db (preserving existing / tampered rows!)
        if (_dataServer.TryGetCoreRecordDrive(routerHostUid, "ManifestService", out var manHostComp, out var manDrive, out var manFs))
        {
            var manifestDb = LoadOrInitDb(manDrive.Value, manFs, CrewManifestProgramComponent.CacheFilePath, "crew_manifest", "/records/", "crew_manifest");
            if (!manifestDb.Data.ContainsKey("__station"))
                manifestDb.Data["__station"] = stationName;

            var alreadyInManifest = manifestDb.Data.Any(kv =>
                kv.Key.StartsWith("row_") &&
                kv.Value.StartsWith(name + "\t", StringComparison.OrdinalIgnoreCase));

            if (!alreadyInManifest)
            {
                var nextIdx = FindNextNumericKey(manifestDb.Data, "row_");
                manifestDb.Data[$"row_{nextIdx}"] = $"{RecordsDbCodec.SanitizeField(name)}\t{RecordsDbCodec.SanitizeField(jobTitle)}\t{RecordsDbCodec.SanitizeField(jobProto)}\t{RecordsDbCodec.SanitizeField(jobIcon)}";
                manifestDb.Data.Remove("__source_hash");
                manifestDb.Size = Math.Max(128, manifestDb.Data.Count * 64);
                if (_fileSystem.TryWriteFile(manDrive.Value, manifestDb, manFs))
                {
                    anyAppended = true;
                    EmitCoreSyncPacket(coreCompUid, manHostComp, CrewManifestProgramComponent.CacheFilePath, $"APPEND MANIFEST: {name} ({jobTitle})", name, jobTitle);
                }
            }
        }

        // 2) Incrementally append to /records/station_records.db & /records/criminal_records.db
        if (_dataServer.TryGetCoreRecordDrive(routerHostUid, "StationRecordsService", out var recHostComp, out var recDrive, out var recFs))
        {
            var stDb = LoadOrInitDb(recDrive.Value, recFs, StationRecordsProgramComponent.CacheFilePath, "station_records", "/records/", "station_records");
            if (!stDb.Data.ContainsKey("__station"))
                stDb.Data["__station"] = stationName;

            var recKey = recordId.HasValue
                ? $"rec_{recordId.Value.ToString(CultureInfo.InvariantCulture)}"
                : $"rec_{FindNextNumericKey(stDb.Data, "rec_").ToString(CultureInfo.InvariantCulture)}";

            var alreadyInRecords = stDb.Data.ContainsKey(recKey) || stDb.Data.Any(kv =>
                kv.Key.StartsWith("rec_") &&
                kv.Value.StartsWith(name + "\t", StringComparison.OrdinalIgnoreCase));

            if (!alreadyInRecords)
            {
                stDb.Data[recKey] = RecordsDbCodec.EncodeStationRecordRow(
                    name, jobTitle, jobProto, jobIcon, age, species, gender, fingerprint, dna);
                stDb.Data.Remove("__source_hash");
                stDb.Size = Math.Max(128, stDb.Data.Count * 96);
                if (_fileSystem.TryWriteFile(recDrive.Value, stDb, recFs))
                {
                    anyAppended = true;
                    EmitCoreSyncPacket(coreCompUid, recHostComp, StationRecordsProgramComponent.CacheFilePath, $"APPEND RECORD: {name} ({jobTitle})", name, jobTitle);
                }
            }

            var crimDb = LoadOrInitDb(recDrive.Value, recFs, CriminalRecordsProgramComponent.CacheFilePath, "criminal_records", "/records/", "criminal_records");
            var crimKey = recordId.HasValue
                ? $"crim_{recordId.Value.ToString(CultureInfo.InvariantCulture)}"
                : $"crim_{FindNextNumericKey(crimDb.Data, "crim_").ToString(CultureInfo.InvariantCulture)}";

            var alreadyInCrim = crimDb.Data.ContainsKey(crimKey) || crimDb.Data.Any(kv =>
                kv.Key.StartsWith("crim_") &&
                kv.Value.StartsWith(name + "\t", StringComparison.OrdinalIgnoreCase));

            if (!alreadyInCrim)
            {
                var initialStatus = SecurityStatus.None;
                var initialReason = string.Empty;
                var initialInit = string.Empty;
                var initialHist = new List<CrimeHistoryEntryDto>();
                if (recordId.HasValue && _records.TryGetRecord(new StationRecordKey(recordId.Value, station), out CriminalRecord? liveCrim))
                {
                    initialStatus = liveCrim.Status;
                    initialReason = liveCrim.Reason ?? string.Empty;
                    initialInit = liveCrim.InitiatorName ?? string.Empty;
                }

                crimDb.Data[crimKey] = RecordsDbCodec.EncodeCriminalRecordRow(
                    name, jobTitle, initialStatus, initialReason, initialInit, initialHist);
                crimDb.Data.Remove("__source_hash");
                crimDb.Size = Math.Max(128, crimDb.Data.Count * 96);
                if (_fileSystem.TryWriteFile(recDrive.Value, crimDb, recFs))
                {
                    anyAppended = true;
                    EmitCoreSyncPacket(coreCompUid, recHostComp, CriminalRecordsProgramComponent.CacheFilePath, $"APPEND CRIMINAL: {name} [{initialStatus}]", name, initialStatus.ToString());
                }
            }
        }

        return anyAppended;
    }

    private void UpdateOrAppendRecordAcrossServers(
        EntityUid coreCompUid,
        EntityUid routerHostUid,
        EntityUid station,
        uint recordId,
        GeneralStationRecord gen,
        bool overwriteIfUntampered)
    {
        if (_dataServer.TryGetCoreRecordDrive(routerHostUid, "StationRecordsService", out _, out var recDrive, out var recFs))
        {
            var stDb = LoadOrInitDb(recDrive.Value, recFs, StationRecordsProgramComponent.CacheFilePath, "station_records", "/records/", "station_records");
            var isTampered = stDb.Data.TryGetValue("__tampered", out var tVal) && tVal == "1";
            var recKey = $"rec_{recordId.ToString(CultureInfo.InvariantCulture)}";
            if (!isTampered && (overwriteIfUntampered || !stDb.Data.ContainsKey(recKey)))
            {
                stDb.Data[recKey] = RecordsDbCodec.EncodeStationRecordRow(
                    gen.Name, gen.JobTitle, gen.JobPrototype, gen.JobIcon,
                    gen.Age, gen.Species, gen.Gender.ToString(),
                    gen.Fingerprint ?? string.Empty, gen.DNA ?? string.Empty);
                stDb.Data.Remove("__source_hash");
                stDb.Size = Math.Max(128, stDb.Data.Count * 96);
                _fileSystem.TryWriteFile(recDrive.Value, stDb, recFs);
            }
        }

        if (_dataServer.TryGetCoreRecordDrive(routerHostUid, "ManifestService", out _, out var manDrive, out var manFs))
        {
            var manDb = LoadOrInitDb(manDrive.Value, manFs, CrewManifestProgramComponent.CacheFilePath, "crew_manifest", "/records/", "crew_manifest");
            var isTampered = manDb.Data.TryGetValue("__tampered", out var tVal) && tVal == "1";
            if (!isTampered)
            {
                var existingKey = manDb.Data
                    .Where(kv => kv.Key.StartsWith("row_") && kv.Value.StartsWith(gen.Name + "\t", StringComparison.OrdinalIgnoreCase))
                    .Select(kv => kv.Key)
                    .FirstOrDefault();

                var key = existingKey ?? $"row_{FindNextNumericKey(manDb.Data, "row_")}";
                manDb.Data[key] = $"{RecordsDbCodec.SanitizeField(gen.Name)}\t{RecordsDbCodec.SanitizeField(gen.JobTitle)}\t{RecordsDbCodec.SanitizeField(gen.JobPrototype)}\t{RecordsDbCodec.SanitizeField(gen.JobIcon)}";
                manDb.Data.Remove("__source_hash");
                manDb.Size = Math.Max(128, manDb.Data.Count * 64);
                _fileSystem.TryWriteFile(manDrive.Value, manDb, manFs);
            }
        }
    }

    private void EmitCoreSyncPacket(EntityUid coreCompUid, EntityUid hostCompUid, string filePath, string summary, string entryKey, string entryVal)
    {
        var pkt = new NetworkDataPacket
        {
            PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            SourceAddress = _router.EnsureDeviceGuid(coreCompUid),
            TargetAddress = _router.EnsureDeviceGuid(hostCompUid),
            Protocol = "TCP/CORESYNC",
            PayloadSummary = summary,
            RawPayload = $"{entryKey}={entryVal}",
            IsPlayerRelevant = true,
            IsEditable = false,
            FilePath = filePath,
            ByteSize = 192,
            Timestamp = _timing.CurTime
        };

        _router.RecordAndValidatePacket(coreCompUid, hostCompUid, pkt);
    }

    private DatabaseVirtualFile LoadOrInitDb(
        EntityUid driveUid,
        FileSystemComponent fs,
        string path,
        string name,
        string dir,
        string tableName)
    {
        if (_fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid, path, out var existing, fs))
            return (DatabaseVirtualFile) existing.Clone();

        return new DatabaseVirtualFile
        {
            Name = name,
            Extension = "db",
            Directory = dir,
            TableName = tableName,
            Data = new Dictionary<string, string>(),
            Size = 128
        };
    }

    private static int FindNextNumericKey(Dictionary<string, string> data, string prefix)
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

    private (Entity<NetworkCoreOsProgramComponent> Prog, EntityUid CompUid, EntityUid RouterHostUid)? FindOperationalAutoSyncCore()
    {
        (Entity<NetworkCoreOsProgramComponent> Prog, EntityUid CompUid, EntityUid RouterHostUid)? debugFallback = null;
        var query = EntityQueryEnumerator<NetworkCoreOsProgramComponent, ProgramComponent>();
        while (query.MoveNext(out var progUid, out var coreComp, out var prog))
        {
            if (!coreComp.AutoSyncRecords || prog.Computer is not { } compUid)
                continue;

            if (!_computerQuery.TryComp(compUid, out var oxyd) || !oxyd.IsBooted || oxyd.NetworkWireCut)
                continue;

            var hostUid = oxyd.HostMachine is { } hm && Exists(hm) ? hm : compUid;
            if (!_router.IsRouterOperational(hostUid))
                continue;

            if (HasComp<DebugFallbackNetworkEntityComponent>(hostUid) || HasComp<DebugFallbackNetworkEntityComponent>(compUid))
            {
                debugFallback ??= ((progUid, coreComp), compUid, hostUid);
                continue;
            }

            return ((progUid, coreComp), compUid, hostUid);
        }

        return debugFallback;
    }

    private bool TryResolveRouterHost(EntityUid compUid, out Entity<NetworkRouterComponent> router)
    {
        router = default;
        if (_computerQuery.TryComp(compUid, out var oxyd) &&
            oxyd.HostMachine is { } hm &&
            _routerQuery.TryComp(hm, out var rComp))
        {
            router = (hm, rComp);
            return true;
        }

        if (_router.FindNearestRouter(compUid) is { } found)
        {
            router = found;
            return true;
        }

        return false;
    }

    private void AppendSyncLog(NetworkCoreOsProgramComponent comp, string line)
    {
        var ts = TimeSpan.FromSeconds(Math.Truncate(_timing.CurTime.TotalSeconds)).ToString(@"hh\:mm\:ss");
        comp.SyncLogs.Add($"[{ts}] {line}");
        while (comp.SyncLogs.Count > MaxSyncLogs)
            comp.SyncLogs.RemoveAt(0);
    }

    public void UpdateCoreUi(EntityUid progUid)
    {
        if (!TryComp<NetworkCoreOsProgramComponent>(progUid, out var coreComp) ||
            !_programQuery.TryComp(progUid, out var prog) ||
            prog.Computer is not { } compUid ||
            !_computerQuery.TryComp(compUid, out var comp))
        {
            return;
        }

        var stats = _statsQuery.CompOrNull(compUid) ?? EnsureComp<ComputerStatsComponent>(compUid);
        var hostName = comp.HostMachine is { } hm && Exists(hm) ? MetaData(hm).EntityName : MetaData(compUid).EntityName;
        var guid = _router.EnsureDeviceGuid(compUid);

        var dataServerOnline = false;
        var dataServerSummary = "None online (using local Router HDD)";
        var srvQuery = EntityQueryEnumerator<DataServerComponent>();
        var onlineServerNames = new List<string>();
        while (srvQuery.MoveNext(out var sUid, out _))
        {
            if (_dataServer.IsDebugFallbackServer(sUid))
                continue;

            var sComp = _router.UnwrapComputerEntity(sUid);
            if (_router.IsRouterOperational(sUid) && _router.HasActiveNetworkCard(sComp))
            {
                onlineServerNames.Add($"{MetaData(sUid).EntityName} [{_router.EnsureDeviceGuid(sComp)}]");
            }
        }
        if (onlineServerNames.Count > 0)
        {
            dataServerOnline = true;
            dataServerSummary = string.Join(", ", onlineServerNames);
        }

        var connectedDevices = 0;
        var activeConns = 0;
        var blacklisted = 0;
        var packets = 0;
        var connectedCameras = 0;

        if (TryResolveRouterHost(compUid, out var routerEnt))
        {
            connectedDevices = routerEnt.Comp.ConnectedDevices.Count;
            activeConns = routerEnt.Comp.ActiveConnections.Count;
            blacklisted = routerEnt.Comp.BlacklistedGuids.Count;
            packets = routerEnt.Comp.RecordedPackets.Count;
            connectedCameras = EntityManager.System<Cameras.OxydCameraSystem>().CountCamerasForRouter(routerEnt);
        }

        var (installed, running) = BuildProgramLists(compUid, comp);
        NetEntity? activeChild = comp.ActiveProgram is { } ap && Exists(ap) && ap != progUid
            ? GetNetEntity(ap)
            : null;

        var hw = new OsHardwareSummaryDto(
            stats.CpuFrequencyMhz,
            stats.CurrentCpuLoadPercent,
            stats.MaxConcurrentPrograms,
            stats.UsedRamMb,
            stats.RamMb,
            stats.UsedStorageBytes,
            stats.StorageBytes,
            stats.NetworkBandwidthBps,
            stats.NetworkUploadBandwidthBps,
            stats.HasNetworkCard,
            stats.HasDataPort,
            stats.HasDiskSlotModule,
            stats.HasIdSlotModule,
            comp.IsPowered,
            comp.AllowRemoteControl && !comp.RemoteControlWireCut,
            comp.AllowDeviceLink && !comp.DeviceLinkWireCut,
            guid,
            hostName,
            coreComp.LastSyncSummary,
            _router.GetConnectedNetworkLabel(compUid));

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
        {
            _ui.SetUiState(progUid, ProgramUiKey.Key, new NetworkCoreOsUiState(
                hostName,
                guid,
                coreComp.CurrentTab,
                coreComp.AutoSyncRecords,
                coreComp.AutoSyncCount,
                coreComp.LastSyncSummary,
                dataServerOnline,
                dataServerSummary,
                connectedDevices,
                connectedCameras,
                activeConns,
                blacklisted,
                packets,
                hw,
                new List<string>(coreComp.SyncLogs),
                installed,
                running,
                activeChild));
        }
    }

    private (List<OsProgramEntryDto> Installed, List<OsProgramEntryDto> Running) BuildProgramLists(EntityUid compUid, OxydComputerComponent comp)
    {
        var installed = new List<OsProgramEntryDto>();
        var running = new List<OsProgramEntryDto>();
        var runningByPath = new Dictionary<string, EntityUid>(StringComparer.OrdinalIgnoreCase);

        foreach (var rUid in comp.RunningPrograms)
        {
            if (!Exists(rUid) || !_programQuery.TryComp(rUid, out var rProg))
                continue;

            var rEntry = new OsProgramEntryDto(
                GetNetEntity(rUid),
                rProg.ExecutablePath,
                rProg.Title,
                rProg.Prototype ?? string.Empty,
                rProg.Icon,
                rProg.RamUsageMb,
                rProg.CpuUsagePercent,
                isRunning: true,
                isBootProgram: rProg.IsBootProgram);
            running.Add(rEntry);
            if (!string.IsNullOrEmpty(rProg.ExecutablePath))
                runningByPath[rProg.ExecutablePath] = rUid;
        }

        if (_fileSystem.TryFindFileSystem(compUid, out _, out var fs))
        {
            foreach (var f in fs.Files.Values)
            {
                if (f is not ProgramVirtualFile pvf)
                    continue;

                var isRun = runningByPath.TryGetValue(pvf.FullPath, out var rUid);
                var isBoot = isRun && comp.BootProgram == rUid;
                installed.Add(new OsProgramEntryDto(
                    isRun ? GetNetEntity(rUid) : null,
                    pvf.FullPath,
                    pvf.Title,
                    pvf.ProgramId,
                    null,
                    pvf.RamUsageMb,
                    0f,
                    isRunning: isRun,
                    isBootProgram: isBoot));
            }
        }

        return (installed, running);
    }
}
