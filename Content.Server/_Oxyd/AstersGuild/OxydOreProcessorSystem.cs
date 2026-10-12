using Content.Server.UserInterface;
using Content.Shared._Oxyd.AstersGuild;
using Content.Shared.Interaction;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.AstersGuild;

/// <summary>
/// Ports Eris machinery/mineral/processing_unit + processing_unit_console
/// (code/modules/mining/machine_processing.dm): ores loaded into the processor smelt into
/// material sheets on a timer; the console (TGUI "Processor" on Eris, ErisNanoStyle here)
/// shows loaded materials w/ Storing toggles + the Alloy Menu.
/// Also ports machinery/mining/deep_drill (drilling/deep_drill.dm): ejects an ore stack
/// every OreEvery seconds while on.
/// </summary>
public sealed partial class OxydOreProcessorSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    /// <summary>Ore EntProtoId -> sheet EntProtoId produced.</summary>
    private static readonly Dictionary<string, string> OreToSheet = new()
    {
        ["SteelOre"] = "SheetSteel1",
        ["UraniumOre"] = "SheetUranium1",
        ["PlasmaOre"] = "SheetPlasma1",
        ["GoldOre"] = "IngotGold1",
        ["SilverOre"] = "IngotSilver1",
        ["Coal"] = "Coal1",
    };

    /// <summary>Alloy Menu (Eris alloys list): output stack of mixed sheets.</summary>
    private static readonly Dictionary<string, string> Alloys = new()
    {
        ["Plasteel"] = "SheetPlasteel1",
        ["Steel"] = "SheetSteel1",
        ["Borosilicate glass"] = "SheetRPGlass1",
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydOreProcessorConsoleComponent, MapInitEvent>(OnConsoleInit);
        SubscribeLocalEvent<OxydOreProcessorConsoleComponent, OxydOreProcessorToggleMessage>(OnToggle);
        SubscribeLocalEvent<OxydOreProcessorConsoleComponent, OxydOreProcessorStoreMessage>(OnStore);
        SubscribeLocalEvent<OxydOreProcessorConsoleComponent, OxydOreProcessorAlloyMessage>(OnAlloy);
        SubscribeLocalEvent<OxydOreProcessorConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<OxydOreProcessorComponent, InteractUsingEvent>(OnProcessorInteract);
        SubscribeLocalEvent<OxydDeepDrillComponent, InteractHandEvent>(OnDrillInteract);
    }

    /// <summary>Eris attack_hand toggles the drill (deep_drill.dm).</summary>
    private void OnDrillInteract(EntityUid uid, OxydDeepDrillComponent d, InteractHandEvent args)
    {
        d.Running = !d.Running;
        _appearance.SetData(uid, OxydGuildVisuals.Working, d.Running);
        args.Handled = true;
    }

    /// <summary>Eris LateInitialize: console finds the closest processor within 3 tiles.</summary>
    private void OnConsoleInit(EntityUid uid, OxydOreProcessorConsoleComponent comp, ref MapInitEvent args)
        => TryPair(uid, comp);

    /// <summary>Re-pair on open if the unit was spawned/moved after the console initialized.</summary>
    private void OnOpened(EntityUid uid, OxydOreProcessorConsoleComponent comp, BoundUIOpenedEvent args)
    {
        if (comp.Machine is not { } m || Deleted(m))
            TryPair(uid, comp);
        PushState(uid, comp);
    }

    private void TryPair(EntityUid uid, OxydOreProcessorConsoleComponent comp)
    {
        var pos = Transform(uid).Coordinates;
        EntityUid? best = null;
        var bestDist = float.MaxValue;
        var q = EntityQueryEnumerator<OxydOreProcessorComponent>();
        while (q.MoveNext(out var puid, out _))
        {
            var d = Transform(puid).Coordinates.TryDistance(EntityManager, pos, out var dist) ? dist : float.MaxValue;
            if (d < bestDist && d <= 3f)
            {
                bestDist = d; best = puid;
            }
        }
        comp.Machine = best;
    }

    /// <summary>Insert ore items (Eris attackby with ore).</summary>
    private void OnProcessorInteract(EntityUid uid, OxydOreProcessorComponent comp, InteractUsingEvent args)
    {
        if (!TryComp(args.Used, out MetaDataComponent? md) || md.EntityPrototype?.ID is not { } proto)
            return;
        if (!OreToSheet.ContainsKey(proto))
            return;
        comp.Queue.Add(proto);
        Del(args.Used);
        args.Handled = true;
    }

    private void OnToggle(EntityUid uid, OxydOreProcessorConsoleComponent comp, OxydOreProcessorToggleMessage _)
    {
        if (comp.Machine is { } m && TryComp(m, out OxydOreProcessorComponent? p))
        {
            p.Running = !p.Running;
            _appearance.SetData(m, OxydGuildVisuals.Working, p.Running);
        }
        PushState(uid, comp);
    }

    private void OnStore(EntityUid uid, OxydOreProcessorConsoleComponent comp, OxydOreProcessorStoreMessage msg)
    {
        if (comp.Machine is { } m && TryComp(m, out OxydOreProcessorComponent? p))
            p.Storing[msg.Material] = !p.Storing.GetValueOrDefault(msg.Material);
        PushState(uid, comp);
    }

    /// <summary>Eris Alloy Menu: spawn the alloy sheet at the processor.</summary>
    private void OnAlloy(EntityUid uid, OxydOreProcessorConsoleComponent comp, OxydOreProcessorAlloyMessage msg)
    {
        if (comp.Machine is not { } m || !Alloys.TryGetValue(msg.Alloy, out var sheet)) return;
        Spawn(sheet, Transform(m).Coordinates);
        PushState(uid, comp);
    }

    private void PushState(EntityUid uid, OxydOreProcessorConsoleComponent comp)
    {
        if (!_ui.IsUiOpen(uid, OxydOreProcessorUiKey.Key))
            return;
        var s = new OxydOreProcessorState { HasMachine = comp.Machine != null };
        if (comp.Machine is { } m && TryComp(m, out OxydOreProcessorComponent? p))
        {
            s.Running = p.Running;
            var counts = new Dictionary<string, int>();
            foreach (var o in p.Queue)
                counts[o] = counts.GetValueOrDefault(o) + 1;
            foreach (var ore in OreToSheet.Keys)
            {
                counts.TryGetValue(ore, out var n);
                s.Materials.Add(new OxydOreProcessorMaterial
                {
                    Name = ore, Loaded = n,
                    Storing = p.Storing.GetValueOrDefault(ore, true),
                });
            }
            s.Alloys.AddRange(Alloys.Keys);
        }
        _ui.SetUiState(uid, OxydOreProcessorUiKey.Key, s);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var pq = EntityQueryEnumerator<OxydOreProcessorComponent>();
        while (pq.MoveNext(out var uid, out var p))
        {
            if (!p.Running || p.Queue.Count == 0) continue;
            p.Accum += frameTime;
            if (p.Accum < p.CycleSeconds) continue;
            p.Accum = 0;
            var ore = p.Queue[0];
            p.Queue.RemoveAt(0);
            if (OreToSheet.TryGetValue(ore, out var sheet))
                Spawn(sheet, Transform(uid).Coordinates);
        }
        var dq = EntityQueryEnumerator<OxydDeepDrillComponent>();
        while (dq.MoveNext(out var uid, out var d))
        {
            if (!d.Running) continue;
            d.Accum += frameTime;
            if (d.Accum < d.OreEvery) continue;
            d.Accum = 0;
            // Eris deep_drill only digs into cave/asteroid ground (cave_generator.dm):
            // require a rock/asteroid wall in reach before the head bites ore out.
            if (!RockNearby(uid)) continue;
            var ores = new[] { "SteelOre", "UraniumOre", "PlasmaOre", "GoldOre", "SilverOre", "Coal" };
            Spawn(ores[Random.Shared.Next(ores.Length)], Transform(uid).Coordinates);
        }
    }

    /// <summary>Debug hook (guild:drill): force every drill's Running state — the
    /// InteractHand toggle can't be reached when machines are stacked for testing.</summary>
    public int DebugSetDrillRunning(bool running)
    {
        var n = 0;
        var q = EntityQueryEnumerator<OxydDeepDrillComponent>();
        while (q.MoveNext(out var u, out var d))
        {
            d.Running = running;
            _appearance.SetData(u, OxydGuildVisuals.Working, running);
            n++;
        }
        return n;
    }

    /// <summary>Cave-generator stand-in (Eris: drill needs cave worldgen beneath) — any
    /// entity whose proto id contains Rock/Asteroid within ~1.5 tiles counts as ground
    /// the drill can bite into (WallRock*/asteroid walls).</summary>
    private bool RockNearby(EntityUid uid)
    {
        foreach (var ent in _lookup.GetEntitiesInRange(uid, 1.5f))
        {
            if (ent == uid) continue;
            if (TryComp(ent, out MetaDataComponent? md) && md.EntityPrototype?.ID is { } id &&
                (id.Contains("Rock") || id.Contains("Asteroid")))
                return true;
        }
        return false;
    }
}
