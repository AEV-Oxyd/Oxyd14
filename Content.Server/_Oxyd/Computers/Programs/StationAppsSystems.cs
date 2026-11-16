using System.Linq;
using Content.Server._Oxyd.Computers.Network;
using Content.Server._Oxyd.Computers.Servers;
using Content.Server.CrewManifest;
using Content.Server.CriminalRecords.Systems;
using Content.Server.MassMedia.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Access.Components;
using Content.Shared.CrewManifest;
using Content.Shared.CriminalRecords;
using Content.Shared.CriminalRecords.Systems;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.GPS.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Labels.EntitySystems;
using Content.Shared.MassMedia.Systems;
using Content.Shared.Medical.HealthAnalyzer;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.StationRecords.Systems;
using Content.Shared.Security;
using Content.Shared.StationRecords;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.Programs;

/// <summary>
/// Consolidated server systems for migrated station & PDA NTOS applications:
/// Notekeeper (.txt), NanoTask (.tsk), CrewManifest (.db), WantedList (.db),
/// NewsReader (.db), NetProbe (.db), LogProbe (.db), MedTek, and AstroNav.
/// Every program delivers BUI states directly on its Program Entity and persists
/// boot-resistant state on the computer's Hard Drive FileSystemComponent.
/// </summary>
public sealed partial class StationAppsSystem : EntitySystem
{
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private NetworkRouterSystem _router = default!;
    [Dependency] private DataServerSystem _dataServer = default!;
    [Dependency] private NetworkBandwidthSystem _bandwidth = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ServerStationSystem _station = default!;
    [Dependency] private CrewManifestSystem _crewManifest = default!;
    [Dependency] private StationRecordsSystem _records = default!;
    [Dependency] private NewsSystem _news = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private LabelSystem _label = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private EntityQuery<ProgramComponent> _programQuery;
    private EntityQuery<OxydComputerComponent> _computerQuery;

    public override void Initialize()
    {
        base.Initialize();

        _programQuery = GetEntityQuery<ProgramComponent>();
        _computerQuery = GetEntityQuery<OxydComputerComponent>();

        // Notekeeper
        SubscribeLocalEvent<NotekeeperProgramComponent, ProgramStartupEvent>(OnNotekeeperStartup);
        SubscribeLocalEvent<NotekeeperProgramComponent, ProgramActivatedEvent>(OnNotekeeperActivated);
        Subs.BuiEvents<NotekeeperProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<NotekeeperActionMessage>(OnNotekeeperAction);
        });

        // NanoTask (.tsk persistent file on HDD)
        SubscribeLocalEvent<NanoTaskProgramComponent, ProgramStartupEvent>(OnNanoTaskStartup);
        SubscribeLocalEvent<NanoTaskProgramComponent, ProgramActivatedEvent>(OnNanoTaskActivated);
        SubscribeLocalEvent<NanoTaskProgramComponent, ProgramRelayedEvent<InteractUsingEvent>>(OnNanoTaskInteractUsing);
        Subs.BuiEvents<NanoTaskProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<NanoTaskActionMessage>(OnNanoTaskAction);
        });

        // CrewManifest (network fetch -> /records/crew_manifest.db)
        SubscribeLocalEvent<CrewManifestProgramComponent, ProgramStartupEvent>(OnCrewManifestStartup);
        SubscribeLocalEvent<CrewManifestProgramComponent, ProgramActivatedEvent>(OnCrewManifestActivated);
        Subs.BuiEvents<CrewManifestProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<CrewManifestRefreshMessage>(OnCrewManifestRefresh);
        });

        // WantedList (network fetch -> /records/wanted_list.db)
        SubscribeLocalEvent<WantedListProgramComponent, ProgramStartupEvent>(OnWantedStartup);
        SubscribeLocalEvent<WantedListProgramComponent, ProgramActivatedEvent>(OnWantedActivated);
        SubscribeLocalEvent<CriminalRecordChangedEvent>(OnCriminalRecordChanged);
        Subs.BuiEvents<WantedListProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<WantedListRefreshMessage>(OnWantedRefresh);
        });

        // NewsReader (network fetch -> /news/articles.db)
        SubscribeLocalEvent<NewsReaderProgramComponent, ProgramStartupEvent>(OnNewsStartup);
        SubscribeLocalEvent<NewsReaderProgramComponent, ProgramActivatedEvent>(OnNewsActivated);
        SubscribeLocalEvent<NewsArticlePublishedEvent>(OnNewsPublished);
        Subs.BuiEvents<NewsReaderProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<NewsReaderActionMessage>(OnNewsAction);
        });

        // NetProbe & LogProbe
        SubscribeLocalEvent<NetProbeProgramComponent, ProgramStartupEvent>(OnNetProbeStartup);
        SubscribeLocalEvent<NetProbeProgramComponent, ProgramActivatedEvent>(OnNetProbeActivated);
        SubscribeLocalEvent<NetProbeProgramComponent, ProgramRelayedEvent<AfterInteractEvent>>(OnNetProbeInteract);
        Subs.BuiEvents<NetProbeProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<NetProbeClearMessage>(OnNetProbeClear);
        });

        SubscribeLocalEvent<LogProbeProgramComponent, ProgramStartupEvent>(OnLogProbeStartup);
        SubscribeLocalEvent<LogProbeProgramComponent, ProgramActivatedEvent>(OnLogProbeActivated);
        SubscribeLocalEvent<LogProbeProgramComponent, ProgramRelayedEvent<AfterInteractEvent>>(OnLogProbeInteract);
        Subs.BuiEvents<LogProbeProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<LogProbePrintMessage>(OnLogProbePrint);
        });

        // MedTek & AstroNav
        SubscribeLocalEvent<MedTekProgramComponent, ProgramStartupEvent>(OnMedTekStartup);
        SubscribeLocalEvent<MedTekProgramComponent, ProgramTerminatedEvent>(OnMedTekTerminated);
        SubscribeLocalEvent<AstroNavProgramComponent, ProgramStartupEvent>(OnAstroNavStartup);
        SubscribeLocalEvent<AstroNavProgramComponent, ProgramTerminatedEvent>(OnAstroNavTerminated);
    }

    // =========================================================================
    // NOTEKEEPER (.txt files on Hard Drive)
    // =========================================================================

    private void OnNotekeeperStartup(Entity<NotekeeperProgramComponent> ent, ref ProgramStartupEvent args)
    {
        EnsureDefaultNote(args.ComputerUid);
        UpdateNotekeeperUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnNotekeeperActivated(Entity<NotekeeperProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdateNotekeeperUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnNotekeeperAction(Entity<NotekeeperProgramComponent> ent, ref NotekeeperActionMessage msg)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
        {
            UpdateNotekeeperUi(ent.Owner, compUid, ent.Comp);
            return;
        }

        switch (msg.Action)
        {
            case NotekeeperUiAction.OpenFile:
                if (!string.IsNullOrWhiteSpace(msg.Path))
                {
                    ent.Comp.CurrentFilePath = msg.Path;
                    Dirty(ent);
                }
                break;

            case NotekeeperUiAction.SaveFile:
            case NotekeeperUiAction.LiveDraftUpdate:
                var savePath = string.IsNullOrWhiteSpace(msg.Path) ? ent.Comp.CurrentFilePath : msg.Path;
                WriteTextNote(driveUid.Value, fs, savePath, msg.Content);
                break;

            case NotekeeperUiAction.CreateFile:
                var rawName = string.IsNullOrWhiteSpace(msg.Path) ? $"note_{_timing.CurTime.Seconds:0}" : msg.Path.Trim();
                var cleanName = rawName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? rawName[..^4] : rawName;
                cleanName = cleanName.Replace("/", "_").Replace(" ", "_");
                var createPath = $"/notes/{cleanName}.txt";
                WriteTextNote(driveUid.Value, fs, createPath, msg.Content);
                ent.Comp.CurrentFilePath = createPath;
                Dirty(ent);
                break;

            case NotekeeperUiAction.DeleteFile:
                var delPath = string.IsNullOrWhiteSpace(msg.Path) ? ent.Comp.CurrentFilePath : msg.Path;
                _fileSystem.TryDeleteFile(driveUid.Value, delPath, fs);
                ent.Comp.CurrentFilePath = "/notes/shift_notes.txt";
                Dirty(ent);
                break;
        }

        UpdateNotekeeperUi(ent.Owner, compUid, ent.Comp);
    }

    private void EnsureDefaultNote(EntityUid compUid)
    {
        if (!_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
            return;

        if (!fs.Files.Values.Any(f => f is TextVirtualFile))
            WriteTextNote(driveUid.Value, fs, "/notes/shift_notes.txt", "Station shift notes.");
    }

    private void WriteTextNote(EntityUid driveUid, FileSystemComponent fs, string fullPath, string content)
    {
        var idx = fullPath.LastIndexOf('/');
        var dir = idx >= 0 ? fullPath[..(idx + 1)] : "/notes/";
        var filePart = idx >= 0 ? fullPath[(idx + 1)..] : fullPath;
        var baseName = filePart.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? filePart[..^4] : filePart;

        _fileSystem.TryWriteFile(driveUid, new TextVirtualFile
        {
            Name = baseName,
            Extension = "txt",
            Directory = dir,
            TextContent = content,
            Size = Math.Max(32, content.Length)
        }, fs);
    }

    private void UpdateNotekeeperUi(EntityUid progUid, EntityUid compUid, NotekeeperProgramComponent comp)
    {
        if (!_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
        {
            _ui.SetUiState(progUid, ProgramUiKey.Key, new NotekeeperUiState(
                false, string.Empty, "No Drive", string.Empty, new(), true));
            return;
        }

        var files = fs.Files
            .Where(kv => kv.Value is TextVirtualFile)
            .Select(kv => kv.Key)
            .ToList();

        if (!files.Contains(comp.CurrentFilePath) && files.Count > 0)
        {
            comp.CurrentFilePath = files[0];
            Dirty(progUid, comp);
        }

        var content = string.Empty;
        var ro = false;
        if (fs.Files.TryGetValue(comp.CurrentFilePath, out var cur) && cur is TextVirtualFile tf)
        {
            content = tf.TextContent;
            ro = tf.IsReadOnly;
        }

        var fileName = comp.CurrentFilePath.Contains('/')
            ? comp.CurrentFilePath[(comp.CurrentFilePath.LastIndexOf('/') + 1)..]
            : comp.CurrentFilePath;

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, new NotekeeperUiState(true, comp.CurrentFilePath, fileName, content, files, ro));
    }

    // =========================================================================
    // NANOTASK (/tasks/tasks.tsk boot-resistant file on Hard Drive)
    // =========================================================================

    private void OnNanoTaskStartup(Entity<NanoTaskProgramComponent> ent, ref ProgramStartupEvent args)
    {
        UpdateNanoTaskUi(ent.Owner, args.ComputerUid);
    }

    private void OnNanoTaskActivated(Entity<NanoTaskProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdateNanoTaskUi(ent.Owner, args.ComputerUid);
    }

    private void OnNanoTaskInteractUsing(Entity<NanoTaskProgramComponent> ent, ref ProgramRelayedEvent<InteractUsingEvent> relayed)
    {
        if (!TryComp<NanoTaskPrintedComponent>(relayed.Args.Used, out var printed) || printed.Task is not { } task)
            return;

        if (!_fileSystem.TryFindFileSystem(relayed.ComputerUid, out var driveUid, out var fs))
            return;

        var file = LoadOrCreateTasksFile(driveUid.Value, fs);
        file.Tasks.Add(new TaskEntryData
        {
            Id = file.NextId++,
            Description = task.Description,
            TaskIsFor = task.TaskIsFor,
            IsTaskDone = task.IsTaskDone,
            Priority = (byte) task.Priority
        });
        _fileSystem.TryWriteFile(driveUid.Value, file, fs);

        relayed.Args.Handled = true;
        QueueDel(relayed.Args.Used);
        UpdateNanoTaskUi(ent.Owner, relayed.ComputerUid);
    }

    private void OnNanoTaskAction(Entity<NanoTaskProgramComponent> ent, ref NanoTaskActionMessage msg)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
        {
            UpdateNanoTaskUi(ent.Owner, compUid);
            return;
        }

        var file = LoadOrCreateTasksFile(driveUid.Value, fs);
        switch (msg.Payload)
        {
            case NanoTaskAddTask add when add.Item.Validate():
                file.Tasks.Add(new TaskEntryData
                {
                    Id = file.NextId++,
                    Description = add.Item.Description,
                    TaskIsFor = add.Item.TaskIsFor,
                    IsTaskDone = add.Item.IsTaskDone,
                    Priority = (byte) add.Item.Priority
                });
                _fileSystem.TryWriteFile(driveUid.Value, file, fs);
                break;

            case NanoTaskUpdateTask upd when upd.Item.Data.Validate():
                var existing = file.Tasks.FirstOrDefault(t => t.Id == upd.Item.Id);
                if (existing != null)
                {
                    existing.Description = upd.Item.Data.Description;
                    existing.TaskIsFor = upd.Item.Data.TaskIsFor;
                    existing.IsTaskDone = upd.Item.Data.IsTaskDone;
                    existing.Priority = (byte) upd.Item.Data.Priority;
                    _fileSystem.TryWriteFile(driveUid.Value, file, fs);
                }
                break;

            case NanoTaskDeleteTask del:
                file.Tasks.RemoveAll(t => t.Id == del.Id);
                _fileSystem.TryWriteFile(driveUid.Value, file, fs);
                break;

            case NanoTaskPrintTask print when print.Item.Validate():
                if (_timing.CurTime < ent.Comp.NextPrintAllowedAfter)
                    break;
                ent.Comp.NextPrintAllowedAfter = _timing.CurTime + ent.Comp.PrintDelay;
                Dirty(ent);

                var paper = Spawn("PaperNanoTaskItem", Transform(msg.Actor).Coordinates);
                _hands.PickupOrDrop(msg.Actor, paper);
                _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/printer.ogg"), ent.Owner);

                var printedComp = EnsureComp<NanoTaskPrintedComponent>(paper);
                printedComp.Task = print.Item.Clone();
                Dirty(paper, printedComp);

                _paper.SetContent(paper, $"Task: {print.Item.Description}\nRequester: {print.Item.TaskIsFor}\nPriority: {print.Item.Priority}");
                break;
        }

        UpdateNanoTaskUi(ent.Owner, compUid);
    }

    private TaskListVirtualFile LoadOrCreateTasksFile(EntityUid driveUid, FileSystemComponent fs)
    {
        if (_fileSystem.TryGetFile<TaskListVirtualFile>(driveUid, NanoTaskProgramComponent.DefaultTasksFilePath, out var existing, fs))
            return existing;

        var fresh = new TaskListVirtualFile
        {
            Name = "tasks",
            Extension = "tsk",
            Directory = "/tasks/",
            Size = 512
        };
        _fileSystem.TryWriteFile(driveUid, fresh, fs);
        return fresh;
    }

    private void UpdateNanoTaskUi(EntityUid progUid, EntityUid compUid)
    {
        if (!_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
        {
            if (_ui.HasUi(progUid, ProgramUiKey.Key))
                _ui.SetUiState(progUid, ProgramUiKey.Key, new NanoTaskUiState(false, new()));
            return;
        }

        var file = LoadOrCreateTasksFile(driveUid.Value, fs);
        var items = file.Tasks.Select(t => new NanoTaskItemAndId(
            t.Id,
            new NanoTaskItem(t.Description, t.TaskIsFor, t.IsTaskDone, (NanoTaskPriority) t.Priority)))
            .ToList();

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, new NanoTaskUiState(true, items));
    }

    // =========================================================================
    // CREW MANIFEST (Network sync -> /records/crew_manifest.db)
    // =========================================================================

    private void OnCrewManifestStartup(Entity<CrewManifestProgramComponent> ent, ref ProgramStartupEvent args)
    {
        SyncAndSendCrewManifest(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnCrewManifestActivated(Entity<CrewManifestProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        SyncAndSendCrewManifest(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnCrewManifestRefresh(Entity<CrewManifestProgramComponent> ent, ref CrewManifestRefreshMessage args)
    {
        if (_programQuery.TryComp(ent.Owner, out var prog) && prog.Computer is { } compUid)
            SyncAndSendCrewManifest(ent.Owner, compUid, ent.Comp);
    }

    private void SyncAndSendCrewManifest(EntityUid progUid, EntityUid compUid, CrewManifestProgramComponent comp)
    {
        var online = false;
        var statusMsg = string.Empty;

        _fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs);

        var station = _station.GetOwningStation(compUid) ?? _station.GetStationsSet().FirstOrDefault();
        if (station != EntityUid.Invalid &&
            _router.HasActiveNetworkCard(compUid) &&
            _router.IsNetworkConnected(compUid))
        {
            var srvOnline = _dataServer.TryGetFirstOnlineRepositoryServer(compUid, out _, out var srvCompUid, out _, out _, out _);
            if (srvOnline)
            {
                var (stName, liveEntries) = _crewManifest.GetCrewManifest(station);
                var data = new Dictionary<string, string> { ["__station"] = stName };
                if (liveEntries?.Entries != null)
                {
                    var idx = 0;
                    foreach (var entry in liveEntries.Entries)
                    {
                        data[$"row_{idx++}"] = $"{entry.Name}\t{entry.JobTitle}\t{entry.JobPrototype}\t{entry.JobIcon}";
                    }
                }

                var size = Math.Max(128, data.Count * 48);
                var pkt = new NetworkDataPacket
                {
                    PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                    SourceAddress = _router.EnsureDeviceGuid(srvCompUid),
                    TargetAddress = _router.EnsureDeviceGuid(compUid),
                    Protocol = "TCP/MANIFEST",
                    PayloadSummary = $"Manifest sync ({data.Count - 1} crew)",
                    ByteSize = size,
                    Timestamp = _timing.CurTime
                };

                if (_router.RecordAndValidatePacket(srvCompUid, compUid, pkt) && driveUid != null && fs != null)
                {
                    _fileSystem.TryWriteFile(driveUid.Value, new DatabaseVirtualFile
                    {
                        Name = "crew_manifest",
                        Extension = "db",
                        Directory = "/records/",
                        TableName = "crew_manifest",
                        Data = data,
                        Size = size
                    }, fs);
                    online = true;
                    statusMsg = $"Synced live from [{_router.EnsureDeviceGuid(srvCompUid)}] -> HDD.";
                }
            }
            else
            {
                statusMsg = "Manifest server offline — showing cached HDD file.";
            }
        }
        else
        {
            statusMsg = "Offline / no NIC — showing local /records/crew_manifest.db cache.";
        }

        // Render UI purely from the cached HDD file!
        var stationName = "Unknown Station";
        var loadedEntries = new List<CrewManifestEntry>();
        if (driveUid != null && fs != null &&
            _fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid.Value, CrewManifestProgramComponent.CacheFilePath, out var db, fs))
        {
            if (db.Data.TryGetValue("__station", out var sName))
                stationName = sName;

            foreach (var (k, v) in db.Data)
            {
                if (!k.StartsWith("row_"))
                    continue;
                var cols = v.Split('\t');
                if (cols.Length >= 4)
                    loadedEntries.Add(new CrewManifestEntry(cols[0], cols[1], cols[2], cols[3]));
            }
        }

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
        {
            _ui.SetUiState(progUid, ProgramUiKey.Key, new CrewManifestUiState(
                stationName,
                loadedEntries.Count > 0 ? new CrewManifestEntries { Entries = loadedEntries.ToArray() } : null,
                online,
                statusMsg));
        }
    }

    // =========================================================================
    // WANTED LIST (Network sync -> /records/wanted_list.db)
    // =========================================================================

    private void OnWantedStartup(Entity<WantedListProgramComponent> ent, ref ProgramStartupEvent args)
    {
        SyncWantedList(ent.Owner, args.ComputerUid);
    }

    private void OnWantedActivated(Entity<WantedListProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        SyncWantedList(ent.Owner, args.ComputerUid);
    }

    private void OnWantedRefresh(Entity<WantedListProgramComponent> ent, ref WantedListRefreshMessage args)
    {
        if (_programQuery.TryComp(ent.Owner, out var prog) && prog.Computer is { } compUid)
            SyncWantedList(ent.Owner, compUid);
    }

    private void OnCriminalRecordChanged(ref CriminalRecordChangedEvent args)
    {
        var q = EntityQueryEnumerator<WantedListProgramComponent, ProgramComponent>();
        while (q.MoveNext(out var uid, out _, out var prog))
        {
            if (prog.Computer is { } cUid)
                SyncWantedList(uid, cUid);
        }
    }

    private void SyncWantedList(EntityUid progUid, EntityUid compUid)
    {
        var online = false;
        var statusMsg = string.Empty;
        _fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs);

        var station = _station.GetOwningStation(compUid) ?? _station.GetStationsSet().FirstOrDefault();
        if (station != EntityUid.Invalid &&
            _router.HasActiveNetworkCard(compUid) &&
            _router.IsNetworkConnected(compUid) &&
            _dataServer.TryGetFirstOnlineRepositoryServer(compUid, out _, out var srvCompUid, out _, out _, out _))
        {
            var data = new Dictionary<string, string>();
            var records = _records.GetRecordsOfType<CriminalRecord>(station)
                .Where(cr => cr.Item2.Status is not SecurityStatus.None || cr.Item2.History.Count > 0)
                .ToList();

            var idx = 0;
            foreach (var (recId, rec) in records)
            {
                var key = new StationRecordKey(recId, station);
                if (_records.TryGetRecord(key, out GeneralStationRecord? gen))
                {
                    data[$"rec_{idx++}"] = $"{gen.Name}\t{gen.JobTitle}\t{rec.Status}\t{rec.Reason ?? ""}\t{rec.InitiatorName ?? ""}";
                }
            }

            var size = Math.Max(128, data.Count * 64);
            var pkt = new NetworkDataPacket
            {
                PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                SourceAddress = _router.EnsureDeviceGuid(srvCompUid),
                TargetAddress = _router.EnsureDeviceGuid(compUid),
                Protocol = "TCP/RECORDS",
                PayloadSummary = $"Security wanted list ({data.Count})",
                ByteSize = size,
                Timestamp = _timing.CurTime
            };

            if (_router.RecordAndValidatePacket(srvCompUid, compUid, pkt) && driveUid != null && fs != null)
            {
                _fileSystem.TryWriteFile(driveUid.Value, new DatabaseVirtualFile
                {
                    Name = "wanted_list",
                    Extension = "db",
                    Directory = "/records/",
                    TableName = "wanted_list",
                    Data = data,
                    Size = size
                }, fs);
                online = true;
                statusMsg = "Synced security records -> /records/wanted_list.db";
            }
        }
        else
        {
            statusMsg = "Offline / no NIC — reading local /records/wanted_list.db";
        }

        var output = new List<WantedRecord>();
        if (station != EntityUid.Invalid &&
            driveUid != null &&
            fs != null &&
            _fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid.Value, WantedListProgramComponent.CacheFilePath, out var db, fs))
        {
            foreach (var v in db.Data.Values)
            {
                var cols = v.Split('\t');
                if (cols.Length < 3)
                    continue;

                var genRec = new GeneralStationRecord { Name = cols[0], JobTitle = cols[1] };
                Enum.TryParse<SecurityStatus>(cols[2], out var secStatus);
                output.Add(new WantedRecord(
                    genRec,
                    secStatus,
                    cols.Length > 3 ? cols[3] : null,
                    cols.Length > 4 ? cols[4] : null,
                    new List<CrimeHistory>()));
            }
        }

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, new WantedListUiState(output, online, statusMsg));
    }

    // =========================================================================
    // NEWS READER (Network sync -> /news/articles.db)
    // =========================================================================

    private void OnNewsStartup(Entity<NewsReaderProgramComponent> ent, ref ProgramStartupEvent args)
    {
        SyncAndSendNews(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnNewsActivated(Entity<NewsReaderProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        SyncAndSendNews(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnNewsPublished(ref NewsArticlePublishedEvent args)
    {
        var q = EntityQueryEnumerator<NewsReaderProgramComponent, ProgramComponent>();
        while (q.MoveNext(out var uid, out var comp, out var prog))
        {
            if (prog.Computer is { } cUid)
                SyncAndSendNews(uid, cUid, comp);
        }
    }

    private void OnNewsAction(Entity<NewsReaderProgramComponent> ent, ref NewsReaderActionMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        switch (args.Action)
        {
            case NewsReaderUiAction.Next:
                ent.Comp.ArticleNumber++;
                break;
            case NewsReaderUiAction.Prev:
                ent.Comp.ArticleNumber--;
                break;
            case NewsReaderUiAction.NotificationSwitch:
                ent.Comp.NotificationOn = !ent.Comp.NotificationOn;
                break;
        }
        Dirty(ent);
        SyncAndSendNews(ent.Owner, compUid, ent.Comp);
    }

    private void SyncAndSendNews(EntityUid progUid, EntityUid compUid, NewsReaderProgramComponent comp)
    {
        var status = string.Empty;
        _fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs);

        var station = _station.GetOwningStation(compUid) ?? _station.GetStationsSet().FirstOrDefault();
        if (station != EntityUid.Invalid &&
            _router.HasActiveNetworkCard(compUid) &&
            _router.IsNetworkConnected(compUid) &&
            _news.TryGetNewsArticles(station, out var liveArticles))
        {
            var data = new Dictionary<string, string>();
            for (var i = 0; i < liveArticles.Count; i++)
            {
                var a = liveArticles[i];
                data[$"art_{i}"] = $"{a.Title}\t{a.Author ?? "Anonymous"}\t{a.ShareTime.Ticks}\t{a.Content.Replace('\t', ' ')}";
            }

            if (driveUid != null && fs != null)
            {
                var bytes = Math.Max(128, data.Count * 128);
                _bandwidth.RecordDataTransmitted(compUid, bytes, isUpload: false);
                _bandwidth.StorePacket(compUid, "NEWS-SRV", _router.EnsureDeviceGuid(compUid), "TCP/NEWS", $"News sync ({data.Count} articles)", bytes);
                _fileSystem.TryWriteFile(driveUid.Value, new DatabaseVirtualFile
                {
                    Name = "articles",
                    Extension = "db",
                    Directory = "/news/",
                    TableName = "station_news",
                    Data = data,
                    Size = bytes
                }, fs);
                status = $"Synced {liveArticles.Count} articles to /news/articles.db.";
            }
        }
        else
        {
            status = "Offline — reading cached /news/articles.db.";
        }

        var articles = new List<NewsArticle>();
        if (driveUid != null && fs != null &&
            _fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid.Value, NewsReaderProgramComponent.CacheFilePath, out var db, fs))
        {
            foreach (var v in db.Data.Values)
            {
                var cols = v.Split('\t', 4);
                if (cols.Length < 4)
                    continue;
                long.TryParse(cols[2], out var ticks);
                articles.Add(new NewsArticle
                {
                    Title = cols[0],
                    Author = cols[1],
                    ShareTime = TimeSpan.FromTicks(ticks),
                    Content = cols[3]
                });
            }
        }

        if (articles.Count == 0)
        {
            if (_ui.HasUi(progUid, ProgramUiKey.Key))
                _ui.SetUiState(progUid, ProgramUiKey.Key, new NewsReaderBoundUserInterfaceState(null, 0, 0, comp.NotificationOn, status));
            return;
        }

        if (comp.ArticleNumber >= articles.Count)
            comp.ArticleNumber = 0;
        if (comp.ArticleNumber < 0)
            comp.ArticleNumber = articles.Count - 1;
        Dirty(progUid, comp);

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
        {
            _ui.SetUiState(progUid, ProgramUiKey.Key, new NewsReaderBoundUserInterfaceState(
                articles[comp.ArticleNumber],
                comp.ArticleNumber + 1,
                articles.Count,
                comp.NotificationOn,
                status));
        }
    }

    // =========================================================================
    // NETPROBE & LOGPROBE (/logs/*.db)
    // =========================================================================

    private void OnNetProbeStartup(Entity<NetProbeProgramComponent> ent, ref ProgramStartupEvent args) => UpdateNetProbeUi(ent.Owner, args.ComputerUid);
    private void OnNetProbeActivated(Entity<NetProbeProgramComponent> ent, ref ProgramActivatedEvent args) => UpdateNetProbeUi(ent.Owner, args.ComputerUid);

    private void OnNetProbeClear(Entity<NetProbeProgramComponent> ent, ref NetProbeClearMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;
        if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
            _fileSystem.TryDeleteFile(driveUid.Value, NetProbeProgramComponent.CacheFilePath, fs);
        UpdateNetProbeUi(ent.Owner, compUid);
    }

    private void OnNetProbeInteract(Entity<NetProbeProgramComponent> ent, ref ProgramRelayedEvent<AfterInteractEvent> relayed)
    {
        if (relayed.Args.Handled || !relayed.Args.CanReach || relayed.Args.Target is not { } target)
            return;

        if (!TryComp<DeviceNetworkComponent>(target, out var netComp))
            return;

        if (!_fileSystem.TryFindFileSystem(relayed.ComputerUid, out var driveUid, out var fs))
            return;

        var db = _fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid.Value, NetProbeProgramComponent.CacheFilePath, out var existing, fs)
            ? existing
            : new DatabaseVirtualFile { Name = "netprobe", Extension = "db", Directory = "/logs/", TableName = "netprobe" };

        var netAddr = netComp.Address;
        var addr = netAddr.ToString();
        db.Data[addr] = $"{Name(target)}\t{addr}\t{netComp.ReceiveFrequencyId}\t{netComp.DeviceNetId}";
        db.Size = Math.Max(64, db.Data.Count * 64);
        _fileSystem.TryWriteFile(driveUid.Value, db, fs);

        _audio.PlayPvs(ent.Comp.SoundScan, target);
        _popup.PopupEntity(Loc.GetString("net-probe-scan", ("device", target)), target, relayed.Args.User);
        relayed.Args.Handled = true;

        UpdateNetProbeUi(ent.Owner, relayed.ComputerUid);
    }

    private void UpdateNetProbeUi(EntityUid progUid, EntityUid compUid)
    {
        var list = new List<ProbedNetworkDevice>();
        if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) &&
            _fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid.Value, NetProbeProgramComponent.CacheFilePath, out var db, fs))
        {
            foreach (var v in db.Data.Values)
            {
                var cols = v.Split('\t');
                if (cols.Length >= 4)
                    list.Add(new ProbedNetworkDevice(cols[0], cols[1], cols[2], cols[3]));
            }
        }

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, new NetProbeUiState(list));
    }

    private void OnLogProbeStartup(Entity<LogProbeProgramComponent> ent, ref ProgramStartupEvent args) => UpdateLogProbeUi(ent.Owner, args.ComputerUid, ent.Comp);
    private void OnLogProbeActivated(Entity<LogProbeProgramComponent> ent, ref ProgramActivatedEvent args) => UpdateLogProbeUi(ent.Owner, args.ComputerUid, ent.Comp);

    private void OnLogProbeInteract(Entity<LogProbeProgramComponent> ent, ref ProgramRelayedEvent<AfterInteractEvent> relayed)
    {
        if (relayed.Args.Handled || !relayed.Args.CanReach || relayed.Args.Target is not { } target)
            return;

        if (!TryComp<AccessReaderComponent>(target, out var reader))
            return;

        if (!_fileSystem.TryFindFileSystem(relayed.ComputerUid, out var driveUid, out var fs))
            return;

        ent.Comp.EntityName = Name(target);
        Dirty(ent);

        var db = new DatabaseVirtualFile
        {
            Name = "logprobe",
            Extension = "db",
            Directory = "/logs/",
            TableName = "access_logs",
            Data = new Dictionary<string, string> { ["__entity"] = ent.Comp.EntityName }
        };

        var idx = 0;
        foreach (var rec in reader.AccessLog)
            db.Data[$"log_{idx++}"] = $"{rec.AccessTime.Ticks}\t{rec.Accessor}";
        db.Size = Math.Max(64, db.Data.Count * 48);
        _fileSystem.TryWriteFile(driveUid.Value, db, fs);

        _audio.PlayPvs(ent.Comp.SoundScan, target);
        _popup.PopupEntity(Loc.GetString("log-probe-scan", ("device", target)), target, relayed.Args.User);
        relayed.Args.Handled = true;

        UpdateLogProbeUi(ent.Owner, relayed.ComputerUid, ent.Comp);
    }

    private void OnLogProbePrint(Entity<LogProbeProgramComponent> ent, ref LogProbePrintMessage args)
    {
        if (_timing.CurTime < ent.Comp.NextPrintAllowed || string.IsNullOrEmpty(ent.Comp.EntityName))
            return;

        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        ent.Comp.NextPrintAllowed = _timing.CurTime + ent.Comp.PrintCooldown;
        Dirty(ent);

        var paper = Spawn(ent.Comp.PaperPrototype, _transform.GetMapCoordinates(args.Actor));
        _label.Label(paper, ent.Comp.EntityName);
        _audio.PlayPvs(ent.Comp.PrintSound, paper);
        _hands.PickupOrDrop(args.Actor, paper, checkActionBlocker: false);

        var logs = ReadLogProbeEntries(compUid);
        var lines = new List<string> { $"Access Logs: {ent.Comp.EntityName}" };
        for (var i = 0; i < logs.Count; i++)
            lines.Add($"{i + 1}. [{TimeSpan.FromSeconds(Math.Truncate(logs[i].Time.TotalSeconds))}] {logs[i].Accessor}");
        _paper.SetContent(paper, string.Join("\n", lines));
    }

    private List<PulledAccessLog> ReadLogProbeEntries(EntityUid compUid)
    {
        var list = new List<PulledAccessLog>();
        if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) &&
            _fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid.Value, LogProbeProgramComponent.CacheFilePath, out var db, fs))
        {
            foreach (var (k, v) in db.Data)
            {
                if (!k.StartsWith("log_"))
                    continue;
                var cols = v.Split('\t', 2);
                if (cols.Length == 2 && long.TryParse(cols[0], out var ticks))
                    list.Add(new PulledAccessLog(TimeSpan.FromTicks(ticks), cols[1]));
            }
        }
        list.Reverse();
        return list;
    }

    private void UpdateLogProbeUi(EntityUid progUid, EntityUid compUid, LogProbeProgramComponent comp)
    {
        var entityName = comp.EntityName;
        if (string.IsNullOrEmpty(entityName) &&
            _fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) &&
            _fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid.Value, LogProbeProgramComponent.CacheFilePath, out var db, fs) &&
            db.Data.TryGetValue("__entity", out var savedName))
        {
            entityName = savedName;
        }

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, new LogProbeUiState(entityName, ReadLogProbeEntries(compUid)));
    }

    // =========================================================================
    // MEDTEK & ASTRONAV
    // =========================================================================

    private void OnMedTekStartup(Entity<MedTekProgramComponent> ent, ref ProgramStartupEvent args)
    {
        var target = _computerQuery.TryComp(args.ComputerUid, out var comp) && comp.HostMachine is { } hm ? hm : args.ComputerUid;
        EnsureComp<HealthAnalyzerComponent>(target);
        if (_ui.HasUi(ent.Owner, ProgramUiKey.Key))
            _ui.SetUiState(ent.Owner, ProgramUiKey.Key, new UtilityProgramUiState("MedTek Scanner", "Health scanner module active on host device.", "ACTIVE"));
    }

    private void OnMedTekTerminated(Entity<MedTekProgramComponent> ent, ref ProgramTerminatedEvent args)
    {
        var target = _computerQuery.TryComp(args.ComputerUid, out var comp) && comp.HostMachine is { } hm ? hm : args.ComputerUid;
        RemComp<HealthAnalyzerComponent>(target);
    }

    private void OnAstroNavStartup(Entity<AstroNavProgramComponent> ent, ref ProgramStartupEvent args)
    {
        var target = _computerQuery.TryComp(args.ComputerUid, out var comp) && comp.HostMachine is { } hm ? hm : args.ComputerUid;
        EnsureComp<HandheldGPSComponent>(target);
        if (_ui.HasUi(ent.Owner, ProgramUiKey.Key))
            _ui.SetUiState(ent.Owner, ProgramUiKey.Key, new UtilityProgramUiState("AstroNav GPS", "Handheld GPS transponder active on host device.", "ACTIVE"));
    }

    private void OnAstroNavTerminated(Entity<AstroNavProgramComponent> ent, ref ProgramTerminatedEvent args)
    {
        var target = _computerQuery.TryComp(args.ComputerUid, out var comp) && comp.HostMachine is { } hm ? hm : args.ComputerUid;
        RemComp<HandheldGPSComponent>(target);
    }
}
