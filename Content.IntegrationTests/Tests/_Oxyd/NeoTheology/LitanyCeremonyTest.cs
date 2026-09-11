using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared._Oxyd.Skills;
using Content.Shared.Chat;
using Content.Shared.Implants;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P5: the group-ritual engine (Eris <c>rituals/group.dm</c>) and the ceremony payloads.
/// A ceremony opens with no followers, rounds advance phrase by phrase, and the payload runs
/// for the starter and every recorded follower.
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanyCeremonyTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHumanOxyd";
    private static readonly EntProtoId EyeProto = "OxydNtEyeOfTheProtector";
    private static readonly EntProtoId ObeliskProto = "OxydNtObelisk";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Preacher = "OxydNtPreacher";
    private static readonly ProtoId<CoreModulePrototype> CrusaderModule = "OxydNtModuleCrusader";
    private static readonly ProtoId<LitanyPrototype> PoundingWhisper = "OxydLitanyPoundingWhisper";
    private static readonly ProtoId<LitanyPrototype> ChantOfObservance = "OxydLitanyChantOfObservance";
    private static readonly ProtoId<LitanyPrototype> Sanctify = "OxydLitanySanctify";
    private static readonly ProtoId<LitanyPrototype> Crusade = "OxydLitanyCrusade";
    private static readonly ProtoId<LitanyPrototype> EternalBrotherhood = "OxydLitanyEternalBrotherhood";
    private static readonly ProtoId<LitanyPrototype> CallToBattle = "OxydLitanyCallToBattle";
    private static readonly ProtoId<LitanyPrototype> SearingRevelation = "OxydLitanySearingRevelation";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly CoreModuleSystem _modules = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly IPrototypeManager _prototypes = default!;
    [SidedDependency(Side.Server)] private readonly IGameTiming _timing = default!;

    [Test]
    public async Task Ceremony_NonClergyCannotStart_CoClergyCan()
    {
        var map = await Pair.CreateTestMap();
        EntityUid disciple = default;
        EntityUid preacher = default;

        await Server.WaitAssertion(() =>
        {
            disciple = PrepareBearer(map.GridCoords);
            preacher = PrepareBearer(map.GridCoords.Offset(new Vector2(1f, 0f)), Preacher);

            // The group set is on the base profile, so the disciple is entitled but not clergy.
            _litany.TestingTreatAsActor(disciple);
            _litany.TestingTreatAsActor(preacher);

            var denied = _litany.TryBeginLitany(disciple, PoundingWhisper, LitanyCastOrigin.ManualSpeech);
            Assert.That(denied.Success, Is.True, "Begin must succeed; the clergy gate lands at commit.");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_litany.TestingTryGetCeremony(disciple, out _), Is.False,
                "A disciple must not lead a high ritual.");
            Assert.That(_litany.TestingTryGetCeremony(preacher, out _), Is.False,
                "The preacher has not started anything yet.");

            var allowed = _litany.TryBeginLitany(preacher, PoundingWhisper, LitanyCastOrigin.ManualSpeech);
            Assert.That(allowed.Success, Is.True, allowed.Reason?.Id ?? "Preacher begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_litany.TestingTryGetCeremony(preacher, out var ceremony), Is.True,
                "A preacher must be able to open the rite.");
            Assert.That(ceremony!.Ritual.Id, Is.EqualTo(PoundingWhisper.Id));
        });
    }

    [Test]
    public async Task StatCeremony_ThreeFollowersGrantScaledBuffAndSpendMiraclePoint()
    {
        var map = await Pair.CreateTestMap();
        EntityUid preacher = default;
        var followers = new List<EntityUid>();
        EntityUid eye = default;

        await Server.WaitAssertion(() =>
        {
            preacher = PrepareBearer(map.GridCoords, Preacher);
            _litany.TestingTreatAsActor(preacher);
            for (var i = 0; i < 3; i++)
                followers.Add(PrepareBearer(map.GridCoords.Offset(new Vector2(1.5f + i, 0f))));

            eye = SSpawnAtPosition(EyeProto, map.GridCoords.Offset(new Vector2(0f, 1f)));
            SComp<EyeOfTheProtectorComponent>(eye).MiraclePoints = 1;

            Assert.That(_litany.TryBeginLitany(preacher, ChantOfObservance, LitanyCastOrigin.ManualSpeech).Success,
                Is.True, "Chant begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_litany.TestingTryGetCeremony(preacher, out var ceremony), Is.True);
            RunRite(preacher, ceremony!, followers);
            Assert.That(_litany.TestingTryGetCeremony(preacher, out _), Is.False,
                "A completed rite must remove the session.");

            // Eris: 3 + 2 * participants (three followers) on the starter and every follower.
            AssertBuff(preacher, "Vig", 9);
            foreach (var follower in followers)
                AssertBuff(follower, "Vig", 9);

            Assert.That(SComp<EyeOfTheProtectorComponent>(eye).MiraclePoints, Is.EqualTo(0),
                "A completed stat rite spends one miracle point.");
        });
    }

    [Test]
    public async Task StatCeremony_BelowThreeFollowersCompletesWithoutPayload()
    {
        var map = await Pair.CreateTestMap();
        EntityUid preacher = default;
        EntityUid follower = default;
        EntityUid eye = default;

        await Server.WaitAssertion(() =>
        {
            preacher = PrepareBearer(map.GridCoords, Preacher);
            _litany.TestingTreatAsActor(preacher);
            follower = PrepareBearer(map.GridCoords.Offset(new Vector2(1.5f, 0f)));
            eye = SSpawnAtPosition(EyeProto, map.GridCoords.Offset(new Vector2(0f, 1f)));
            SComp<EyeOfTheProtectorComponent>(eye).MiraclePoints = 1;

            Assert.That(_litany.TryBeginLitany(preacher, PoundingWhisper, LitanyCastOrigin.ManualSpeech).Success,
                Is.True, "Pounding Whisper begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_litany.TestingTryGetCeremony(preacher, out var ceremony), Is.True);
            RunRite(preacher, ceremony!, new List<EntityUid> { follower });

            Assert.That(_litany.TestingTryGetCeremony(preacher, out _), Is.False,
                "The rite completes even below the floor.");
            // Eris still consumes the miracle point: trigger_success decrements before success().
            Assert.That(SComp<EyeOfTheProtectorComponent>(eye).MiraclePoints, Is.EqualTo(0));
            AssertBuff(follower, "Mec", 0, "The payload is skipped below three followers.");
        });
    }

    [Test]
    public async Task Sanctify_ForcesObelisksActive()
    {
        var map = await Pair.CreateTestMap();
        EntityUid disciple = default;
        EntityUid follower = default;
        EntityUid obelisk = default;

        await Server.WaitAssertion(() =>
        {
            disciple = PrepareBearer(map.GridCoords);
            _litany.TestingTreatAsActor(disciple);
            follower = PrepareBearer(map.GridCoords.Offset(new Vector2(1.5f, 0f)));
            obelisk = SSpawnAtPosition(ObeliskProto, map.GridCoords.Offset(new Vector2(0f, 1f)));

            Assert.That(_litany.TryBeginLitany(disciple, Sanctify, LitanyCastOrigin.ManualSpeech).Success,
                Is.True, "Sanctify begin failed (low ritual, any bearer)");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_litany.TestingTryGetCeremony(disciple, out var ceremony), Is.True);
            var before = _timing.CurTime;
            RunRite(disciple, ceremony!, new List<EntityUid> { follower });

            Assert.That(_litany.TestingTryGetCeremony(disciple, out _), Is.False);
            Assert.That(SComp<ObeliskComponent>(obelisk).ForceActiveUntil, Is.GreaterThan(before),
                "Sanctify must force every obelisk active (Eris force_active >= 60).");
        });
    }

    [Test]
    public async Task WrongStarterPhraseFailsTheRite()
    {
        var map = await Pair.CreateTestMap();
        EntityUid preacher = default;
        EntityUid follower = default;
        EntityUid eye = default;

        await Server.WaitAssertion(() =>
        {
            preacher = PrepareBearer(map.GridCoords, Preacher);
            _litany.TestingTreatAsActor(preacher);
            follower = PrepareBearer(map.GridCoords.Offset(new Vector2(1.5f, 0f)));
            eye = SSpawnAtPosition(EyeProto, map.GridCoords.Offset(new Vector2(0f, 1f)));
            SComp<EyeOfTheProtectorComponent>(eye).MiraclePoints = 1;

            Assert.That(_litany.TryBeginLitany(preacher, ChantOfObservance, LitanyCastOrigin.ManualSpeech).Success,
                Is.True, "Chant begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_litany.TestingTryGetCeremony(preacher, out var ceremony), Is.True);
            Speak(follower, ceremony!.Phrases[0]);
            Speak(preacher, "This is not the next verse.");

            Assert.That(_litany.TestingTryGetCeremony(preacher, out _), Is.False,
                "A wrong starter phrase must fail the rite.");
            AssertBuff(follower, "Vig", 0, "A failed rite must not buff anyone.");
        });
    }

    [Test]
    public async Task Crusade_SixFollowersLearnTheCrusaderSet()
    {
        var map = await Pair.CreateTestMap();
        EntityUid preacher = default;
        var followers = new List<EntityUid>();
        EntityUid obelisk = default;

        await Server.WaitAssertion(() =>
        {
            preacher = PrepareBearer(map.GridCoords, Preacher);
            _litany.TestingTreatAsActor(preacher);
            for (var i = 0; i < 6; i++)
                followers.Add(PrepareBearer(map.GridCoords.Offset(new Vector2(1.5f + (i % 3), 0.5f + i / 3))));

            obelisk = SSpawnAtPosition(ObeliskProto, map.GridCoords.Offset(new Vector2(0f, 1f)));

            Assert.That(_litany.TryBeginLitany(preacher, Crusade, LitanyCastOrigin.ManualSpeech).Success,
                Is.True, "Crusade begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            // The obelisk step check reads the computed active flag; force it for the test.
            SComp<ObeliskComponent>(obelisk).Active = true;

            Assert.That(_litany.TestingTryGetCeremony(preacher, out var ceremony), Is.True);
            RunRite(preacher, ceremony!, followers);

            foreach (var follower in followers)
            {
                var cruciform = SComp<CruciformBearerComponent>(follower).Cruciform!.Value;
                Assert.That(SComp<CruciformComponent>(cruciform).GrantedSets,
                    Does.Contain(LitanyCrusadeEffect.CrusaderSet),
                    "A Crusade follower must learn the crusader set.");
                Assert.That(SComp<CruciformComponent>(cruciform).UnlockedSets,
                    Does.Contain(LitanyCrusadeEffect.CrusaderSet),
                    "The grant must unlock the set immediately.");
            }
        });
    }

    [Test]
    public async Task EternalBrotherhood_TogglesDiscipleHud()
    {
        var map = await Pair.CreateTestMap();
        EntityUid crusader = default;

        await Server.WaitAssertion(() =>
        {
            crusader = PrepareBearer(map.GridCoords);
            _litany.TestingTreatAsActor(crusader);
            GrantCrusaderLitany(crusader);

            Assert.That(_litany.TryBeginLitany(crusader, EternalBrotherhood, LitanyCastOrigin.ManualSpeech).Success,
                Is.True, "Eternal Brotherhood begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<NtDiscipleHudComponent>(crusader), Is.True,
                "The first use must enable the disciple HUD.");

            Assert.That(_litany.TryBeginLitany(crusader, EternalBrotherhood, LitanyCastOrigin.ManualSpeech).Success,
                Is.True, "Eternal Brotherhood recast failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<NtDiscipleHudComponent>(crusader), Is.False,
                "The second use must disable the disciple HUD.");
        });
    }

    [Test]
    public async Task CallToBattle_ScalesWithVisibleFollowers()
    {
        var map = await Pair.CreateTestMap();
        EntityUid crusader = default;

        await Server.WaitAssertion(() =>
        {
            crusader = PrepareBearer(map.GridCoords);
            _litany.TestingTreatAsActor(crusader);
            GrantCrusaderLitany(crusader);
            PrepareBearer(map.GridCoords.Offset(new Vector2(1.5f, 0f)));
            PrepareBearer(map.GridCoords.Offset(new Vector2(3f, 0f)));

            Assert.That(_litany.TryBeginLitany(crusader, CallToBattle, LitanyCastOrigin.ManualSpeech).Success,
                Is.True, "Call to Battle begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            // Three bearers (caster included) => Tgh/Rob 6, Vig 3.
            var skills = SComp<MobSkillComponent>(crusader);
            Assert.That(skills.skills["Tgh"][1], Is.EqualTo(6));
            Assert.That(skills.skills["Rob"][1], Is.EqualTo(6));
            Assert.That(skills.skills["Vig"][1], Is.EqualTo(3));
            var remaining = skills.buffSources["Tgh"][CallToBattle.Id][0].expires - _timing.CurTime;
            Assert.That(remaining.TotalSeconds, Is.EqualTo(600).Within(5));
        });
    }

    [Test]
    public async Task SearingRevelation_KnocksDownNonFollowers()
    {
        var map = await Pair.CreateTestMap();
        EntityUid crusader = default;
        EntityUid heathen = default;

        await Server.WaitAssertion(() =>
        {
            crusader = PrepareBearer(map.GridCoords);
            _litany.TestingTreatAsActor(crusader);
            GrantCrusaderLitany(crusader);
            heathen = SSpawnAtPosition(HumanProto, map.GridCoords.Offset(new Vector2(2f, 0f)));

            Assert.That(_litany.TryBeginLitany(crusader, SearingRevelation, LitanyCastOrigin.ManualSpeech).Success,
                Is.True, "Searing Revelation begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            // Vigilance 0 makes both rolls certain: the caster falls, so does the heathen.
            Assert.That(SEntMan.HasComponent<KnockedDownComponent>(heathen), Is.True,
                "A cruciform-less creature in view must be knocked down.");
            Assert.That(SEntMan.HasComponent<KnockedDownComponent>(crusader), Is.True,
                "The caster can be knocked down by their own psy-wave.");
        });
    }

    /// <summary>Speaks every phrase in order: followers first, then the starter's advance.</summary>
    private void RunRite(EntityUid starter, ActiveCeremonyComponent ceremony, List<EntityUid> followers)
    {
        var phrases = new List<string>(ceremony.Phrases);
        for (var i = 0; i + 1 < phrases.Count; i++)
        {
            foreach (var follower in followers)
                Speak(follower, phrases[i]);

            Speak(starter, phrases[i + 1]);
        }
    }

    private void Speak(EntityUid speaker, string phrase)
    {
        _litany.TestingHandleSpeech(new EntitySpokeEvent(speaker, phrase, null, null, phrase));
    }

    private void AssertBuff(EntityUid body, string skill, int amount, string? why = null)
    {
        var skills = SComp<MobSkillComponent>(body);
        var actual = skills.skills.TryGetValue(skill, out var value) && value.Length > 1 ? value[1] : 0;
        Assert.That(actual, Is.EqualTo(amount), why);
    }

    /// <summary>Installs the crusader module the Crusade rite otherwise grants.</summary>
    private void GrantCrusaderLitany(EntityUid body)
    {
        var cruciform = SComp<CruciformBearerComponent>(body).Cruciform!.Value;
        var component = SComp<CruciformComponent>(cruciform);
        Assert.That(_modules.TryInstall(cruciform, component, CrusaderModule), Is.True,
            "The crusader module must install for the crusader litany tests.");
    }

    /// <summary>A living bearer with an active cruciform and an optional profile override.</summary>
    private EntityUid PrepareBearer(EntityCoordinates coords, ProtoId<NeoTheologyProfilePrototype>? profile = null)
    {
        _litany.TestingClearAvailabilityOverrides();
        _litany.TestingClearCooldowns();

        var body = SSpawnAtPosition(HumanProto, coords);
        _litany.TestingTreatAsActor(body);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);

        if (profile is { } profileId)
        {
            Assert.That(_cruciform.TrySetProfile(body, profileId), Is.True,
                $"Profile {profileId.Id} must apply.");
        }

        return body;
    }

    /// <summary>Waits out the cast DoAfter until no pending cast remains.</summary>
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
