using System;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared._Oxyd.Skills;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Implants;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P4.4 faith effects: Revelation (Belief sanity + vision), Epiphany (cruciform baptism)
/// and DivineBlessing (oddity giving). Asserts authoritative numeric state after cast
/// completion, not handler invocation. Each effect reaches server-only systems through
/// a shared [ByRefEvent] bridge handled by SanitySystem / CruciformSystem.
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanyEffectsFaithTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId FaithHumanProto = "MobHumanOxyd";
    private static readonly EntProtoId OddityProto = "Crowbar";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Preacher = "OxydNtPreacher";
    private static readonly ProtoId<LitanyPrototype> Revelation = "OxydLitanyRevelation";
    private static readonly ProtoId<LitanyPrototype> Epiphany = "OxydLitanyEpiphany";
    private static readonly ProtoId<LitanyPrototype> DivineBlessing = "OxydLitanyDivineBlessing";

    /// <summary>Fixed seed for the Revelation 0..10 roll (LitanyEffectSystem uses IRobustRandom).</summary>
    private const int RevelationRngSeed = unchecked((int)0x5E7E1A70);

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly LitanyEffectSystem _effects = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly SharedHandsSystem _hands = default!;
    [SidedDependency(Side.Server)] private readonly MobStateSystem _mobState = default!;
    [SidedDependency(Side.Server)] private readonly IPrototypeManager _prototypes = default!;
    [SidedDependency(Side.Server)] private readonly IRobustRandom _random = default!;
    [SidedDependency(Side.Server)] private readonly ILocalizationManager _loc = default!;

    [Test]
    public async Task Revelation_MovesTargetSanityByRolledBeliefGain_OneVisionMessage()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid target = default;
        float sanityBefore = 0;
        float sanityAfterFirst = 0;
        int firstDelta = 0;
        double holinessBefore = 0;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin, Revelation);
            target = SSpawnAtPosition(FaithHumanProto, origin.Offset(new Vector2(1f, 0f)));
            Assert.That(SEntMan.HasComponent<SanityComponent>(target), Is.True,
                "The revelation receiver must carry Sanity.");

            // Start below max: a positive Belief gain must not clamp to zero.
            var sanity = SComp<SanityComponent>(target);
            sanity.Sanity = 50f;
            sanityBefore = sanity.Sanity;

            _effects.TestingClearSocialNotices();
            _random.SetSeed(RevelationRngSeed);
            holinessBefore = _cruciform.GetHoliness(caster);

            var begin = _litany.TryBeginLitany(caster, Revelation, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Revelation begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            var delta = SComp<SanityComponent>(target).Sanity - sanityBefore;
            firstDelta = (int) delta;
            sanityAfterFirst = SComp<SanityComponent>(target).Sanity;
            Assert.That(delta, Is.EqualTo(MathF.Round(delta)), "The Belief gain is an integer roll.");
            Assert.That(delta, Is.InRange(0f, LitanyRevelationEffect.MaxSanityGain),
                "Revelation must move sanity by the rolled 0..10 Belief amount.");

            var notices = _effects.TestingGetSocialNotices(target);
            Assert.That(notices, Has.Count.EqualTo(1),
                "Revelation must deliver exactly one vision message to the target.");
            Assert.That(notices[0], Is.EqualTo(_loc.GetString("oxyd-litany-revelation-vision")));
            Assert.That(_effects.TestingGetSocialNotices(caster), Is.Empty,
                "The caster is not the revelation's recipient.");
            Assert.That(_cruciform.GetHoliness(caster),
                Is.EqualTo(holinessBefore - _prototypes.Index(Revelation).Cost).Within(1.0),
                "Revelation must debit exactly its cost once.");
        });

        // Same seed → same roll: proves the gain is the rolled amount, not background drift.
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            SComp<CruciformBearerComponent>(caster).PersonalCooldowns.Remove(Revelation.Id);
            _effects.TestingClearSocialNotices();
            _random.SetSeed(RevelationRngSeed);
            var begin = _litany.TryBeginLitany(caster, Revelation, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Revelation recast begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            var secondDelta = (int) (SComp<SanityComponent>(target).Sanity - sanityAfterFirst);
            Assert.That(secondDelta, Is.EqualTo(firstDelta),
                "Reseeding IRobustRandom to the same seed must reproduce the same revelation gain.");
            Assert.That(firstDelta, Is.GreaterThan(0),
                "The seeded revelation roll must be non-zero (retune RevelationRngSeed if the RNG stream moved).");
            Assert.That(_effects.TestingGetSocialNotices(target), Has.Count.EqualTo(1),
                "Each revelation delivers exactly one vision message.");
        });
    }

    [Test]
    public async Task Epiphany_ActivatesInstalledCruciform_RejectsNoCruciformActiveAndDead()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid bearer = default;
        EntityUid cruciform = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin, Epiphany);
            bearer = SSpawnAtPosition(FaithHumanProto, origin.Offset(new Vector2(1f, 0f)));
            var implant = _implants.AddImplant(bearer, CruciformProto);
            Assert.That(implant, Is.Not.Null);
            cruciform = implant!.Value;
            Assert.That(SComp<CruciformComponent>(cruciform).Active, Is.False,
                "A freshly installed cruciform must start inactive.");

            var begin = _litany.TryBeginLitany(caster, Epiphany, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Epiphany begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CruciformComponent>(cruciform);
            Assert.That(comp.Active, Is.True,
                "Epiphany must activate the target's installed cruciform.");
            Assert.That(comp.EverActivated, Is.True);
        });

        // Already active → begin fails with the loc reason before any cost.
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            var begin = _litany.TryBeginLitany(caster, Epiphany, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.False, "An active cruciform cannot be baptized again.");
            Assert.That(begin.Reason?.Id, Is.EqualTo("oxyd-litany-already-active"));
        });

        // No installed cruciform → begin fails with the loc reason.
        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords).Offset(new Vector2(10f, 0f));
            var fresh = PrepareCaster(origin, Epiphany);
            SSpawnAtPosition(FaithHumanProto, origin.Offset(new Vector2(1f, 0f)));

            var begin = _litany.TryBeginLitany(fresh, Epiphany, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.False, "Epiphany requires an installed cruciform.");
            Assert.That(begin.Reason?.Id, Is.EqualTo("oxyd-litany-no-cruciform"));
        });

        // Dead target → no candidate resolves, so the begin fails closed on targets.
        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords).Offset(new Vector2(20f, 0f));
            var fresh = PrepareCaster(origin, Epiphany);
            var corpse = SSpawnAtPosition(FaithHumanProto, origin.Offset(new Vector2(1f, 0f)));
            _implants.AddImplant(corpse, CruciformProto);
            _mobState.ChangeMobState(corpse, MobState.Dead);

            var begin = _litany.TryBeginLitany(fresh, Epiphany, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.False, "A dead body cannot be baptized.");
            Assert.That(begin.Reason?.Id, Is.EqualTo("oxyd-litany-no-target"));
        });
    }

    [Test]
    public async Task DivineBlessing_BlessesHeldOddity_PaysCasterOwnSkill()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid oddity = default;
        double holinessBefore = 0;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin, DivineBlessing);
            oddity = SSpawnAtPosition(OddityProto, origin);
            var oddityComp = SEntMan.AddComponent<OddityComponent>(oddity);
            oddityComp.giving["Mec"] = 4;
            oddityComp.giving["Cog"] = 0;

            Assert.That(_hands.TryPickup(caster, oddity), Is.True,
                "The caster must hold the oddity in hand.");
            Assert.That(_hands.TryGetActiveItem(caster, out var active) && active == oddity, Is.True);
            holinessBefore = _cruciform.GetHoliness(caster);

            var begin = _litany.TryBeginLitany(caster, DivineBlessing, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "DivineBlessing begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            var giving = SComp<OddityComponent>(oddity).giving;
            var gain = giving["Mec"] - 4;
            Assert.That(gain, Is.InRange(LitanyDivineBlessingEffect.MinGain, LitanyDivineBlessingEffect.MaxGain),
                "DivineBlessing must add rand(1..8) to a non-zero giving entry.");
            Assert.That(giving["Cog"], Is.EqualTo(0), "Zero giving entries must be skipped.");

            // Eris changeStat(stat, -max(round(gain/2), 1)): round() is half-up.
            var expectedPenalty = -new[] { 0, 1, 1, 2, 2, 3, 3, 4, 4 }[gain];
            var skills = SComp<MobSkillComponent>(caster);
            Assert.That(skills.skills["Mec"][1], Is.EqualTo(expectedPenalty),
                $"The caster must pay {expectedPenalty} Mec for a +{gain} blessing.");
            Assert.That(skills.buffSources["Mec"][DivineBlessing.Id], Has.Count.EqualTo(1));
            Assert.That(skills.buffSources["Mec"][DivineBlessing.Id][0].amount, Is.EqualTo(expectedPenalty));

            Assert.That(_cruciform.GetHoliness(caster),
                Is.EqualTo(holinessBefore - _prototypes.Index(DivineBlessing).Cost).Within(1.0),
                "DivineBlessing must debit exactly its cost once.");
        });
    }

    /// <summary>Centre of the tile at <paramref name="gridCoords"/> so tile math is unambiguous.</summary>
    private static EntityCoordinates TileCentre(EntityCoordinates gridCoords)
        => gridCoords.Offset(new Vector2(0.5f, 0.5f));

    /// <summary>A Preacher with an active cruciform: the caster fixture for faith litanies.</summary>
    private EntityUid PrepareCaster(EntityCoordinates coords, ProtoId<LitanyPrototype> litany)
    {
        _litany.TestingClearAvailabilityOverrides();
        _litany.TestingClearActors();
        _litany.TestingSetAvailabilityOverride(litany.Id, true);
        var body = SSpawnAtPosition(FaithHumanProto, coords);
        _litany.TestingTreatAsActor(body);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);
        Assert.That(_cruciform.TrySetProfile(body, Preacher), Is.True,
            $"{litany.Id} requires the Preacher profile's litany sets.");
        return body;
    }

    /// <summary>Waits out the cast DoAfter / extra delay until no pending cast remains.</summary>
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
