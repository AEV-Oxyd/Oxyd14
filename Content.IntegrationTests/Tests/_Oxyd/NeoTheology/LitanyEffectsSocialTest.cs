using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.IdentityManagement;
using Content.Shared.IdentityManagement.Components;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// Milestone 4 Packet C: Entreaty + CruciformSense social effect contracts.
/// Asserts authoritative notice delivery / fail outcomes after cast completion
/// (not mere handler invocation). Packet C handlers must be Implemented and catalog-enabled.
/// Engineer 2 owns this file; CE owns LitanySystem.Social.cs / catalog enablement.
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanyEffectsSocialTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly ProtoId<LitanyPrototype> Entreaty = "OxydLitanyEntreaty";
    private static readonly ProtoId<LitanyPrototype> CruciformSense = "OxydLitanyCruciformSense";
    private static readonly ProtoId<LitanyPrototype> Relief = "OxydLitanyRelief";
    private static readonly ProtoId<LitanyPrototype> SoulHunger = "OxydLitanySoulHunger";
    private static readonly ProtoId<LitanyPrototype> ActivateDoor = "OxydLitanyActivateDoor";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Disciple = "OxydNtDisciple";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Preacher = "OxydNtPreacher";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Inquisitor = "OxydNtInquisitor";

    private static readonly LitanyEffectKind[] PacketCImplemented =
    [
        LitanyEffectKind.Relief,
        LitanyEffectKind.SoulHunger,
        LitanyEffectKind.Entreaty,
        LitanyEffectKind.CruciformSense,
        LitanyEffectKind.ActivateDoor,
        LitanyEffectKind.HandOfMercy,
        LitanyEffectKind.AbsolutionOfWounds,
        LitanyEffectKind.Convalescence,
        LitanyEffectKind.Succour,
    ];

    /// <summary>Fixed seed for independent 50% Entreaty rolls (CE uses IRobustRandom.Prob).</summary>
    private const int EntreatyRngSeed = unchecked((int)0xE47EA701);

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly LitanyEffectSystem _effects = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly IPrototypeManager _prototypes = default!;
    [SidedDependency(Side.Server)] private readonly IGameTiming _timing = default!;
    [SidedDependency(Side.Server)] private readonly IRobustRandom _random = default!;
    [SidedDependency(Side.Server)] private readonly SharedTransformSystem _xform = default!;
    [SidedDependency(Side.Server)] private readonly MetaDataSystem _meta = default!;
    [SidedDependency(Side.Server)] private readonly SharedContainerSystem _containers = default!;

    [Test]
    public async Task Entreaty_PreacherInquisitorAlways_Others50pct_EscapedNameLocation()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid preacher = default;
        EntityUid inquisitor = default;
        EntityUid discipleA = default;
        EntityUid discipleB = default;
        EntityUid discipleC = default;
        EntityUid discipleD = default;
        string escapedCasterName = null!;
        HashSet<EntityUid> firstWaveDisciples = null!;

        await Server.WaitAssertion(() =>
        {
            AssertEntreatyProtoContract();

            caster = PrepareCaster(map.GridCoords, Entreaty);
            // Markup in perceived identity must be escaped in delivered notices.
            escapedCasterName = SetPerceivedName(caster, "Aid[color=red]Seeker[/color]");
            Assert.That(escapedCasterName, Does.Contain("Aid"));
            Assert.That(escapedCasterName, Does.Contain(@"\[color="),
                "Expected FormattedMessage.EscapeText to escape [color markup in caster identity.");
            Assert.That(ContainsUnescapedColorMarkup(escapedCasterName), Is.False);

            preacher = PrepareFollower(map.GridCoords.Offset(new Vector2(1f, 0f)), Preacher, "PriestMarkup");
            inquisitor = PrepareFollower(map.GridCoords.Offset(new Vector2(0f, 1f)), Inquisitor, "Inq");
            discipleA = PrepareFollower(map.GridCoords.Offset(new Vector2(2f, 0f)), Disciple, "DiscA");
            discipleB = PrepareFollower(map.GridCoords.Offset(new Vector2(0f, 2f)), Disciple, "DiscB");
            discipleC = PrepareFollower(map.GridCoords.Offset(new Vector2(2f, 2f)), Disciple, "DiscC");
            discipleD = PrepareFollower(map.GridCoords.Offset(new Vector2(-1f, 0f)), Disciple, "DiscD");

            _effects.TestingClearSocialNotices();
            _random.SetSeed(EntreatyRngSeed);

            var begin = _litany.TryBeginLitany(caster, Entreaty, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Entreaty begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_effects.TestingGetSocialNotices(caster), Is.Empty,
                "Caster must not receive their own Entreaty notice.");

            var preacherNotice = _effects.TestingGetSocialNotices(preacher);
            var inquisitorNotice = _effects.TestingGetSocialNotices(inquisitor);
            Assert.That(preacherNotice, Is.Not.Empty, "Preacher must always receive Entreaty.");
            Assert.That(inquisitorNotice, Is.Not.Empty, "Inquisitor must always receive Entreaty.");
            AssertNoticeEscaped(preacherNotice, escapedCasterName, "Preacher notice");
            AssertNoticeEscaped(inquisitorNotice, escapedCasterName, "Inquisitor notice");
            Assert.That(preacherNotice.Any(n => n.Contains("cries for salvation", StringComparison.Ordinal)), Is.True);
            Assert.That(preacherNotice.Any(n => n.Contains('(') && n.Contains(')')), Is.True,
                "Entreaty notice must include escaped location (DescribeCasterLocation).");

            Assert.That(preacherNotice.Count, Is.EqualTo(1));
            Assert.That(inquisitorNotice.Count, Is.EqualTo(1));

            firstWaveDisciples = new HashSet<EntityUid>();
            foreach (var disciple in new[] { discipleA, discipleB, discipleC, discipleD })
            {
                var notices = _effects.TestingGetSocialNotices(disciple);
                if (notices.Count == 0)
                    continue;
                firstWaveDisciples.Add(disciple);
                AssertNoticeEscaped(notices, escapedCasterName, $"Disciple {disciple} notice");
            }

            Assert.That(firstWaveDisciples.Count, Is.GreaterThan(0).And.LessThan(4),
                "With four independent 50% rolls under a fixed seed, expect a mixed receive set (not all/none). Retune seed if CE Prob consumption order changes.");

            Assert.That(_cruciform.GetHoliness(caster), Is.EqualTo(50).Within(0.01),
                "Entreaty cost is 0 — holiness unchanged on disciple caster.");
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
        });

        // Same seed → same disciple subset (deterministic server RNG).
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            ClearPersonalCooldown(caster, Entreaty.Id);
            _effects.TestingClearSocialNotices();
            _random.SetSeed(EntreatyRngSeed);

            var begin = _litany.TryBeginLitany(caster, Entreaty, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Entreaty reseed begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_effects.TestingGetSocialNotices(preacher), Is.Not.Empty);
            Assert.That(_effects.TestingGetSocialNotices(inquisitor), Is.Not.Empty);

            var secondWave = new HashSet<EntityUid>();
            foreach (var disciple in new[] { discipleA, discipleB, discipleC, discipleD })
            {
                if (_effects.TestingGetSocialNotices(disciple).Count > 0)
                    secondWave.Add(disciple);
            }

            Assert.That(secondWave, Is.EquivalentTo(firstWaveDisciples),
                "Reseeding IRobustRandom to the same seed must reproduce the same disciple receive set.");
        });
    }

    [Test]
    public async Task Entreaty_FourTransactionOutcomes()
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default;
        EntityUid peer = default;
        string successRequestId = null!;
        double holinessAfterSuccess = 0;

        // 1) Success once (cost 0) + personal cooldown once
        await Server.WaitAssertion(() =>
        {
            AssertEntreatyProtoContract();

            body = PrepareCaster(map.GridCoords, Entreaty);
            peer = PrepareFollower(map.GridCoords.Offset(new Vector2(1f, 0f)), Preacher, "PeerPriest");
            _effects.TestingClearSocialNotices();
            _random.SetSeed(EntreatyRngSeed);

            var before = _cruciform.GetHoliness(body);
            var begin = _litany.TryBeginLitany(body, Entreaty, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "success begin failed");
            Assert.That(begin.RequestId, Is.Not.Null);
            successRequestId = begin.RequestId!;
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(before),
                "Cost 0: holiness must remain unchanged through begin.");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            holinessAfterSuccess = _cruciform.GetHoliness(body);
            Assert.That(holinessAfterSuccess, Is.EqualTo(50).Within(0.01));
            Assert.That(_effects.TestingGetSocialNotices(peer), Is.Not.Empty,
                "Successful Entreaty must deliver at least to Preacher peer.");
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(SComp<CruciformBearerComponent>(body).PendingRequestId, Is.Null);

            var bearer = SComp<CruciformBearerComponent>(body);
            Assert.That(bearer.PersonalCooldowns.TryGetValue(Entreaty.Id, out var until), Is.True,
                "Successful Entreaty must arm personal cooldown key OxydLitanyEntreaty.");
            var remaining = until - _timing.CurTime;
            Assert.That(remaining.TotalSeconds, Is.EqualTo(60).Within(2.5),
                $"Entreaty personal cooldown should be ~60s from injected IGameTiming, was {remaining.TotalSeconds:F2}s.");
        });

        // 4) Duplicate completion no-op
        await Server.WaitAssertion(() =>
        {
            var peerCount = _effects.TestingGetSocialNotices(peer).Count;
            RaiseStaleLitanyCompletion(body, successRequestId!);
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(holinessAfterSuccess).Within(0.01));
            Assert.That(_effects.TestingGetSocialNotices(peer).Count, Is.EqualTo(peerCount),
                "Duplicate completion must not re-deliver Entreaty notices.");
        });

        // 2) Invalid begin no-op (still on personal cooldown)
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            _effects.TestingClearSocialNotices();
            var before = _cruciform.GetHoliness(body);
            var begin = _litany.TryBeginLitany(body, Entreaty, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.False, "Personal cooldown must reject a second Entreaty begin.");
            Assert.That(begin.Reason?.Id, Is.EqualTo("oxyd-litany-denied-cooldown"));
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(before));
            Assert.That(_effects.TestingGetSocialNotices(peer), Is.Empty);
        });

        // 3) Interrupted delay refund (cancel before commit → no notice / no cooldown refresh)
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            ClearPersonalCooldown(body, Entreaty.Id);
            _effects.TestingClearSocialNotices();
            var before = _cruciform.GetHoliness(body);
            var begin = _litany.TryBeginLitany(body, Entreaty, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "interrupt begin failed");
            Assert.That(begin.RequestId, Is.Not.Null);

            var cancel = _litany.TryCancelLitany(body, begin.RequestId!);
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(SComp<CruciformBearerComponent>(body).PendingRequestId, Is.Null);
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(before).Within(0.01));
            Assert.That(_effects.TestingGetSocialNotices(peer), Is.Empty,
                "Interrupted Entreaty must not deliver notices.");
            Assert.That(SComp<CruciformBearerComponent>(body).PersonalCooldowns.ContainsKey(Entreaty.Id), Is.False,
                "Interrupted Entreaty must not arm personal cooldown.");
            Assert.That(cancel.Success, Is.False); // cancel returns Fail("cancelled") by M3 contract
        });
    }

    [Test]
    public async Task CruciformSense_ListsVisibleActiveFollowers7m_FailsIfNone_NoInactiveSoulLeak()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid nearActive = default;
        EntityUid nearInactive = default;
        EntityUid farActive = default;
        EntityUid mundane = default;
        EntityUid extractedSoul = default;
        string escapedNear = null!;

        // Fail-if-none: alone caster, no other followers in view.
        await Server.WaitAssertion(() =>
        {
            AssertCruciformSenseProtoContract();

            caster = PrepareCaster(map.GridCoords, CruciformSense);
            _effects.TestingClearSocialNotices();
            var before = _cruciform.GetHoliness(caster);
            var begin = _litany.TryBeginLitany(caster, CruciformSense, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.False, "CruciformSense must fail when no other active visible followers are in range.");
            Assert.That(begin.Reason?.Id, Is.EqualTo("oxyd-litany-no-target"));
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(_cruciform.GetHoliness(caster), Is.EqualTo(before),
                "Failed CruciformSense must not charge.");
            Assert.That(_effects.TestingGetSocialNotices(caster), Is.Empty);
        });

        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            // Near active follower (within 7 m) — must be listed with escaped perceived identity.
            nearActive = PrepareFollower(map.GridCoords.Offset(new Vector2(3f, 0f)), Disciple, "Near[color=red]One[/color]");
            escapedNear = FormattedMessage.EscapeText(Identity.Name(nearActive, SEntMan, caster));

            // Near inactive implant (never activated) — must not leak.
            nearInactive = SSpawnAtPosition(HumanProto, map.GridCoords.Offset(new Vector2(0f, 3f)));
            SetPerceivedName(nearInactive, "InactiveSoulHost");
            var inactiveImplant = _implants.AddImplant(nearInactive, CruciformProto);
            Assert.That(inactiveImplant, Is.Not.Null);
            Assert.That(SComp<CruciformComponent>(inactiveImplant!.Value).Active, Is.False);
            Assert.That(SComp<CruciformComponent>(inactiveImplant.Value).EverActivated, Is.False);

            // Far active follower beyond 7 m — must not be listed.
            farActive = PrepareFollower(map.GridCoords.Offset(new Vector2(12f, 0f)), Disciple, "FarFollower");

            // Mundane human in range — must not be listed / no registry dump of all humans.
            mundane = SSpawnAtPosition(HumanProto, map.GridCoords.Offset(new Vector2(1f, 1f)));
            SetPerceivedName(mundane, "MundaneBystander");

            // Extracted ever-activated cruciform (soul-bearing, not implanted) on the ground.
            var donor = PrepareFollower(map.GridCoords.Offset(new Vector2(-2f, 0f)), Disciple, "Donor");
            Assert.That(_cruciform.TryGetCruciformEntity(donor, out extractedSoul, out _), Is.True);
            ExtractRecoverable(donor, extractedSoul);
            _xform.SetCoordinates(extractedSoul, map.GridCoords.Offset(new Vector2(2f, 1f)));
            Assert.That(SComp<CruciformComponent>(extractedSoul).Active, Is.False);
            Assert.That(SComp<CruciformComponent>(extractedSoul).EverActivated, Is.True);
            Assert.That(SComp<CruciformComponent>(extractedSoul).ImplantedEntity, Is.Null);

            ClearPersonalCooldown(caster, CruciformSense.Id);
            _cruciform.Refund(caster, 20);
            _effects.TestingClearSocialNotices();

            var begin = _litany.TryBeginLitany(caster, CruciformSense, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "CruciformSense begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            var notices = _effects.TestingGetSocialNotices(caster);
            Assert.That(notices, Is.Not.Empty, "Caster must receive CruciformSense listing.");
            AssertNoticeEscaped(notices, escapedNear, "CruciformSense near-active listing");
            Assert.That(notices.Any(n => n.Contains("has a cruciform installed", StringComparison.Ordinal)), Is.True);

            var joined = string.Join('\n', notices);
            Assert.That(joined, Does.Not.Contain("InactiveSoulHost"),
                "Inactive (never-activated) soul implant must not leak.");
            Assert.That(joined, Does.Not.Contain("FarFollower"),
                "Active follower beyond 7 m must not be listed.");
            Assert.That(joined, Does.Not.Contain("MundaneBystander"),
                "Non-followers must not appear (no global human registry dump).");
            Assert.That(joined, Does.Not.Contain("Donor"),
                "Extracted / unimplanted soul cruciform must not leak via global implant registry.");

            // Peers must not receive the sense listing (caster-only).
            Assert.That(_effects.TestingGetSocialNotices(nearActive), Is.Empty);
            Assert.That(_effects.TestingGetSocialNotices(farActive), Is.Empty);

            Assert.That(_cruciform.GetHoliness(caster), Is.EqualTo(30).Within(0.01),
                "Disciple 50 − CruciformSense 20.");
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task CruciformSense_FourTransactionOutcomes()
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default;
        EntityUid peer = default;
        string successRequestId = null!;
        double holinessAfterSuccess = 0;
        int noticesAfterSuccess = 0;

        // 1) Success once + charge once
        await Server.WaitAssertion(() =>
        {
            AssertCruciformSenseProtoContract();

            body = PrepareCaster(map.GridCoords, CruciformSense);
            peer = PrepareFollower(map.GridCoords.Offset(new Vector2(2f, 0f)), Disciple, "SensePeer");
            _effects.TestingClearSocialNotices();

            var before = _cruciform.GetHoliness(body);
            var begin = _litany.TryBeginLitany(body, CruciformSense, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "success begin failed");
            successRequestId = begin.RequestId!;
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(before),
                "Charge must wait until successful completion.");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            holinessAfterSuccess = _cruciform.GetHoliness(body);
            Assert.That(holinessAfterSuccess, Is.EqualTo(30).Within(0.01),
                "Disciple 50 − CruciformSense 20 once.");
            noticesAfterSuccess = _effects.TestingGetSocialNotices(body).Count;
            Assert.That(noticesAfterSuccess, Is.GreaterThan(0));
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(SComp<CruciformBearerComponent>(body).PendingRequestId, Is.Null);
        });

        // 4) Duplicate completion no-op
        await Server.WaitAssertion(() =>
        {
            RaiseStaleLitanyCompletion(body, successRequestId!);
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(holinessAfterSuccess).Within(0.01));
            Assert.That(_effects.TestingGetSocialNotices(body).Count, Is.EqualTo(noticesAfterSuccess),
                "Duplicate completion must not re-list followers / re-charge.");
        });

        // 2) Invalid begin no-op (insufficient holiness) — clear cooldown so cost gate is what fails.
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            ClearPersonalCooldown(body, CruciformSense.Id);
            Assert.That(_cruciform.TrySpend(body, _cruciform.GetHoliness(body)), Is.True);
            _effects.TestingClearSocialNotices();
            var before = _cruciform.GetHoliness(body);
            var begin = _litany.TryBeginLitany(body, CruciformSense, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.False);
            Assert.That(begin.Reason?.Id, Is.EqualTo("oxyd-litany-no-cost"));
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(before));
            Assert.That(_effects.TestingGetSocialNotices(body), Is.Empty);
        });

        // 3) Interrupted delay refund
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            _cruciform.Refund(body, 50);
            ClearPersonalCooldown(body, CruciformSense.Id);
            _effects.TestingClearSocialNotices();
            var before = _cruciform.GetHoliness(body);
            var begin = _litany.TryBeginLitany(body, CruciformSense, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "interrupt begin failed");
            _litany.TryCancelLitany(body, begin.RequestId!);

            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(before).Within(0.01),
                "Interrupted CruciformSense must never charge.");
            Assert.That(_effects.TestingGetSocialNotices(body), Is.Empty,
                "Interrupted CruciformSense must not deliver listings.");
        });
    }

    [Test]
    public async Task Catalog_OnlyReliefSoulHungerEntreatyCruciformSenseAvailable()
    {
        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();

            // Fail-closed handler gate: Implemented must be exactly the enabled catalog effects.
            Assert.That(
                LitanyHandlerCatalog.Implemented,
                Is.EquivalentTo(PacketCImplemented),
                "LitanyHandlerCatalog.Implemented must be exactly the enabled catalog effects.");

            foreach (var effect in LitanyHandlerCatalog.Foundation)
            {
                if (PacketCImplemented.Contains(effect))
                    continue;
                Assert.That(LitanyHandlerCatalog.HasHandler(effect), Is.False,
                    $"Foundation effect {effect} must remain unimplemented / fail-closed.");
                Assert.That(LitanyHandlerCatalog.AllowsEnabledCatalogEntry(effect), Is.False,
                    $"Foundation effect {effect} must not allow enabled catalog entries yet.");
            }

            var available = _prototypes.EnumeratePrototypes<LitanyPrototype>()
                .Where(l => l.IsAvailable)
                .Select(l => l.ID)
                .OrderBy(id => id)
                .ToArray();

            Assert.That(
                available,
                Is.EquivalentTo(new[]
                {
                    CruciformSense.Id, Entreaty.Id, Relief.Id, SoulHunger.Id, ActivateDoor.Id,
                    "OxydLitanyHandOfMercy", "OxydLitanyAbsolutionOfWounds",
                    "OxydLitanyConvalescence", "OxydLitanySuccour",
                }),
                "Only implemented effects may be IsAvailable (enabled:true).");

            Assert.That(_prototypes.Index(Relief).IsAvailable, Is.True);
            Assert.That(_prototypes.Index(SoulHunger).IsAvailable, Is.True);
            Assert.That(_prototypes.Index(Entreaty).IsAvailable, Is.True);
            Assert.That(_prototypes.Index(CruciformSense).IsAvailable, Is.True);
            Assert.That(_prototypes.Index(Entreaty).Enabled, Is.True);
            Assert.That(_prototypes.Index(CruciformSense).Enabled, Is.True);
        });
    }

    private void AssertEntreatyProtoContract()
    {
        var proto = _prototypes.Index(Entreaty);
        Assert.That(proto.Cost, Is.EqualTo(0));
        Assert.That(proto.Effect, Is.EqualTo(LitanyEffectKind.Entreaty));
        Assert.That(proto.TargetMode, Is.EqualTo(LitanyTargetMode.StationFollower));
        Assert.That(proto.IgnoreStuttering, Is.True);
        Assert.That(proto.CooldownKey, Is.EqualTo(Entreaty.Id));
        Assert.That(proto.CooldownScope, Is.EqualTo(LitanyCooldownScope.Personal));
        Assert.That(proto.CooldownDuration, Is.EqualTo(TimeSpan.FromSeconds(60)));
    }

    private void AssertCruciformSenseProtoContract()
    {
        var proto = _prototypes.Index(CruciformSense);
        Assert.That(proto.Cost, Is.EqualTo(20));
        Assert.That(proto.Effect, Is.EqualTo(LitanyEffectKind.CruciformSense));
        Assert.That(proto.TargetMode, Is.EqualTo(LitanyTargetMode.VisibleFollower));
        Assert.That(proto.Range, Is.EqualTo(7f).Within(0.01f));
        Assert.That(proto.CooldownKey, Is.EqualTo(CruciformSense.Id));
        Assert.That(proto.CooldownScope, Is.EqualTo(LitanyCooldownScope.Personal));
        Assert.That(proto.CooldownDuration, Is.EqualTo(TimeSpan.FromSeconds(60)));
    }

    private EntityUid PrepareCaster(EntityCoordinates coords, ProtoId<LitanyPrototype> litany)
    {
        _litany.TestingClearAvailabilityOverrides();
        _litany.TestingClearActors();
        _litany.TestingSetAvailabilityOverride(litany.Id, true);
        var body = SSpawnAtPosition(HumanProto, coords);
        SetPerceivedName(body, "Caster");
        _litany.TestingTreatAsActor(body);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);
        Assert.That(_cruciform.GetHoliness(body), Is.GreaterThanOrEqualTo(_prototypes.Index(litany).Cost));
        return body;
    }

    private EntityUid PrepareFollower(
        EntityCoordinates coords,
        ProtoId<NeoTheologyProfilePrototype> profile,
        string name)
    {
        var body = SSpawnAtPosition(HumanProto, coords);
        SetPerceivedName(body, name);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);
        if (!profile.Equals(Disciple))
            Assert.That(_cruciform.TrySetProfile(body, profile), Is.True);
        return body;
    }

    /// <summary>
    /// Sets MetaData + Identity slot name so Identity.Name (viewer-aware) matches the test label.
    /// </summary>
    private string SetPerceivedName(EntityUid body, string rawName)
    {
        _meta.SetEntityName(body, rawName);
        if (SEntMan.TryGetComponent(body, out IdentityComponent? identity) &&
            identity.IdentityEntitySlot?.ContainedEntity is { } ident)
        {
            _meta.SetEntityName(ident, rawName);
        }

        return FormattedMessage.EscapeText(Identity.Name(body, SEntMan));
    }

    /// <summary>
    /// Recoverable extraction: container remove without ForceRemove (which deletes).
    /// Mirrors CruciformLifecycleTest — keeps an ever-activated soul implant entity.
    /// </summary>
    private void ExtractRecoverable(EntityUid body, EntityUid implant)
    {
        var installed = SComp<ImplantedComponent>(body);
        Assert.That(_containers.Remove(implant, installed.ImplantContainer), Is.True);
        Assert.That(SComp<CruciformComponent>(implant).ImplantedEntity, Is.Null);
    }

    private void ClearPersonalCooldown(EntityUid body, string cooldownKey)
    {
        var bearer = SComp<CruciformBearerComponent>(body);
        bearer.PersonalCooldowns.Remove(cooldownKey);
    }

    private void RaiseStaleLitanyCompletion(EntityUid body, string requestId)
    {
        SEntMan.EventBus.RaiseLocalEvent(body, new LitanyDoAfterEvent(requestId));
    }

    private async Task AdvancePastCast()
    {
        for (var i = 0; i < 60; i++)
        {
            await Pair.RunTicksSync(5);
            var done = false;
            await Server.WaitPost(() => done = _litany.TestingPendingCount == 0);
            if (done)
                return;
        }

        Assert.Fail("Cast did not complete within expected ticks.");
    }

    private static void AssertNoticeEscaped(IReadOnlyList<string> notices, string escapedName, string label)
    {
        Assert.That(notices.Any(n => n.Contains(escapedName, StringComparison.Ordinal)), Is.True,
            $"{label}: expected escaped identity '{escapedName}' in notices: [{string.Join(" | ", notices)}]");
        // EscapeText only escapes '[' → '\['. Raw unescaped rich-text openers must not appear.
        Assert.That(notices.Any(ContainsUnescapedColorMarkup), Is.False,
            $"{label}: raw unescaped [color markup must not appear in delivered notices: [{string.Join(" | ", notices)}]");
    }

    private static bool ContainsUnescapedColorMarkup(string text)
    {
        var idx = 0;
        while ((idx = text.IndexOf("[color=", idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            if (idx == 0 || text[idx - 1] != '\\')
                return true;
            idx += 7;
        }

        return false;
    }

}
