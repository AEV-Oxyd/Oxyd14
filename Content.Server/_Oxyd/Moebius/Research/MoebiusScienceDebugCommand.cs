// Eris Moebius research port: debug commands for live verification.
// Mirrors the LitanyDebugCommand pattern (colon subcommands, uid args).
// Used to drive paths the stock admin verbs can't: simulated blasts for the
// explosion watcher, lathe material feeds, forced node unlocks, point grants.
using Content.Server.Administration;
using Content.Shared.Administration;
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

    /// <summary>Simulate a blast on an explosion watcher: deals Blast damage so
    /// the watcher's DamageChanged handler banks points (real explosions need
    /// atmos+bombs; the banking path is identical).</summary>
    [CommandImplementation("blast")]
    public void Blast(IInvocationContext ctx, EntityUid watcher, int amount = 100)
    {
        _dmg ??= GetSys<DamageableSystem>();
        var spec = new DamageSpecifier();
        spec.DamageDict["Blast"] = amount;
        if (_dmg.TryChangeDamage(watcher, spec) != null)
            ctx.WriteLine($"moebius: dealt {amount} blast to {watcher}");
        else
            ctx.WriteLine($"moebius: watcher {watcher} has no DamageableComponent");
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
        if (!TryComp<OxydRdConsoleComponent>(console, out var comp))
        {
            ctx.WriteLine($"moebius: {console} is not an Oxyd R&D console");
            return;
        }
        comp.UnlockedNodes.Add(node);
        ctx.WriteLine($"moebius: unlocked {node} on {console}");
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
