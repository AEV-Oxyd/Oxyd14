using System.Linq;
using System.Numerics;
using Content.Server._Oxyd.Computers.Hardware;
using Content.Server._Oxyd.Computers.Network;
using Content.Server._Oxyd.Computers.Servers;
using Content.Server.Access.Systems;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Chat.Systems;
using Content.Server.CrewManifest;
using Content.Server.CriminalRecords.Systems;
using Content.Server.MassMedia.Systems;
using Content.Server.Solar.EntitySystems;
using Content.Server.Stack;
using Content.Server.Station.Systems;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.AlertLevel;
using Content.Shared.Cargo;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.CrewManifest;
using Content.Shared.CriminalRecords;
using Content.Shared.CriminalRecords.Systems;
using Content.Shared.Damage.Components;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.GPS.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Labels.EntitySystems;
using Content.Shared.MassMedia.Systems;
using Content.Shared.Medical.HealthAnalyzer;
using Content.Shared.Medical.SuitSensors;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.Security;
using Content.Shared.Stacks;
using Content.Shared.StationRecords;
using Content.Shared.StationRecords.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.Programs;

/// <summary>
/// Active-component marker added while a Crew Monitor UI has at least one open viewer,
/// so periodic suit-sensor auto-sync only runs for active open monitors.
/// </summary>
[RegisterComponent]
public sealed partial class ActiveCrewMonitorComponent : Component
{
    [ViewVariables]
    public TimeSpan NextSync;
}

/// <summary>
/// Consolidated server systems for migrated station &amp; PDA NTOS applications:
/// Notekeeper (.txt), NanoTask (.tsk), CrewManifest (.db), WantedList (.db),
/// NewsReader (.db), NetProbe (.db), LogProbe (.db), MedTek, AstroNav,
/// CargoOrders (Bank Account ID + PIN auth &amp; withdrawal), CrewMonitor, CommsConsole, and SolarControl.
/// Every program delivers BUI states directly on its Program Entity and persists
/// boot-resistant state on the computer's Hard Drive FileSystemComponent.
/// </summary>
public sealed partial class StationAppsSystem : EntitySystem
{
    [Dependency] private OxydComputerSystem _computer = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private NetworkRouterSystem _router = default!;
    [Dependency] private DataServerSystem _dataServer = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ServerStationSystem _station = default!;
    [Dependency] private CargoSystem _cargo = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private AlertLevelSystem _alertLevel = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private PowerSolarSystem _solar = default!;
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private IdCardSystem _idCard = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private Content.Shared.Damage.Systems.DamageableSystem _damageable = default!;
    [Dependency] private StationRecordsSystem _records = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private LabelSystem _label = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    [Dependency] private EntityQuery<ProgramComponent> _programQuery = default!;
    [Dependency] private EntityQuery<OxydComputerComponent> _computerQuery = default!;

    private static readonly ProtoId<AccessLevelPrototype> CommandAccess = "Command";
    private static readonly ProtoId<StackPrototype> CreditStackProto = "Credit";

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<NotekeeperProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<NotekeeperActionMessage>(OnNotekeeperAction);
        });

        Subs.BuiEvents<NanoTaskProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<NanoTaskActionMessage>(OnNanoTaskAction);
        });

        Subs.BuiEvents<CrewManifestProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<CrewManifestRefreshMessage>(OnCrewManifestRefresh);
        });

        Subs.BuiEvents<WantedListProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<WantedListRefreshMessage>(OnWantedRefresh);
        });

        Subs.BuiEvents<NewsReaderProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<NewsReaderActionMessage>(OnNewsAction);
        });

        Subs.BuiEvents<NetProbeProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<NetProbeClearMessage>(OnNetProbeClear);
        });

        Subs.BuiEvents<LogProbeProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<LogProbePrintMessage>(OnLogProbePrint);
        });

        Subs.BuiEvents<CargoOrdersProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnCargoOrdersBuiOpened);
            subs.Event<CargoOrdersActionMessage>(OnCargoOrdersAction);
        });

        Subs.BuiEvents<CrewMonitorProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnCrewMonitorBuiOpened);
            subs.Event<BoundUIClosedEvent>(OnCrewMonitorBuiClosed);
            subs.Event<CrewMonitorRefreshMessage>(OnCrewMonitorRefresh);
        });

        Subs.BuiEvents<CommsConsoleProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnCommsBuiOpened);
            subs.Event<CommsConsoleActionMessage>(OnCommsAction);
        });

        Subs.BuiEvents<SolarControlProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnSolarBuiOpened);
            subs.Event<SolarControlActionMessage>(OnSolarAction);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var crewQuery = EntityQueryEnumerator<ActiveCrewMonitorComponent, CrewMonitorProgramComponent, ProgramComponent>();
        while (crewQuery.MoveNext(out var progUid, out var active, out _, out var prog))
        {
            if (!_ui.HasUi(progUid, ProgramUiKey.Key) || !_ui.IsUiOpen(progUid, ProgramUiKey.Key))
            {
                RemCompDeferred<ActiveCrewMonitorComponent>(progUid);
                continue;
            }

            if (now < active.NextSync)
                continue;

            active.NextSync = now + TimeSpan.FromSeconds(1.0);
            if (prog.Computer is { } compUid && Exists(compUid))
                UpdateCrewMonitorUi(progUid, compUid);
        }
    }

    // =========================================================================
    // NOTEKEEPER (.txt files on Hard Drive)
    // =========================================================================

    [SubscribeLocalEvent]
    private void OnNotekeeperStartup(Entity<NotekeeperProgramComponent> ent, ref ProgramStartupEvent args)
    {
        EnsureDefaultNote(args.ComputerUid);
        UpdateNotekeeperUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
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

    [SubscribeLocalEvent]
    private void OnNanoTaskStartup(Entity<NanoTaskProgramComponent> ent, ref ProgramStartupEvent args)
    {
        UpdateNanoTaskUi(ent.Owner, args.ComputerUid);
    }

    [SubscribeLocalEvent]
    private void OnNanoTaskActivated(Entity<NanoTaskProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdateNanoTaskUi(ent.Owner, args.ComputerUid);
    }

    [SubscribeLocalEvent]
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
    // CREW MANIFEST (Network download from Server/Router local copy -> /records/crew_manifest.db)
    // =========================================================================

    [SubscribeLocalEvent]
    private void OnCrewManifestStartup(Entity<CrewManifestProgramComponent> ent, ref ProgramStartupEvent args)
    {
        SyncAndSendCrewManifest(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
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
        if (_router.HasActiveNetworkCard(compUid) &&
            _router.IsNetworkConnected(compUid))
        {
            if (_dataServer.TryGetLocalRecordCopyFromHost(
                    compUid,
                    "ManifestService",
                    CrewManifestProgramComponent.CacheFilePath,
                    station,
                    out var srvCompUid,
                    out var serverCopy,
                    out var netErr))
            {
                var editableEntries = new Dictionary<string, string>();
                var parameters = new Dictionary<string, string>();
                if (serverCopy.Data.TryGetValue("__station", out var stName))
                    parameters["__station"] = stName;

                foreach (var (k, v) in serverCopy.Data)
                {
                    if (!k.StartsWith("row_"))
                        continue;
                    var cols = v.Split('\t');
                    if (cols.Length < 2)
                        continue;

                    var charName = cols[0];
                    var jobTitle = cols[1];
                    var jobProto = cols.Length > 2 ? cols[2] : string.Empty;
                    var jobIcon = cols.Length > 3 ? cols[3] : "JobIconUnknown";
                    editableEntries[charName] = jobTitle;
                    parameters[$"meta:{charName}"] = $"{jobProto}\t{jobIcon}";
                }

                var size = Math.Max(128, serverCopy.Size);
                var pkt = new NetworkDataPacket
                {
                    PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                    SourceAddress = _router.EnsureDeviceGuid(srvCompUid),
                    TargetAddress = _router.EnsureDeviceGuid(compUid),
                    Protocol = "TCP/MANIFEST",
                    PayloadSummary = $"Crew Manifest ({editableEntries.Count} crew) [{MetaData(srvCompUid).EntityName}]",
                    RawPayload = string.Join("; ", editableEntries.Select(kv => $"{kv.Key}={kv.Value}")),
                    IsPlayerRelevant = true,
                    IsEditable = true,
                    FilePath = CrewManifestProgramComponent.CacheFilePath,
                    EditableEntries = editableEntries,
                    Parameters = parameters,
                    ByteSize = size,
                    Timestamp = _timing.CurTime
                };

                if (_router.RecordAndValidatePacket(srvCompUid, compUid, pkt) && driveUid != null && fs != null)
                {
                    var localFile = (DatabaseVirtualFile) serverCopy.Clone();
                    localFile.Data.Remove("__tampered");
                    localFile.Data.Remove("__source_hash");
                    localFile.Size = size;
                    _fileSystem.TryWriteFile(driveUid.Value, localFile, fs);
                    online = true;
                    statusMsg = $"Downloaded from {MetaData(srvCompUid).EntityName} [{_router.EnsureDeviceGuid(srvCompUid)}] -> {CrewManifestProgramComponent.CacheFilePath}";
                }
            }
            else
            {
                statusMsg = $"{netErr} — showing local {CrewManifestProgramComponent.CacheFilePath}.";
            }
        }
        else
        {
            statusMsg = $"Offline / no NIC — reading local {CrewManifestProgramComponent.CacheFilePath}.";
        }

        SendCrewManifestFromLocalCache(progUid, compUid, online, statusMsg);
    }

    public void SendCrewManifestFromLocalCache(EntityUid progUid, EntityUid compUid, bool online, string statusMsg)
    {
        _fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs);

        var stationName = "Unknown Station";
        var loadedEntries = new List<CrewManifestEntry>();
        TimeSpan? fileTimestamp = null;

        if (driveUid != null && fs != null &&
            _fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid.Value, CrewManifestProgramComponent.CacheFilePath, out var db, fs))
        {
            fileTimestamp = db.LastModified;
            if (db.Data.TryGetValue("__station", out var sName) && !string.IsNullOrWhiteSpace(sName))
                stationName = sName;

            foreach (var (k, v) in db.Data.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (!k.StartsWith("row_"))
                    continue;
                var cols = v.Split('\t');
                if (cols.Length >= 4)
                    loadedEntries.Add(new CrewManifestEntry(cols[0], cols[1], cols[2], cols[3]));
                else if (cols.Length >= 2)
                    loadedEntries.Add(new CrewManifestEntry(cols[0], cols[1], string.Empty, "JobIconUnknown"));
            }
        }

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
        {
            _ui.SetUiState(progUid, ProgramUiKey.Key, new CrewManifestUiState(
                stationName,
                loadedEntries.Count > 0 ? new CrewManifestEntries { Entries = loadedEntries.ToArray() } : null,
                online,
                statusMsg,
                fileTimestamp));
        }
    }

    // =========================================================================
    // WANTED LIST (Network download from Server/Router local copy -> /records/wanted_list.db)
    // =========================================================================

    [SubscribeLocalEvent]
    private void OnWantedStartup(Entity<WantedListProgramComponent> ent, ref ProgramStartupEvent args)
    {
        SyncWantedList(ent.Owner, args.ComputerUid);
    }

    [SubscribeLocalEvent]
    private void OnWantedActivated(Entity<WantedListProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        SyncWantedList(ent.Owner, args.ComputerUid);
    }

    private void OnWantedRefresh(Entity<WantedListProgramComponent> ent, ref WantedListRefreshMessage args)
    {
        if (_programQuery.TryComp(ent.Owner, out var prog) && prog.Computer is { } compUid)
            SyncWantedList(ent.Owner, compUid);
    }

    [SubscribeLocalEvent]
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
        if (_router.HasActiveNetworkCard(compUid) &&
            _router.IsNetworkConnected(compUid))
        {
            if (_dataServer.TryGetLocalRecordCopyFromHost(
                    compUid,
                    "StationRecordsService",
                    WantedListProgramComponent.CacheFilePath,
                    station,
                    out var srvCompUid,
                    out var serverCopy,
                    out var netErr))
            {
                var editableEntries = new Dictionary<string, string>();
                var parameters = new Dictionary<string, string>();
                foreach (var (k, v) in serverCopy.Data)
                {
                    if (!k.StartsWith("rec_"))
                        continue;
                    var cols = v.Split('\t');
                    if (cols.Length < 3)
                        continue;

                    var charName = cols[0];
                    var jobTitle = cols[1];
                    var secStatus = cols[2];
                    var reason = cols.Length > 3 ? cols[3] : string.Empty;
                    var initiator = cols.Length > 4 ? cols[4] : string.Empty;
                    editableEntries[charName] = string.IsNullOrWhiteSpace(reason) ? secStatus : $"{secStatus} | {reason}";
                    parameters[$"meta:{charName}"] = $"{jobTitle}\t{initiator}";
                }

                var size = Math.Max(128, serverCopy.Size);
                var pkt = new NetworkDataPacket
                {
                    PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                    SourceAddress = _router.EnsureDeviceGuid(srvCompUid),
                    TargetAddress = _router.EnsureDeviceGuid(compUid),
                    Protocol = "TCP/RECORDS",
                    PayloadSummary = $"Wanted List ({editableEntries.Count} entries) [{MetaData(srvCompUid).EntityName}]",
                    RawPayload = string.Join("; ", editableEntries.Select(kv => $"{kv.Key}={kv.Value}")),
                    IsPlayerRelevant = true,
                    IsEditable = true,
                    FilePath = WantedListProgramComponent.CacheFilePath,
                    EditableEntries = editableEntries,
                    Parameters = parameters,
                    ByteSize = size,
                    Timestamp = _timing.CurTime
                };

                if (_router.RecordAndValidatePacket(srvCompUid, compUid, pkt) && driveUid != null && fs != null)
                {
                    var localFile = (DatabaseVirtualFile) serverCopy.Clone();
                    localFile.Data.Remove("__tampered");
                    localFile.Data.Remove("__source_hash");
                    localFile.Size = size;
                    _fileSystem.TryWriteFile(driveUid.Value, localFile, fs);
                    online = true;
                    statusMsg = $"Downloaded from {MetaData(srvCompUid).EntityName} [{_router.EnsureDeviceGuid(srvCompUid)}] -> {WantedListProgramComponent.CacheFilePath}";
                }
            }
            else
            {
                statusMsg = $"{netErr} — reading local {WantedListProgramComponent.CacheFilePath}.";
            }
        }
        else
        {
            statusMsg = $"Offline / no NIC — reading local {WantedListProgramComponent.CacheFilePath}.";
        }

        SendWantedListFromLocalCache(progUid, compUid, online, statusMsg);
    }

    public void SendWantedListFromLocalCache(EntityUid progUid, EntityUid compUid, bool online, string statusMsg)
    {
        _fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs);

        var output = new List<WantedRecord>();
        TimeSpan? fileTimestamp = null;

        if (driveUid != null &&
            fs != null &&
            _fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid.Value, WantedListProgramComponent.CacheFilePath, out var db, fs))
        {
            fileTimestamp = db.LastModified;
            foreach (var (k, v) in db.Data.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (!k.StartsWith("rec_"))
                    continue;

                var cols = v.Split('\t');
                if (cols.Length < 3)
                    continue;

                var genRec = new GeneralStationRecord { Name = cols[0], JobTitle = cols[1] };
                Enum.TryParse<SecurityStatus>(cols[2], true, out var secStatus);
                output.Add(new WantedRecord(
                    genRec,
                    secStatus,
                    cols.Length > 3 ? cols[3] : null,
                    cols.Length > 4 ? cols[4] : null,
                    new List<CrimeHistory>()));
            }
        }

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, new WantedListUiState(output, online, statusMsg, fileTimestamp));
    }

    // =========================================================================
    // NEWS READER (Network download from Server/Router local copy -> /news/articles.db)
    // =========================================================================

    [SubscribeLocalEvent]
    private void OnNewsStartup(Entity<NewsReaderProgramComponent> ent, ref ProgramStartupEvent args)
    {
        SyncAndSendNews(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnNewsActivated(Entity<NewsReaderProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        SyncAndSendNews(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
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
                Dirty(ent);
                SendNewsFromLocalCache(ent.Owner, compUid, ent.Comp, $"Reading local {NewsReaderProgramComponent.CacheFilePath}.");
                return;

            case NewsReaderUiAction.Prev:
                ent.Comp.ArticleNumber--;
                Dirty(ent);
                SendNewsFromLocalCache(ent.Owner, compUid, ent.Comp, $"Reading local {NewsReaderProgramComponent.CacheFilePath}.");
                return;

            case NewsReaderUiAction.NotificationSwitch:
                ent.Comp.NotificationOn = !ent.Comp.NotificationOn;
                Dirty(ent);
                SendNewsFromLocalCache(ent.Owner, compUid, ent.Comp, $"Reading local {NewsReaderProgramComponent.CacheFilePath}.");
                return;

            case NewsReaderUiAction.SyncNetwork:
                SyncAndSendNews(ent.Owner, compUid, ent.Comp);
                return;
        }
    }

    private void SyncAndSendNews(EntityUid progUid, EntityUid compUid, NewsReaderProgramComponent comp)
    {
        var status = string.Empty;
        _fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs);

        var station = _station.GetOwningStation(compUid) ?? _station.GetStationsSet().FirstOrDefault();
        if (_router.HasActiveNetworkCard(compUid) &&
            _router.IsNetworkConnected(compUid))
        {
            if (_dataServer.TryGetLocalRecordCopyFromHost(
                    compUid,
                    "NewsService",
                    NewsReaderProgramComponent.CacheFilePath,
                    station,
                    out var srvCompUid,
                    out var serverCopy,
                    out var netErr))
            {
                var editableEntries = new Dictionary<string, string>();
                var parameters = new Dictionary<string, string>();
                foreach (var (k, v) in serverCopy.Data)
                {
                    if (!k.StartsWith("art_"))
                        continue;
                    var cols = v.Split('\t', 4);
                    if (cols.Length < 4)
                        continue;

                    editableEntries[cols[0]] = cols[3];
                    parameters[$"meta:{cols[0]}"] = $"{cols[1]}\t{cols[2]}";
                }

                var size = Math.Max(128, serverCopy.Size);
                var pkt = new NetworkDataPacket
                {
                    PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                    SourceAddress = _router.EnsureDeviceGuid(srvCompUid),
                    TargetAddress = _router.EnsureDeviceGuid(compUid),
                    Protocol = "TCP/NEWS",
                    PayloadSummary = $"Station News ({editableEntries.Count} articles) [{MetaData(srvCompUid).EntityName}]",
                    RawPayload = string.Join("; ", editableEntries.Select(kv => $"{kv.Key}={kv.Value}")),
                    IsPlayerRelevant = true,
                    IsEditable = true,
                    FilePath = NewsReaderProgramComponent.CacheFilePath,
                    EditableEntries = editableEntries,
                    Parameters = parameters,
                    ByteSize = size,
                    Timestamp = _timing.CurTime
                };

                if (_router.RecordAndValidatePacket(srvCompUid, compUid, pkt) && driveUid != null && fs != null)
                {
                    var localFile = (DatabaseVirtualFile) serverCopy.Clone();
                    localFile.Data.Remove("__tampered");
                    localFile.Data.Remove("__source_hash");
                    localFile.Size = size;
                    _fileSystem.TryWriteFile(driveUid.Value, localFile, fs);
                    status = $"Downloaded {editableEntries.Count} articles from {MetaData(srvCompUid).EntityName} -> {NewsReaderProgramComponent.CacheFilePath}";
                }
            }
            else
            {
                status = $"{netErr} — reading local {NewsReaderProgramComponent.CacheFilePath}.";
            }
        }
        else
        {
            status = $"Offline — reading cached {NewsReaderProgramComponent.CacheFilePath}.";
        }

        SendNewsFromLocalCache(progUid, compUid, comp, status);
    }

    public void SendNewsFromLocalCache(EntityUid progUid, EntityUid compUid, NewsReaderProgramComponent comp, string status)
    {
        _fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs);

        var articles = new List<NewsArticle>();
        TimeSpan? fileTimestamp = null;

        if (driveUid != null && fs != null &&
            _fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid.Value, NewsReaderProgramComponent.CacheFilePath, out var db, fs))
        {
            fileTimestamp = db.LastModified;
            foreach (var (k, v) in db.Data.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (!k.StartsWith("art_"))
                    continue;
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
                _ui.SetUiState(progUid, ProgramUiKey.Key, new NewsReaderBoundUserInterfaceState(null, 0, 0, comp.NotificationOn, status, fileTimestamp));
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
                status,
                fileTimestamp));
        }
    }

    /// <summary>
    /// Refreshes any open networked-record UI on a target computer directly from its local HDD file
    /// (for instance, immediately after a modified packet updates its local VFS memory).
    /// </summary>
    public void RefreshRecordProgramsFromLocalCache(EntityUid compUid, string filePath)
    {
        if (filePath.Equals(CrewManifestProgramComponent.CacheFilePath, StringComparison.OrdinalIgnoreCase))
        {
            var q = EntityQueryEnumerator<CrewManifestProgramComponent, ProgramComponent>();
            while (q.MoveNext(out var progUid, out _, out var prog))
            {
                if (prog.Computer == compUid)
                    SendCrewManifestFromLocalCache(progUid, compUid, true, $"Updated from network packet -> {filePath}");
            }
        }
        else if (filePath.Equals(WantedListProgramComponent.CacheFilePath, StringComparison.OrdinalIgnoreCase))
        {
            var q = EntityQueryEnumerator<WantedListProgramComponent, ProgramComponent>();
            while (q.MoveNext(out var progUid, out _, out var prog))
            {
                if (prog.Computer == compUid)
                    SendWantedListFromLocalCache(progUid, compUid, true, $"Updated from network packet -> {filePath}");
            }
        }
        else if (filePath.Equals(NewsReaderProgramComponent.CacheFilePath, StringComparison.OrdinalIgnoreCase))
        {
            var q = EntityQueryEnumerator<NewsReaderProgramComponent, ProgramComponent>();
            while (q.MoveNext(out var progUid, out var newsComp, out var prog))
            {
                if (prog.Computer == compUid)
                    SendNewsFromLocalCache(progUid, compUid, newsComp, $"Updated from network packet -> {filePath}");
            }
        }
    }

    // =========================================================================
    // NETPROBE & LOGPROBE (/logs/*.db)
    // =========================================================================

    [SubscribeLocalEvent]
    private void OnNetProbeStartup(Entity<NetProbeProgramComponent> ent, ref ProgramStartupEvent args) => UpdateNetProbeUi(ent.Owner, args.ComputerUid);
    [SubscribeLocalEvent]
    private void OnNetProbeActivated(Entity<NetProbeProgramComponent> ent, ref ProgramActivatedEvent args) => UpdateNetProbeUi(ent.Owner, args.ComputerUid);

    private void OnNetProbeClear(Entity<NetProbeProgramComponent> ent, ref NetProbeClearMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;
        if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
            _fileSystem.TryDeleteFile(driveUid.Value, NetProbeProgramComponent.CacheFilePath, fs);
        UpdateNetProbeUi(ent.Owner, compUid);
    }

    [SubscribeLocalEvent]
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

    [SubscribeLocalEvent]
    private void OnLogProbeStartup(Entity<LogProbeProgramComponent> ent, ref ProgramStartupEvent args) => UpdateLogProbeUi(ent.Owner, args.ComputerUid, ent.Comp);
    [SubscribeLocalEvent]
    private void OnLogProbeActivated(Entity<LogProbeProgramComponent> ent, ref ProgramActivatedEvent args) => UpdateLogProbeUi(ent.Owner, args.ComputerUid, ent.Comp);

    [SubscribeLocalEvent]
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

    [SubscribeLocalEvent]
    private void OnMedTekStartup(Entity<MedTekProgramComponent> ent, ref ProgramStartupEvent args)
    {
        var target = _computerQuery.TryComp(args.ComputerUid, out var comp) && comp.HostMachine is { } hm ? hm : args.ComputerUid;
        EnsureComp<HealthAnalyzerComponent>(target);
        if (_ui.HasUi(ent.Owner, ProgramUiKey.Key))
            _ui.SetUiState(ent.Owner, ProgramUiKey.Key, new UtilityProgramUiState("MedTek Scanner", "Health scanner module active on host device.", "ACTIVE"));
    }

    [SubscribeLocalEvent]
    private void OnMedTekTerminated(Entity<MedTekProgramComponent> ent, ref ProgramTerminatedEvent args)
    {
        var target = _computerQuery.TryComp(args.ComputerUid, out var comp) && comp.HostMachine is { } hm ? hm : args.ComputerUid;
        RemComp<HealthAnalyzerComponent>(target);
    }

    [SubscribeLocalEvent]
    private void OnAstroNavStartup(Entity<AstroNavProgramComponent> ent, ref ProgramStartupEvent args)
    {
        var target = _computerQuery.TryComp(args.ComputerUid, out var comp) && comp.HostMachine is { } hm ? hm : args.ComputerUid;
        EnsureComp<HandheldGPSComponent>(target);
        if (_ui.HasUi(ent.Owner, ProgramUiKey.Key))
            _ui.SetUiState(ent.Owner, ProgramUiKey.Key, new UtilityProgramUiState("AstroNav GPS", "Handheld GPS transponder active on host device.", "ACTIVE"));
    }

    [SubscribeLocalEvent]
    private void OnAstroNavTerminated(Entity<AstroNavProgramComponent> ent, ref ProgramTerminatedEvent args)
    {
        var target = _computerQuery.TryComp(args.ComputerUid, out var comp) && comp.HostMachine is { } hm ? hm : args.ComputerUid;
        RemComp<HandheldGPSComponent>(target);
    }

    // =========================================================================
    // CARGO ORDERS (/bin/cargo_orders.nte) — BANK ACCOUNT ID + PIN AUTH & WITHDRAWAL
    // =========================================================================

    [SubscribeLocalEvent]
    private void OnCargoOrdersStartup(Entity<CargoOrdersProgramComponent> ent, ref ProgramStartupEvent args)
    {
        UpdateCargoOrdersUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnCargoOrdersActivated(Entity<CargoOrdersProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdateCargoOrdersUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnCargoOrdersBuiOpened(Entity<CargoOrdersProgramComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (_programQuery.TryComp(ent.Owner, out var prog) && prog.Computer is { } compUid)
            UpdateCargoOrdersUi(ent.Owner, compUid, ent.Comp);
    }

    private void OnCargoOrdersAction(Entity<CargoOrdersProgramComponent> ent, ref CargoOrdersActionMessage msg)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        ResolveOrEnsureStationBank(compUid, out var stationUid, out var bankComp, out var orderDb);

        switch (msg.Action)
        {
            case CargoOrdersAction.Refresh:
                ent.Comp.StatusMessage = "Synced cargo catalog and bank account ledger.";
                break;

            case CargoOrdersAction.SelectCategory:
                ent.Comp.SelectedCategory = string.IsNullOrWhiteSpace(msg.Category) ? "All" : msg.Category.Trim();
                break;

            case CargoOrdersAction.AuthenticateBank:
                if (_cargo.TryAuthenticateBankAccount(
                        (stationUid, bankComp),
                        msg.AccountId,
                        msg.Pin,
                        out var matchedAcc,
                        out var resolvedId,
                        out var curBalance))
                {
                    ent.Comp.IsAuthenticated = true;
                    ent.Comp.AuthenticatedAccountId = resolvedId;
                    ent.Comp.AuthenticatedPin = msg.Pin.Trim();
                    ent.Comp.AuthenticatedAccountProto = matchedAcc.Id;
                    ent.Comp.StatusMessage = $"Authenticated bank account {resolvedId} ({matchedAcc.Id}) — Balance: ${curBalance}.";
                    _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/id_swipe.ogg"), compUid);
                }
                else
                {
                    ent.Comp.IsAuthenticated = false;
                    ent.Comp.AuthenticatedAccountId = string.Empty;
                    ent.Comp.AuthenticatedPin = string.Empty;
                    ent.Comp.AuthenticatedAccountProto = string.Empty;
                    ent.Comp.StatusMessage = "Bank authentication failed: invalid Bank Account ID or PIN.";
                }
                break;

            case CargoOrdersAction.LogoutBank:
                ent.Comp.IsAuthenticated = false;
                ent.Comp.AuthenticatedAccountId = string.Empty;
                ent.Comp.AuthenticatedPin = string.Empty;
                ent.Comp.AuthenticatedAccountProto = string.Empty;
                ent.Comp.StatusMessage = "Logged out of bank account.";
                break;

            case CargoOrdersAction.QueueOrder:
                HandleCargoQueueOrder(ent.Comp, compUid, stationUid, bankComp, orderDb, msg);
                break;

            case CargoOrdersAction.PlaceAndPayOrder:
                HandleCargoPlaceAndPayOrder(ent.Comp, compUid, stationUid, bankComp, orderDb, msg);
                break;

            case CargoOrdersAction.ApproveOrder:
                HandleCargoApproveOrder(ent.Comp, compUid, stationUid, bankComp, orderDb, msg);
                break;

            case CargoOrdersAction.RemoveOrder:
                HandleCargoRemoveOrder(ent.Comp, stationUid, orderDb, msg.OrderId);
                break;

            case CargoOrdersAction.WithdrawCash:
                HandleCargoWithdrawCash(ent.Comp, compUid, stationUid, bankComp, msg);
                break;
        }

        Dirty(ent);
        UpdateCargoOrdersUi(ent.Owner, compUid, ent.Comp);
    }

    private bool TryResolveOrderBankAuth(
        CargoOrdersProgramComponent comp,
        EntityUid stationUid,
        StationBankAccountComponent bankComp,
        string? msgAccountId,
        string? msgPin,
        out ProtoId<CargoAccountPrototype> matchedAccount,
        out string resolvedAccountId,
        out string usedPin,
        out int balance)
    {
        var accId = !string.IsNullOrWhiteSpace(msgAccountId) ? msgAccountId.Trim() : comp.AuthenticatedAccountId;
        var pin = !string.IsNullOrWhiteSpace(msgPin) ? msgPin.Trim() : comp.AuthenticatedPin;
        usedPin = pin;

        if (_cargo.TryAuthenticateBankAccount((stationUid, bankComp), accId, pin, out matchedAccount, out resolvedAccountId, out balance))
        {
            comp.IsAuthenticated = true;
            comp.AuthenticatedAccountId = resolvedAccountId;
            comp.AuthenticatedPin = pin;
            comp.AuthenticatedAccountProto = matchedAccount.Id;
            return true;
        }

        return false;
    }

    private void HandleCargoQueueOrder(
        CargoOrdersProgramComponent comp,
        EntityUid compUid,
        EntityUid stationUid,
        StationBankAccountComponent bankComp,
        StationCargoOrderDatabaseComponent orderDb,
        CargoOrdersActionMessage msg)
    {
        if (!_proto.TryIndex<CargoProductPrototype>(msg.ProductId, out var product))
        {
            comp.StatusMessage = "Order failed: Unknown product prototype.";
            return;
        }

        var qty = Math.Clamp(msg.Quantity, 1, 20);
        var requester = !string.IsNullOrWhiteSpace(msg.Requester)
            ? msg.Requester.Trim()
            : (msg.Actor.IsValid() && Exists(msg.Actor) ? MetaData(msg.Actor).EntityName : "Crew");
        var reason = !string.IsNullOrWhiteSpace(msg.Reason) ? msg.Reason.Trim() : "Station supply request";

        var accountProto = !string.IsNullOrWhiteSpace(comp.AuthenticatedAccountProto)
            ? new ProtoId<CargoAccountPrototype>(comp.AuthenticatedAccountProto)
            : ResolveConsoleDefaultAccount(compUid, bankComp);

        if (!orderDb.Orders.TryGetValue(accountProto, out var list))
        {
            list = new List<CargoOrderData>();
            orderDb.Orders[accountProto] = list;
        }

        var orderId = ++orderDb.NumOrdersCreated;
        var order = new CargoOrderData(orderId, product.ID, qty, requester, reason, accountProto);
        list.Add(order);
        comp.StatusMessage = $"Queued request #{orderId}: {qty}x {product.Name} (${product.Cost * qty}). Authenticate bank account to approve & withdraw.";
    }

    private void HandleCargoPlaceAndPayOrder(
        CargoOrdersProgramComponent comp,
        EntityUid compUid,
        EntityUid stationUid,
        StationBankAccountComponent bankComp,
        StationCargoOrderDatabaseComponent orderDb,
        CargoOrdersActionMessage msg)
    {
        if (!_proto.TryIndex<CargoProductPrototype>(msg.ProductId, out var product))
        {
            comp.StatusMessage = "Order failed: Unknown product prototype.";
            return;
        }

        if (!TryResolveOrderBankAuth(
                comp,
                stationUid,
                bankComp,
                msg.AccountId,
                msg.Pin,
                out var authAccount,
                out var resolvedAccountId,
                out var usedPin,
                out var curBalance))
        {
            comp.StatusMessage = "Order denied: Valid Bank Account ID and PIN required to authorize & withdraw funds.";
            return;
        }

        var qty = Math.Clamp(msg.Quantity, 1, 20);
        var totalCost = product.Cost * qty;
        if (curBalance < totalCost)
        {
            comp.StatusMessage = $"Insufficient funds in {resolvedAccountId} (Balance: ${curBalance}, Order Cost: ${totalCost}).";
            return;
        }

        if (!_cargo.TryWithdrawFromAuthenticatedAccount(
                (stationUid, bankComp),
                resolvedAccountId,
                usedPin,
                totalCost,
                out _,
                out _,
                out var remainingBalance))
        {
            comp.StatusMessage = "Withdrawal failed during cargo order.";
            return;
        }

        var requester = !string.IsNullOrWhiteSpace(msg.Requester)
            ? msg.Requester.Trim()
            : (msg.Actor.IsValid() && Exists(msg.Actor) ? MetaData(msg.Actor).EntityName : resolvedAccountId);
        var reason = !string.IsNullOrWhiteSpace(msg.Reason) ? msg.Reason.Trim() : $"Paid via {resolvedAccountId}";

        if (!orderDb.Orders.TryGetValue(authAccount, out var list))
        {
            list = new List<CargoOrderData>();
            orderDb.Orders[authAccount] = list;
        }

        var orderId = ++orderDb.NumOrdersCreated;
        var order = new CargoOrderData(orderId, product.ID, qty, requester, reason, authAccount)
        {
            Approved = true,
            Approver = $"{requester} [{resolvedAccountId}]",
            ApprovingConsole = GetNetEntity(compUid)
        };
        list.Add(order);

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg"), compUid);
        comp.StatusMessage = $"Order #{orderId} paid & approved: {qty}x {product.Name} (-${totalCost} withdrawn from {resolvedAccountId}, remaining: ${remainingBalance}).";
    }

    private void HandleCargoApproveOrder(
        CargoOrdersProgramComponent comp,
        EntityUid compUid,
        EntityUid stationUid,
        StationBankAccountComponent bankComp,
        StationCargoOrderDatabaseComponent orderDb,
        CargoOrdersActionMessage msg)
    {
        if (!TryResolveOrderBankAuth(
                comp,
                stationUid,
                bankComp,
                msg.AccountId,
                msg.Pin,
                out var authAccount,
                out var resolvedAccountId,
                out var usedPin,
                out var curBalance))
        {
            comp.StatusMessage = "Approval denied: Valid Bank Account ID and PIN required to withdraw order funds.";
            return;
        }

        CargoOrderData? targetOrder = null;
        foreach (var list in orderDb.Orders.Values)
        {
            targetOrder = list.FirstOrDefault(o => o.OrderId == msg.OrderId && !o.Approved);
            if (targetOrder != null)
                break;
        }

        if (targetOrder == null || !_proto.TryIndex<CargoProductPrototype>(targetOrder.Product, out var product))
        {
            comp.StatusMessage = "Order not found or already approved.";
            return;
        }

        var totalCost = product.Cost * targetOrder.OrderQuantity;
        if (curBalance < totalCost)
        {
            comp.StatusMessage = $"Insufficient funds in {resolvedAccountId} (Balance: ${curBalance}, Order Cost: ${totalCost}).";
            return;
        }

        if (!_cargo.TryWithdrawFromAuthenticatedAccount(
                (stationUid, bankComp),
                resolvedAccountId,
                usedPin,
                totalCost,
                out _,
                out _,
                out var remainingBalance))
        {
            comp.StatusMessage = "Withdrawal failed during order approval.";
            return;
        }

        var approverName = msg.Actor.IsValid() && Exists(msg.Actor) ? MetaData(msg.Actor).EntityName : resolvedAccountId;
        targetOrder.Account = authAccount;
        targetOrder.Approved = true;
        targetOrder.Approver = $"{approverName} [{resolvedAccountId}]";
        targetOrder.ApprovingConsole = GetNetEntity(compUid);

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg"), compUid);
        comp.StatusMessage = $"Approved order #{targetOrder.OrderId} ({targetOrder.OrderQuantity}x {product.Name}): -${totalCost} withdrawn from {resolvedAccountId} (remaining: ${remainingBalance}).";
    }

    private void HandleCargoRemoveOrder(
        CargoOrdersProgramComponent comp,
        EntityUid stationUid,
        StationCargoOrderDatabaseComponent orderDb,
        int orderId)
    {
        foreach (var list in orderDb.Orders.Values)
        {
            var idx = list.FindIndex(o => o.OrderId == orderId);
            if (idx >= 0)
            {
                list.RemoveAt(idx);
                comp.StatusMessage = $"Removed order #{orderId}.";
                return;
            }
        }

        comp.StatusMessage = $"Order #{orderId} not found.";
    }

    private void HandleCargoWithdrawCash(
        CargoOrdersProgramComponent comp,
        EntityUid compUid,
        EntityUid stationUid,
        StationBankAccountComponent bankComp,
        CargoOrdersActionMessage msg)
    {
        if (!TryResolveOrderBankAuth(
                comp,
                stationUid,
                bankComp,
                msg.AccountId,
                msg.Pin,
                out _,
                out var resolvedAccountId,
                out var usedPin,
                out var curBalance))
        {
            comp.StatusMessage = "Withdrawal denied: Valid Bank Account ID and PIN required.";
            return;
        }

        var amount = Math.Max(1, msg.Quantity);
        if (curBalance < amount)
        {
            comp.StatusMessage = $"Insufficient funds in {resolvedAccountId} (Balance: ${curBalance}, Requested: ${amount}).";
            return;
        }

        if (!_cargo.TryWithdrawFromAuthenticatedAccount(
                (stationUid, bankComp),
                resolvedAccountId,
                usedPin,
                amount,
                out _,
                out _,
                out var remainingBalance))
        {
            comp.StatusMessage = "Cash withdrawal failed.";
            return;
        }

        var spawnTarget = _computerQuery.TryComp(compUid, out var cComp) && cComp.HostMachine is { } hm ? hm : compUid;
        if (_proto.TryIndex<StackPrototype>(CreditStackProto, out var stackProto))
        {
            _stack.SpawnAtPosition(amount, stackProto, Transform(spawnTarget).Coordinates);
        }

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg"), spawnTarget);
        comp.StatusMessage = $"Withdrew ${amount} in cash from {resolvedAccountId} (remaining: ${remainingBalance}).";
    }

    private ProtoId<CargoAccountPrototype> ResolveConsoleDefaultAccount(EntityUid compUid, StationBankAccountComponent bankComp)
    {
        if (_computerQuery.TryComp(compUid, out var cComp) &&
            cComp.HostMachine is { } hostUid &&
            TryComp<CargoOrderConsoleComponent>(hostUid, out var legacyConsole))
        {
            return legacyConsole.Account;
        }

        return bankComp.PrimaryAccount;
    }

    private void ResolveOrEnsureStationBank(
        EntityUid compUid,
        out EntityUid stationUid,
        out StationBankAccountComponent bankComp,
        out StationCargoOrderDatabaseComponent orderDb)
    {
        var anchorUid = _computerQuery.TryComp(compUid, out var cComp) && cComp.HostMachine is { } hm ? hm : compUid;
        var owning = _station.GetOwningStation(anchorUid) ?? _station.GetStationsSet().FirstOrDefault();

        if (owning != EntityUid.Invalid && Exists(owning))
        {
            stationUid = owning;
        }
        else
        {
            // Fallback for test grids without a full Station entity: find or attach on grid/map
            var q = EntityQueryEnumerator<StationBankAccountComponent>();
            if (q.MoveNext(out var existingBankUid, out _))
            {
                stationUid = existingBankUid;
            }
            else
            {
                var xform = Transform(anchorUid);
                stationUid = xform.GridUid ?? xform.MapUid ?? anchorUid;
            }
        }

        bankComp = EnsureComp<StationBankAccountComponent>(stationUid);
        orderDb = EnsureComp<StationCargoOrderDatabaseComponent>(stationUid);
        foreach (var acc in bankComp.Accounts.Keys)
        {
            if (!orderDb.Orders.ContainsKey(acc))
                orderDb.Orders[acc] = new List<CargoOrderData>();
        }
    }

    private void UpdateCargoOrdersUi(EntityUid progUid, EntityUid compUid, CargoOrdersProgramComponent comp)
    {
        ResolveOrEnsureStationBank(compUid, out var stationUid, out var bankComp, out var orderDb);

        var authBalance = 0;
        var authName = string.Empty;
        if (comp.IsAuthenticated &&
            _cargo.TryAuthenticateBankAccount(
                (stationUid, bankComp),
                comp.AuthenticatedAccountId,
                comp.AuthenticatedPin,
                out var matchedProto,
                out var resolvedId,
                out var bal))
        {
            authBalance = bal;
            comp.AuthenticatedAccountId = resolvedId;
            comp.AuthenticatedAccountProto = matchedProto.Id;
            authName = _proto.TryIndex<CargoAccountPrototype>(matchedProto, out var accProto)
                ? Loc.GetString(accProto.Name)
                : matchedProto.Id;
        }
        else if (comp.IsAuthenticated)
        {
            comp.IsAuthenticated = false;
        }

        var allowedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "market" };
        if (_computerQuery.TryComp(compUid, out var cComp) &&
            cComp.HostMachine is { } hostUid &&
            TryComp<CargoOrderConsoleComponent>(hostUid, out var legacyConsole))
        {
            foreach (var g in legacyConsole.AllowedGroups)
                allowedGroups.Add(g.Id);
        }

        var orders = new List<CargoOrderEntryDto>();
        foreach (var list in orderDb.Orders.Values)
        {
            foreach (var o in list)
            {
                orders.Add(new CargoOrderEntryDto(
                    o.OrderId,
                    o.Product.Id,
                    o.OrderQuantity,
                    o.Requester,
                    o.Reason,
                    o.Account.Id,
                    o.Approved,
                    o.Approver ?? string.Empty));
            }
        }
        orders.Sort((a, b) => b.OrderId.CompareTo(a.OrderId));

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
        {
            _ui.SetUiState(progUid, ProgramUiKey.Key, new CargoOrdersUiState(
                comp.IsAuthenticated,
                comp.AuthenticatedAccountId,
                authName,
                authBalance,
                comp.SelectedCategory,
                comp.StatusMessage,
                allowedGroups.OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList(),
                orders));
        }
    }

    // =========================================================================
    // CREW MONITOR (/bin/crew_monitor.nte)
    // =========================================================================

    [SubscribeLocalEvent]
    private void OnCrewMonitorStartup(Entity<CrewMonitorProgramComponent> ent, ref ProgramStartupEvent args) => UpdateCrewMonitorUi(ent.Owner, args.ComputerUid);
    [SubscribeLocalEvent]
    private void OnCrewMonitorActivated(Entity<CrewMonitorProgramComponent> ent, ref ProgramActivatedEvent args) => UpdateCrewMonitorUi(ent.Owner, args.ComputerUid);
    private void OnCrewMonitorBuiOpened(Entity<CrewMonitorProgramComponent> ent, ref BoundUIOpenedEvent args)
    {
        var active = EnsureComp<ActiveCrewMonitorComponent>(ent.Owner);
        active.NextSync = _timing.CurTime + TimeSpan.FromSeconds(1.0);
        if (_programQuery.TryComp(ent.Owner, out var prog) && prog.Computer is { } compUid)
            UpdateCrewMonitorUi(ent.Owner, compUid);
    }

    private void OnCrewMonitorBuiClosed(Entity<CrewMonitorProgramComponent> ent, ref BoundUIClosedEvent args)
    {
        if (!_ui.HasUi(ent.Owner, ProgramUiKey.Key) || !_ui.IsUiOpen(ent.Owner, ProgramUiKey.Key))
            RemCompDeferred<ActiveCrewMonitorComponent>(ent.Owner);
    }

    private void OnCrewMonitorRefresh(Entity<CrewMonitorProgramComponent> ent, ref CrewMonitorRefreshMessage args)
    {
        if (_programQuery.TryComp(ent.Owner, out var prog) && prog.Computer is { } compUid)
            UpdateCrewMonitorUi(ent.Owner, compUid);
    }

    private void UpdateCrewMonitorUi(EntityUid progUid, EntityUid compUid)
    {
        var anchorUid = _computerQuery.TryComp(compUid, out var cComp) && cComp.HostMachine is { } hm ? hm : compUid;
        var station = _station.GetOwningStation(anchorUid) ?? _station.GetStationsSet().FirstOrDefault();
        var stationName = station != EntityUid.Invalid && Exists(station) ? MetaData(station).EntityName : "Station Telemetry";
        var anchorXform = Transform(anchorUid);
        var anchorMap = anchorXform.MapID;

        EntityUid? fallbackGridUid = anchorXform.GridUid is { Valid: true } ag ? ag : null;
        if (fallbackGridUid == null)
        {
            var gridQuery = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
            while (gridQuery.MoveNext(out var gUid, out _, out var gXform))
            {
                if (gXform.MapID == anchorMap)
                {
                    fallbackGridUid = gUid;
                    break;
                }
            }
        }
        NetEntity? stationGridNet = fallbackGridUid is { Valid: true } sg ? GetNetEntity(sg) : null;

        var recordsByName = new Dictionary<string, GeneralStationRecord>(StringComparer.OrdinalIgnoreCase);
        if (station != EntityUid.Invalid && Exists(station))
        {
            foreach (var (_, genRec) in _records.GetRecordsOfType<GeneralStationRecord>(station))
            {
                if (!string.IsNullOrWhiteSpace(genRec.Name))
                    recordsByName[genRec.Name] = genRec;
            }
        }

        var list = new List<CrewSensorEntryDto>();
        var sensorQuery = EntityQueryEnumerator<SuitSensorComponent, TransformComponent>();
        while (sensorQuery.MoveNext(out _, out var sensor, out var sensorXform))
        {
            if (sensorXform.MapID != anchorMap)
                continue;

            if (sensor.Mode == SuitSensorMode.SensorOff || sensor.User is not { Valid: true } wearer || !Exists(wearer))
                continue;

            var name = MetaData(wearer).EntityName;
            var jobTitle = "Crew";
            var jobIcon = "JobIconNoId";

            if (_idCard.TryFindIdCard(wearer, out var idCard))
            {
                if (!string.IsNullOrWhiteSpace(idCard.Comp.FullName))
                    name = idCard.Comp.FullName;
                if (!string.IsNullOrWhiteSpace(idCard.Comp.LocalizedJobTitle))
                    jobTitle = idCard.Comp.LocalizedJobTitle;
                if (!string.IsNullOrWhiteSpace(idCard.Comp.JobIcon))
                    jobIcon = idCard.Comp.JobIcon;
            }

            if (recordsByName.TryGetValue(name, out var genRec))
            {
                if (!string.IsNullOrWhiteSpace(genRec.JobTitle))
                    jobTitle = genRec.JobTitle;
                if (!string.IsNullOrWhiteSpace(genRec.JobIcon))
                    jobIcon = genRec.JobIcon;
            }

            var alive = !_mobState.IsDead(wearer);
            var hasVitals = sensor.Mode >= SuitSensorMode.SensorVitals;
            var isCrit = hasVitals && _mobState.IsCritical(wearer);
            var dmg = hasVitals && TryComp<DamageableComponent>(wearer, out var dmgComp)
                ? (int) _damageable.GetTotalDamage((wearer, dmgComp)).Int()
                : 0;

            var healthRatio = 1f;
            if (!alive)
            {
                healthRatio = 0f;
            }
            else if (hasVitals)
            {
                var critLimit = 100f;
                if (_mobThreshold.TryGetThresholdForState(wearer, MobState.Critical, out var critThresh) && critThresh.Value.Float() > 0f)
                    critLimit = critThresh.Value.Float();
                healthRatio = Math.Clamp(1f - dmg / critLimit, 0f, 1f);
            }

            var hasCoords = sensor.Mode == SuitSensorMode.SensorCords;
            var coords = "Hidden";
            NetEntity? gridNet = null;
            var localPos = Vector2.Zero;
            if (hasCoords)
            {
                var wearerXform = Transform(wearer);
                if (wearerXform.GridUid is { Valid: true } gUid)
                {
                    gridNet = GetNetEntity(gUid);
                    stationGridNet ??= gridNet;
                    localPos = Vector2.Transform(_transform.GetWorldPosition(wearerXform), _transform.GetInvWorldMatrix(gUid));
                    coords = $"({localPos.X:0.0}, {localPos.Y:0.0})";
                }
                else
                {
                    localPos = _transform.GetWorldPosition(wearerXform);
                    coords = $"({localPos.X:0.0}, {localPos.Y:0.0})";
                }
            }

            list.Add(new CrewSensorEntryDto(
                name,
                jobTitle,
                alive,
                dmg,
                coords,
                hasCoords ? GetNetEntity(wearer) : null,
                gridNet,
                localPos,
                hasCoords,
                jobIcon,
                hasVitals,
                isCrit,
                healthRatio));
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, new CrewMonitorUiState(stationName, list, stationGridNet));
    }

    // =========================================================================
    // COMMUNICATIONS CONSOLE (/bin/comms_console.nte)
    // =========================================================================

    [SubscribeLocalEvent]
    private void OnCommsStartup(Entity<CommsConsoleProgramComponent> ent, ref ProgramStartupEvent args) => UpdateCommsUi(ent.Owner, args.ComputerUid, ent.Comp, null);
    [SubscribeLocalEvent]
    private void OnCommsActivated(Entity<CommsConsoleProgramComponent> ent, ref ProgramActivatedEvent args) => UpdateCommsUi(ent.Owner, args.ComputerUid, ent.Comp, null);

    [SubscribeLocalEvent]
    private void OnCommsCardReaderChanged(Entity<CommsConsoleProgramComponent> ent, ref ComputerCardReaderChangedEvent args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid || compUid != args.ComputerUid)
            return;

        if (!HasCommsAccess(compUid, ent.Comp, null) && _computer.GetInsertedIdCards(compUid).Count == 0)
            ent.Comp.StatusMessage = "ID card removed from slot — Command authorization locked.";

        Dirty(ent);
        UpdateCommsUi(ent.Owner, compUid, ent.Comp, null);
    }

    [SubscribeLocalEvent]
    private void OnCommsCardScanned(Entity<CommsConsoleProgramComponent> ent, ref ProgramCardScannedEvent scanned)
    {
        if (scanned.Handled)
            return;

        if (_accessReader.FindAccessTags(scanned.Card).Contains(CommandAccess))
        {
            if (scanned.Retained)
            {
                ent.Comp.StatusMessage = $"Authorized Command broadcast via ID Slot ({MetaData(scanned.Card).EntityName}) while inserted.";
            }
            else
            {
                ent.Comp.AuthorizedBySwipedCard = true;
                ent.Comp.StatusMessage = $"Authorized Command broadcast via swiped ID ({MetaData(scanned.Card).EntityName}).";
            }
            _popup.PopupEntity("Communications console authorized.", scanned.HostUid, scanned.User);
        }
        else
        {
            ent.Comp.StatusMessage = "Access denied: Swiped ID card lacks Command clearance.";
            _popup.PopupEntity("Access denied: Command clearance required.", scanned.HostUid, scanned.User);
        }

        scanned.Handled = true;
        Dirty(ent);
        UpdateCommsUi(ent.Owner, scanned.ComputerUid, ent.Comp, scanned.User);
    }
    private void OnCommsBuiOpened(Entity<CommsConsoleProgramComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (_programQuery.TryComp(ent.Owner, out var prog) && prog.Computer is { } compUid)
            UpdateCommsUi(ent.Owner, compUid, ent.Comp, args.Actor);
    }

    private void OnCommsAction(Entity<CommsConsoleProgramComponent> ent, ref CommsConsoleActionMessage msg)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        var canBroadcast = HasCommsAccess(compUid, ent.Comp, msg.Actor);
        var station = _station.GetOwningStation(compUid) ?? _station.GetStationsSet().FirstOrDefault();

        switch (msg.Action)
        {
            case CommsConsoleAction.BroadcastAnnouncement:
                if (!canBroadcast)
                {
                    ent.Comp.StatusMessage = "Access denied: Command authorization required.";
                    break;
                }
                var text = (msg.Text ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(text))
                {
                    ent.Comp.StatusMessage = "Announcement message cannot be empty.";
                    break;
                }
                var sender = msg.Actor.IsValid() && Exists(msg.Actor) ? MetaData(msg.Actor).EntityName : "Command";
                _chat.DispatchStationAnnouncement(station != EntityUid.Invalid ? station : compUid, text, sender);
                ent.Comp.StatusMessage = $"Broadcasted announcement ({text.Length} chars).";
                break;

            case CommsConsoleAction.SetAlertLevel:
                if (!canBroadcast)
                {
                    ent.Comp.StatusMessage = "Access denied: Command authorization required.";
                    break;
                }
                if (station != EntityUid.Invalid && !string.IsNullOrWhiteSpace(msg.Text))
                {
                    _alertLevel.SetLevel(station, msg.Text.Trim(), true, true);
                    ent.Comp.StatusMessage = $"Station alert level set to {msg.Text.Trim()}.";
                }
                break;

            case CommsConsoleAction.RevokeAuth:
                ent.Comp.AuthorizedBySwipedCard = false;
                ent.Comp.StatusMessage = "Card authorization revoked.";
                break;
        }

        Dirty(ent);
        UpdateCommsUi(ent.Owner, compUid, ent.Comp, msg.Actor);
    }

    private bool HasCommsAccess(EntityUid compUid, CommsConsoleProgramComponent comp, EntityUid? actor)
    {
        if (comp.AuthorizedBySwipedCard)
            return true;

        foreach (var insCard in _computer.GetInsertedIdCards(compUid))
        {
            if (_accessReader.FindAccessTags(insCard).Contains(CommandAccess))
                return true;
        }

        if (actor is { Valid: true } user && Exists(user) && _accessReader.FindAccessTags(user).Contains(CommandAccess))
            return true;

        if (_fileSystem.TryFindFileSystem(compUid, out _, out var fs) && fs != null)
        {
            foreach (var f in fs.Files.Values)
            {
                if (f is AccessCodesVirtualFile acc && !acc.IsEncrypted && acc.AccessTags.Contains(CommandAccess))
                    return true;
            }
        }

        return false;
    }

    private void UpdateCommsUi(EntityUid progUid, EntityUid compUid, CommsConsoleProgramComponent comp, EntityUid? actor)
    {
        var station = _station.GetOwningStation(compUid) ?? _station.GetStationsSet().FirstOrDefault();
        var currentLevel = "green";
        var levels = new List<string> { "green", "blue", "red" };

        if (station != EntityUid.Invalid && TryComp<AlertLevelComponent>(station, out var alertComp))
        {
            currentLevel = alertComp.CurrentAlertLevel.Id;
            var selectable = _alertLevel.GetSelectableAlertLevels((station, alertComp));
            if (selectable.Count > 0)
                levels = selectable.Select(l => l.Id).ToList();
        }

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
        {
            _ui.SetUiState(progUid, ProgramUiKey.Key, new CommsConsoleUiState(
                HasCommsAccess(compUid, comp, actor),
                currentLevel,
                levels,
                comp.StatusMessage,
                comp.AuthorizedBySwipedCard));
        }
    }

    // =========================================================================
    // SOLAR CONTROL CONSOLE (/bin/solar_control.nte)
    // =========================================================================

    [SubscribeLocalEvent]
    private void OnSolarStartup(Entity<SolarControlProgramComponent> ent, ref ProgramStartupEvent args) => UpdateSolarUi(ent.Owner, ent.Comp);
    [SubscribeLocalEvent]
    private void OnSolarActivated(Entity<SolarControlProgramComponent> ent, ref ProgramActivatedEvent args) => UpdateSolarUi(ent.Owner, ent.Comp);
    private void OnSolarBuiOpened(Entity<SolarControlProgramComponent> ent, ref BoundUIOpenedEvent args) => UpdateSolarUi(ent.Owner, ent.Comp);

    private void OnSolarAction(Entity<SolarControlProgramComponent> ent, ref SolarControlActionMessage msg)
    {
        switch (msg.Action)
        {
            case SolarControlAction.SetAngle:
                if (float.IsFinite(msg.ValueDegrees))
                {
                    _solar.TargetPanelRotation = Angle.FromDegrees(msg.ValueDegrees).Reduced();
                    ent.Comp.StatusMessage = $"Set solar array target angle to {msg.ValueDegrees:0.0}°.";
                }
                break;

            case SolarControlAction.SetVelocity:
                if (float.IsFinite(msg.ValueDegrees))
                {
                    var clamped = Math.Clamp(msg.ValueDegrees, (float) -PowerSolarSystem.MaxPanelVelocityDegrees, (float) PowerSolarSystem.MaxPanelVelocityDegrees);
                    _solar.TargetPanelVelocity = Angle.FromDegrees(clamped);
                    ent.Comp.StatusMessage = $"Set solar array tracking velocity to {clamped:0.00}°/min.";
                }
                break;
        }

        Dirty(ent);
        UpdateSolarUi(ent.Owner, ent.Comp);
    }

    private void UpdateSolarUi(EntityUid progUid, SolarControlProgramComponent comp)
    {
        if (_ui.HasUi(progUid, ProgramUiKey.Key))
        {
            _ui.SetUiState(progUid, ProgramUiKey.Key, new SolarControlUiState(
                (float) _solar.TargetPanelRotation.Degrees,
                (float) _solar.TargetPanelVelocity.Degrees,
                (float) _solar.TowardsSun.Degrees,
                _solar.TotalPanelPower,
                comp.StatusMessage));
        }
    }
}
