using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Shared._Oxyd.NeoTheology.Components;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P2.2 — the altar finds items resting on its turf and ignores items beyond its radius.
/// </summary>
[TestOf(typeof(AltarSystem))]
public sealed class AltarTest : GameTest
{
    private const string AltarProto = "OxydNtAltar";
    private const string UpgradeProto = "OxydNtUpgradeFaithsShield";
    private const float OutsideRadius = 5f;

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly AltarSystem _altar = default!;

    [Test]
    public async Task FindsUpgradeRestingOnAltar()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var altar = SSpawnAtPosition(AltarProto, map.GridCoords);
            var item = SSpawnAtPosition(UpgradeProto, map.GridCoords);

            Assert.That(_altar.TryFindItemOnAltar<CruciformUpgradeComponent>(altar, out var found), Is.True);
            Assert.That(found, Is.EqualTo(item));
        });
    }

    [Test]
    public async Task IgnoresUpgradeOutsideRadius()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var altar = SSpawnAtPosition(AltarProto, map.GridCoords);
            SSpawnAtPosition(UpgradeProto, map.GridCoords.Offset(new Vector2(OutsideRadius, 0f)));

            Assert.That(_altar.TryFindItemOnAltar<CruciformUpgradeComponent>(altar, out _), Is.False);
        });
    }
}
