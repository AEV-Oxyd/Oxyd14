using Content.Server.Administration;
using Content.Shared._Oxyd.Technomancer;
using Content.Shared.Administration;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Toolshed;

namespace Content.Server._Oxyd.Technomancer;

/// <summary>
/// Debug layout commands for the Technomancer port live-verification matrix:
/// stock spawn:on stacks ~4 copies of everything on one tile under the mob
/// sprite, so these lay each prototype out on distinct tiles instead.
/// Dev/test only — colon subcommands like litany:giveoddity.
/// </summary>
[ToolshedCommand, AdminCommand(AdminFlags.Debug)]
public sealed class TechnomancerCommand : ToolshedCommand
{
    private static readonly EntProtoId[] Machines =
    [
        "OxydSmesUnit", "OxydBreakerBox", "OxydPulsarConsole",
        "OxydPulsarPowerBridge", "OxydPulsarFuelTank", "OxydShipShieldGenerator",
        "OxydRconConsole", "OxydAtmosConsole", "OxydAirAlarm",
        "OxydCanister", "OxydOmniFilter", "OxydOmniMixer",
        "OxydTermoPump", "OxydTermoVolumePump", "OxydPassiveValve",
        "OxydNanoforge",
    ];

    private static readonly EntProtoId[] Items =
    [
        "OxydDuctTape", "OxydCombiDriver", "OxydArmatureCutter",
        "OxydPneumaticCrowbar", "OxydPoweredHammer", "OxydWelderAdv",
        "OxydToolModComplicator", "OxydRapidPipeDispenser",
        "OxydRapidConstructionDevice", "OxydDesignDiskArk",
        "OxydDesignDiskIjiro", "OxydCompressedMatterSingle",
    ];

    /// <summary>
    /// technomancer:grid — spawn every ported machine in a spaced row on the
    /// mob's grid, one per tile, so right-click/Toggle UI works on each.
    /// </summary>
    [CommandImplementation("grid")]
    public void Grid(IInvocationContext ctx)
    {
        var uid = ExecutingEntity(ctx)
            ?? throw new InvalidOperationException("no executing entity");
        var coords = Transform(uid).Coordinates;
        var x = coords.X;
        var y = coords.Y;
        for (var i = 0; i < Machines.Length; i++)
        {
            var at = new EntityCoordinates(coords.EntityId, x + (i % 8) * 2f, y + 1.5f + (i / 8) * 2f);
            var ent = Spawn(Machines[i], at);
            ctx.WriteLine($"{Machines[i]} -> {ent}");
        }
    }

    /// <summary>technomancer:items — the handheld gear row.</summary>
    [CommandImplementation("items")]
    public void SpawnItems(IInvocationContext ctx)
    {
        var uid = ExecutingEntity(ctx)
            ?? throw new InvalidOperationException("no executing entity");
        var coords = Transform(uid).Coordinates;
        for (var i = 0; i < Items.Length; i++)
        {
            var at = new EntityCoordinates(coords.EntityId, coords.X + i * 1.5f, coords.Y - 1.5f);
            var ent = Spawn(Items[i], at);
            ctx.WriteLine($"{Items[i]} -> {ent}");
        }
    }
}
