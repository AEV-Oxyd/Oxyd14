using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server.Atmos.Components;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared.Implants;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P4.7 conversion roles. Each litany must swap the cruciform's profile AND its derived rank
/// modules / litany sets in the same operation — a profile swap without the module swap is the
/// bug this suite exists to catch.
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanyEffectsConversionTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";

    private static readonly ProtoId<NeoTheologyProfilePrototype> Disciple = "OxydNtDisciple";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Acolyte = "OxydNtAcolyte";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Preacher = "OxydNtPreacher";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Inquisitor = "OxydNtInquisitor";

    private static readonly ProtoId<CoreModulePrototype> BaseModule = "OxydNtModuleBase";
    private static readonly ProtoId<CoreModulePrototype> AcolyteModule = "OxydNtModuleAcolyte";
    private static readonly ProtoId<CoreModulePrototype> PriestModule = "OxydNtModulePriest";
    private static readonly ProtoId<CoreModulePrototype> PriestConvertModule = "OxydNtModulePriestConvert";

    private static readonly ProtoId<LitanySetPrototype> CommonSet = "OxydLitanyCommon";
    private static readonly ProtoId<LitanySetPrototype> MachinerySet = "OxydLitanyMachinery";
    private static readonly ProtoId<LitanySetPrototype> GroupSet = "OxydLitanyGroup";
    private static readonly ProtoId<LitanySetPrototype> AcolyteSet = "OxydLitanyAcolyte";
    private static readonly ProtoId<LitanySetPrototype> PriestSet = "OxydLitanyPriest";

    private static readonly ProtoId<LitanyPrototype> Adoption = "OxydLitanyAdoption";
    private static readonly ProtoId<LitanyPrototype> Confirmation = "OxydLitanyConfirmation";
    private static readonly ProtoId<LitanyPrototype> Ordination = "OxydLitanyOrdination";
    private static readonly ProtoId<LitanyPrototype> Omission = "OxydLitanyOmission";
    private static readonly ProtoId<LitanyPrototype> Excommunication = "OxydLitanyExcommunication";
    private static readonly ProtoId<LitanyPrototype> Initiation = "OxydLitanyInitiation";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly LitanyEffectSystem _effects = default!;
    [SidedDependency(Side.Server)] private readonly SatiationSystem _satiation = default!;

    [Test]
    public async Task Adoption_GrantsADiscipleCruciformToANonBeliever()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid target = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin);
            target = SSpawnAtPosition(HumanProto, origin.Offset(new Vector2(1f, 0f)));
            StabilizeNeeds(target);
            Assert.That(STryComp<CruciformBearerComponent>(target, out _), Is.False,
                "Setup: the Adoption target must be a non-believer.");

            var begin = _litany.TryBeginLitany(caster, Adoption, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Adoption begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_cruciform.TryGetCruciform(target, out var cruciform, out var component), Is.True,
                "Adoption must leave the target with an active cruciform.");
            Assert.That(SEntMan.HasComponent<CruciformBearerComponent>(target), Is.True);
            Assert.That(SEntMan.EntityExists(cruciform), Is.True);

            Assert.That(component.Profile, Is.EqualTo(Disciple));
            Assert.That(component.InstalledModules, Is.EquivalentTo(new[] { BaseModule }),
                "A disciple carries the base module only.");
            Assert.That(component.UnlockedSets, Is.EquivalentTo(new[] { CommonSet, MachinerySet, GroupSet }),
                "The disciple rank must unlock exactly the common/machinery/group sets.");
        });
    }

    [Test]
    public async Task Confirmation_PromotesADiscipleToAcolyte()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid target = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin);
            target = SpawnBearer(origin.Offset(new Vector2(1f, 0f)), Disciple);

            var begin = _litany.TryBeginLitany(caster, Confirmation, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Confirmation begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_cruciform.TryGetCruciform(target, out _, out var component), Is.True);
            Assert.That(component.Profile, Is.EqualTo(Acolyte));
            Assert.That(component.InstalledModules, Is.EquivalentTo(new[] { BaseModule, AcolyteModule }),
                "Confirmation must swap in the acolyte rank modules, not only the profile.");
            Assert.That(component.UnlockedSets,
                Is.EquivalentTo(new[] { CommonSet, MachinerySet, GroupSet, AcolyteSet }),
                "The acolyte rank must unlock exactly the acolyte sets.");
        });
    }

    [Test]
    public async Task Ordination_RaisesAnAcolyteToPreacher()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid target = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin);
            target = SpawnBearer(origin.Offset(new Vector2(1f, 0f)), Acolyte);

            var begin = _litany.TryBeginLitany(caster, Ordination, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Ordination begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_cruciform.TryGetCruciform(target, out _, out var component), Is.True);
            Assert.That(component.Profile, Is.EqualTo(Preacher));
            Assert.That(component.InstalledModules, Is.EquivalentTo(new[] { BaseModule, AcolyteModule, PriestModule }),
                "Ordination must swap in the priest rank modules, not only the profile.");
            Assert.That(component.UnlockedSets,
                Is.EquivalentTo(new[] { CommonSet, MachinerySet, GroupSet, AcolyteSet, PriestSet }),
                "The preacher rank must unlock exactly the preacher sets.");
            Assert.That(component.MaxHoliness, Is.EqualTo(80d).Within(0.001),
                "The preacher profile capacity (80) must land with the rank swap.");
        });
    }

    [Test]
    public async Task Omission_DemotesAPreacherToAcolyte()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid target = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin);
            target = SpawnBearer(origin.Offset(new Vector2(1f, 0f)), Preacher);

            var begin = _litany.TryBeginLitany(caster, Omission, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Omission begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_cruciform.TryGetCruciform(target, out _, out var component), Is.True);
            Assert.That(component.Profile, Is.EqualTo(Acolyte));
            Assert.That(component.InstalledModules, Is.EquivalentTo(new[] { BaseModule, AcolyteModule }),
                "Omission must strip the priest modules, not only the profile.");
            Assert.That(component.UnlockedSets,
                Is.EquivalentTo(new[] { CommonSet, MachinerySet, GroupSet, AcolyteSet }),
                "The demanded acolyte state must unlock exactly the acolyte sets.");
        });
    }

    [Test]
    public async Task Excommunication_ReturnsAPreacherToDiscipleAndNotifies()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid target = default;

        await Server.WaitAssertion(() =>
        {
            _effects.TestingClearSocialNotices();
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin);
            target = SpawnBearer(origin.Offset(new Vector2(2f, 0f)), Preacher);

            var begin = _litany.TryBeginLitany(caster, Excommunication, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Excommunication begin failed");
            Assert.That(_litany.TestingTryGetPending(begin.RequestId!, out var cast), Is.True);
            Assert.That(cast!.Targets, Is.EqualTo(new[] { target }),
                "StationFollower must resolve exactly the one other bearer in the test.");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_cruciform.TryGetCruciform(target, out _, out var component), Is.True);
            Assert.That(component.Profile, Is.EqualTo(Disciple),
                "Excommunication must return the target to disciple status.");
            Assert.That(component.InstalledModules, Is.EquivalentTo(new[] { BaseModule }),
                "Excommunication must strip every rank module, not only the profile.");
            Assert.That(component.UnlockedSets, Is.EquivalentTo(new[] { CommonSet, MachinerySet, GroupSet }),
                "The disciple state must unlock exactly the disciple sets.");
            Assert.That(_effects.TestingGetSocialNotices(target),
                Does.Contain(Loc.GetString("oxyd-litany-excommunication-notice")),
                "The target must be told they were cut off, as Eris to_chat does.");
        });
    }

    [Test]
    public async Task Initiation_PromotesAnAdjacentDiscipleToPreacher()
    {
        var map = await Pair.CreateTestMap();
        EntityUid target = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            // Initiation is inquisitor-set; only an inquisitor may chant it.
            var caster = PrepareCaster(origin, Inquisitor);
            target = SpawnBearer(origin.Offset(new Vector2(1f, 0f)), Disciple);

            var begin = _litany.TryBeginLitany(caster, Initiation, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Initiation begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_cruciform.TryGetCruciform(target, out _, out var component), Is.True);
            Assert.That(component.Profile, Is.EqualTo(Preacher),
                "Initiation must complete the preacher ascension (Eris priest_convert.activate).");
            Assert.That(component.InstalledModules, Does.Contain(PriestModule),
                "The promotion must bring the preacher rank modules with it.");
            Assert.That(component.InstalledModules, Does.Contain(PriestConvertModule),
                "The completed ascension kit stays on the cruciform, as Eris' module does.");
            Assert.That(component.UnlockedSets, Does.Contain(PriestSet),
                "The preacher rank must unlock the priest set.");
        });
    }

    [Test]
    public async Task Initiation_OnAnAlreadyPromotedTarget_FailsClosed()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            var caster = PrepareCaster(origin, Inquisitor);
            SpawnBearer(origin.Offset(new Vector2(1f, 0f)), Preacher);

            var begin = _litany.TryBeginLitany(caster, Initiation, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.False);
            Assert.That(begin.Reason?.Id, Is.EqualTo("oxyd-litany-initiation-already-preacher"));
        });
    }

    /// <summary>Centre of the tile at <paramref name="gridCoords"/> so tile math is unambiguous.</summary>
    private static EntityCoordinates TileCentre(EntityCoordinates gridCoords)
        => gridCoords.Offset(new Vector2(0.5f, 0.5f));

    /// <summary>
    /// A living caster with the test actor flag and a full cruciform, ranked to
    /// <paramref name="rank"/> (preacher by default) — the conversion litanies are priest-set,
    /// and Initiation is inquisitor-set, so the caster must be entitled to them.
    /// </summary>
    private EntityUid PrepareCaster(EntityCoordinates coords, ProtoId<NeoTheologyProfilePrototype>? rank = null)
    {
        _litany.TestingClearAvailabilityOverrides();
        _litany.TestingClearActors();
        var body = SSpawnAtPosition(HumanProto, coords);
        _litany.TestingTreatAsActor(body);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);

        var component = SComp<CruciformComponent>(implant!.Value);
        _cruciform.MakeRank(implant.Value, component, rank ?? Preacher);
        component.Holiness = component.MaxHoliness;
        StabilizeNeeds(body);
        return body;
    }

    /// <summary>A living active bearer, optionally already ranked to <paramref name="rank"/>.</summary>
    private EntityUid SpawnBearer(EntityCoordinates coords, ProtoId<NeoTheologyProfilePrototype> rank)
    {
        var body = SSpawnAtPosition(HumanProto, coords);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);

        var component = SComp<CruciformComponent>(implant!.Value);
        _cruciform.MakeRank(implant.Value, component, rank);
        component.Holiness = component.MaxHoliness;
        StabilizeNeeds(body);
        return body;
    }

    /// <summary>
    /// Removes the environment drift the commitment suite also strips: satiation decay damage and
    /// the vacuum test map's barotrauma so a ~1-2 s cast cannot kill the participants.
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
