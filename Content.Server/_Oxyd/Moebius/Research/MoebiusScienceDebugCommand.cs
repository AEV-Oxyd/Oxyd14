// Eris Moebius research port: debug commands for live verification.
// Mirrors the LitanyDebugCommand pattern (colon subcommands, uid args).
// Used to drive paths the stock admin verbs can't: simulated blasts for the
// explosion watcher, lathe material feeds, forced node unlocks, point grants.
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared._Oxyd.Moebius.Research;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Toolshed;
using Robust.Shared.Toolshed.Syntax;

namespace Content.Server._Oxyd.Moebius.Research;

[ToolshedCommand, AdminCommand(AdminFlags.Debug)]
public sealed class MoebiusScienceCommand : ToolshedCommand
{
    private DamageableSystem? _dmg;
    private SharedMaterialStorageSystem? _mat;
    private IPrototypeManager? _proto;

    /// <summary>Anchors an entity in place (watcher/console verification).</summary>
    [CommandImplementation("anchor")]
    public void Anchor(IInvocationContext ctx, EntityUid ent)
    {
        Transform(ent).Anchored = true;
        ctx.WriteLine($"moebius: anchored {ent}");
    }

    /// <summary>Simulate a blast on an explosion watcher: deals Blast damage so
    /// the watcher's DamageChanged handler banks points (real explosions need
    /// atmos+bombs; the banking path is identical).</summary>
    [CommandImplementation("blast")]
    public void Blast(IInvocationContext ctx, EntityUid watcher, int amount = 100)
    {
        _dmg ??= GetSys<DamageableSystem>();
        var spec = new DamageSpecifier();
        spec.DamageDict["Structural"] = amount; // explosions deal Structural to inorganic objects
        var done = _dmg.ChangeDamage(watcher, spec);
        var banked = TryComp<OxydExplosionWatcherComponent>(watcher, out var w) ? w.BankedPoints : -1;
        ctx.WriteLine($"moebius: dealt {done.GetTotal()} blast to {watcher}, bankedPoints={banked}");
    }

    /// <summary>Load a linked lathe with steel+glass so Queue can be exercised
    /// from the console BUI without dragging stacks.</summary>
    [CommandImplementation("feed")]
    public void Feed(IInvocationContext ctx, EntityUid lathe, int amount = 1000)
    {
        _mat ??= GetSys<SharedMaterialStorageSystem>();
        var ok = _mat.TryChangeMaterialAmount(lathe, "Steel", amount)
              && _mat.TryChangeMaterialAmount(lathe, "Glass", amount);
        ctx.WriteLine(ok ? $"moebius: fed {amount} steel+glass to {lathe}"
                         : $"moebius: {lathe} has no MaterialStorageComponent");
    }

    /// <summary>Force-unlock a tech node on a console (skips point cost and
    /// prereqs) so gated packs can be checked on linked lathes.</summary>
    [CommandImplementation("unlock")]
    public void Unlock(IInvocationContext ctx, EntityUid console, string node)
    {
        if (!TryComp<OxydRdConsoleComponent>(console, out _))
        {
            ctx.WriteLine($"moebius: {console} is not an Oxyd R&D console");
            return;
        }
        GetSys<OxydResearchSystem>().DebugForceUnlock(console, node);
        ctx.WriteLine($"moebius: unlocked {node} on {console} + synced linked lathes");
    }

    /// <summary>Raise UseInHandEvent on an item as if `user` activated it in-hand
    /// (mental imprinter, drone shell deploy).</summary>
    [CommandImplementation("use")]
    public void Use(IInvocationContext ctx, EntityUid item, EntityUid user)
    {
        EntityManager.EventBus.RaiseLocalEvent(item, new UseInHandEvent(user));
        ctx.WriteLine($"moebius: UseInHand {item} by {user}");
    }

    /// <summary>Raise ActivateInWorldEvent on a target (drone fabricator cycle).</summary>
    [CommandImplementation("activate")]
    public void Activate(IInvocationContext ctx, EntityUid target, EntityUid user)
    {
        EntityManager.EventBus.RaiseLocalEvent(target, new ActivateInWorldEvent(user, target, false));
        ctx.WriteLine($"moebius: Activate {target} by {user}");
    }

    /// <summary>Raise AfterInteractUsingEvent on an item against a target
    /// (slime extractor on slime, science tool on mob).</summary>
    [CommandImplementation("useon")]
    public void UseOn(IInvocationContext ctx, EntityUid item, EntityUid target, EntityUid user)
    {
        EntityManager.EventBus.RaiseLocalEvent(item, new AfterInteractUsingEvent(user, item, target, Transform(target).Coordinates, true));
        ctx.WriteLine($"moebius: UseOn {item} -> {target} by {user}");
    }

    /// <summary>Grant research points to a console.</summary>
    [CommandImplementation("points")]
    public void Points(IInvocationContext ctx, EntityUid console, int amount)
    {
        if (!TryComp<OxydRdConsoleComponent>(console, out var comp))
        {
            ctx.WriteLine($"moebius: {console} is not an Oxyd R&D console");
            return;
        }
        comp.Points += amount;
        ctx.WriteLine($"moebius: {console} now has {comp.Points} points");
    }
}
