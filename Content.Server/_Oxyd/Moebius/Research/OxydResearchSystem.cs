// Eris Moebius research port: R&D console logic.
// Eris source: code/modules/research/rdconsole.dm (Topic commands, link/unlink,
// tech unlock, lathe queueing) + research.dm (CanResearch/ResearchTech).
using Content.Server.Lathe;
using Content.Server.Popups;
using Content.Shared._Oxyd.Moebius.Research;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Lathe;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Moebius.Research;

public sealed partial class OxydResearchSystem : EntitySystem
{
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private LatheSystem _lathe = default!;
    [Dependency] private SharedMaterialStorageSystem _matStorage = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Eris tech_points values per tree (destructive_analyzer.dm get_object_research_value).</summary>
    private static readonly Dictionary<string, int> TreePointValues = new()
    {
        ["OxydTreeEngineering"] = 250,
        ["OxydTreeBiotech"] = 300,
        ["OxydTreeCombat"] = 500,
        ["OxydTreePowerstorage"] = 300,
        ["OxydTreeBluespace"] = 1000,
        ["OxydTreeRobotics"] = 350,
        ["OxydTreeCovert"] = 5000,
    };

    public const string DiskSlot = "disk";
    public const string ToolSlot = "tool";
    public const string InputSlot = "input";

    /// <summary>Eris console device-link scan radius (~3 tiles, generous).</summary>
    private const float LinkRange = 6f;

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydRdConsoleComponent, MapInitEvent>(OnConsoleMapInit);
        SubscribeLocalEvent<OxydRdConsoleComponent, AfterActivatableUIOpenEvent>(OnConsoleUIOpen);
        SubscribeLocalEvent<OxydRdConsoleComponent, OxydRdScreenMessage>(OnScreenMsg);
        SubscribeLocalEvent<OxydRdConsoleComponent, OxydRdSelectTreeMessage>(OnSelectTree);
        SubscribeLocalEvent<OxydRdConsoleComponent, OxydRdSelectDeviceMessage>(OnSelectDevice);
        SubscribeLocalEvent<OxydRdConsoleComponent, OxydRdUnlockMessage>(OnUnlock);
        SubscribeLocalEvent<OxydRdConsoleComponent, OxydRdDeconstructMessage>(OnDeconstruct);
        SubscribeLocalEvent<OxydRdConsoleComponent, OxydRdEjectItemMessage>(OnEjectItem);
        SubscribeLocalEvent<OxydRdConsoleComponent, OxydRdRescanMessage>(OnRescan);
        SubscribeLocalEvent<OxydRdConsoleComponent, OxydRdPrintMessage>(OnPrint);
        SubscribeLocalEvent<OxydRdConsoleComponent, OxydRdDiskMessage>(OnDisk);
        SubscribeLocalEvent<OxydRdConsoleComponent, OxydRdUploadToolMessage>(OnUploadTool);
        InitializeDevices();
    }

    // ---- linkage (Eris rdconsole auto-link on init + "Rescan" verb) ----
    private void OnConsoleMapInit(Entity<OxydRdConsoleComponent> ent, ref MapInitEvent args)
        => RescanDevices(ent);

    private void OnConsoleUIOpen(Entity<OxydRdConsoleComponent> ent, ref AfterActivatableUIOpenEvent args)
        => UpdateUi(ent);

    private void OnRescan(Entity<OxydRdConsoleComponent> ent, ref OxydRdRescanMessage args)
    {
        RescanDevices(ent);
        UpdateUi(ent);
    }

    private void RescanDevices(Entity<OxydRdConsoleComponent> ent)
    {
        var xform = Transform(ent);
        var found = new HashSet<EntityUid>();
        foreach (var dev in EntityManager.EntityQuery<OxydRdLinkableComponent>())
        {
            var uid = dev.Owner;
            if (uid == ent.Owner || TerminatingOrDeleted(uid))
                continue;
            if (!xform.Coordinates.TryDistance(EntityManager, Transform(uid).Coordinates, out var dist) || dist > LinkRange)
                continue;
            found.Add(uid);
        }
        // keep manual links valid, drop vanished ones
        ent.Comp.LinkedDevices.RemoveWhere(u => TerminatingOrDeleted(u));
        ent.Comp.LinkedDevices.UnionWith(found);
        Dirty(ent);
        // Eris mechfab/protolathe sync: push every unlocked recipe to all linked lathes.
        foreach (var dev in ent.Comp.LinkedDevices)
            SyncLatheRecipes(ent, dev);
    }

    /// <summary>Push recipes for all unlocked nodes into a linked lathe
    /// (Eris lathes mirror console known_designs after sync).</summary>
    private void SyncLatheRecipes(Entity<OxydRdConsoleComponent> console, EntityUid device)
    {
        if (!TryComp<LatheComponent>(device, out var lathe))
            return;
        var changed = false;
        foreach (var nodeId in console.Comp.UnlockedNodes)
        {
            if (!_proto.TryIndex<OxydTechNodePrototype>(nodeId, out var node))
                continue;
            foreach (var recipe in node.UnlocksRecipes)
            {
                var pack = new ProtoId<LatheRecipePackPrototype>($"OxydPack{recipe.Id[6..]}");
                if (_proto.TryIndex(pack, out _) && !lathe.DynamicPacks.Contains(pack))
                {
                    lathe.DynamicPacks.Add(pack);
                    changed = true;
                }
            }
        }
        if (changed)
            Dirty(device, lathe);
    }

    // ---- screen / selection ----
    private void OnScreenMsg(Entity<OxydRdConsoleComponent> ent, ref OxydRdScreenMessage args)
    {
        ent.Comp.Screen = (int) args.Screen;
        Dirty(ent);
        UpdateUi(ent);
    }

    private void OnSelectTree(Entity<OxydRdConsoleComponent> ent, ref OxydRdSelectTreeMessage args)
    {
        _selectedTree[ent.Owner] = args.Tree;
        UpdateUi(ent);
    }

    private void OnSelectDevice(Entity<OxydRdConsoleComponent> ent, ref OxydRdSelectDeviceMessage args)
    {
        _selectedDevice[ent.Owner] = GetEntity(args.Device);
        UpdateUi(ent);
    }

    private readonly Dictionary<EntityUid, string> _selectedTree = new();
    private readonly Dictionary<EntityUid, EntityUid> _selectedDevice = new();

    // ---- tech unlock (Eris Research_Tech / CanResearch) ----
    private void OnUnlock(Entity<OxydRdConsoleComponent> ent, ref OxydRdUnlockMessage args)
    {
        if (!_proto.TryIndex<OxydTechNodePrototype>(args.Node, out var node))
            return;
        if (ent.Comp.UnlockedNodes.Contains(node.ID))
            return;
        foreach (var req in node.RequiredNodes)
        {
            if (!ent.Comp.UnlockedNodes.Contains(req.Id))
            {
                _popup.PopupEntity("Prerequisite research missing.", ent, args.Actor);
                return;
            }
        }
        if (ent.Comp.Points < node.Cost)
        {
            _popup.PopupEntity("Not enough research points.", ent, args.Actor);
            return;
        }
        ent.Comp.Points -= node.Cost;
        ent.Comp.UnlockedNodes.Add(node.ID);
        // Eris research.dm: Research_Tech bumps the tree level.
        ent.Comp.TreeLevels.TryGetValue(node.Tree, out var lvl);
        ent.Comp.TreeLevels[node.Tree] = lvl + 1;
        Dirty(ent);
        foreach (var dev in ent.Comp.LinkedDevices)
            SyncLatheRecipes(ent, dev);
        UpdateUi(ent);
    }

    // ---- remote print (Eris console queues designs on linked lathes) ----
    private void OnPrint(Entity<OxydRdConsoleComponent> ent, ref OxydRdPrintMessage args)
    {
        var dev = GetEntity(args.Device);
        if (!ent.Comp.LinkedDevices.Contains(dev) || !TryComp<LatheComponent>(dev, out var lathe))
            return;
        if (!_proto.TryIndex<LatheRecipePrototype>(args.Recipe, out var recipe))
            return;
        _lathe.TryAddToQueue(dev, recipe, Math.Clamp(args.Amount, 1, 25), lathe);
        UpdateUi(ent);
    }

    public void UpdateUi(Entity<OxydRdConsoleComponent> ent)
        => _ui.SetUiState(ent.Owner, OxydRdUiKey.Key, BuildState(ent));
}
