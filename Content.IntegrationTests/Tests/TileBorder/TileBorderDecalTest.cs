using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Decals;
using Content.Server._Oxyd.TileBorder;
using Content.Shared._Oxyd.TileBorder;
using Content.Shared.Decals;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.TileBorder;

[TestFixture]
public sealed class TileBorderDecalTest : GameTest
{
    // Avoid a connected pair: ticking without a round trips Oxyd ChatSystem.Update
    // (LanguageDataCoreComponent is only spawned on RoundStartingEvent).
    public override PoolSettings PoolSettings => new() { Connected = false };

    [Test]
    public async Task PrototypesExistForEveryBorderSpriteState()
    {
        var server = Pair.Server;
        await server.WaitAssertion(() =>
        {
            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var protos = server.ResolveDependency<IPrototypeManager>();

            foreach (var def in tiles)
            {
                if (def is not ContentTileDefinition content || content.BorderSprites == null)
                    continue;

                foreach (var state in TileBorderDecals.States)
                {
                    var id = TileBorderDecals.PrototypeId(content.BorderSprites.Value, state);
                    Assert.That(protos.TryIndex<DecalPrototype>(id, out _), $"Missing decal prototype {id}");
                }
            }
        });
    }

    [Test]
    public async Task InteriorSkips_EdgeEmitsCardinal_IsolatedEmitsFullRim()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap(initialized: true);
        var grid = map.Grid;

        await server.WaitPost(() =>
        {
            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var maps = server.System<SharedMapSystem>();
            var steel = new Tile(tiles["FloorSteel"].TileId);
            var plating = new Tile(tiles["Plating"].TileId);

            for (var x = 0; x <= 4; x++)
            {
                for (var y = 0; y <= 4; y++)
                {
                    maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), plating);
                }
            }

            for (var x = 1; x <= 3; x++)
            {
                for (var y = 1; y <= 3; y++)
                {
                    maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), steel);
                }
            }

            server.System<TileBorderSystem>().Update(0f);
        });

        await server.WaitAssertion(() =>
        {
            var decals = server.System<DecalSystem>();
            var center = GeneratedAt(decals, grid.Owner, new Vector2i(2, 2));
            Assert.That(center, Is.Empty, "Interior steel tile must have no rim decals");

            var northEdge = GeneratedAt(decals, grid.Owner, new Vector2i(2, 3));
            Assert.That(northEdge, Is.EqualTo(new[] { "TileBorder-tiles_steel-n" }));
        });
    }

    [Test]
    public async Task TileChangeRebuildsNeighboursAndPreservesMapperDecals()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap(initialized: true);
        var grid = map.Grid;

        await server.WaitPost(() =>
        {
            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var maps = server.System<SharedMapSystem>();
            var decals = server.System<DecalSystem>();
            var steel = new Tile(tiles["FloorSteel"].TileId);

            for (var x = 0; x < 3; x++)
            {
                for (var y = 0; y < 3; y++)
                {
                    maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), steel);
                }
            }

            Assert.That(decals.TryAddDecal(
                "WoodTrimThinBox",
                new EntityCoordinates(grid.Owner, new Vector2(1, 1)),
                out _,
                zIndex: 0,
                cleanable: false));

            server.System<TileBorderSystem>().Update(0f);

            maps.SetTile(grid.Owner, grid.Comp, new Vector2i(1, 1), new Tile(tiles["FloorWhite"].TileId));
            server.System<TileBorderSystem>().Update(0f);
        });

        await server.WaitAssertion(() =>
        {
            var decals = server.System<DecalSystem>();
            var center = GeneratedAt(decals, grid.Owner, new Vector2i(1, 1));
            Assert.That(center, Does.Contain("TileBorder-tiles_white-n"));
            Assert.That(center, Does.Not.Contain("TileBorder-tiles_steel-n"));

            var neighbour = GeneratedAt(decals, grid.Owner, new Vector2i(1, 2));
            Assert.That(neighbour, Does.Contain("TileBorder-tiles_steel-s"));

            var mapper = decals.GetDecalsIntersecting(grid.Owner, new Box2(new Vector2(1, 1), new Vector2(2, 2)))
                .Where(d => d.Decal.Id == "WoodTrimThinBox")
                .ToList();
            Assert.That(mapper, Is.Not.Empty, "Mapper wood trim must survive rim rebuild");
        });
    }

    [Test]
    public async Task SpaceClearsGeneratedRimsOnThatTile()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap(initialized: true);
        var grid = map.Grid;

        await server.WaitPost(() =>
        {
            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var maps = server.System<SharedMapSystem>();
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, new Tile(tiles["FloorSteel"].TileId));
            server.System<TileBorderSystem>().Update(0f);
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, Tile.Empty);
            server.System<TileBorderSystem>().Update(0f);
        });

        await server.WaitAssertion(() =>
        {
            var decals = server.System<DecalSystem>();
            Assert.That(GeneratedAt(decals, grid.Owner, Vector2i.Zero), Is.Empty);
        });
    }

    [Test]
    public async Task SerializerOmitsGeneratedRims()
    {
        var server = Pair.Server;
        await server.WaitAssertion(() =>
        {
            var seri = server.ResolveDependency<ISerializationManager>();
            var deps = server.ResolveDependency<IDependencyCollection>();
            var dict = new Dictionary<ushort, Decal>
            {
                [0] = new Decal(Vector2.Zero, "TileBorder-tiles_steel-n", null, Angle.Zero, -1, false),
                [1] = new Decal(new Vector2(1, 1), "WoodTrimThinBox", null, Angle.Zero, 0, false),
            };

            var node = new DecalChunkDecalsSerializer().Write(seri, dict, deps);
            var yaml = node.ToString();
            Assert.That(yaml, Does.Contain("WoodTrimThinBox"));
            Assert.That(yaml, Does.Not.Contain("TileBorder-"));
        });
    }

    private static string[] GeneratedAt(DecalSystem decals, EntityUid grid, Vector2i tile)
    {
        var origin = new Vector2(tile.X, tile.Y);
        var pad = new Vector2(0.01f);
        return decals.GetDecalsIntersecting(grid, new Box2(origin - pad, origin + pad))
            .Where(d => TileBorderDecals.IsGenerated(d.Decal.Id))
            .Select(d => d.Decal.Id)
            .ToArray();
    }
}
