using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Decals;
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
    public async Task PrototypesExistForEveryCanonicalMask()
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

                if (!content.BorderRotate)
                {
                    // Absolute cardinal art (e.g. Eris lattices): only dir_sum 00..0f is baked.
                    for (byte dirSum = 0; dirSum < 16; dirSum++)
                    {
                        var id = TileBorderDecals.PrototypeId(content.BorderSprites.Value, dirSum);
                        Assert.That(protos.TryIndex<DecalPrototype>(id, out _), $"Missing decal prototype {id}");
                    }

                    continue;
                }

                foreach (var mask in TileBorderDecals.AllCanonicalMasks)
                {
                    var id = TileBorderDecals.PrototypeId(content.BorderSprites.Value, mask);
                    Assert.That(protos.TryIndex<DecalPrototype>(id, out _), $"Missing decal prototype {id}");
                }
            }
        });
    }

    [Test]
    public async Task InteriorSkips_EdgeEmitsCanonical_IsolatedEmitsZero()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap(initialized: true);
        var grid = map.Grid;

        // Event-immediate: TileChanged rebuilds without Update.
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
        });

        await server.WaitAssertion(() =>
        {
            var decals = server.System<DecalSystem>();
            var center = GeneratedAt(decals, grid.Owner, new Vector2i(2, 2));
            Assert.That(center, Is.Empty, "Interior steel tile must have no rim decals");

            // North edge of 3x3 steel: mask 0xC7 → canonical 0x1f (3 CW turns)
            var northEdge = GeneratedAt(decals, grid.Owner, new Vector2i(2, 3));
            Assert.That(northEdge, Is.EqualTo(new[] { "TileBorder-tiles_steel-1f" }));
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

            maps.SetTile(grid.Owner, grid.Comp, new Vector2i(1, 1), new Tile(tiles["FloorWhite"].TileId));
        });

        await server.WaitAssertion(() =>
        {
            var decals = server.System<DecalSystem>();
            // Isolated white (all 8 neighbours other group): mask 0 → tiles_white-00
            var center = GeneratedAt(decals, grid.Owner, new Vector2i(1, 1));
            Assert.That(center, Does.Contain("TileBorder-tiles_white-00"));
            Assert.That(center.Any(id => id.StartsWith("TileBorder-tiles_steel-")), Is.False);

            // Neighbour (1,2): mask 0xC6 → canonical 0x1b
            var neighbour = GeneratedAt(decals, grid.Owner, new Vector2i(1, 2));
            Assert.That(neighbour, Does.Contain("TileBorder-tiles_steel-1b"));

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
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, Tile.Empty);
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
                [0] = new Decal(Vector2.Zero, "TileBorder-tiles_steel-0a", null, Angle.Zero, -1, false),
                [1] = new Decal(new Vector2(1, 1), "WoodTrimThinBox", null, Angle.Zero, 0, false),
            };

            var node = new DecalChunkDecalsSerializer().Write(seri, dict, deps);
            var yaml = node.ToString();
            Assert.That(yaml, Does.Contain("WoodTrimThinBox"));
            Assert.That(yaml, Does.Not.Contain("TileBorder-"));
        });
    }

    [Test]
    public async Task LatticeIsSpace_EmitsTileBorderOnFreshPlace()
    {
        // Evidence: IsSpace exception lets TileBorder-lattices-* persist on Lattice.
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap(initialized: true);
        var grid = map.Grid;

        await server.WaitPost(() =>
        {
            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var maps = server.System<SharedMapSystem>();
            var lattice = new Tile(tiles["Lattice"].TileId);

            maps.SetTile(grid.Owner, grid.Comp, new Vector2i(1, 1), lattice);
            maps.SetTile(grid.Owner, grid.Comp, new Vector2i(2, 1), lattice);
        });

        await server.WaitAssertion(() =>
        {
            var decals = server.System<DecalSystem>();
            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var def = (ContentTileDefinition) tiles["Lattice"];
            Assert.That(def.MapAtmosphere, Is.True);
            Assert.That(def.BorderRotate, Is.False);

            var left = GeneratedAt(decals, grid.Owner, new Vector2i(1, 1));
            var right = GeneratedAt(decals, grid.Owner, new Vector2i(2, 1));
            Assert.That(left, Does.Contain("TileBorder-lattices-04"),
                $"left expected E-connected 04, got [{string.Join(',', left)}]");
            Assert.That(right, Does.Contain("TileBorder-lattices-08"),
                $"right expected W-connected 08, got [{string.Join(',', right)}]");

            Assert.That(decals.TryAddDecal(
                "TileBorder-lattices-00",
                new EntityCoordinates(grid.Owner, new Vector2(5, 5)),
                out _,
                zIndex: TileBorderDecals.ZIndex,
                cleanable: false), Is.True);

            // Empty tile at (5,5) is space — ordinary decal must still fail.
            Assert.That(decals.TryAddDecal(
                "WoodTrimThinBox",
                new EntityCoordinates(grid.Owner, new Vector2(5, 5)),
                out _,
                zIndex: 0,
                cleanable: false), Is.False);
        });
    }


    [Test]
    public async Task LatticeLinksTowardNonSpaceFloor()
    {
        // Eris parity: lattice dir_sum includes non-space solid neighbours.
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap(initialized: true);
        var grid = map.Grid;

        await server.WaitPost(() =>
        {
            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var maps = server.System<SharedMapSystem>();
            var lattice = new Tile(tiles["Lattice"].TileId);
            var steel = new Tile(tiles["FloorSteel"].TileId);

            maps.SetTile(grid.Owner, grid.Comp, new Vector2i(1, 1), lattice);
            maps.SetTile(grid.Owner, grid.Comp, new Vector2i(2, 1), steel);
        });

        await server.WaitAssertion(() =>
        {
            var decals = server.System<DecalSystem>();
            var left = GeneratedAt(decals, grid.Owner, new Vector2i(1, 1));
            // Lattice west of FloorSteel → E bit set → dir_sum 04
            Assert.That(left, Does.Contain("TileBorder-lattices-04"),
                $"lattice next to FloorSteel expected E-link 04, got [{string.Join(',', left)}]");
        });
    }

    [Test]
    public async Task LatticeIsolatedEmitsCappedSquare()
    {
        // Isolated lattice (no linked neighbour) → dir_sum 0 → emits the bespoke
        // capped-square frame (state 00) so it reads as "connected to nothing".
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap(initialized: true);
        var grid = map.Grid;

        await server.WaitPost(() =>
        {
            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var maps = server.System<SharedMapSystem>();
            var lattice = new Tile(tiles["Lattice"].TileId);
            maps.SetTile(grid.Owner, grid.Comp, new Vector2i(13, 8), lattice);
        });

        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var decals = server.System<DecalSystem>();

            maps.TryGetTile(grid.Comp, new Vector2i(13, 8), out var tile);
            Assert.That(tile.TypeId, Is.EqualTo(tiles["Lattice"].TileId), "isolated lattice tile must be present");

            Assert.That(GeneratedAt(decals, grid.Owner, new Vector2i(13, 8)),
                Is.EqualTo(new[] { "TileBorder-lattices-00" }),
                "isolated lattice must emit its capped-square state 00");
        });
    }

    [Test]
    public async Task LatticeInteriorEmitsFullSquare_EdgesEmitExactStates()
    {
        // Frames are the sole per-tile art over the transparent fill: the fully
        // enclosed interior (dir_sum 0x0f) emits the full Eris mesh (state 0f),
        // and partial-boundary tiles emit their exact dir_sum connection states.
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap(initialized: true);
        var grid = map.Grid;

        await server.WaitPost(() =>
        {
            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var maps = server.System<SharedMapSystem>();
            var lattice = new Tile(tiles["Lattice"].TileId);

            // 3x3 lattice block: the centre tile is fully enclosed.
            for (var x = 8; x < 11; x++)
            {
                for (var y = 8; y < 11; y++)
                {
                    maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), lattice);
                }
            }
        });

        await server.WaitAssertion(() =>
        {
            var decals = server.System<DecalSystem>();

            // Centre of the 3x3: dir_sum 0x0f (all four cardinals linked) → 0f.
            Assert.That(GeneratedAt(decals, grid.Owner, new Vector2i(9, 9)),
                Is.EqualTo(new[] { "TileBorder-lattices-0f" }),
                "fully-enclosed interior lattice must emit full-mesh state 0f");

            // Partial-boundary tiles emit their exact connection states.
            // Corner (8,8) links N+E → dir_sum 05.
            Assert.That(GeneratedAt(decals, grid.Owner, new Vector2i(8, 8)),
                Is.EqualTo(new[] { "TileBorder-lattices-05" }),
                "corner lattice expected N+E dir_sum 05");

            // Bottom edge (9,8) links N+E+W → dir_sum 0x0d.
            Assert.That(GeneratedAt(decals, grid.Owner, new Vector2i(9, 8)),
                Is.EqualTo(new[] { "TileBorder-lattices-0d" }),
                "edge lattice expected N+E+W dir_sum 0d");
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
