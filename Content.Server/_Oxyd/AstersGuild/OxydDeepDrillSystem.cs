using Content.Shared._Oxyd.AstersGuild;
using Content.Shared.Interaction;
using Robust.Server.GameObjects;

namespace Content.Server._Oxyd.AstersGuild;

/// <summary>Eris deep drill. Ore smelting is the stock lathe, not this system.</summary>
public sealed partial class OxydDeepDrillSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private AppearanceSystem _appearance = default!;

    [SubscribeLocalEvent]
    private void OnDrillInteract(Entity<OxydDeepDrillComponent> ent, ref InteractHandEvent args)
    {
        ent.Comp.Running = !ent.Comp.Running;
        _appearance.SetData(ent, OxydGuildVisuals.Working, ent.Comp.Running);
        args.Handled = true;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<OxydDeepDrillComponent>();
        while (query.MoveNext(out var uid, out var drill))
        {
            if (!drill.Running)
                continue;

            drill.Accum += frameTime;
            if (drill.Accum < drill.OreEvery)
                continue;

            drill.Accum = 0;
            if (!RockNearby(uid))
                continue;

            var ores = new[] { "SteelOre", "UraniumOre", "PlasmaOre", "GoldOre", "SilverOre", "Coal" };
            Spawn(ores[Random.Shared.Next(ores.Length)], Transform(uid).Coordinates);
        }
    }

    public int DebugSetDrillRunning(bool running)
    {
        var count = 0;
        var query = EntityQueryEnumerator<OxydDeepDrillComponent>();
        while (query.MoveNext(out var uid, out var drill))
        {
            drill.Running = running;
            _appearance.SetData(uid, OxydGuildVisuals.Working, running);
            count++;
        }

        return count;
    }

    private bool RockNearby(EntityUid uid)
    {
        foreach (var ent in _lookup.GetEntitiesInRange(uid, 1.5f))
        {
            if (ent == uid)
                continue;

            if (TryComp(ent, out MetaDataComponent? meta) && meta.EntityPrototype?.ID is { } id &&
                (id.Contains("Rock") || id.Contains("Asteroid")))
                return true;
        }

        return false;
    }
}
