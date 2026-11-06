// Eris Moebius research port: R&D console logic.
// Eris source: code/modules/research/rdconsole.dm (Topic commands, link/unlink,
// tech unlock, lathe queueing) + research.dm (CanResearch/ResearchTech).
using Content.Server.Lathe;
using Content.Server.Popups;
using Content.Shared._Oxyd.Moebius.Research;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Components;
using Content.Shared.DeviceLinking.Systems;
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
    [Dependency] private DeviceLinkSystem _deviceLink = default!;

    public const string DiskSlot = "disk";
    public const string ToolSlot = "tool";
    public const string InputSlot = "input";

    /// <summary>Eris console device-link port pair (range lives on
    /// DeviceLinkSourceComponent in research_machines.yml).</summary>
    private static readonly ProtoId<SourcePortPrototype> RdSourcePort = "OxydRdConsole";
    private static readonly ProtoId<SinkPortPrototype> RdSinkPort = "OxydRdDevice";

    // ---- linkage (Eris rdconsole auto-link on init + "Rescan" verb) ----
    [SubscribeLocalEvent]
    private void OnConsoleMapInit(Entity<OxydRdConsoleComponent> ent, ref MapInitEvent args)
        => RescanDevices(ent);

    [SubscribeLocalEvent]
    private void OnConsoleUIOpen(Entity<OxydRdConsoleComponent> ent, ref AfterActivatableUIOpenEvent args)
        => UpdateUi(ent);

    [SubscribeLocalEvent]
    private void OnRescan(Entity<OxydRdConsoleComponent> ent, ref OxydRdRescanMessage args)
    {
        RescanDevices(ent);
        UpdateUi(ent);
    }

    /// <summary>Eris rdconsole auto-link on init + "Rescan" verb: link every
    /// R&D device within the console's DeviceLinkSource range. Links persist
    /// until the device is deleted (DeviceLinkSystem cleanup) — same union
    /// semantics as the old LinkedDevices set.</summary>
    private void RescanDevices(Entity<OxydRdConsoleComponent> ent)
    {
        if (!TryComp<DeviceLinkSourceComponent>(ent.Owner, out var source))
            return;

        var xform = Transform(ent);
        var query = EntityQueryEnumerator<DeviceLinkSinkComponent, OxydRdLinkableComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var sink, out _, out var sxform))
        {
            if (uid == ent.Owner || TerminatingOrDeleted(uid))
                continue;
            if (!xform.Coordinates.TryDistance(EntityManager, sxform.Coordinates, out var dist) || dist > source.Range)
                continue;
            _deviceLink.SaveLinks(null, (ent.Owner, source), (uid, sink),
                [new DeviceLink(RdSourcePort, RdSinkPort)]);
        }

        // Eris mechfab/protolathe sync: push every unlocked recipe to all linked lathes.
        foreach (var dev in GetLinkedDevices(ent.Owner))
            SyncLatheRecipes(ent, dev);
    }

    /// <summary>Devices currently linked to this console via DeviceLinkSystem.</summary>
    private HashSet<EntityUid> GetLinkedDevices(EntityUid console)
    {
        if (!TryComp<DeviceLinkSourceComponent>(console, out var source))
            return [];
        return _deviceLink.GetLinkedSinks((console, source), RdSourcePort);
    }

    /// <summary>True when <paramref name="device"/> is device-linked to this console.</summary>
    private bool IsLinkedToConsole(EntityUid console, EntityUid device)
        => TryComp<DeviceLinkSourceComponent>(console, out var source) &&
           _deviceLink.GetLinkedSinks((console, source), RdSourcePort).Contains(device);

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
            // whole stock packs (Eris mechfab design categories: borg modules, mech parts)
            foreach (var packId in node.UnlocksPacks)
            {
                if (!lathe.DynamicPacks.Contains(packId))
                {
                    lathe.DynamicPacks.Add(packId);
                    changed = true;
                }
            }
        }
        if (changed)
            Dirty(device, lathe);
    }

    /// <summary>Debug-command helper: force-unlock a node and sync every
    /// linked lathe (moebiusresearch:unlock — same effect as the BUI unlock
    /// without point cost or prereq checks).</summary>
    public void DebugForceUnlock(EntityUid console, string nodeId)
    {
        if (!TryComp<OxydRdConsoleComponent>(console, out var comp))
            return;
        comp.UnlockedNodes.Add(nodeId);
        Dirty(console, comp);
        foreach (var dev in GetLinkedDevices(console))
            SyncLatheRecipes((console, comp), dev);
    }

    // ---- screen / selection ----
    [SubscribeLocalEvent]
    private void OnScreenMsg(Entity<OxydRdConsoleComponent> ent, ref OxydRdScreenMessage args)
    {
        ent.Comp.Screen = (int) args.Screen;
        Dirty(ent);
        UpdateUi(ent);
    }

    [SubscribeLocalEvent]
    private void OnSelectTree(Entity<OxydRdConsoleComponent> ent, ref OxydRdSelectTreeMessage args)
    {
        ent.Comp.SelectedTree = args.Tree;
        UpdateUi(ent);
    }

    [SubscribeLocalEvent]
    private void OnSelectDevice(Entity<OxydRdConsoleComponent> ent, ref OxydRdSelectDeviceMessage args)
    {
        ent.Comp.SelectedDevice = GetEntity(args.Device);
        UpdateUi(ent);
    }

    // ---- tech unlock (Eris Research_Tech / CanResearch) ----
    [SubscribeLocalEvent]
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
        foreach (var dev in GetLinkedDevices(ent.Owner))
            SyncLatheRecipes(ent, dev);
        UpdateUi(ent);
    }

    // ---- remote print (Eris console queues designs on linked lathes) ----
    [SubscribeLocalEvent]
    private void OnPrint(Entity<OxydRdConsoleComponent> ent, ref OxydRdPrintMessage args)
    {
        var dev = GetEntity(args.Device);
        if (!IsLinkedToConsole(ent.Owner, dev) || !TryComp<LatheComponent>(dev, out var lathe))
            return;
        if (!_proto.TryIndex<LatheRecipePrototype>(args.Recipe, out var recipe))
            return;
        _lathe.TryAddToQueue(dev, recipe, Math.Clamp(args.Amount, 1, 25), lathe);
        UpdateUi(ent);
    }

    public void UpdateUi(Entity<OxydRdConsoleComponent> ent)
        => _ui.SetUiState(ent.Owner, OxydRdUiKey.Key, BuildState(ent));
}
