using System.Linq;
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
using Content.Shared.Paper;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P4 foundation: Baptismal Record (Eris <c>rituals/priest.dm:213-230</c>). The priest faces an
/// altar and a paper listing the active cruciform bearers slides out; no altar means no record.
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanyEffectsRecordsTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly EntProtoId AltarProto = "OxydNtAltar";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Preacher = "OxydNtPreacher";
    private static readonly ProtoId<LitanyPrototype> BaptismalRecord = "OxydLitanyBaptismalRecord";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly SatiationSystem _satiation = default!;
    [SidedDependency(Side.Server)] private readonly MetaDataSystem _meta = default!;

    [Test]
    public async Task BaptismalRecord_ListsActiveBearersOnly()
    {
        var map = await Pair.CreateMachineTestMap();
        EntityUid altar = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            var caster = PrepareCaster(origin);
            _meta.SetEntityName(caster, "Zeta");

            var second = SpawnActiveBearer(origin);
            _meta.SetEntityName(second, "Alpha");

            var inactive = SpawnActiveBearer(origin);
            _meta.SetEntityName(inactive, "Omega");
            Assert.That(_cruciform.Deactivate(inactive), Is.True, "Setup: the third bearer must deactivate.");

            altar = SSpawnAtPosition(AltarProto, origin.Offset(new Vector2(1f, 0f)));

            var begin = _litany.TryBeginLitany(caster, BaptismalRecord, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "BaptismalRecord begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            EntityUid? paper = null;
            PaperComponent? paperComp = null;
            var papers = SEntMan.EntityQueryEnumerator<PaperComponent>();
            var paperCount = 0;
            while (papers.MoveNext(out var uid, out var comp))
            {
                paper = uid;
                paperComp = comp;
                paperCount++;
            }

            Assert.Multiple(() =>
            {
                Assert.That(paperCount, Is.EqualTo(1), "One record paper must slide out of the altar.");
                Assert.That(paperComp!.Content, Is.EqualTo("Alpha\nZeta"),
                    "The record lists active bearers by name, sorted, and excludes inactive ones.");
                Assert.That(SComp<TransformComponent>(paper!.Value).Coordinates,
                    Is.EqualTo(SComp<TransformComponent>(altar).Coordinates));
            });
        });
    }

    [Test]
    public async Task BaptismalRecord_WithoutAnAltar_FailsClosed()
    {
        var map = await Pair.CreateMachineTestMap();

        await Server.WaitAssertion(() =>
        {
            var caster = PrepareCaster(TileCentre(map.GridCoords));

            var begin = _litany.TryBeginLitany(caster, BaptismalRecord, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.False, "The litany needs an altar on the faced tile.");
            Assert.That(begin.Reason?.Id, Is.EqualTo("oxyd-litany-no-target"));
            Assert.That(_litany.TestingPendingCount, Is.Zero);
        });

        await Server.WaitAssertion(() =>
        {
            var papers = SEntMan.EntityQueryEnumerator<PaperComponent>();
            var paperCount = 0;
            while (papers.MoveNext(out _, out _))
                paperCount++;

            Assert.That(paperCount, Is.Zero, "A failed cast must not spawn a record.");
        });
    }

    private static EntityCoordinates TileCentre(EntityCoordinates gridCoords)
        => gridCoords.Offset(new Vector2(0.5f, 0.5f));

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

    private EntityUid SpawnActiveBearer(EntityCoordinates coords)
    {
        var body = SSpawnAtPosition(HumanProto, coords);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);
        StabilizeNeeds(body);
        return body;
    }

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
