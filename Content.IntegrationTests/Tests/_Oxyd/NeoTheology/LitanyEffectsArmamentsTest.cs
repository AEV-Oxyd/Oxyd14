using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server.Atmos.Components;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.UI;
using Content.Shared.Implants;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P4.13 Armaments: Order armaments (Eris <c>rituals/priest.dm:492-520</c>) opens the fork's
/// armaments printer UI for the priest standing at the machine, and fails closed with no machine.
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanyEffectsArmamentsTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly EntProtoId PrinterProto = "OxydNtArmamentsPrinter";
    private static readonly EntProtoId EyeProto = "OxydNtEyeOfTheProtector";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Preacher = "OxydNtPreacher";
    private static readonly ProtoId<LitanyPrototype> OrderArmaments = "OxydLitanyOrderArmaments";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly SharedUserInterfaceSystem _ui = default!;
    [SidedDependency(Side.Server)] private readonly SatiationSystem _satiation = default!;

    [Test]
    public async Task OrderArmaments_OpensThePrinterShopForTheCaster()
    {
        var map = await Pair.CreateTestMap();
        EntityUid printer = default;
        EntityUid caster = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin);
            printer = SSpawnAtPosition(PrinterProto, origin.Offset(new Vector2(1f, 0f)));
            SSpawnAtPosition(EyeProto, origin.Offset(new Vector2(0f, 1f)));

            Assert.That(_ui.GetActors(printer, ArmamentsPrinterUiKey.Key), Is.Empty,
                "Setup: the shop must start closed.");

            var begin = _litany.TryBeginLitany(caster, OrderArmaments, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "OrderArmaments begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_ui.GetActors(printer, ArmamentsPrinterUiKey.Key).Contains(caster), Is.True,
                "The litany must open the armaments shop for the caster.");
        });
    }

    [Test]
    public async Task OrderArmaments_WithoutAPrinter_FailsClosed()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            var caster = PrepareCaster(origin);

            // The Eye is a nearby machine, but it is not the shop.
            SSpawnAtPosition(EyeProto, origin.Offset(new Vector2(1f, 0f)));

            var begin = _litany.TryBeginLitany(caster, OrderArmaments, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.False);
            Assert.That(begin.Reason?.Id, Is.EqualTo("oxyd-litany-no-target"));
        });
    }

    /// <summary>Centre of the tile at <paramref name="gridCoords"/> so tile math is unambiguous.</summary>
    private static EntityCoordinates TileCentre(EntityCoordinates gridCoords)
        => gridCoords.Offset(new Vector2(0.5f, 0.5f));

    /// <summary>
    /// A living preacher with the test actor flag: OrderArmaments is priest-set, so the caster
    /// must be entitled to it.
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
