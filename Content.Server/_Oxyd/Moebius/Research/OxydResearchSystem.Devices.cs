// Eris Moebius research port: destructive analyzer + experiment sources.
// Eris source: destructive_analyzer.dm, experiment.dm (science_tool,
// autopsy_scanner, explosion_watcher, points disks).
using Content.Server.Materials;
using Content.Shared._Oxyd.Moebius.Research;
using Content.Shared.Containers.ItemSlots;
using Content.Server.Stack;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.Lathe;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Materials;
using Content.Shared.Mobs.Components;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Oxyd.Moebius.Research;

public sealed partial class OxydResearchSystem
{
    private void InitializeDevices()
    {
        SubscribeLocalEvent<OxydRdConsoleComponent, EntInsertedIntoContainerMessage>(OnConsoleSlotInsert);
        SubscribeLocalEvent<OxydRdConsoleComponent, EntRemovedFromContainerMessage>(OnConsoleSlotRemove);
        SubscribeLocalEvent<OxydDestructiveAnalyzerComponent, EntInsertedIntoContainerMessage>(OnDaInsert);
        SubscribeLocalEvent<OxydScienceToolComponent, AfterInteractUsingEvent>(OnScienceToolUse);
        SubscribeLocalEvent<OxydAutopsyScannerComponent, AfterInteractUsingEvent>(OnAutopsyUse);
        SubscribeLocalEvent<OxydExplosionWatcherComponent, DamageChangedEvent>(OnWatcherDamaged);
    }

    // ---- slot plumbing: refresh the UI when disks/tools change ----
    private void OnConsoleSlotInsert(Entity<OxydRdConsoleComponent> ent, ref EntInsertedIntoContainerMessage args) => UpdateUi(ent);
    private void OnConsoleSlotRemove(Entity<OxydRdConsoleComponent> ent, ref EntRemovedFromContainerMessage args) => UpdateUi(ent);
    private void OnDaInsert(Entity<OxydDestructiveAnalyzerComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        foreach (var console in FindConsolesLinkedTo(ent.Owner))
            UpdateUi(console);
    }

    private IEnumerable<Entity<OxydRdConsoleComponent>> FindConsolesLinkedTo(EntityUid device)
    {
        var q = EntityQuery<OxydRdConsoleComponent>();
        foreach (var c in EntityManager.EntityQuery<OxydRdConsoleComponent>())
        {
            if (c.LinkedDevices.Contains(device))
                yield return (c.Owner, c);
        }
    }

    // ---- destructive analyzer (Eris destructive_analyzer.dm) ----
    private void OnDeconstruct(Entity<OxydRdConsoleComponent> ent, ref OxydRdDeconstructMessage args)
    {
        var da = FindAnalyzerWithItem(ent);
        if (da == null)
        {
            _popup.PopupEntity("No loaded item in linked analyzer.", ent, args.Actor);
            return;
        }
        var daUid = da.Value;
        var daComp = Comp<OxydDestructiveAnalyzerComponent>(daUid);
        if (daComp.Busy)
            return;
        var item = _slots.GetItemOrNull(daUid, InputSlot);
        if (item == null)
            return;
        daComp.Busy = true;
        daComp.BusyUntil = _timing.CurTime + TimeSpan.FromSeconds(daComp.DeconstructTime);
        Dirty(daUid, daComp);
        _pendingDeconstruct[(daUid, item.Value)] = ent.Owner;
        _popup.PopupEntity("Destructive analysis started.", ent, args.Actor);
        UpdateUi(ent);
    }

    private readonly Dictionary<(EntityUid Da, EntityUid Item), EntityUid> _pendingDeconstruct = new();

    public override void Update(float frameTime)
    {
        var done = new List<(EntityUid, EntityUid)>();
        foreach (var ((da, item), console) in _pendingDeconstruct)
        {
            if (TerminatingOrDeleted(da) || TerminatingOrDeleted(item))
            {
                done.Add((da, item));
                continue;
            }
            var comp = Comp<OxydDestructiveAnalyzerComponent>(da);
            if (_timing.CurTime < comp.BusyUntil)
                continue;
            FinishDeconstruct(da, item, console);
            done.Add((da, item));
        }
        foreach (var k in done)
            _pendingDeconstruct.Remove(k);
    }

    private void FinishDeconstruct(EntityUid da, EntityUid item, EntityUid consoleUid)
    {
        if (TryComp<OxydDestructiveAnalyzerComponent>(da, out var comp))
        {
            comp.Busy = false;
            Dirty(da, comp);
        }
        if (!TryComp<OxydRdConsoleComponent>(consoleUid, out var console))
        {
            Del(item);
            return;
        }
        // Eris get_object_research_value: new tech levels grant big point chunks;
        // already-known levels refund 1 point.
        if (TryComp<OxydOriginTechComponent>(item, out var origin))
        {
            foreach (var (tree, level) in origin.Techs)
            {
                console.TreeLevels.TryGetValue(tree, out var cur);
                if (level > cur)
                {
                    var value = TreePointValues.GetValueOrDefault(tree, 250);
                    console.Points += value;
                    console.TreeLevels[tree] = level;
                }
                else
                {
                    console.Points += 1;
                }
            }
        }
        else
        {
            // Eris DA still returns materials for objects with no origin_tech.
            console.Points += 1;
        }
        // Eris DA refunds material sheets to the analyzer output.
        if (TryComp<PhysicalCompositionComponent>(item, out var phys))
        {
            foreach (var (matId, amount) in phys.MaterialComposition)
            {
                if (!_proto.TryIndex<MaterialPrototype>(matId, out var mat) || mat.StackEntity == null)
                    continue;
                var sheets = Math.Max(1, amount / 200); // ~half the item's mass (Eris returns stacks)
                var stack = Spawn(mat.StackEntity.Value, Transform(da).Coordinates);
                _stack.SetCount(stack, sheets);
            }
        }
        Dirty(consoleUid, console);
        Del(item);
        UpdateUi((consoleUid, console));
        var consoleEnt = new Entity<OxydRdConsoleComponent>(consoleUid, console);
        UpdateUi(consoleEnt);
    }

    [Dependency] private readonly StackSystem _stack = default!;

    private EntityUid? FindAnalyzerWithItem(Entity<OxydRdConsoleComponent> ent)
    {
        foreach (var dev in ent.Comp.LinkedDevices)
        {
            if (TerminatingOrDeleted(dev))
                continue;
            if (!TryComp<OxydRdLinkableComponent>(dev, out var link) || link.Kind != OxydRdDeviceKind.Analyzer)
                continue;
            if (_slots.GetItemOrNull(dev, InputSlot) != null)
                return dev;
        }
        return null;
    }

    private void OnEjectItem(Entity<OxydRdConsoleComponent> ent, ref OxydRdEjectItemMessage args)
    {
        var da = FindAnalyzerWithItem(ent);
        if (da == null)
            return;
        if (_slots.TryGetSlot(da.Value, InputSlot, out var slot) && slot.HasItem)
            _slots.TryEject(da.Value, slot, args.Actor, out _);
        UpdateUi(ent);
    }

    // ---- disk management (Eris disk_management screens) ----
    private void OnDisk(Entity<OxydRdConsoleComponent> ent, ref OxydRdDiskMessage args)
    {
        switch (args.Op)
        {
            case OxydRdDiskOp.Eject:
                if (_slots.TryGetSlot(ent.Owner, DiskSlot, out var diskSlot) && diskSlot.HasItem)
                    _slots.TryEject(ent.Owner, diskSlot, args.Actor, out _);
                break;
            case OxydRdDiskOp.Upload:
                DiskUpload(ent);
                break;
            case OxydRdDiskOp.Download:
                DiskDownload(ent);
                break;
            case OxydRdDiskOp.Clear:
                DiskClear(ent);
                break;
        }
        UpdateUi(ent);
    }

    private void DiskUpload(Entity<OxydRdConsoleComponent> ent)
    {
        var disk = _slots.GetItemOrNull(ent.Owner, DiskSlot);
        if (disk == null)
            return;
        if (TryComp<OxydResearchPointsDiskComponent>(disk, out var points))
        {
            if (!points.Spent)
            {
                ent.Comp.Points += points.Points;
                points.Spent = true;
                Dirty(disk.Value, points);
                Dirty(ent);
            }
            return;
        }
        if (!TryComp<OxydResearchDataDiskComponent>(disk, out var data))
            return;
        // Eris "Load Data": stored tech nodes research free of cost; stored designs
        // get pushed to linked lathes.
        foreach (var nodeId in data.StoredNodes)
        {
            if (ent.Comp.UnlockedNodes.Add(nodeId))
            {
                if (_proto.TryIndex<OxydTechNodePrototype>(nodeId, out var node))
                {
                    ent.Comp.TreeLevels.TryGetValue(node.Tree, out var l);
                    ent.Comp.TreeLevels[node.Tree] = l + 1;
                }
            }
        }
        var changed = false;
        foreach (var dev in ent.Comp.LinkedDevices)
        {
            if (!TryComp<LatheComponent>(dev, out var lathe))
                continue;
            foreach (var recipeId in data.StoredDesigns)
            {
                var pack = new ProtoId<LatheRecipePackPrototype>($"OxydPack{recipeId[6..]}");
                if (_proto.TryIndex(pack, out _) && !lathe.DynamicPacks.Contains(pack))
                {
                    lathe.DynamicPacks.Add(pack);
                    changed = true;
                }
            }
            if (changed)
                Dirty(dev, lathe);
        }
        foreach (var dev in ent.Comp.LinkedDevices)
            SyncLatheRecipes(ent, dev);
        Dirty(ent);
    }

    private void DiskDownload(Entity<OxydRdConsoleComponent> ent)
    {
        var disk = _slots.GetItemOrNull(ent.Owner, DiskSlot);
        if (disk == null || !TryComp<OxydResearchDataDiskComponent>(disk, out var data))
            return;
        data.StoredNodes.UnionWith(ent.Comp.UnlockedNodes);
        foreach (var nodeId in ent.Comp.UnlockedNodes)
        {
            if (!_proto.TryIndex<OxydTechNodePrototype>(nodeId, out var node))
                continue;
            foreach (var recipe in node.UnlocksRecipes)
                data.StoredDesigns.Add(recipe.Id);
        }
        Dirty(disk.Value, data);
    }

    private void DiskClear(Entity<OxydRdConsoleComponent> ent)
    {
        var disk = _slots.GetItemOrNull(ent.Owner, DiskSlot);
        if (disk == null || !TryComp<OxydResearchDataDiskComponent>(disk, out var data))
            return;
        data.StoredNodes.Clear();
        data.StoredDesigns.Clear();
        Dirty(disk.Value, data);
    }

    // ---- science tool upload (Eris attackby with science_tool) ----
    private void OnUploadTool(Entity<OxydRdConsoleComponent> ent, ref OxydRdUploadToolMessage args)
    {
        var item = _slots.GetItemOrNull(ent.Owner, ToolSlot);
        if (item == null)
            return;
        if (TryComp<OxydScienceToolComponent>(item, out var tool) && tool.Blocks.Count > 0)
        {
            var sum = 0;
            foreach (var b in tool.Blocks)
                sum += b.Value;
            ent.Comp.Points += sum;
            tool.Blocks.Clear();
            tool.TotalValue = 0;
            Dirty(item.Value, tool);
            Dirty(ent);
            _popup.PopupEntity($"Uploaded {sum} research points.", ent, args.Actor);
            return;
        }
        if (TryComp<OxydAutopsyReportComponent>(item, out var report))
        {
            ent.Comp.Points += report.Value;
            Dirty(ent);
            if (_slots.TryGetSlot(ent.Owner, ToolSlot, out var slot))
                _slots.TryEject(ent.Owner, slot, null, out _);
            Del(item.Value);
            _popup.PopupEntity($"Autopsy data uploaded: +{report.Value} points.", ent, args.Actor);
        }
    }

    // ---- science tool use (Eris science_tool attack) ----
    private void OnScienceToolUse(Entity<OxydScienceToolComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;
        var target = args.Target;
        if (target == null || !TryComp<OxydOriginTechComponent>(target.Value, out var origin) || origin.Techs.Count == 0)
        {
            _popup.PopupEntity("No research value detected.", ent, args.User);
            return;
        }
        var value = 0;
        foreach (var (tree, lvl) in origin.Techs)
            value += lvl * 100;
        ent.Comp.Blocks.Add(new OxydScanBlock { Subject = Name(target.Value), Value = value });
        ent.Comp.TotalValue += value;
        Dirty(ent);
        _popup.PopupEntity($"Scan recorded: {Name(target.Value)} (+{value}).", ent, args.User);
        args.Handled = true;
    }

    // ---- autopsy scanner (Eris autopsy_scanner) ----
    private void OnAutopsyUse(Entity<OxydAutopsyScannerComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;
        if (_timing.CurTime < ent.Comp.NextUse)
            return;
        var target = args.Target;
        if (target == null || !HasComp<MobStateComponent>(target.Value))
            return;
        ent.Comp.NextUse = _timing.CurTime + TimeSpan.FromSeconds(8);
        Dirty(ent);
        var report = Spawn("OxydAutopsyReport", Transform(args.User).Coordinates);
        if (TryComp<OxydAutopsyReportComponent>(report, out var rep))
        {
            rep.Report = $"Autopsy of {Name(target.Value)}. Time of death unestablished; " +
                         "external trauma consistent with station hazards.";
            Dirty(report, rep);
        }
        _popup.PopupEntity("Autopsy report printed.", ent, args.User);
        args.Handled = true;
    }

    // ---- explosion watcher (Eris KES: banks points per explosion) ----
    private void OnWatcherDamaged(Entity<OxydExplosionWatcherComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageDelta == null || !Transform(ent).Anchored)
            return;
        var delta = args.DamageDelta.GetTotal().Int();
        if (delta < 20)
            return;
        ent.Comp.ExplosionsRecorded++;
        ent.Comp.BankedPoints += Math.Min(2000, delta * 20); // Eris scales with blast power
        Dirty(ent);
    }

    // ---- BUI state ----
    private OxydRdState BuildState(Entity<OxydRdConsoleComponent> ent)
    {
        var state = new OxydRdState
        {
            Screen = (OxydRdScreen) ent.Comp.Screen,
            Points = ent.Comp.Points,
        };
        foreach (var tree in _proto.EnumeratePrototypes<OxydTechTreePrototype>())
        {
            ent.Comp.TreeLevels.TryGetValue(tree.ID, out var lvl);
            state.Trees.Add(new OxydRdTreeInfo { Id = tree.ID, Name = Loc.GetString(tree.Name), Level = lvl, Color = tree.Color, Shown = tree.Shown });
        }
        state.Trees.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

        // analyzer card
        var da = FindAnalyzerWithItem(ent);
        if (da != null)
        {
            var item = _slots.GetItemOrNull(da.Value, InputSlot);
            var dcomp = Comp<OxydDestructiveAnalyzerComponent>(da.Value);
            state.Da.Busy = dcomp.Busy;
            if (item != null)
            {
                state.Da.HasItem = true;
                state.Da.ItemName = Name(item.Value);
                if (TryComp<OxydOriginTechComponent>(item.Value, out var origin))
                {
                    foreach (var (tree, lvl) in origin.Techs)
                    {
                        var tname = _proto.TryIndex<OxydTechTreePrototype>(tree, out var t) ? Loc.GetString(t.Name) : tree;
                        state.Da.TechLines.Add($"{tname} +{lvl}");
                    }
                }
            }
        }

        // devices
        foreach (var dev in ent.Comp.LinkedDevices)
        {
            if (TerminatingOrDeleted(dev) || !TryComp<OxydRdLinkableComponent>(dev, out var link))
                continue;
            state.Devices.Add(new OxydRdDeviceInfo { Uid = GetNetEntity(dev), Name = Name(dev), Kind = link.Kind });
        }

        // lathe screen: selected device's known recipes
        var selDev = _selectedDevice.GetValueOrDefault(ent.Owner);
        if (selDev == default || !ent.Comp.LinkedDevices.Contains(selDev))
            selDev = state.Devices.Count > 0 ? GetEntity(state.Devices[0].Uid) : default;
        if (selDev != default && TryComp<LatheComponent>(selDev, out var lathe))
        {
            state.SelectedDevice = GetNetEntity(selDev);
            foreach (var rid in _lathe.GetAvailableRecipes(selDev, lathe))
            {
                var recipe = _proto.Index(rid);
                var info = new OxydRdDesignInfo { RecipeId = rid.Id, Name = Loc.GetString(recipe.Name ?? rid.Id), CanPrint = true };
                var missing = new List<string>();
                foreach (var (mat, need) in recipe.Materials)
                {
                    var have = _matStorage.GetMaterialAmount(selDev, mat.Id);
                    if (have < need)
                    {
                        var mname = _proto.TryIndex<MaterialPrototype>(mat, out var mp) ? mp.Name : mat.Id;
                        missing.Add($"{mname} {need - have}");
                        info.CanPrint = false;
                    }
                }
                info.Missing = string.Join(", ", missing);
                state.Designs.Add(info);
            }
            state.Designs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        }

        // trees screen
        var selTree = _selectedTree.GetValueOrDefault(ent.Owner);
        if (string.IsNullOrEmpty(selTree))
            selTree = state.Trees.Count > 0 ? state.Trees[0].Id : null;
        state.SelectedTree = selTree;
        if (selTree != null)
        {
            foreach (var node in _proto.EnumeratePrototypes<OxydTechNodePrototype>())
            {
                if (node.Tree != selTree)
                    continue;
                var info = new OxydRdNodeInfo { Id = node.ID, Name = Loc.GetString(node.Name),
                    Desc = node.Description != null ? Loc.GetString(node.Description.Value) : string.Empty,
                    Cost = node.Cost, X = node.PosX, Y = node.PosY };
                if (ent.Comp.UnlockedNodes.Contains(node.ID))
                    info.State = 2;
                else
                {
                    var ok = true;
                    foreach (var req in node.RequiredNodes)
                    {
                        if (!ent.Comp.UnlockedNodes.Contains(req.Id))
                        {
                            ok = false;
                            info.Missing.Add(_proto.TryIndex<OxydTechNodePrototype>(req.Id, out var r) ? Loc.GetString(r.Name) : req.Id);
                        }
                    }
                    info.State = ok && ent.Comp.Points >= node.Cost ? 1 : 0;
                }
                foreach (var recipe in node.UnlocksRecipes)
                    info.Unlocks.Add(_proto.TryIndex(recipe, out var rp) ? Loc.GetString(rp.Name ?? recipe.Id) : recipe.Id);
                state.Nodes.Add(info);
            }
            state.Nodes.Sort((a, b) => a.Cost != b.Cost ? a.Cost - b.Cost : string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        }

        // disk slot
        var disk = _slots.GetItemOrNull(ent.Owner, DiskSlot);
        if (disk != null)
        {
            state.HasDisk = true;
            if (TryComp<OxydResearchPointsDiskComponent>(disk, out var pd))
            {
                state.DiskIsPointsDisk = true;
                state.DiskPoints = pd.Points;
                state.DiskSpent = pd.Spent;
            }
            else if (TryComp<OxydResearchDataDiskComponent>(disk, out var data))
            {
                state.DiskNodeCount = data.StoredNodes.Count;
                state.DiskDesignCount = data.StoredDesigns.Count;
            }
        }
        // tool slot
        var tool = _slots.GetItemOrNull(ent.Owner, ToolSlot);
        if (tool != null && TryComp<OxydScienceToolComponent>(tool, out var toolComp))
            state.ToolBlocks = toolComp.Blocks.Count;
        else if (tool != null && HasComp<OxydAutopsyReportComponent>(tool.Value))
            state.ToolBlocks = 1;
        return state;
    }
}
