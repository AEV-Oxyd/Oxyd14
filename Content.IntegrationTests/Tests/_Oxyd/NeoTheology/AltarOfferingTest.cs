using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P3.8: the altar collects a named offering's items from its turf and, only when every
/// requirement is met, consumes them and banks the observation on the Eye. A partial match
/// consumes nothing.
/// </summary>
[TestOf(typeof(AltarSystem))]
public sealed class AltarOfferingTest : GameTest
{
    private static readonly EntProtoId BiomatterProto = "OxydNtBiomatterStack1";
    private const string OfferingId = "OxydNtOfferingDivineIntervention";

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly AltarSystem _altar = default!;
    [SidedDependency(Side.Server)] private readonly SharedStackSystem _stack = default!;

    [Test]
    public async Task StockedOfferingConsumesBiomatterAndFeedsTheEye()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var altar = SpawnAltar(map.GridCoords);
            var eye = SpawnEye(map.GridCoords);
            var eyeComp = SComp<EyeOfTheProtectorComponent>(eye);
            var stackA = SpawnBiomatter(map.GridCoords, 100);
            var stackB = SpawnBiomatter(map.GridCoords, 100);

            Assert.That(_altar.TryMakeOffering(altar, eye, OfferingId, out var accepted), Is.True,
                "A fully-stocked offering must be accepted.");
            Assert.That(accepted, Is.EqualTo(200), "The offering must consume every required biomatter.");
            Assert.That(eyeComp.Observation, Is.EqualTo(1000f).Within(1e-6),
                "The offering must bank its observation on the Eye.");

            Assert.That(SEntMan.IsQueuedForDeletion(stackA), Is.True,
                "A fully-consumed biomatter stack must be deleted.");
            Assert.That(SEntMan.IsQueuedForDeletion(stackB), Is.True,
                "A fully-consumed biomatter stack must be deleted.");
        });
    }

    [Test]
    public async Task UnderstockedOfferingConsumesNothing()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var altar = SpawnAltar(map.GridCoords);
            var eye = SpawnEye(map.GridCoords);
            var eyeComp = SComp<EyeOfTheProtectorComponent>(eye);
            var stack = SpawnBiomatter(map.GridCoords, 100);

            Assert.That(_altar.TryMakeOffering(altar, eye, OfferingId, out var accepted), Is.False,
                "An under-stocked offering must be refused.");
            Assert.That(accepted, Is.Zero, "A refused offering must consume nothing.");
            Assert.That(SComp<StackComponent>(stack).Count, Is.EqualTo(100),
                "A refused offering must leave the biomatter untouched.");
            Assert.That(eyeComp.Observation, Is.Zero,
                "A refused offering must not bank observation.");
        });
    }

    private EntityUid SpawnAltar(EntityCoordinates coords)
    {
        var altar = SSpawnAtPosition(null, coords);
        SEntMan.AddComponent<NeoTheologyAltarComponent>(altar);
        return altar;
    }

    private EntityUid SpawnEye(EntityCoordinates coords)
    {
        var eye = SSpawnAtPosition(null, coords);
        SEntMan.AddComponent<EyeOfTheProtectorComponent>(eye);
        return eye;
    }

    private EntityUid SpawnBiomatter(EntityCoordinates coords, int count)
    {
        var stack = SSpawnAtPosition(BiomatterProto, coords);
        _stack.SetCount((stack, (StackComponent?) null), count);
        return stack;
    }
}
