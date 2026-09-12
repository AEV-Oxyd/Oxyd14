using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Prototypes;
using Content.Shared._Oxyd.NeoTheology.UI;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Implants;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// Construction packet (P4.13): DivineGuidance prints a blueprint's materials, Manifestation
/// spends materials on the tile in front and raises the structure, and Uproot returns the
/// materials and deletes the structure. Book casts pick the blueprint; manual speech fails closed.
/// </summary>
[TestOf(typeof(NeoTheologyConstructionSystem))]
public sealed class LitanyEffectsConstructionTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly EntProtoId BibleProto = "OxydNtBible";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Acolyte = "OxydNtAcolyte";
    private static readonly ProtoId<LitanyPrototype> DivineGuidance = "OxydLitanyDivineGuidance";
    private static readonly ProtoId<LitanyPrototype> Manifestation = "OxydLitanyManifestation";
    private static readonly ProtoId<LitanyPrototype> Uproot = "OxydLitanyUproot";
    private static readonly ProtoId<NeoTheologyBlueprintPrototype> ObeliskBlueprint = "OxydNtBlueprintObelisk";
    private static readonly ProtoId<StackPrototype> PlasteelStack = "Plasteel";
    private static readonly ProtoId<StackPrototype> GoldStack = "Gold";

    private const string ObeliskProto = "OxydNtObelisk";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly NeoTheologyConstructionSystem _construction = default!;
    [SidedDependency(Side.Server)] private readonly EntityLookupSystem _lookup = default!;
    [SidedDependency(Side.Server)] private readonly SharedHandsSystem _hands = default!;
    [SidedDependency(Side.Server)] private readonly SharedStackSystem _stack = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly SatiationSystem _satiation = default!;
    [SidedDependency(Side.Server)] private readonly IGameTiming _timing = default!;

    [Test]
    public async Task DivineGuidance_BookChoiceOffersEveryBlueprintAndDescribesTheChosenOne()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin);
            var book = HoldBook(caster, origin);

            var begin = BeginFromBook(caster, book, DivineGuidance);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Divine Guidance begin failed");
            Assert.That(_litany.TestingTryGetPending(begin.RequestId!, out var cast), Is.True);
            Assert.That(cast!.Stage, Is.EqualTo(LitanyCastStage.Choosing));
            Assert.That(cast.ChoiceBlueprints, Does.Contain(ObeliskBlueprint));
            Assert.That(cast.ChoiceBlueprints, Has.Count.EqualTo(11),
                "The catalog lists every NeoTheology blueprint.");

            var selection = Submit(caster, begin.RequestId!, [$"b:{ObeliskBlueprint.Id}"]);
            Assert.That(selection.Success, Is.True, selection.Reason?.Id ?? "Divine Guidance choice failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_construction.TryDescribeBlueprint(ObeliskBlueprint, out var description), Is.True);
            Assert.That(description, Does.Contain("10"));
            Assert.That(description, Does.Contain("5"));
            Assert.That(description, Does.Contain(Loc.GetString("oxyd-nt-blueprint-obelisk-name")));
        });
    }

    [Test]
    public async Task Manifestation_SpendsFrontTileMaterialsAndBuildsTheStructure()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityCoordinates front = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin);
            front = origin.Offset(new Vector2(1f, 0f));

            SpawnExactStack("SheetPlasteel", PlasteelStack, 10, front);
            SpawnExactStack("IngotGold", GoldStack, 5, front);
            SSpawnAtPosition(CruciformProto, front);

            var book = HoldBook(caster, origin);
            var begin = BeginFromBook(caster, book, Manifestation);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Manifestation begin failed");
            Assert.That(_litany.TestingTryGetPending(begin.RequestId!, out _), Is.True);

            var selection = Submit(caster, begin.RequestId!, [$"b:{ObeliskBlueprint.Id}"]);
            Assert.That(selection.Success, Is.True, selection.Reason?.Id ?? "Manifestation choice failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(CountPrototype(front, ObeliskProto), Is.EqualTo(1),
                "Manifestation must raise the chosen structure on the front tile.");
            Assert.That(CountStack(front, PlasteelStack), Is.Zero,
                "The plasteel cost must leave no separate stack.");
            Assert.That(CountStack(front, GoldStack), Is.Zero,
                "The gold cost must leave no separate stack.");
            Assert.That(CountPrototype(front, CruciformProto.Id), Is.Zero,
                "The cruciform cost must be consumed.");
        });
    }

    [Test]
    public async Task Manifestation_WithoutMaterialsFailsClosed()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityCoordinates front = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin);
            front = origin.Offset(new Vector2(1f, 0f));
            var book = HoldBook(caster, origin);

            var begin = BeginFromBook(caster, book, Manifestation);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Manifestation begin failed");

            var selection = Submit(caster, begin.RequestId!, [$"b:{ObeliskBlueprint.Id}"]);
            Assert.That(selection.Success, Is.False);
            Assert.That(selection.Reason?.Id, Is.EqualTo("oxyd-litany-blueprint-missing"));
            Assert.That(_litany.TestingPendingCount, Is.Zero);
            Assert.That(CountPrototype(front, ObeliskProto), Is.Zero);
        });
    }

    [Test]
    public async Task Uproot_ReturnsTheMaterialsAndDeletesTheConstruct()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityCoordinates front = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin);
            front = origin.Offset(new Vector2(1f, 0f));
            SSpawnAtPosition(ObeliskProto, front);

            var book = HoldBook(caster, origin);
            var begin = BeginFromBook(caster, book, Uproot);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Uproot begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(CountPrototype(front, ObeliskProto), Is.Zero,
                "Uproot must delete the construct in front of the caster.");
            Assert.That(CountStack(front, PlasteelStack), Is.EqualTo(10),
                "Uproot must return the plasteel cost.");
            Assert.That(CountStack(front, GoldStack), Is.EqualTo(5),
                "Uproot must return the gold cost.");
            Assert.That(CountPrototype(front, CruciformProto.Id), Is.EqualTo(1),
                "Uproot must return the cruciform cost.");
        });
    }

    [Test]
    public async Task Construction_ManualSpeechFailsClosed()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var caster = PrepareCaster(TileCentre(map.GridCoords));
            var result = _litany.TryBeginLitany(caster, Manifestation, LitanyCastOrigin.ManualSpeech);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason?.Id, Is.EqualTo("oxyd-litany-book-required"));
            Assert.That(_litany.TestingPendingCount, Is.Zero);
        });
    }

    /// <summary>Centre of the tile at <paramref name="gridCoords"/> so tile math is unambiguous.</summary>
    private static EntityCoordinates TileCentre(EntityCoordinates gridCoords)
        => gridCoords.Offset(new Vector2(0.5f, 0.5f));

    private EntityUid SpawnExactStack(string proto, ProtoId<StackPrototype> stackType, int amount, EntityCoordinates coords)
    {
        var stack = SSpawnAtPosition(proto, coords);
        Assert.That(SEntMan.TryGetComponent(stack, out StackComponent? component), Is.True);
        _stack.SetCount(stack, amount, component);
        Assert.That(component!.StackTypeId, Is.EqualTo(stackType));
        return stack;
    }

    private int CountPrototype(EntityCoordinates coords, string prototypeId)
    {
        return EntitiesAt(coords)
            .Count(uid => SComp<MetaDataComponent>(uid).EntityPrototype?.ID == prototypeId);
    }

    private int CountStack(EntityCoordinates coords, ProtoId<StackPrototype> stackType)
    {
        var total = 0;
        foreach (var uid in EntitiesAt(coords))
        {
            if (SEntMan.TryGetComponent<StackComponent>(uid, out var stack) && stack.StackTypeId == stackType)
                total += stack.Count;
        }

        return total;
    }

    private List<EntityUid> EntitiesAt(EntityCoordinates coords)
        => _lookup.GetEntitiesInRange(coords, 0.6f).OrderBy(uid => uid).ToList();

    private EntityUid HoldBook(EntityUid body, EntityCoordinates coords)
    {
        var book = SSpawnAtPosition(BibleProto, coords);
        Assert.That(_hands.TryPickup(body, book), Is.True);
        Assert.That(_litany.TestingOpenBookUi(book, body), Is.True);
        return book;
    }

    private LitanyActionResult BeginFromBook(EntityUid actor, EntityUid book, ProtoId<LitanyPrototype> litany)
    {
        var revision = SComp<CruciformBearerComponent>(actor).UiRevision;
        return _litany.TestingHandleBeginMessage(book, actor, new BeginLitanyMessage(litany, revision));
    }

    private LitanyActionResult Submit(EntityUid actor, string requestId, List<string> tokens)
        => _litany.TestingSubmitChoices(actor, requestId, tokens);

    /// <summary>A living active caster with the test actor flag and a full cruciform.</summary>
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
        _cruciform.MakeRank(implant.Value, component, Acolyte);
        component.Holiness = component.MaxHoliness;
        StabilizeNeeds(body);
        return body;
    }

    /// <summary>Removes the environment drift the other litany suites also strip.</summary>
    private void StabilizeNeeds(EntityUid body)
    {
        if (SEntMan.TryGetComponent(body, out SatiationComponent? satiation))
        {
            var sat = new Entity<SatiationComponent>(body, satiation);
            if (_satiation.GetMaximumValue(sat, SatiationSystem.Hunger) is { } maxH)
                _satiation.SetValue(sat, SatiationSystem.Hunger, Math.Min(80f, (float) maxH));
            if (_satiation.GetMaximumValue(sat, SatiationSystem.Thirst) is { } maxT)
                _satiation.SetValue(sat, SatiationSystem.Thirst, (float) maxT);
        }

        SEntMan.RemoveComponent<SatiationDamageComponent>(body);
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
