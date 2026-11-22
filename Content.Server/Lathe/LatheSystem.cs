using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Lathe.Components;
using Content.Server.Materials;
using Content.Server.Popups;
using Content.Server.Power.EntitySystems;
using Content.Server.Radio.EntitySystems;
using Content.Server.Stack;
using Content.Shared._Oxyd;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Packets;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Components;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.DeviceLinking.Systems;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.UserInterface;
using Content.Shared.Database;
using Content.Shared.Emag.Systems;
using Content.Shared.Lathe;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Localizations;
using Content.Shared.Materials;
using Content.Shared.Power;
using Content.Shared.ReagentSpeed;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using JetBrains.Annotations;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Lathe
{
    [UsedImplicitly]
    public sealed partial class LatheSystem : SharedLatheSystem
    {
        [Dependency] private IGameTiming _timing = default!;
        [Dependency] private IAdminLogManager _adminLogger = default!;
        [Dependency] private AtmosphereSystem _atmosphere = default!;
        [Dependency] private SharedAppearanceSystem _appearance = default!;
        [Dependency] private SharedAudioSystem _audio = default!;
        [Dependency] private ContainerSystem _container = default!;
        [Dependency] private EmagSystem _emag = default!;
        [Dependency] private UserInterfaceSystem _uiSys = default!;
        [Dependency] private MaterialStorageSystem _materialStorage = default!;
        [Dependency] private PopupSystem _popup = default!;
        [Dependency] private PuddleSystem _puddle = default!;
        [Dependency] private ReagentSpeedSystem _reagentSpeed = default!;
        [Dependency] private SharedSolutionContainerSystem _solution = default!;
        [Dependency] private StackSystem _stack = default!;
        [Dependency] private TransformSystem _transform = default!;
        [Dependency] private RadioSystem _radio = default!;
        [Dependency] private ItemSlotsSystem itemslots = default!;
        [Dependency] private SharedInteractionSystem _interaction = default!;
        [Dependency] private DeviceLinkSystem _deviceLink = default!;
        [Dependency] private SharedFileSystemSystem _fileSystem = default!;

        private static readonly ProtoId<SourcePortPrototype> PortAutolatheStatusSource = "AutolatheStatusSource";
        private static readonly ProtoId<SourcePortPrototype> PortMachineStatusSender = "MachineStatusSender";
        private static readonly ProtoId<SinkPortPrototype> PortAutolatheBlueprintSink = "AutolatheBlueprintSink";
        private static readonly ProtoId<SinkPortPrototype> PortAutolatheCommandSink = "AutolatheCommandSink";
        private static readonly ProtoId<SinkPortPrototype> PortAutolatheSetRecipeSink = "AutolatheSetRecipeSink";
        private static readonly ProtoId<SinkPortPrototype> PortMachineCommandReceiver = "MachineCommandReceiver";
        /// <summary>
        /// Per-tick cache
        /// </summary>
        private readonly List<GasMixture> _environments = new();

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<LatheComponent, GetMaterialWhitelistEvent>(OnGetWhitelist);
            SubscribeLocalEvent<LatheComponent, MapInitEvent>(OnMapInit);
            SubscribeLocalEvent<LatheComponent, PowerChangedEvent>(OnPowerChanged);
            SubscribeLocalEvent<LatheComponent, TechnologyDatabaseModifiedEvent>(OnDatabaseModified);
            SubscribeLocalEvent<LatheAnnouncingComponent, TechnologyDatabaseModifiedEvent>(OnTechnologyDatabaseModified);
            SubscribeLocalEvent<LatheComponent, ResearchRegistrationChangedEvent>(OnResearchRegistrationChanged);

            SubscribeLocalEvent<LatheComponent, LatheQueueRecipeMessage>(OnLatheQueueRecipeMessage);
            SubscribeLocalEvent<LatheComponent, LatheSyncRequestMessage>(OnLatheSyncRequestMessage);
            SubscribeLocalEvent<LatheComponent, LatheDeleteRequestMessage>(OnLatheDeleteRequestMessage);
            SubscribeLocalEvent<LatheComponent, LatheMoveRequestMessage>(OnLatheMoveRequestMessage);
            SubscribeLocalEvent<LatheComponent, LatheAbortFabricationMessage>(OnLatheAbortFabricationMessage);
            SubscribeLocalEvent<LatheComponent, LatheSelectBlueprintFileMessage>(OnLatheSelectBlueprintFileMessage);

            SubscribeLocalEvent<LatheComponent, BeforeActivatableUIOpenEvent>((u, c, _) => UpdateUserInterfaceState(u, c));
            SubscribeLocalEvent<LatheComponent, MaterialAmountChangedEvent>(OnMaterialAmountChanged);
            SubscribeLocalEvent<TechnologyDatabaseComponent, LatheGetRecipesEvent>(OnGetRecipes);
            SubscribeLocalEvent<EmagLatheRecipesComponent, LatheGetRecipesEvent>(GetEmagLatheRecipes);
            SubscribeLocalEvent<LatheHeatProducingComponent, LatheStartPrintingEvent>(OnHeatStartPrinting);
            SubscribeLocalEvent<LatheComponent, SignalReceivedEvent>(OnSignalReceived);
            SubscribeLocalEvent<LatheComponent, SignalReceivedEvent<ComputerDeviceLinkPayload>>(OnComputerDeviceLinkSignal);
            SubscribeLocalEvent<LatheComponent, SignalReceivedEvent<ComputerPacket>>(OnComputerPacketSignal);
            SubscribeLocalEvent<FileSystemChangedEvent>(OnFileSystemChanged);
        }

        private void OnFileSystemChanged(ref FileSystemChangedEvent args)
        {
            var query = EntityQueryEnumerator<LatheComponent>();
            while (query.MoveNext(out var latheUid, out var latheComp))
            {
                if (latheComp.SelectedRecipeId == null)
                    continue;

                var prevSelected = latheComp.SelectedRecipeId;
                if (!ValidateSelectedRecipe(latheUid, latheComp) && prevSelected != null)
                    UpdateUserInterfaceState(latheUid, latheComp);
            }
        }

        private void OnSignalReceived(Entity<LatheComponent> ent, ref SignalReceivedEvent args)
        {
            if (args.Port == PortAutolatheSetRecipeSink)
            {
                CycleSelectedRecipe(ent);
                return;
            }

            if (args.Port == PortAutolatheCommandSink ||
                args.Port == PortMachineCommandReceiver ||
                args.Port == PortAutolatheBlueprintSink)
            {
                TryPrintSelectedRecipe(ent);
            }
        }

        public void ClearSelectedRecipe(LatheComponent comp)
        {
            comp.SelectedRecipeId = null;
            comp.SelectedRecipeFilePath = null;
            comp.SelectedRecipeFromDisk = false;
            comp.SelectedRecipeDriveUid = null;
        }

        /// <summary>
        /// Verifies the lathe's remembered selected recipe still has physical access to its source
        /// disk or embedded HDD blueprint file. Clears the selection if physical access was removed.
        /// </summary>
        public bool ValidateSelectedRecipe(EntityUid uid, LatheComponent comp)
        {
            if (comp.SelectedRecipeId is not { } selectedId)
                return false;

            if (!ProtoMan.HasIndex(selectedId))
            {
                ClearSelectedRecipe(comp);
                return false;
            }

            if (comp.SelectedRecipeFromDisk)
            {
                if (comp.diskSlot?.Item is not { } diskUid || TerminatingOrDeleted(diskUid))
                {
                    ClearSelectedRecipe(comp);
                    return false;
                }

                if (comp.SelectedRecipeDriveUid is { } expectedDisk && expectedDisk != diskUid)
                {
                    ClearSelectedRecipe(comp);
                    return false;
                }

                if (!TryComp<FileSystemComponent>(diskUid, out var diskFs) ||
                    string.IsNullOrEmpty(comp.SelectedRecipeFilePath) ||
                    !diskFs.Files.TryGetValue(comp.SelectedRecipeFilePath, out var vf) ||
                    vf is not RecipeBlueprintFile bp ||
                    !bp.GetAllRecipes().Contains(selectedId))
                {
                    ClearSelectedRecipe(comp);
                    return false;
                }

                comp.SelectedRecipeDriveUid = diskUid;
                return true;
            }

            if (!TryComp<EmbeddedComputerHostComponent>(uid, out var host) ||
                host.ComputerEntity is not { } compUid ||
                !_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var hostFs))
            {
                ClearSelectedRecipe(comp);
                return false;
            }

            if (comp.SelectedRecipeDriveUid is { } expectedDrive && expectedDrive != driveUid.Value)
            {
                ClearSelectedRecipe(comp);
                return false;
            }

            if (string.IsNullOrEmpty(comp.SelectedRecipeFilePath) ||
                !hostFs.Files.TryGetValue(comp.SelectedRecipeFilePath, out var memVf) ||
                memVf is not RecipeBlueprintFile memBp ||
                memBp.IsEncrypted ||
                !memBp.GetAllRecipes().Contains(selectedId))
            {
                ClearSelectedRecipe(comp);
                return false;
            }

            comp.SelectedRecipeDriveUid = driveUid.Value;
            return true;
        }

        private List<(ProtoId<LatheRecipePrototype> RecipeId, string FilePath, bool FromDisk, EntityUid DriveUid)> CollectAccessibleBlueprints(
            EntityUid uid,
            LatheComponent comp)
        {
            SyncBlueprintsToEmbeddedComputer(uid, comp);
            var list = new List<(ProtoId<LatheRecipePrototype> RecipeId, string FilePath, bool FromDisk, EntityUid DriveUid)>();
            var seen = new HashSet<(bool FromDisk, string RecipeId, string FilePath)>();

            if (comp.diskSlot?.Item is { } diskUid &&
                !TerminatingOrDeleted(diskUid) &&
                TryComp<FileSystemComponent>(diskUid, out var diskFs))
            {
                foreach (var (path, file) in diskFs.Files)
                {
                    if (file is not RecipeBlueprintFile bp)
                        continue;

                    foreach (var recId in bp.GetAllRecipes())
                    {
                        if (ProtoMan.HasIndex(recId) && seen.Add((true, recId.Id, path)))
                            list.Add((recId, path, true, diskUid));
                    }
                }
            }

            if (TryComp<EmbeddedComputerHostComponent>(uid, out var host) &&
                host.ComputerEntity is { } compUid &&
                _fileSystem.TryFindFileSystem(compUid, out var driveUid, out var hostFs))
            {
                foreach (var (path, file) in hostFs.Files)
                {
                    if (file is not RecipeBlueprintFile bp || bp.IsEncrypted)
                        continue;

                    foreach (var recId in bp.GetAllRecipes())
                    {
                        if (ProtoMan.HasIndex(recId) && seen.Add((false, recId.Id, path)))
                            list.Add((recId, path, false, driveUid.Value));
                    }
                }
            }

            list.Sort((a, b) =>
            {
                var diskCmp = b.FromDisk.CompareTo(a.FromDisk);
                if (diskCmp != 0)
                    return diskCmp;
                var recCmp = string.Compare(a.RecipeId.Id, b.RecipeId.Id, StringComparison.OrdinalIgnoreCase);
                return recCmp != 0 ? recCmp : string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
            });

            return list;
        }

        private void CycleSelectedRecipe(Entity<LatheComponent> ent)
        {
            ValidateSelectedRecipe(ent.Owner, ent.Comp);
            var entries = CollectAccessibleBlueprints(ent.Owner, ent.Comp);
            if (entries.Count == 0)
            {
                ClearSelectedRecipe(ent.Comp);
                UpdateUserInterfaceState(ent.Owner, ent.Comp);
                return;
            }

            var nextIdx = 0;
            if (ent.Comp.SelectedRecipeId is { } currentId)
            {
                var curIdx = entries.FindIndex(e =>
                    e.RecipeId == currentId &&
                    e.FromDisk == ent.Comp.SelectedRecipeFromDisk &&
                    string.Equals(e.FilePath, ent.Comp.SelectedRecipeFilePath, StringComparison.Ordinal));
                if (curIdx >= 0)
                    nextIdx = (curIdx + 1) % entries.Count;
            }

            var chosen = entries[nextIdx];
            ent.Comp.SelectedRecipeId = chosen.RecipeId;
            ent.Comp.SelectedRecipeFilePath = chosen.FilePath;
            ent.Comp.SelectedRecipeFromDisk = chosen.FromDisk;
            ent.Comp.SelectedRecipeDriveUid = chosen.DriveUid;
            UpdateUserInterfaceState(ent.Owner, ent.Comp);
        }

        private void TryPrintSelectedRecipe(Entity<LatheComponent> ent)
        {
            SyncBlueprintsToEmbeddedComputer(ent.Owner, ent.Comp);
            if (!ValidateSelectedRecipe(ent.Owner, ent.Comp) ||
                ent.Comp.SelectedRecipeId is not { } selectedId ||
                ent.Comp.SelectedRecipeDriveUid is not { } driveUid ||
                string.IsNullOrEmpty(ent.Comp.SelectedRecipeFilePath))
            {
                UpdateUserInterfaceState(ent.Owner, ent.Comp);
                return;
            }

            var pkt = new ComputerPacket("lathe_print");
            pkt.Set("Recipe", selectedId.Id);
            pkt.Set("Amount", 1);
            pkt.SetReference("DriveUid", driveUid);
            pkt.Set("FilePath", ent.Comp.SelectedRecipeFilePath);
            HandleLathePrintPacket(ent, pkt);
            UpdateUserInterfaceState(ent.Owner, ent.Comp);
        }

        private string? HandleLatheSetRecipePacket(Entity<LatheComponent> ent, ComputerPacket packet)
        {
            SyncBlueprintsToEmbeddedComputer(ent.Owner, ent.Comp);

            var recipeName = (packet.GetString("Recipe", string.Empty) ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(recipeName))
            {
                ClearSelectedRecipe(ent.Comp);
                return null;
            }

            if (!ProtoMan.TryIndex<LatheRecipePrototype>(recipeName, out var targetRecipe))
                return $"Unknown recipe '{recipeName}'.";

            var insertedDisk = ent.Comp.diskSlot?.Item is { } dUid && !TerminatingOrDeleted(dUid) ? dUid : (EntityUid?) null;

            // 1. Explicit DriveUid + FilePath reference (from UI or linked computer)
            if (packet.ProtectedReferences.TryGetValue("DriveUid", out var driveUid) &&
                packet.Data.TryGetValue("FilePath", out var fpObj) &&
                fpObj is string filePath &&
                !string.IsNullOrEmpty(filePath) &&
                TryComp<FileSystemComponent>(driveUid, out var fs) &&
                fs.Files.TryGetValue(filePath, out var vf) &&
                vf is RecipeBlueprintFile bpFile &&
                bpFile.GetAllRecipes().Contains(targetRecipe.ID))
            {
                var isFromDisk = insertedDisk != null && driveUid == insertedDisk.Value;
                if (bpFile.IsEncrypted && !isFromDisk)
                    return $"Encrypted recipe file '{filePath}' can only be selected directly from an inserted disk.";

                // Verify the drive is physically attached (inserted disk or embedded HDD)
                var isEmbeddedHdd = TryComp<EmbeddedComputerHostComponent>(ent.Owner, out var host) &&
                    host.ComputerEntity is { } compUid &&
                    _fileSystem.TryFindFileSystem(compUid, out var hddUid, out _) &&
                    hddUid == driveUid;

                if (!isFromDisk && !isEmbeddedHdd)
                    return $"Drive for '{filePath}' is not physically attached to this lathe.";

                ent.Comp.SelectedRecipeId = targetRecipe.ID;
                ent.Comp.SelectedRecipeFilePath = filePath;
                ent.Comp.SelectedRecipeFromDisk = isFromDisk;
                ent.Comp.SelectedRecipeDriveUid = driveUid;
                return null;
            }

            // 2. Fallback: look up recipe across inserted disk and embedded HDD memory
            var wantDisk = packet.GetString("FromDisk", string.Empty)?.Equals("True", StringComparison.OrdinalIgnoreCase) == true;
            var entries = CollectAccessibleBlueprints(ent.Owner, ent.Comp);
            var match = entries.FirstOrDefault(e => e.RecipeId == targetRecipe.ID && (!wantDisk || e.FromDisk));
            if (match.RecipeId == default)
                match = entries.FirstOrDefault(e => e.RecipeId == targetRecipe.ID);

            if (match.RecipeId == default)
                return $"No accessible blueprint for '{GetRecipeName(targetRecipe)}'.";

            ent.Comp.SelectedRecipeId = match.RecipeId;
            ent.Comp.SelectedRecipeFilePath = match.FilePath;
            ent.Comp.SelectedRecipeFromDisk = match.FromDisk;
            ent.Comp.SelectedRecipeDriveUid = match.DriveUid;
            return null;
        }

        private void OnComputerPacketSignal(Entity<LatheComponent> ent, ref SignalReceivedEvent<ComputerPacket> args)
        {
            var port = args.Port;
            var packet = args.Data;

            if (port == PortAutolatheSetRecipeSink)
            {
                var setErr = HandleLatheSetRecipePacket(ent, packet);
                UpdateUserInterfaceState(ent.Owner, ent.Comp, setErr);
                return;
            }
            else if (port == "AutolatheBlueprintSink")
            {
                if (packet.ProtectedReferences.TryGetValue("DiskUid", out var diskUid) &&
                    packet.Data.TryGetValue("FileName", out var fileNameObj) &&
                    fileNameObj is string fileName)
                {
                    if (TryComp<FileSystemComponent>(diskUid, out var fs) &&
                        fs.Files.TryGetValue(fileName, out var vfile) &&
                        vfile is RecipeBlueprintFile rFile)
                    {
                        foreach (var recipeId in rFile.GetAllRecipes())
                        {
                            if (ProtoMan.TryIndex<LatheRecipePrototype>(recipeId, out _))
                            {
                                // Validated recipe blueprint reference
                            }
                        }
                    }
                }

                string? statusFeedback = null;
                var cmd = packet.PacketType.ToLowerInvariant();
                if (cmd is "print" or "lathe_print" or "blueprint_print")
                {
                    statusFeedback = HandleLathePrintPacket(ent, packet);
                    UpdateUserInterfaceState(ent.Owner, ent.Comp, statusFeedback);
                    return;
                }
                else if (cmd is "sync_disk_to_host" or "import_disk")
                {
                    var specificPath = packet.GetString("FilePath", string.Empty);
                    statusFeedback = ImportInsertedDiskBlueprintsToComputer(
                        ent.Owner,
                        ent.Comp,
                        string.IsNullOrWhiteSpace(specificPath) ? null : specificPath);
                    UpdateUserInterfaceState(ent.Owner, ent.Comp, statusFeedback);
                    return;
                }

                EmitLatheStatus(ent, statusFeedback);
            }
            else if (port == "AutolatheCommandSink" || port == "MachineCommandReceiver")
            {
                var cmd = packet.PacketType.ToLowerInvariant();
                if (cmd is "status" or "status_request")
                {
                    SyncBlueprintsToEmbeddedComputer(ent.Owner, ent.Comp);
                    EmitLatheStatus(ent);
                    return;
                }

                string? statusFeedback = null;
                if (cmd is "print" or "lathe_print")
                {
                    statusFeedback = HandleLathePrintPacket(ent, packet);
                }
                else if (cmd is "set_recipe" or "lathe_set_recipe")
                {
                    statusFeedback = HandleLatheSetRecipePacket(ent, packet);
                }
                else if (cmd is "cancel" or "lathe_cancel")
                {
                    while (ent.Comp.Queue.First is { } node)
                    {
                        RefundBatch(ent.Owner, ent.Comp, node.Value);
                        ent.Comp.Queue.RemoveFirst();
                    }
                    if (ent.Comp.CurrentRecipe != null)
                    {
                        RefundCurrentRecipe(ent.Owner, ent.Comp);
                        ent.Comp.CurrentRecipe = null;
                        FinishProducing(ent.Owner, ent.Comp);
                    }
                }
                else if (cmd is "eject_material" or "eject")
                {
                    var matId = packet.GetString("Material", string.Empty);
                    var sheetsRequested = Math.Max(1, packet.GetInt("Count", 1));
                    if (!string.IsNullOrEmpty(matId) &&
                        TryComp<MaterialStorageComponent>(ent.Owner, out var matStorage) &&
                        matStorage.CanEjectStoredMaterials &&
                        ProtoMan.TryIndex<MaterialPrototype>(matId, out var matProto) &&
                        matProto.StackEntity is { } stackEntId &&
                        ProtoMan.Index(stackEntId).TryComp<PhysicalCompositionComponent>(out var composition, EntityManager.ComponentFactory) &&
                        composition.MaterialComposition.TryGetValue(matId, out var volumePerSheet) &&
                        volumePerSheet > 0)
                    {
                        var maxStack = _stack.GetMaxCount(stackEntId);
                        var sheetsToExtract = Math.Min(sheetsRequested, maxStack);
                        var volume = sheetsToExtract * volumePerSheet;
                        var spawned = _materialStorage.EjectMaterial(ent.Owner, matId, volume, Transform(ent.Owner).Coordinates, matStorage);
                        foreach (var mat in spawned)
                        {
                            if (!TerminatingOrDeleted(mat))
                                _stack.TryMergeToContacts(mat);
                        }
                    }
                }
                else if (cmd is "eject_disk")
                {
                    if (ent.Comp.diskSlot is { Item: not null } slot)
                    {
                        if (packet.ProtectedReferences.TryGetValue("Actor", out var actorUid) &&
                            Exists(actorUid) &&
                            _interaction.InRangeUnobstructed(actorUid, ent.Owner))
                        {
                            itemslots.TryEjectToHands(ent.Owner, slot, actorUid);
                        }
                        else
                        {
                            itemslots.TryEject(ent.Owner, slot, null, out _);
                        }
                    }
                }
                UpdateUserInterfaceState(ent.Owner, ent.Comp, statusFeedback);
            }
        }

        private static string FormatPackFileName(string packId)
        {
            var raw = packId.EndsWith("Static", StringComparison.OrdinalIgnoreCase)
                ? packId[..^6]
                : packId;
            if (string.IsNullOrWhiteSpace(raw))
                raw = packId;

            var sb = new System.Text.StringBuilder(raw.Length + 4);
            for (var i = 0; i < raw.Length; i++)
            {
                var ch = raw[i];
                if (char.IsUpper(ch) && i > 0 && !char.IsUpper(raw[i - 1]) && raw[i - 1] != '_')
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Materializes the lathe's default/unlocked blueprints into real .lathe files inside its
        /// embedded computer's VFS memory (/blueprints/*.lathe) so all available blueprints live in memory.
        /// </summary>
        public void SyncBlueprintsToEmbeddedComputer(EntityUid uid, LatheComponent comp)
        {
            if (!TryComp<EmbeddedComputerHostComponent>(uid, out var host) ||
                host.ComputerEntity is not { } compUid ||
                !_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
            {
                return;
            }

            if (!comp.EmbeddedBlueprintsSeeded)
            {
                foreach (var packId in comp.StaticPacks)
                {
                    if (!ProtoMan.TryIndex(packId, out var pack) || pack.Recipes.Count == 0)
                        continue;

                    var fileName = FormatPackFileName(packId.Id);
                    var fullPath = $"/blueprints/{fileName}.lathe";
                    if (fs.Files.ContainsKey(fullPath))
                        continue;

                    var bpFile = new RecipeBlueprintFile
                    {
                        Name = fileName,
                        Extension = "lathe",
                        Directory = "/blueprints/",
                        Recipes = new List<ProtoId<LatheRecipePrototype>>(pack.Recipes),
                        DrmLocked = false,
                        RemainingUses = null,
                        IsEncrypted = false,
                        IsReadOnly = false
                    };
                    bpFile.Size = bpFile.CalculateSize(ProtoMan);
                    _fileSystem.TryWriteFile(driveUid.Value, bpFile, fs);
                }

                comp.EmbeddedBlueprintsSeeded = true;
            }

            // If emagged, materialize emag static packs into computer memory too.
            if (_emag.CheckFlag(uid, EmagType.Interaction) &&
                TryComp<EmagLatheRecipesComponent>(uid, out var emagComp) &&
                emagComp.EmagStaticPacks.Count > 0)
            {
                var emagRecipes = new HashSet<ProtoId<LatheRecipePrototype>>();
                AddRecipesFromPacks(emagRecipes, emagComp.EmagStaticPacks);
                if (emagRecipes.Count > 0 && !fs.Files.ContainsKey("/blueprints/emag_override.lathe"))
                {
                    var emagFile = new RecipeBlueprintFile
                    {
                        Name = "emag_override",
                        Extension = "lathe",
                        Directory = "/blueprints/",
                        Recipes = emagRecipes.ToList(),
                        DrmLocked = false,
                        RemainingUses = null,
                        IsEncrypted = false,
                        IsReadOnly = false
                    };
                    emagFile.Size = emagFile.CalculateSize(ProtoMan);
                    _fileSystem.TryWriteFile(driveUid.Value, emagFile, fs);
                }
            }
        }

        private static string FormatStorageKb(int bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            return $"{bytes / 1024f:0.#} KB";
        }

        private static bool BlueprintRequiresUses(RecipeBlueprintFile bp, LatheRecipePrototype recipe)
        {
            return bp.IsEncrypted || bp.DrmLocked || bp.RemainingUses != null || recipe.pointUsage > 0;
        }

        private static int GetRecipeUseCost(LatheRecipePrototype recipe, int quantity)
        {
            var pointCost = recipe.pointUsage > 0 ? recipe.pointUsage : 1;
            return pointCost * quantity;
        }

        private static bool TryValidateBlueprintUses(
            RecipeBlueprintFile bp,
            LatheRecipePrototype recipe,
            int quantity,
            out string error)
        {
            error = string.Empty;
            if (!BlueprintRequiresUses(bp, recipe))
                return true;

            var required = GetRecipeUseCost(recipe, quantity);
            if (bp.RemainingUses == null || bp.RemainingUses.Value < required)
            {
                error = $"Insufficient uses on '{bp.Name}.{bp.Extension}' (needs {required} LP, {bp.RemainingUses ?? 0} remaining).";
                return false;
            }

            return true;
        }

        private void ConsumeBlueprintUses(
            EntityUid driveUid,
            FileSystemComponent fs,
            RecipeBlueprintFile bp,
            LatheRecipePrototype recipe,
            int quantity)
        {
            if (!BlueprintRequiresUses(bp, recipe) || bp.RemainingUses == null)
                return;

            var usage = GetRecipeUseCost(recipe, quantity);
            bp.RemainingUses = Math.Max(0, bp.RemainingUses.Value - usage);
            _fileSystem.NotifyFileModified(driveUid, bp.FullPath, fs);
        }

        private string ImportInsertedDiskBlueprintsToComputer(
            EntityUid uid,
            LatheComponent comp,
            string? specificFilePath = null)
        {
            if (comp.diskSlot?.Item is not { } diskItem ||
                TerminatingOrDeleted(diskItem) ||
                !TryComp<FileSystemComponent>(diskItem, out var diskFs))
            {
                return "No blueprint disk inserted.";
            }

            if (!TryComp<EmbeddedComputerHostComponent>(uid, out var host) ||
                host.ComputerEntity is not { } compUid ||
                !_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var hostFs))
            {
                return "No embedded computer hard drive available.";
            }

            if (!string.IsNullOrWhiteSpace(specificFilePath))
            {
                if (!diskFs.Files.TryGetValue(specificFilePath, out var rawFile) || rawFile is not RecipeBlueprintFile bp)
                    return $"Blueprint file '{specificFilePath}' not found on inserted disk.";

                if (_fileSystem.IsCopyProtectedBlueprint(bp))
                    return $"Cannot save '{bp.Name}.{bp.Extension}': encrypted recipe files are copy-protected and must be printed directly from disk.";

                var clone = (RecipeBlueprintFile) bp.Clone();
                clone.Directory = "/blueprints/";
                clone.IsReadOnly = false;
                clone.Size = clone.CalculateSize(ProtoMan);

                hostFs.Files.TryGetValue(clone.FullPath, out var existing);
                var delta = clone.Size - (existing?.Size ?? 0);
                var freeBytes = Math.Max(0, hostFs.MaxStorageBytes - hostFs.UsedStorageBytes);
                if (delta > freeBytes)
                    return $"Insufficient lathe memory for '{clone.Name}.{clone.Extension}' (needs {FormatStorageKb(clone.Size)}, {FormatStorageKb(freeBytes)} free). Print directly from disk or free memory.";

                return _fileSystem.TryWriteFile(driveUid.Value, clone, hostFs)
                    ? $"Saved '{clone.Name}.{clone.Extension}' ({FormatStorageKb(clone.Size)}) to lathe memory."
                    : $"Failed to save '{clone.Name}.{clone.Extension}' to lathe memory.";
            }

            var copied = 0;
            var skippedEncrypted = 0;
            var skippedNoSpace = 0;
            var totalBpFiles = 0;

            foreach (var file in diskFs.Files.Values)
            {
                if (file is not RecipeBlueprintFile bp)
                    continue;

                totalBpFiles++;
                if (_fileSystem.IsCopyProtectedBlueprint(bp))
                {
                    skippedEncrypted++;
                    continue;
                }

                var clone = (RecipeBlueprintFile) bp.Clone();
                clone.Directory = "/blueprints/";
                clone.IsReadOnly = false;
                clone.Size = clone.CalculateSize(ProtoMan);
                if (_fileSystem.TryWriteFile(driveUid.Value, clone, hostFs))
                    copied++;
                else
                    skippedNoSpace++;
            }

            if (totalBpFiles == 0)
                return "No .lathe blueprint files found on disk.";

            if (copied > 0)
            {
                var notes = new List<string>();
                if (skippedEncrypted > 0)
                    notes.Add($"{skippedEncrypted} encrypted skipped");
                if (skippedNoSpace > 0)
                    notes.Add($"{skippedNoSpace} skipped: insufficient memory");
                var suffix = notes.Count > 0 ? $" ({string.Join(", ", notes)})" : string.Empty;
                return $"Saved {copied} blueprint file(s) from disk into lathe memory{suffix}.";
            }

            if (skippedNoSpace > 0)
            {
                var freeBytes = Math.Max(0, hostFs.MaxStorageBytes - hostFs.UsedStorageBytes);
                return $"Insufficient lathe memory to save disk blueprints ({FormatStorageKb(freeBytes)} free). Print directly from disk or delete stored packs.";
            }

            if (skippedEncrypted > 0)
                return "Encrypted recipe files are copy-protected and cannot be saved to lathe memory. Print directly from disk.";

            return "No blueprint files could be saved from disk.";
        }

        private string? HandleLathePrintPacket(Entity<LatheComponent> ent, ComputerPacket packet)
        {
            SyncBlueprintsToEmbeddedComputer(ent.Owner, ent.Comp);

            var recipeName = packet.GetString("Recipe", string.Empty);
            var amount = Math.Max(1, packet.GetInt("Amount", 1));

            if (string.IsNullOrEmpty(recipeName) || !ProtoMan.TryIndex<LatheRecipePrototype>(recipeName, out var targetRecipe))
                return $"Unknown recipe '{recipeName}'.";

            var insertedDisk = ent.Comp.diskSlot?.Item is { } dUid && !TerminatingOrDeleted(dUid) ? dUid : (EntityUid?) null;

            // 1. Explicit disk or HDD .lathe blueprint reference
            if (packet.ProtectedReferences.TryGetValue("DriveUid", out var driveUid) &&
                packet.Data.TryGetValue("FilePath", out var fpObj) &&
                fpObj is string filePath &&
                TryComp<FileSystemComponent>(driveUid, out var fs) &&
                fs.Files.TryGetValue(filePath, out var vf) &&
                vf is RecipeBlueprintFile bpFile)
            {
                var isFromInsertedDisk = insertedDisk != null && driveUid == insertedDisk.Value;
                if (bpFile.IsEncrypted && !isFromInsertedDisk)
                    return $"Encrypted recipe file '{filePath}' can only be printed directly from a recipe disk.";

                if (!bpFile.GetAllRecipes().Contains(targetRecipe.ID))
                    return $"Recipe '{GetRecipeName(targetRecipe)}' not found in '{filePath}'.";

                if (!TryValidateBlueprintUses(bpFile, targetRecipe, amount, out var useErr))
                    return useErr;

                if (!TryAddToQueue(ent.Owner, targetRecipe, amount, ent.Comp, fromMemoryBlueprint: true))
                    return $"Cannot fabricate '{GetRecipeName(targetRecipe)}': insufficient materials or unavailable.";

                ConsumeBlueprintUses(driveUid, fs, bpFile, targetRecipe, amount);
                if (ent.Comp.SelectedRecipeId == null)
                {
                    ent.Comp.SelectedRecipeId = targetRecipe.ID;
                    ent.Comp.SelectedRecipeFilePath = filePath;
                    ent.Comp.SelectedRecipeFromDisk = isFromInsertedDisk;
                    ent.Comp.SelectedRecipeDriveUid = driveUid;
                }
                TryStartProducing(ent.Owner, ent.Comp);
                return null;
            }

            // 2. Fallback: inserted disk first (supports encrypted & unencrypted disks directly)
            if (insertedDisk != null && TryComp<FileSystemComponent>(insertedDisk.Value, out var diskFs))
            {
                RecipeBlueprintFile? diskBp = null;
                foreach (var f in diskFs.Files.Values)
                {
                    if (f is RecipeBlueprintFile bp &&
                        bp.GetAllRecipes().Contains(targetRecipe.ID))
                    {
                        diskBp = bp;
                        break;
                    }
                }

                if (diskBp != null)
                {
                    if (!TryValidateBlueprintUses(diskBp, targetRecipe, amount, out var diskUseErr))
                        return diskUseErr;

                    if (!TryAddToQueue(ent.Owner, targetRecipe, amount, ent.Comp, fromMemoryBlueprint: true))
                        return $"Cannot fabricate '{GetRecipeName(targetRecipe)}': insufficient materials.";

                    ConsumeBlueprintUses(insertedDisk.Value, diskFs, diskBp, targetRecipe, amount);
                    TryStartProducing(ent.Owner, ent.Comp);
                    return null;
                }
            }

            // 3. Fallback: look up matching unencrypted blueprint in embedded computer memory
            if (TryComp<EmbeddedComputerHostComponent>(ent.Owner, out var host) &&
                host.ComputerEntity is { } compUid &&
                _fileSystem.TryFindFileSystem(compUid, out var memDrive, out var memFs))
            {
                RecipeBlueprintFile? memBp = null;
                foreach (var f in memFs.Files.Values)
                {
                    if (f is RecipeBlueprintFile bp &&
                        !bp.IsEncrypted &&
                        bp.GetAllRecipes().Contains(targetRecipe.ID) &&
                        TryValidateBlueprintUses(bp, targetRecipe, amount, out _))
                    {
                        memBp = bp;
                        break;
                    }
                }

                if (memBp == null)
                    return $"No usable blueprint for '{GetRecipeName(targetRecipe)}' in memory or inserted disk.";

                if (!TryAddToQueue(ent.Owner, targetRecipe, amount, ent.Comp, fromMemoryBlueprint: true))
                    return $"Cannot fabricate '{GetRecipeName(targetRecipe)}': insufficient materials.";

                ConsumeBlueprintUses(memDrive.Value, memFs, memBp, targetRecipe, amount);
                TryStartProducing(ent.Owner, ent.Comp);
                return null;
            }

            // 4. Non-embedded fallback
            if (TryAddToQueue(ent.Owner, targetRecipe, amount, ent.Comp))
            {
                TryStartProducing(ent.Owner, ent.Comp);
                return null;
            }

            return $"Cannot fabricate '{GetRecipeName(targetRecipe)}': insufficient materials or missing blueprint.";
        }

        public void EmitLatheStatus(Entity<LatheComponent> ent, string? statusMessage = null)
        {
            SyncBlueprintsToEmbeddedComputer(ent.Owner, ent.Comp);
            ValidateSelectedRecipe(ent.Owner, ent.Comp);

            var isPrinting = ent.Comp.CurrentRecipe != null;
            var status = isPrinting ? "Printing" : "Standby";

            var pkt = new ComputerPacket("AutolatheStatus");
            pkt.Set("Status", status);
            pkt.Set("Recipe", ent.Comp.CurrentRecipe?.Id ?? string.Empty);
            pkt.Set("SelectedRecipeId", ent.Comp.SelectedRecipeId?.Id ?? string.Empty);
            pkt.Set("SelectedRecipeFilePath", ent.Comp.SelectedRecipeFilePath ?? string.Empty);
            pkt.Set("SelectedRecipeFromDisk", ent.Comp.SelectedRecipeFromDisk.ToString());
            pkt.Set("QueueCount", ent.Comp.Queue.Count);
            pkt.Set("MaterialMultiplier", ent.Comp.MaterialUseMultiplier.ToString(CultureInfo.InvariantCulture));
            if (statusMessage != null)
                pkt.Set("StatusMessage", statusMessage);

            if (isPrinting && TryComp<LatheProducingComponent>(ent.Owner, out var prodComp))
            {
                pkt.Set("ProdStartTicks", prodComp.StartTime.Ticks.ToString(CultureInfo.InvariantCulture));
                pkt.Set("ProdLengthTicks", prodComp.ProductionLength.Ticks.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                pkt.Set("ProdStartTicks", "0");
                pkt.Set("ProdLengthTicks", "0");
            }

            var queueNames = new List<string>();
            var queueBatches = new List<string>();
            foreach (var batch in ent.Comp.Queue)
            {
                var rem = Math.Max(0, batch.ItemsRequested - batch.ItemsPrinted);
                if (rem <= 0)
                    continue;
                queueNames.Add(batch.Recipe.Id);
                queueBatches.Add($"{batch.Recipe.Id}={rem}");
            }
            pkt.Set("QueueRaw", string.Join(",", queueNames));
            pkt.Set("QueueBatchesRaw", string.Join(",", queueBatches));

            var hostRecipes = GetAvailableRecipes(ent.Owner, ent.Comp, getUnavailable: false);
            pkt.Set("HostRecipesRaw", string.Join(",", hostRecipes.Select(r => r.Id)));

            if (TryComp<MaterialStorageComponent>(ent.Owner, out var matComp))
            {
                var mats = new List<string>();
                foreach (var (matId, amount) in matComp.Storage)
                    mats.Add($"{matId}={amount}");
                pkt.Set("MaterialsRaw", string.Join(";", mats));
            }

            if (ent.Comp.diskSlot?.Item is { } diskItem && Exists(diskItem))
            {
                pkt.SetReference("DiskUid", diskItem);
                pkt.Set("DiskName", MetaData(diskItem).EntityName);
            }

            if (TryComp<DeviceLinkSourceComponent>(ent.Owner, out var srcComp) &&
                _deviceLink.GetLinkedSinks((ent.Owner, srcComp), PortAutolatheStatusSource).Any())
            {
                _deviceLink.InvokePort((ent.Owner, srcComp), PortAutolatheStatusSource, ref pkt);
            }
            else
            {
                _deviceLink.InvokePort(ent.Owner, PortMachineStatusSender, ref pkt);
            }
        }
        
        private void OnComputerDeviceLinkSignal(Entity<LatheComponent> ent, ref SignalReceivedEvent<ComputerDeviceLinkPayload> args)
        {
            var cmd = args.Data.Command?.Trim().ToLowerInvariant() ?? string.Empty;
            if (args.Port == PortAutolatheSetRecipeSink || cmd is "set_recipe" or "lathe_set_recipe")
            {
                var setPkt = new ComputerPacket("set_recipe");
                setPkt.Set("Recipe", (args.Data.Argument ?? string.Empty).Trim());
                HandleLatheSetRecipePacket(ent, setPkt);
                UpdateUserInterfaceState(ent.Owner, ent.Comp);
                return;
            }

            if (cmd is "status" or "status_request")
            {
                EmitLatheStatus(ent);
                return;
            }

            if (cmd is "lathe_print" or "print")
            {
                var parts = (args.Data.Argument ?? string.Empty).Split(new[] { ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && ProtoMan.TryIndex<LatheRecipePrototype>(parts[0], out var recipe))
                {
                    var amount = parts.Length > 1 && int.TryParse(parts[1], out var parsed) ? Math.Max(1, parsed) : 1;
                    if (TryAddToQueue(ent.Owner, recipe, amount, ent.Comp))
                        TryStartProducing(ent.Owner, ent.Comp);
                }
                UpdateUserInterfaceState(ent.Owner, ent.Comp);
            }
            else if (cmd is "lathe_cancel" or "cancel")
            {
                while (ent.Comp.Queue.First is { } node)
                {
                    RefundBatch(ent.Owner, ent.Comp, node.Value);
                    ent.Comp.Queue.RemoveFirst();
                }
                if (ent.Comp.CurrentRecipe != null)
                {
                    RefundCurrentRecipe(ent.Owner, ent.Comp);
                    ent.Comp.CurrentRecipe = null;
                    FinishProducing(ent.Owner, ent.Comp);
                }
                UpdateUserInterfaceState(ent.Owner, ent.Comp);
            }
            else if (cmd is "lathe_sync_disk" or "lathe_status" or "sync_disk" or "status")
            {
                UpdateUserInterfaceState(ent.Owner, ent.Comp);
            }
        }

        [SubscribeLocalEvent]
        public void OnInsert(EntityUid ent, LatheComponent comp, EntInsertedIntoContainerMessage ev)
        {
            if (comp.diskSlot is ItemSlot real)
            {
                if (ev.Container is ContainerSlot verified)
                {
                    if (verified.ID != real.ID)
                        return;
                    if (!HasComp<DigitalDataHolderComponent>(ev.Entity) && !HasComp<FileSystemComponent>(ev.Entity))
                        return;
                    UpdateUserInterfaceState(ent, comp);
                }
            }
        }
        
        [SubscribeLocalEvent]
        public void OnRemove(EntityUid ent, LatheComponent comp, EntRemovedFromContainerMessage ev)
        {
            if (comp.diskSlot is ItemSlot real)
            {
                if (ev.Container is ContainerSlot verified)
                {
                    if (verified.ID != real.ID)
                        return;
                    if (!HasComp<DigitalDataHolderComponent>(ev.Entity) && !HasComp<FileSystemComponent>(ev.Entity))
                        return;
                    if (comp.SelectedRecipeFromDisk &&
                        (comp.SelectedRecipeDriveUid == null || comp.SelectedRecipeDriveUid == ev.Entity))
                    {
                        ClearSelectedRecipe(comp);
                    }
                    UpdateUserInterfaceState(ent, comp);
                }
            }
        }
        public override void Update(float frameTime)
        {
            var query = EntityQueryEnumerator<LatheProducingComponent, LatheComponent>();
            while (query.MoveNext(out var uid, out var comp, out var lathe))
            {
                if (lathe.CurrentRecipe == null)
                    continue;

                if (_timing.CurTime - comp.StartTime >= comp.ProductionLength)
                    FinishProducing(uid, lathe);
            }

            var heatQuery = EntityQueryEnumerator<LatheHeatProducingComponent, LatheProducingComponent, TransformComponent>();
            while (heatQuery.MoveNext(out var uid, out var heatComp, out _, out var xform))
            {
                if (_timing.CurTime < heatComp.NextSecond)
                    continue;
                heatComp.NextSecond += TimeSpan.FromSeconds(1);

                var position = _transform.GetGridTilePositionOrDefault((uid, xform));
                _environments.Clear();

                if (_atmosphere.GetTileMixture(xform.GridUid, xform.MapUid, position, true) is { } tileMix)
                    _environments.Add(tileMix);

                if (xform.GridUid != null)
                {
                    var enumerator = _atmosphere.GetAdjacentTileMixtures(xform.GridUid.Value, position, false, true);
                    while (enumerator.MoveNext(out var mix))
                    {
                        _environments.Add(mix);
                    }
                }

                if (_environments.Count > 0)
                {
                    var heatPerTile = heatComp.EnergyPerSecond / _environments.Count;
                    foreach (var env in _environments)
                    {
                        _atmosphere.AddHeat(env, heatPerTile);
                    }
                }
            }
        }

        private void OnGetWhitelist(EntityUid uid, LatheComponent component, ref GetMaterialWhitelistEvent args)
        {
            if (args.Storage != uid)
                return;
            var materialWhitelist = new List<ProtoId<MaterialPrototype>>();
            var recipes = GetAvailableRecipes(uid, component, true);
            foreach (var id in recipes)
            {
                if (!ProtoMan.Resolve(id, out var proto))
                    continue;
                foreach (var (mat, _) in proto.Materials)
                {
                    if (!materialWhitelist.Contains(mat))
                    {
                        materialWhitelist.Add(mat);
                    }
                }
            }

            var combined = args.Whitelist.Union(materialWhitelist).ToList();
            args.Whitelist = combined;
        }

        [PublicAPI]
        public bool TryGetAvailableRecipes(EntityUid uid, [NotNullWhen(true)] out List<ProtoId<LatheRecipePrototype>>? recipes, [NotNullWhen(true)] LatheComponent? component = null, bool getUnavailable = false)
        {
            recipes = null;
            if (!Resolve(uid, ref component))
                return false;
            recipes = GetAvailableRecipes(uid, component, getUnavailable);
            return true;
        }

        public HashSet<ProtoId<LatheRecipePrototype>> GetDiskRecipes(DigitalDataHolderComponent data)
        {
            HashSet<ProtoId<LatheRecipePrototype>> returning = new();
            foreach (var file in data.files)
            {
                if (file is DigitalDataLathe recipe)
                {
                    foreach (var r in recipe.recipes)
                    {
                        if (ProtoMan.HasIndex<LatheRecipePrototype>(r))
                            returning.Add(r);
                    }
                }
            }

            return returning;
        }

        public HashSet<ProtoId<LatheRecipePrototype>> GetDiskRecipes(FileSystemComponent fsComp, string? selectedFilePath = null)
        {
            HashSet<ProtoId<LatheRecipePrototype>> returning = new();

            if (!string.IsNullOrEmpty(selectedFilePath) &&
                fsComp.Files.TryGetValue(selectedFilePath, out var selFile) &&
                selFile is RecipeBlueprintFile chosenBp)
            {
                if (!((chosenBp.IsEncrypted || chosenBp.DrmLocked || chosenBp.RemainingUses != null) && chosenBp.RemainingUses is <= 0))
                {
                    foreach (var r in chosenBp.GetAllRecipes())
                    {
                        if (ProtoMan.HasIndex<LatheRecipePrototype>(r))
                            returning.Add(r);
                    }
                }
                return returning;
            }

            foreach (var file in fsComp.Files.Values)
            {
                if (file is RecipeBlueprintFile recipe)
                {
                    if ((recipe.IsEncrypted || recipe.DrmLocked || recipe.RemainingUses != null) && recipe.RemainingUses is <= 0)
                        continue;

                    foreach (var r in recipe.GetAllRecipes())
                    {
                        if (ProtoMan.HasIndex<LatheRecipePrototype>(r))
                            returning.Add(r);
                    }
                }
            }

            return returning;
        }

        public List<ProtoId<LatheRecipePrototype>> GetAvailableRecipes(EntityUid uid, LatheComponent component, bool getUnavailable = false)
        {
            var ev = new LatheGetRecipesEvent((uid, component), getUnavailable);

            // Embedded computer lathes (like Autolathe) keep all available blueprints in their VFS memory (/blueprints/*.lathe).
            var hasEmbeddedMemory = false;
            if (TryComp<EmbeddedComputerHostComponent>(uid, out var host) &&
                host.ComputerEntity is { } compUid &&
                _fileSystem.TryFindFileSystem(compUid, out _, out var memFs))
            {
                if (!component.EmbeddedBlueprintsSeeded)
                    SyncBlueprintsToEmbeddedComputer(uid, component);

                if (component.EmbeddedBlueprintsSeeded)
                {
                    hasEmbeddedMemory = true;
                    foreach (var file in memFs.Files.Values)
                    {
                        if (file is not RecipeBlueprintFile bp || bp.IsEncrypted)
                            continue;
                        if (bp.DrmLocked && bp.RemainingUses is <= 0)
                            continue;

                        foreach (var r in bp.GetAllRecipes())
                            ev.Recipes.Add(r);
                    }
                }
            }

            if (!hasEmbeddedMemory)
                AddRecipesFromPacks(ev.Recipes, component.StaticPacks);

            if (component.diskSlot?.Item is EntityUid loaded && !TerminatingOrDeleted(loaded))
            {
                if (TryComp<FileSystemComponent>(loaded, out var fsComp))
                    ev.Recipes = ev.Recipes.Concat(GetDiskRecipes(fsComp, component.SelectedBlueprintFile)).ToHashSet();
                else if (TryComp<DigitalDataHolderComponent>(loaded, out var dataComp))
                    ev.Recipes = ev.Recipes.Concat(GetDiskRecipes(dataComp)).ToHashSet();
            }
            RaiseLocalEvent(uid, ev);
            return ev.Recipes.ToList();
        }

        public bool TryAddToQueue(EntityUid uid, LatheRecipePrototype recipe, int quantity, LatheComponent? component = null, bool fromMemoryBlueprint = false)
        {
            if (!Resolve(uid, ref component))
                return false;

            if (quantity <= 0)
                return false;
            quantity = int.Min(quantity, MaxItemsPerRequest);
            if (!CanProduce(uid, recipe, quantity, component))
                return false;
            var isDrmDisk = false;
            FileSystemComponent? diskFs = null;
            EntityUid? diskUid = null;
            RecipeBlueprintFile? diskTargetBp = null;

            if (!fromMemoryBlueprint && component.diskSlot?.Item is EntityUid loaded && !TerminatingOrDeleted(loaded) && TryComp<FileSystemComponent>(loaded, out diskFs))
            {
                diskUid = loaded;

                if (!string.IsNullOrEmpty(component.SelectedBlueprintFile) &&
                    diskFs.Files.TryGetValue(component.SelectedBlueprintFile, out var chosenFile) &&
                    chosenFile is RecipeBlueprintFile chosenBp &&
                    chosenBp.GetAllRecipes().Contains(recipe.ID))
                {
                    diskTargetBp = chosenBp;
                }
                else
                {
                    foreach (var file in diskFs.Files.Values)
                    {
                        if (file is RecipeBlueprintFile rFile && rFile.GetAllRecipes().Contains(recipe.ID))
                        {
                            diskTargetBp = rFile;
                            break;
                        }
                    }
                }

                if (diskTargetBp != null && BlueprintRequiresUses(diskTargetBp, recipe))
                    isDrmDisk = true;
            }

            if (!fromMemoryBlueprint && (recipe.pointUsage != 0 || isDrmDisk))
            {
                var usage = GetRecipeUseCost(recipe, quantity);

                if (diskFs != null && diskUid != null)
                {
                    if (diskTargetBp == null)
                        return false;

                    if (!TryValidateBlueprintUses(diskTargetBp, recipe, quantity, out _))
                        return false;

                    ConsumeBlueprintUses(diskUid.Value, diskFs, diskTargetBp, recipe, quantity);
                }
                else if (component.diskSlot?.Item is EntityUid legacyLoaded && !TerminatingOrDeleted(legacyLoaded) && TryComp<DigitalDataHolderComponent>(legacyLoaded, out var data))
                {
                    foreach (var file in data.files)
                    {
                        if (file is DigitalDataLathe latheData && latheData.recipes.Contains(recipe.ID))
                        {
                            if (latheData.uses < usage)
                                return false;
                            latheData.uses -= usage;
                        }
                    }
                }
                else
                    return false;
            }

            foreach (var (mat, amount) in GetAdjustedAmount(component, recipe))
                _materialStorage.TryChangeMaterialAmount(uid, mat, -amount * quantity);

            if (component.Queue.Last is { } node && node.ValueRef.Recipe == recipe.ID)
                node.ValueRef.ItemsRequested += quantity;
            else
                component.Queue.AddLast(new LatheRecipeBatch(recipe.ID, 0, quantity));

            return true;
        }

        public bool TryStartProducing(EntityUid uid, LatheComponent? component = null)
        {
            if (!Resolve(uid, ref component))
                return false;
            if (component.CurrentRecipe != null || component.Queue.Count <= 0 || !this.IsPowered(uid, EntityManager))
                return false;

            var batch = component.Queue.First();
            batch.ItemsPrinted++;
            if (batch.ItemsPrinted >= batch.ItemsRequested || batch.ItemsPrinted < 0) // Rollover sanity check
                component.Queue.RemoveFirst();
            var recipe = ProtoMan.Index(batch.Recipe);

            var time = _reagentSpeed.ApplySpeed(uid, recipe.CompleteTime) * component.TimeMultiplier;

            var lathe = EnsureComp<LatheProducingComponent>(uid);
            lathe.StartTime = _timing.CurTime;
            lathe.ProductionLength = time;
            component.CurrentRecipe = recipe;

            var ev = new LatheStartPrintingEvent(recipe);
            RaiseLocalEvent(uid, ref ev);

            _audio.PlayPvs(component.ProducingSound, uid);
            UpdateRunningAppearance(uid, true);
            UpdateUserInterfaceState(uid, component);

            if (time == TimeSpan.Zero)
            {
                FinishProducing(uid, component, lathe);
            }
            return true;
        }

        public void FinishProducing(EntityUid uid, LatheComponent? comp = null, LatheProducingComponent? prodComp = null)
        {
            if (!Resolve(uid, ref comp, ref prodComp, false))
                return;

            if (comp.CurrentRecipe != null)
            {
                var currentRecipe = ProtoMan.Index(comp.CurrentRecipe.Value);
                if (currentRecipe.Result is { } resultProto)
                {
                    var result = Spawn(resultProto, Transform(uid).Coordinates);
                    _stack.TryMergeToContacts(result);
                }

                if (currentRecipe.ResultReagents is { } resultReagents &&
                    comp.ReagentOutputSlotId is { } slotId)
                {
                    var toAdd = new Solution(
                        resultReagents.Select(p => new ReagentQuantity(p.Key.Id, p.Value, null)));

                    // dispense it in the container if we have it and dump it if we don't
                    if (_container.TryGetContainer(uid, slotId, out var container) &&
                        container.ContainedEntities.Count == 1 &&
                        _solution.TryGetFitsInDispenser(container.ContainedEntities.First(), out var solution, out _))
                    {
                        _solution.AddSolution(solution.Value, toAdd);
                    }
                    else
                    {
                        _popup.PopupEntity(Loc.GetString("lathe-reagent-dispense-no-container", ("name", uid)), uid);
                        _puddle.TrySpillAt(uid, toAdd, out _);
                    }
                }
            }

            comp.CurrentRecipe = null;
            prodComp.StartTime = _timing.CurTime;

            if (!TryStartProducing(uid, comp))
            {
                RemCompDeferred(uid, prodComp);
                UpdateUserInterfaceState(uid, comp);
                UpdateRunningAppearance(uid, false);
            }
        }

        public void UpdateUserInterfaceState(EntityUid uid, LatheComponent? component = null, string? statusFeedback = null)
        {
            if (!Resolve(uid, ref component))
                return;

            var producing = component.CurrentRecipe;
            if (producing == null && component.Queue.First is { } node)
                producing = node.Value.Recipe;

            TimeSpan? prodStart = null;
            TimeSpan? prodLength = null;
            if (component.CurrentRecipe != null && TryComp<LatheProducingComponent>(uid, out var prodComp))
            {
                prodStart = prodComp.StartTime;
                prodLength = prodComp.ProductionLength;
            }

            var blueprintFiles = new List<LatheBlueprintFileDto>();
            string? activeFile = component.SelectedBlueprintFile;
            var diskName = string.Empty;
            var uses = 0;

            if (component.diskSlot?.Item is EntityUid validDisk && !TerminatingOrDeleted(validDisk) && TryComp<FileSystemComponent>(validDisk, out var validFs))
            {
                foreach (var (path, file) in validFs.Files)
                {
                    if (file is RecipeBlueprintFile rFile)
                    {
                        var fileRecipes = rFile.GetAllRecipes().ToList();
                        blueprintFiles.Add(new LatheBlueprintFileDto(
                            rFile.Name,
                            rFile.FullPath,
                            rFile.RemainingUses,
                            rFile.DrmLocked,
                            rFile.IsEncrypted,
                            fileRecipes
                        ));
                    }
                }

                // Only reset activeFile if a specific file was selected that no longer exists on the disk
                if (!string.IsNullOrEmpty(activeFile) && !blueprintFiles.Any(f => f.FullPath == activeFile))
                {
                    activeFile = null;
                    component.SelectedBlueprintFile = null;
                }

                var diskEntityName = MetaData(validDisk).EntityName;
                diskName = diskEntityName;
                if (!string.IsNullOrEmpty(activeFile) && validFs.Files.TryGetValue(activeFile, out var chosen) && chosen is RecipeBlueprintFile chosenBp)
                {
                    uses = chosenBp.RemainingUses ?? -1;
                }
                else
                {
                    uses = -1;
                }
            }
            else if (component.diskSlot?.Item is EntityUid legacyDisk && !TerminatingOrDeleted(legacyDisk) && TryComp<DigitalDataHolderComponent>(legacyDisk, out var dataComp))
            {
                foreach (var file in dataComp.getFileByData<DigitalDataLathe>())
                {
                    diskName = file.name;
                    if (file.uses is not null)
                    {
                        uses = file.uses.Value;
                    }
                    break;
                }
            }
            else
            {
                component.SelectedBlueprintFile = null;
                activeFile = null;
            }

            var recipes = GetAvailableRecipes(uid, component);
            var state = new LatheUpdateState(
                recipes,
                component.Queue.ToArray(),
                producing,
                prodStart,
                prodLength,
                blueprintFiles,
                activeFile
            )
            {
                diskName = diskName,
                uses = uses
            };

            _uiSys.SetUiState(uid, LatheUiKey.Key, state);
            EmitLatheStatus((uid, component), statusFeedback);
        }

        private void OnLatheSelectBlueprintFileMessage(EntityUid uid, LatheComponent component, LatheSelectBlueprintFileMessage args)
        {
            component.SelectedBlueprintFile = args.FilePath;
            UpdateUserInterfaceState(uid, component);
        }

        /// <summary>
        /// Adds every unlocked recipe from each pack to the recipes list.
        /// </summary>
        public void AddRecipesFromDynamicPacks(ref LatheGetRecipesEvent args, TechnologyDatabaseComponent database, IEnumerable<ProtoId<LatheRecipePackPrototype>> packs)
        {
            foreach (var id in packs)
            {
                var pack = ProtoMan.Index(id);
                foreach (var recipe in pack.Recipes)
                {
                    if (args.GetUnavailable || database.UnlockedRecipes.Contains(recipe))
                        args.Recipes.Add(recipe);
                }
            }
        }

        private void OnGetRecipes(EntityUid uid, TechnologyDatabaseComponent component, LatheGetRecipesEvent args)
        {
            if (uid == args.Lathe)
                AddRecipesFromDynamicPacks(ref args, component, args.Comp.DynamicPacks);
        }

        private void GetEmagLatheRecipes(EntityUid uid, EmagLatheRecipesComponent component, LatheGetRecipesEvent args)
        {
            if (uid != args.Lathe)
                return;

            if (!args.GetUnavailable && !_emag.CheckFlag(uid, EmagType.Interaction))
                return;

            AddRecipesFromPacks(args.Recipes, component.EmagStaticPacks);

            if (TryComp<TechnologyDatabaseComponent>(uid, out var database))
                AddRecipesFromDynamicPacks(ref args, database, component.EmagDynamicPacks);
        }

        private void OnHeatStartPrinting(EntityUid uid, LatheHeatProducingComponent component, LatheStartPrintingEvent args)
        {
            component.NextSecond = _timing.CurTime;
        }

        private void OnMaterialAmountChanged(EntityUid uid, LatheComponent component, ref MaterialAmountChangedEvent args)
        {
            UpdateUserInterfaceState(uid, component);
        }

        /// <summary>
        /// Initialize the UI and appearance.
        /// Appearance requires initialization or the layers break
        /// </summary>
        private void OnMapInit(EntityUid uid, LatheComponent component, MapInitEvent args)
        {
            _appearance.SetData(uid, LatheVisuals.IsInserting, false);
            _appearance.SetData(uid, LatheVisuals.IsRunning, false);
            if(component.diskSlot is not null)
                itemslots.AddItemSlot(uid, diskSlot, component.diskSlot);

            _materialStorage.UpdateMaterialWhitelist(uid);
            SyncBlueprintsToEmbeddedComputer(uid, component);
        }

        /// <summary>
        /// Sets the machine sprite to either play the running animation
        /// or stop.
        /// </summary>
        private void UpdateRunningAppearance(EntityUid uid, bool isRunning)
        {
            _appearance.SetData(uid, LatheVisuals.IsRunning, isRunning);
        }

        private void OnPowerChanged(EntityUid uid, LatheComponent component, ref PowerChangedEvent args)
        {
            if (!args.Powered)
            {
                AbortProduction(uid);
            }
            else
            {
                TryStartProducing(uid, component);
            }
        }

        private void OnDatabaseModified(EntityUid uid, LatheComponent component, ref TechnologyDatabaseModifiedEvent args)
        {
            UpdateUserInterfaceState(uid, component);
        }

        private void OnTechnologyDatabaseModified(Entity<LatheAnnouncingComponent> ent, ref TechnologyDatabaseModifiedEvent args)
        {
            if (args.NewlyUnlockedRecipes is null)
                return;

            if (!TryGetAvailableRecipes(ent.Owner, out var potentialRecipes))
                return;

            var recipeNames = new List<string>();
            foreach (var recipeId in args.NewlyUnlockedRecipes)
            {
                if (!potentialRecipes.Contains(new(recipeId)))
                    continue;

                if (!ProtoMan.TryIndex(recipeId, out LatheRecipePrototype? recipe))
                    continue;

                var itemName = GetRecipeName(recipe!);
                recipeNames.Add(Loc.GetString("lathe-unlock-recipe-radio-broadcast-item", ("item", itemName)));
            }

            if (recipeNames.Count == 0)
                return;

            var message =
                recipeNames.Count > ent.Comp.MaximumItems ?
                    Loc.GetString(
                        "lathe-unlock-recipe-radio-broadcast-overflow",
                        ("items", ContentLocalizationManager.FormatList(recipeNames.GetRange(0, ent.Comp.MaximumItems))),
                        ("count", recipeNames.Count)
                    ) :
                    Loc.GetString(
                        "lathe-unlock-recipe-radio-broadcast",
                        ("items", ContentLocalizationManager.FormatList(recipeNames))
                    );

            foreach (var channel in ent.Comp.Channels)
            {
                _radio.SendRadioMessage(ent.Owner, message, channel, ent.Owner, escapeMarkup: false);
            }
        }

        private void OnResearchRegistrationChanged(EntityUid uid, LatheComponent component, ref ResearchRegistrationChangedEvent args)
        {
            UpdateUserInterfaceState(uid, component);
        }

        protected override bool HasRecipe(EntityUid uid, LatheRecipePrototype recipe, LatheComponent component)
        {
            return GetAvailableRecipes(uid, component).Contains(recipe.ID);
        }

        /// <summary>
        /// Iterator returning adjusted amount of material needed to
        /// produce a given recipe
        /// </summary>
        private static IEnumerable<(ProtoId<MaterialPrototype> mat, int amount)> GetAdjustedAmount(LatheComponent lathe, LatheRecipePrototype recipe)
        {
            foreach (var (mat, amount) in recipe.Materials)
            {
                var adjustedAmount = recipe.ApplyMaterialDiscount
                    ? (int)(amount * lathe.MaterialUseMultiplier)
                    : amount;

                yield return (mat, adjustedAmount);
            }
        }

        /// <summary>
        /// Refunds the material cost of the currently running recipe,
        /// without cancelling production
        private void RefundLicensePoints(EntityUid uid, LatheComponent lathe, ProtoId<LatheRecipePrototype> recipeId, int count)
        {
            if (count <= 0)
                return;

            if (!ProtoMan.TryIndex(recipeId, out LatheRecipePrototype? recipe))
                return;

            var pointCost = recipe.pointUsage > 0 ? recipe.pointUsage : 1;
            var pointsToRefund = pointCost * count;

            if (lathe.diskSlot?.Item is EntityUid diskUid && !TerminatingOrDeleted(diskUid) && TryComp<FileSystemComponent>(diskUid, out var diskFs))
            {
                RecipeBlueprintFile? targetBp = null;
                // If a specific blueprint file was selected and contains this recipe, refund to that file first
                if (!string.IsNullOrEmpty(lathe.SelectedBlueprintFile) &&
                    diskFs.Files.TryGetValue(lathe.SelectedBlueprintFile, out var chosenFile) &&
                    chosenFile is RecipeBlueprintFile chosenBp &&
                    chosenBp.GetAllRecipes().Contains(recipe.ID) &&
                    chosenBp.RemainingUses != null)
                {
                    targetBp = chosenBp;
                }
                else
                {
                    foreach (var file in diskFs.Files.Values)
                    {
                        if (file is RecipeBlueprintFile rFile && rFile.GetAllRecipes().Contains(recipe.ID) && rFile.RemainingUses != null)
                        {
                            targetBp = rFile;
                            break;
                        }
                    }
                }

                if (targetBp != null && targetBp.RemainingUses != null)
                {
                    targetBp.RemainingUses += pointsToRefund;
                    _fileSystem.NotifyFileModified(diskUid, targetBp.FullPath, diskFs);
                }
            }
            else if (lathe.diskSlot?.Item is EntityUid legacyDisk && !TerminatingOrDeleted(legacyDisk) && TryComp<DigitalDataHolderComponent>(legacyDisk, out var legacyData))
            {
                foreach (var file in legacyData.getFileByData<DigitalDataLathe>())
                {
                    if (file.recipes.Contains(recipe.ID) && file.uses != null)
                    {
                        file.uses += pointsToRefund;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Refunds the material and license point cost of the recipe currently being printed
        /// </summary>
        private void RefundCurrentRecipe(EntityUid uid, LatheComponent lathe)
        {
            if (lathe.CurrentRecipe == null)
                return;

            var recipeId = lathe.CurrentRecipe.Value;
            if (ProtoMan.Resolve(recipeId, out var recipe))
            {
                foreach (var (mat, amount) in GetAdjustedAmount(lathe, recipe!))
                    _materialStorage.TryChangeMaterialAmount(uid, mat, amount);
            }

            RefundLicensePoints(uid, lathe, recipeId, 1);
        }

        /// <summary>
        /// Refunds the material and license point cost of a given batch,
        /// without deleting it
        /// </summary>
        private void RefundBatch(EntityUid uid, LatheComponent lathe, LatheRecipeBatch batch)
        {
            var delta = batch.ItemsRequested - batch.ItemsPrinted;
            if (delta <= 0)
                return;

            if (ProtoMan.Resolve(batch.Recipe, out var recipe))
            {
                foreach (var (mat, amount) in GetAdjustedAmount(lathe, recipe!))
                    _materialStorage.TryChangeMaterialAmount(uid, mat, amount * delta);
            }

            RefundLicensePoints(uid, lathe, batch.Recipe, delta);
        }

        /// <summary>
        /// Aborts the recipe currently printing, putting it back on the queue.
        /// </summary>
        /// <remarks>
        /// Alters batch counts where possible instead of creating a new batch.
        /// </remarks>
        public void AbortProduction(EntityUid uid, LatheComponent? component = null)
        {
            if (!Resolve(uid, ref component))
                return;

            if (component.CurrentRecipe != null)
            {
                if (component.Queue.Count > 0)
                {
                    var batch = component.Queue.First();
                    if (batch.Recipe != component.CurrentRecipe)
                    {
                        // Batch on top doesn't match, create a new one to store the current recipe.
                        var newBatch = new LatheRecipeBatch(component.CurrentRecipe.Value, 0, 1);
                        component.Queue.AddFirst(newBatch);
                    }
                    else if (batch.ItemsPrinted > 0)
                    {
                        // Partial batch in progress, remove from the printed count
                        batch.ItemsPrinted--;
                    }
                    else
                    {
                        // Batch not yet started, add to the requested set.
                        batch.ItemsRequested++;
                    }
                }
                else
                {
                    // Nothing exists, create a new batch to save the current recipe.
                    var newBatch = new LatheRecipeBatch(component.CurrentRecipe.Value, 0, 1);
                    component.Queue.AddFirst(newBatch);
                }

                // Finally, clear the current recipe.
                component.CurrentRecipe = null;
            }
            RemCompDeferred<LatheProducingComponent>(uid);
            UpdateUserInterfaceState(uid, component);
            UpdateRunningAppearance(uid, false);
        }

        #region UI Messages

        private void OnLatheQueueRecipeMessage(EntityUid uid, LatheComponent component, LatheQueueRecipeMessage args)
        {
            if (ProtoMan.TryIndex(args.ID, out LatheRecipePrototype? recipe))
            {
                if (TryAddToQueue(uid, recipe, args.Quantity, component))
                {
                    _adminLogger.Add(LogType.Action,
                        LogImpact.Low,
                        $"{ToPrettyString(args.Actor):player} queued {args.Quantity} {GetRecipeName(recipe)} at {ToPrettyString(uid):lathe}");
                }
            }
            TryStartProducing(uid, component);
            UpdateUserInterfaceState(uid, component);
        }

        private void OnLatheSyncRequestMessage(EntityUid uid, LatheComponent component, LatheSyncRequestMessage args)
        {
            UpdateUserInterfaceState(uid, component);
        }

        /// <summary>
        /// Removes a batch from the batch queue by index.
        /// If the index given does not exist or is outside of the bounds of the lathe's batch queue, nothing happens.
        /// </summary>
        /// <param name="uid">The lathe whose queue is being altered.</param>
        /// <param name="component"></param>
        /// <param name="args"></param>
        public void OnLatheDeleteRequestMessage(EntityUid uid, LatheComponent component, ref LatheDeleteRequestMessage args)
        {
            if (args.Index < 0 || args.Index >= component.Queue.Count)
                return;

            var node = component.Queue.First;
            for (int i = 0; i < args.Index; i++)
                node = node?.Next;

            if (node == null) // Shouldn't happen with checks above.
                return;

            var batch = node.Value;
            _adminLogger.Add(LogType.Action,
                LogImpact.Low,
                $"{ToPrettyString(args.Actor):player} deleted a lathe job for ({batch.ItemsPrinted}/{batch.ItemsRequested}) {GetRecipeName(batch.Recipe)} at {ToPrettyString(uid):lathe}");

            RefundBatch(uid, component, batch);
            component.Queue.Remove(node);
            UpdateUserInterfaceState(uid, component);
        }

        public void OnLatheMoveRequestMessage(EntityUid uid, LatheComponent component, ref LatheMoveRequestMessage args)
        {
            if (args.Change == 0 || args.Index < 0 || args.Index >= component.Queue.Count)
                return;

            // New index must be within the bounds of the batch.
            var newIndex = args.Index + args.Change;
            if (newIndex < 0 || newIndex >= component.Queue.Count)
                return;

            var node = component.Queue.First;
            for (int i = 0; i < args.Index; i++)
                node = node?.Next;

            if (node == null) // Something went wrong.
                return;

            if (args.Change > 0)
            {
                var newRelativeNode = node.Next;
                for (int i = 1; i < args.Change; i++) // 1-indexed: starting from Next
                    newRelativeNode = newRelativeNode?.Next;

                if (newRelativeNode == null) // Something went wrong.
                    return;

                component.Queue.Remove(node);
                component.Queue.AddAfter(newRelativeNode, node);
            }
            else
            {
                var newRelativeNode = node.Previous;
                for (int i = 1; i < -args.Change; i++) // 1-indexed: starting from Previous
                    newRelativeNode = newRelativeNode?.Previous;

                if (newRelativeNode == null) // Something went wrong.
                    return;

                component.Queue.Remove(node);
                component.Queue.AddBefore(newRelativeNode, node);
            }

            UpdateUserInterfaceState(uid, component);
        }

        public void OnLatheAbortFabricationMessage(EntityUid uid, LatheComponent component, ref LatheAbortFabricationMessage args)
        {
            if (component.CurrentRecipe == null)
                return;

            _adminLogger.Add(LogType.Action,
                LogImpact.Low,
                $"{ToPrettyString(args.Actor):player} aborted printing {GetRecipeName(component.CurrentRecipe.Value)} at {ToPrettyString(uid):lathe}");

            RefundCurrentRecipe(uid, component);
            component.CurrentRecipe = null;
            FinishProducing(uid, component);
        }
        #endregion
    }
}
