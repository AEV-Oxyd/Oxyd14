using System.Collections.Generic;
using Content.IntegrationTests.Pair;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

internal static class NeoTheologyTestMap
{
    /// <summary>Provide floor tiles for machines outside the map's initial tile.</summary>
    public static async Task<TestMapData> CreateMachineTestMap(this TestPair pair)
    {
        var map = await pair.CreateTestMap();
        await pair.Server.WaitPost(() =>
        {
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -5; x <= 5; x++)
            for (var y = -5; y <= 5; y++)
                tiles.Add((new Vector2i(x, y), map.Tile.Tile));
            pair.Server.EntMan.System<SharedMapSystem>().SetTiles(map.Grid, tiles);
        });
        return map;
    }
}
