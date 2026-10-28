using System.Collections.Generic;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Toolshed;

namespace Content.Server._Oxyd.AstersGuild;

/// <summary>
/// Debug commands for Asters Guild states the generic admin verbs can't reach in a
/// test client where BUI LineEdits can't be typed into (no keyboard focus). Lets QA
/// drive the order-request queue and bank from the server console. Dev/test only —
/// pattern follows LitanyDebugCommand (Content.Server/_Oxyd/NeoTheology).
/// </summary>
[ToolshedCommand, AdminCommand(AdminFlags.Debug)]
public sealed class GuildCommand : ToolshedCommand
{
    private OxydGuildTradeConsoleSystem? _trade;
    private Content.Server._Oxyd.Economy.OxydEconomySystem? _economy;

    /// <summary>Enqueue a crew order request as if a request console submitted it.</summary>
    [CommandImplementation("submitrequest")]
    public void SubmitRequest(IInvocationContext ctx, string item, int amount = 1)
    {
        _trade ??= GetSys<OxydGuildTradeConsoleSystem>();
        _trade.DebugSubmitRequest(item, amount, "crew console (debug)");
        ctx.WriteLine($"guild: submitted order request '{item}' x{amount}");
    }

    /// <summary>Credit the shared guild account (as if a sale/ATM deposit ran).</summary>
    [CommandImplementation("credit")]
    public void Credit(IInvocationContext ctx, int amount)
    {
        _economy ??= GetSys<Content.Server._Oxyd.Economy.OxydEconomySystem>();
        var guild = _economy.FindDepartmentAccount(Content.Shared._Oxyd.Economy.OxydEconomyConstants.EmployerGuild);
        if (guild == null) { ctx.WriteLine("guild: no department account"); return; }
        _economy.TryAdjust(guild.AccountNumber, amount, "Debug credit", "GuildDebug");
        ctx.WriteLine($"guild: credited {amount}cr; balance={guild.Balance}cr");
    }

    /// <summary>Print the shared guild account balance.</summary>
    [CommandImplementation("balance")]
    public void Balance(IInvocationContext ctx)
    {
        _economy ??= GetSys<Content.Server._Oxyd.Economy.OxydEconomySystem>();
        var guild = _economy.FindDepartmentAccount(Content.Shared._Oxyd.Economy.OxydEconomyConstants.EmployerGuild);
        ctx.WriteLine($"guild: balance={(guild?.Balance ?? 0)}cr");
    }

    /// <summary>Force all deep drills on/off (InteractHand can't reach stacked machines).</summary>
    [CommandImplementation("drill")]
    public void Drill(IInvocationContext ctx, string state)
    {
        var proc = GetSys<OxydDeepDrillSystem>();
        var on = state == "on";
        var n = proc.DebugSetDrillRunning(on);
        ctx.WriteLine($"guild: set Running={on} on {n} drill(s)");
    }
}
