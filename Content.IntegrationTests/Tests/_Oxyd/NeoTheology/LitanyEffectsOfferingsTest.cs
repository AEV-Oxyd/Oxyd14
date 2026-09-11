using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server.Atmos.Components;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Implants;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P4.13 EyeEconomy: the offering litanies drive the altar/Eye pipeline Eris
/// <c>rituals/priest.dm:232-320</c> describes — a fully-stocked offering is consumed and banked
/// as observation on the Eye; an under-stocked one consumes nothing.
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanyEffectsOfferingsTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly EntProtoId EyeProto = "OxydNtEyeOfTheProtector";
    private static readonly EntProtoId AltarProto = "OxydNtAltar";
    private static readonly EntProtoId BiomatterProto = "OxydNtBiomatterStack1";
    private static readonly EntProtoId FruitProto = "FoodApple";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Preacher = "OxydNtPreacher";
    private static readonly ProtoId<LitanyPrototype> DivineIntervention = "OxydLitanyDivineIntervention";
    private static readonly ProtoId<LitanyPrototype> HolyGuidance = "OxydLitanyHolyGuidance";

    private const int FruitCount = 40;

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly SharedStackSystem _stack = default!;
    [SidedDependency(Side.Server)] private readonly SatiationSystem _satiation = default!;

    [Test]
    public async Task DivineIntervention_ConsumesBiomatterAndBanksObservationOnTheEye()
    {
        var map = await Pair.CreateTestMap();
        EntityUid eye = default;
        EntityUid stackA = default;
        EntityUid stackB = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            var caster = PrepareCaster(origin);
            eye = SpawnEye(origin.Offset(new Vector2(1f, 0f)));
            var altar = SSpawnAtPosition(AltarProto, origin.Offset(new Vector2(0f, 1f)));
            var altarCoords = SComp<TransformComponent>(altar).Coordinates;

            stackA = SpawnBiomatter(altarCoords, 100);
            stackB = SpawnBiomatter(altarCoords, 100);

            var begin = _litany.TryBeginLitany(caster, DivineIntervention, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "DivineIntervention begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SComp<EyeOfTheProtectorComponent>(eye).Observation, Is.EqualTo(1000f).Within(1e-6),
                    "The accepted offering must bank its observation on the Eye.");
                Assert.That(Consumed(stackA), Is.True,
                    "A fully-consumed biomatter stack must be deleted.");
                Assert.That(Consumed(stackB), Is.True,
                    "A fully-consumed biomatter stack must be deleted.");
            });
        });
    }

    [Test]
    public async Task HolyGuidance_ConsumesFortyProduceAndBanksObservation()
    {
        var map = await Pair.CreateTestMap();
        EntityUid eye = default;
        var fruit = new List<EntityUid>();

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            var caster = PrepareCaster(origin);
            eye = SpawnEye(origin.Offset(new Vector2(1f, 0f)));
            var altar = SSpawnAtPosition(AltarProto, origin.Offset(new Vector2(0f, 1f)));
            var altarCoords = SComp<TransformComponent>(altar).Coordinates;

            for (var i = 0; i < FruitCount; i++)
                fruit.Add(SSpawnAtPosition(FruitProto, altarCoords));

            var begin = _litany.TryBeginLitany(caster, HolyGuidance, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "HolyGuidance begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<EyeOfTheProtectorComponent>(eye).Observation, Is.EqualTo(500f).Within(1e-6),
                "The fruit offering must bank its observation on the Eye.");
            foreach (var apple in fruit)
                Assert.That(Consumed(apple), Is.True,
                    "Every offered fruit must be consumed.");
        });
    }

    [Test]
    public async Task DivineIntervention_UnderStocked_ConsumesNothing()
    {
        var map = await Pair.CreateTestMap();
        EntityUid eye = default;
        EntityUid stack = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            var caster = PrepareCaster(origin);
            eye = SpawnEye(origin.Offset(new Vector2(1f, 0f)));
            var altar = SSpawnAtPosition(AltarProto, origin.Offset(new Vector2(0f, 1f)));
            stack = SpawnBiomatter(SComp<TransformComponent>(altar).Coordinates, 100);

            var begin = _litany.TryBeginLitany(caster, DivineIntervention, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "DivineIntervention begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SComp<EyeOfTheProtectorComponent>(eye).Observation, Is.Zero,
                    "A refused offering must not bank observation.");
                Assert.That(SComp<StackComponent>(stack).Count, Is.EqualTo(100),
                    "A refused offering must leave the biomatter untouched.");
                Assert.That(_litany.TestingPendingCount, Is.Zero);
            });
        });
    }

    /// <summary>Centre of the tile at <paramref name="gridCoords"/> so tile math is unambiguous.</summary>
    private static EntityCoordinates TileCentre(EntityCoordinates gridCoords)
        => gridCoords.Offset(new Vector2(0.5f, 0.5f));

    /// <summary>
    /// True once the offering consumed the item: deleted outright, or still queued for deletion
    /// when the assertion wins the race against culling.
    /// </summary>
    private bool Consumed(EntityUid uid)
        => SEntMan.Deleted(uid) || SEntMan.IsQueuedForDeletion(uid);

    /// <summary>
    /// A living preacher with the test actor flag: both offering litanies are priest-set, so the
    /// caster must be entitled to them.
    /// </summary>
    private EntityUid PrepareCaster(EntityCoordinates coords)
    {
        _litany.TestingClearAvailabilityOverrides();
        _litany.TestingClearActors();
        var body = SSpawnAtPosition(HumanProto, coords);
        _litany.TestingTreatAsActor(body);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);

        var component = SComp<CruciformComponent>(implant!.Value);
        _cruciform.MakeRank(implant.Value, component, Preacher);
        component.Holiness = component.MaxHoliness;
        StabilizeNeeds(body);
        return body;
    }

    /// <summary>
    /// The real Eye, with its scanning silenced: this suite asserts exact observation totals and
    /// a live scan would award +10 per bearer per window.
    /// </summary>
    private EntityUid SpawnEye(EntityCoordinates coords)
    {
        var eye = SSpawnAtPosition(EyeProto, coords);
        SComp<EyeOfTheProtectorComponent>(eye).ObservationRadius = 0f;
        return eye;
    }

    private EntityUid SpawnBiomatter(EntityCoordinates coords, int count)
    {
        var stack = SSpawnAtPosition(BiomatterProto, coords);
        _stack.SetCount((stack, (StackComponent?) null), count);
        return stack;
    }

    /// <summary>
    /// Removes the environment drift the other litany suites also strip: satiation decay damage and
    /// the vacuum test map's barotrauma so a ~1-2 s cast cannot kill the caster.
    /// </summary>
    private void StabilizeNeeds(EntityUid body)
    {
        if (SEntMan.TryGetComponent(body, out SatiationComponent satiation))
        {
            var sat = new Entity<SatiationComponent>(body, satiation);
            if (_satiation.GetMaximumValue(sat, SatiationSystem.Hunger) is { } maxH)
                _satiation.SetValue(sat, SatiationSystem.Hunger, Math.Min(80f, (float) maxH));
            if (_satiation.GetMaximumValue(sat, SatiationSystem.Thirst) is { } maxT)
                _satiation.SetValue(sat, SatiationSystem.Thirst, (float) maxT);
        }

        SEntMan.RemoveComponent<SatiationDamageComponent>(body);
        SEntMan.RemoveComponent<BarotraumaComponent>(body);
    }

    /// <summary>Waits out the chant DoAfter until no pending cast remains.</summary>
    private async Task AdvancePastCast()
    {
        for (var i = 0; i < 150; i++)
        {
            await Pair.RunTicksSync(5);
            var done = false;
            await Server.WaitPost(() => done = _litany.TestingPendingCount == 0);
            if (done)
                return;
        }

        Assert.Fail("Cast did not complete within expected ticks.");
    }
}
