using System.Linq;
// Eris Moebius research port: destructive analyzer + experiment sources.
// Eris source: destructive_analyzer.dm, experiment.dm (science_tool,
// autopsy_scanner, explosion_watcher, points disks).
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Shared._Oxyd.Sanity;
using Content.Server.Materials;
using Content.Shared._Oxyd.Moebius.Research;
using Content.Shared.Interaction.Events;
using Content.Shared.Tools.Components;
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
        SubscribeLocalEvent<OxydScienceToolComponent, AfterInteractEvent>(OnScienceToolUse);
        SubscribeLocalEvent<OxydExplosionWatcherComponent, DamageChangedEvent>(OnWatcherDamaged);
        SubscribeLocalEvent<OxydMentalImprinterComponent, UseInHandEvent>(OnImprinterUse);
        SubscribeLocalEvent<OxydDroneFabricatorComponent, ActivateInWorldEvent>(OnDroneFabricate);
        SubscribeLocalEvent<OxydSlimeExtractorComponent, AfterInteractEvent>(OnSlimeExtract);
        SubscribeLocalEvent<OxydMaintenanceDroneShellComponent, UseInHandEvent>(OnDroneDeploy);
    }

    [Dependency] private SanitySystem _sanity = default!;

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
        // Reviewer round-2: pending work lives on the component, not a system dict.
        daComp.PendingItem = item.Value;
        daComp.PendingConsole = ent.Owner;
        // Lock the input slot while Busy — an ejected item could be merged into a
        // stack (Busy never cleared, analyzer stuck) or kept and then deleted
        // from a player's hand while still awarding points.
        _slots.SetLock(daUid, InputSlot, true);
        Dirty(daUid, daComp);
        _popup.PopupEntity("Destructive analysis started.", ent, args.Actor);
        UpdateUi(ent);
    }

    public override void Update(float frameTime)
    {
        var q = EntityQueryEnumerator<OxydDestructiveAnalyzerComponent>();
        while (q.MoveNext(out var da, out var comp))
        {
            if (comp.PendingItem is not { } item)
                continue;
            if (TerminatingOrDeleted(da))
                continue;
            // If the item left the input slot (destroyed, teleported, merged),
            // abort: never delete or award points for an item the analyzer no
            // longer holds, and clear Busy so the machine isn't stuck forever.
            if (TerminatingOrDeleted(item) || _slots.GetItemOrNull(da, InputSlot) != item)
            {
                ClearPending(da, comp);
                continue;
            }
            if (_timing.CurTime < comp.BusyUntil)
                continue;
            FinishDeconstruct(da, item, comp.PendingConsole ?? EntityUid.Invalid, comp);
        }
    }

    private void ClearPending(EntityUid da, OxydDestructiveAnalyzerComponent comp)
    {
        comp.Busy = false;
        comp.PendingItem = null;
        comp.PendingConsole = null;
        _slots.SetLock(da, InputSlot, false);
        Dirty(da, comp);
    }

    private void FinishDeconstruct(EntityUid da, EntityUid item, EntityUid consoleUid,
        OxydDestructiveAnalyzerComponent comp)
    {
        ClearPending(da, comp);
        // Re-validate the item is still in the input slot before deleting/awarding.
        if (_slots.GetItemOrNull(da, InputSlot) != item)
            return;
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
            // Composition is per unit; a stack holds Count units.
            var units = TryComp<StackComponent>(item, out var itemStack) ? itemStack.Count : 1;
            foreach (var (matId, amount) in phys.MaterialComposition)
            {
                if (!_proto.TryIndex<MaterialPrototype>(matId, out var mat) || mat.StackEntity == null)
                    continue;
                // Eris refunds about half the item's material; never more than went in.
                var sheets = amount * units / 2 / MaterialPerSheet;
                if (sheets <= 0)
                    continue;
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

    [Dependency] private StackSystem _stack = default!;

    /// <summary>Material units in one sheet/ingot stack unit (SS14 materials use 100).</summary>
    private const int MaterialPerSheet = 100;

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
    // AfterInteractEvent is raised on the USED item (the tool) when the click
    // had no other handler — the right hook for item-side scan/extract behavior.
    private void OnScienceToolUse(Entity<OxydScienceToolComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;
        var target = args.Target;
        if (target == null)
        {
            _popup.PopupEntity("No research value detected.", ent, args.User);
            return;
        }
        // Each specimen/object yields data once; rescanning it adds nothing.
        if (HasComp<OxydResearchScannedComponent>(target.Value))
        {
            _popup.PopupEntity("Already scanned — no new research value.", ent, args.User);
            args.Handled = true;
            return;
        }
        // Eris symptom scans (experiment.dm): scanning a specimen yields a
        // datablock — vitals on the living, pathology on the dead.
        if (!TryComp<OxydOriginTechComponent>(target.Value, out var origin) || origin.Techs.Count == 0)
        {
            if (HasComp<MobStateComponent>(target.Value))
            {
                var alive = Comp<MobStateComponent>(target.Value).CurrentState == Shared.Mobs.MobState.Alive;
                var label = alive ? "vitals" : "pathology";
                var svalue = alive ? 50 : 100;
                ent.Comp.Blocks.Add(new OxydScanBlock { Subject = $"{label} sample: {Name(target.Value)}", Value = svalue });
                ent.Comp.TotalValue += svalue;
                EnsureComp<OxydResearchScannedComponent>(target.Value);
                Dirty(ent);
                _popup.PopupEntity($"Symptom scan recorded: {Name(target.Value)} (+{svalue}).", ent, args.User);
                args.Handled = true;
                return;
            }
            _popup.PopupEntity("No research value detected.", ent, args.User);
            return;
        }
        var value = 0;
        foreach (var (tree, lvl) in origin.Techs)
            value += lvl * 100;
        ent.Comp.Blocks.Add(new OxydScanBlock { Subject = Name(target.Value), Value = value });
        ent.Comp.TotalValue += value;
        EnsureComp<OxydResearchScannedComponent>(target.Value);
        Dirty(ent);
        _popup.PopupEntity($"Scan recorded: {Name(target.Value)} (+{value}).", ent, args.User);
        args.Handled = true;
    }

    // ---- mental imprinter (Eris mental_imprinter: eye-stab stat boost at
    // a heavy sanity toll; adapted — no stat system, so it is flavor + cost) ----
    private void OnImprinterUse(Entity<OxydMentalImprinterComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        if (TryComp<SanityComponent>(args.User, out var sanity))
            _sanity.ApplySanityDelta((args.User, sanity), SanitySource.Mental, -ent.Comp.SanityDamage);
        _popup.PopupEntity("You drive the imprinter needle into your eye. Design data floods your mind.", args.User, args.User);
        QueueDel(ent);
    }

    // ---- drone fabricator (Eris drone_fabricator: produces drone shells on a
    // cycle; shells can't be inhabited without a silicon stack) ----
    private void OnDroneFabricate(Entity<OxydDroneFabricatorComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        if (_timing.CurTime < ent.Comp.NextUse)
        {
            _popup.PopupEntity("The fabricator is still assembling a shell.", ent, args.User);
            return;
        }
        ent.Comp.NextUse = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.CooldownSeconds);
        Spawn(ent.Comp.ShellProto, Transform(ent).Coordinates);
        _popup.PopupEntity("The fabricator deposits a folded drone shell.", ent, args.User);
    }

    // ---- slime core extraction (Eris xenobiology extract tooling: cores are
    // research specimens; adapted — draws from any refinable biological mob) ----
    private void OnSlimeExtract(Entity<OxydSlimeExtractorComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target == null)
            return;
        if (!HasComp<MobStateComponent>(args.Target.Value) || !HasComp<ToolRefinableComponent>(args.Target.Value))
            return;
        args.Handled = true;
        if (ent.Comp.Uses <= 0)
        {
            _popup.PopupEntity("The extractor is empty.", ent, args.User);
            return;
        }
        ent.Comp.Uses--;
        Spawn(ent.Comp.CoreProto, Transform(args.Target.Value).Coordinates);
        _popup.PopupEntity($"You draw a specimen core from {Name(args.Target.Value)}.", ent, args.User);
        if (ent.Comp.Uses <= 0)
            QueueDel(ent);
    }

    // ---- maintenance drone shell (Eris maint drone folded form; unfolds into
    // a non-player NPC — inhabited drones need a silicon stack) ----
    private void OnDroneDeploy(Entity<OxydMaintenanceDroneShellComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        Spawn(ent.Comp.DroneProto, Transform(args.User).Coordinates);
        _popup.PopupEntity("The shell unfolds into a maintenance drone.", args.User, args.User);
        QueueDel(ent);
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
        foreach (var dev in ent.Comp.LinkedDevices)
        {
            if (TerminatingOrDeleted(dev))
                continue;
            if (TryComp<OxydRdLinkableComponent>(dev, out var link) && link.Kind == OxydRdDeviceKind.Analyzer)
            {
                state.Da.Linked = true;
                break;
            }
        }
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
                    Cost = node.Cost, X = node.PosX, Y = node.PosY,
                    Icon = string.IsNullOrEmpty(node.IconHint) ? "rd" : node.IconHint,
                    Requires = node.RequiredNodes.Select(id => id.Id).ToList() };
                var prereqsMet = true;
                foreach (var req in node.RequiredNodes)
                {
                    var met = ent.Comp.UnlockedNodes.Contains(req.Id);
                    var reqName = _proto.TryIndex<OxydTechNodePrototype>(req.Id, out var r) ? Loc.GetString(r.Name) : req.Id;
                    info.Prereqs.Add(new OxydRdReqInfo { Name = reqName, Met = met });
                    if (!met)
                    {
                        prereqsMet = false;
                        info.Missing.Add(reqName);
                    }
                }
                if (ent.Comp.UnlockedNodes.Contains(node.ID))
                    info.State = 2;
                else
                    info.State = prereqsMet && ent.Comp.Points >= node.Cost ? 1 : 0;
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
