using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// Milestone 2 cruciform lifecycle: implant uniqueness, extraction, pending-cast
/// clearance, promotion without refill, and strongest available persistence checks.
/// </summary>
[TestOf(typeof(CruciformSystem))]
public sealed class CruciformLifecycleTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Disciple = "OxydNtDisciple";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Preacher = "OxydNtPreacher";
    private static readonly ProtoId<AccessLevelPrototype> FollowerAccess = "OxydNtFollower";

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly SharedContainerSystem _containers = default!;
    [SidedDependency(Side.Server)] private readonly AccessReaderSystem _access = default!;
    [SidedDependency(Side.Server)] private readonly MobStateSystem _mobState = default!;
    [SidedDependency(Side.Server)] private readonly IGameTiming _timing = default!;
    [SidedDependency(Side.Server)] private readonly ISerializationManager _serialization = default!;

    [Test]
    public async Task DuplicateImplantIsRejectedAndFirstRemainsLinked()
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default;
        EntityUid first = default;
        EntityUid? second = null;

        await Server.WaitAssertion(() =>
        {
            body = SSpawnAtPosition(HumanProto, map.GridCoords);
            first = Implant(body);
            Assert.That(_cruciform.Activate(body), Is.True);

            second = _implants.AddImplant(body, CruciformProto);
            Assert.That(second, Is.Not.Null);

            Assert.That(SComp<CruciformBearerComponent>(body).Cruciform, Is.EqualTo(first));
            Assert.That(SComp<CruciformComponent>(first).ImplantedEntity, Is.EqualTo(body));
            Assert.That(_cruciform.TryGetCruciformEntity(body, out var linked, out _), Is.True);
            Assert.That(linked, Is.EqualTo(first));

            Assert.That(SComp<ImplantedComponent>(body).ImplantContainer.ContainedEntities,
                Does.Not.Contain(second.Value));
            Assert.That(SComp<CruciformComponent>(second.Value).ImplantedEntity, Is.Null);
            Assert.That(SComp<SubdermalImplantComponent>(second.Value).ImplantedEntity, Is.Null);
            Assert.That(SEntMan.Deleted(second.Value), Is.False,
                "Rejected duplicate must remain a recoverable implant entity.");
        });
    }

    [Test]
    public async Task ExtractionClearsBearerLinkAndPendingRequest()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var body = SSpawnAtPosition(HumanProto, map.GridCoords);
            var implant = Implant(body);
            Assert.That(_cruciform.Activate(body), Is.True);

            var bearer = SComp<CruciformBearerComponent>(body);
            bearer.PendingRequestId = "cast-pending-test";
            SEntMan.Dirty(body, bearer);

            ExtractRecoverable(body, implant);

            Assert.That(STryComp<CruciformBearerComponent>(body, out var after) && after != null, Is.True);
            Assert.That(after!.Cruciform, Is.Null);
            Assert.That(after.PendingRequestId, Is.Null);
            Assert.That(SComp<CruciformComponent>(implant).ImplantedEntity, Is.Null);
            Assert.That(SComp<CruciformComponent>(implant).Active, Is.False);
            Assert.That(SComp<CruciformComponent>(implant).EverActivated, Is.True);
            Assert.That(_cruciform.TryGetCruciformEntity(body, out _, out _), Is.False);
            Assert.That(SEntMan.Deleted(implant), Is.False);
        });
    }

    [Test]
    public async Task RemovalWhilePendingClearsPendingRequestId()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var body = SSpawnAtPosition(HumanProto, map.GridCoords);
            var implant = Implant(body);
            Assert.That(_cruciform.Activate(body), Is.True);

            var bearer = SComp<CruciformBearerComponent>(body);
            bearer.PendingRequestId = "pending-while-removal";
            SEntMan.Dirty(body, bearer);

            ExtractRecoverable(body, implant);

            Assert.That(SComp<CruciformBearerComponent>(body).PendingRequestId, Is.Null);
            Assert.That(SComp<CruciformBearerComponent>(body).Cruciform, Is.Null);
        });
    }

    [Test]
    public async Task PromotionDoesNotRefillHoliness()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var body = SSpawnAtPosition(HumanProto, map.GridCoords);
            Implant(body);
            Assert.That(_cruciform.Activate(body), Is.True);

            Assert.That(_cruciform.GetMaximumHoliness(body), Is.EqualTo(50d).Within(1e-9));
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(50d).Within(1e-9));
            Assert.That(_cruciform.TrySpend(body, 20d), Is.True);

            var spent = _cruciform.GetHoliness(body);
            Assert.That(spent, Is.EqualTo(30d).Within(1e-6));

            Assert.That(_cruciform.TrySetProfile(body, Preacher), Is.True);
            Assert.That(_cruciform.GetMaximumHoliness(body), Is.EqualTo(80d).Within(1e-9));
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(spent).Within(1e-6),
                "Rank change must preserve absolute holiness and only clamp; never refill.");
            Assert.That(_cruciform.GetHoliness(body), Is.Not.EqualTo(80d).Within(1e-6));
        });
    }

    [Test]
    public async Task CruciformStateSurvivesSerializationRoundTrip()
    {
        // Full map-save of implanted MobHuman is not a supported harness path
        // (mobs are not MapSavable). Strongest available persistence assertion:
        // DataField round-trip of CruciformComponent after activation/spend, plus
        // retained state on a recoverable extracted implant entity.
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var body = SSpawnAtPosition(HumanProto, map.GridCoords);
            var implant = Implant(body);
            Assert.That(_cruciform.Activate(body), Is.True);
            Assert.That(_cruciform.TrySpend(body, 12d), Is.True);
            Assert.That(_cruciform.TrySetProfile(body, Preacher), Is.True);

            var before = SComp<CruciformComponent>(implant);
            var holiness = before.Holiness;
            var max = before.MaxHoliness;
            var ever = before.EverActivated;
            var profile = before.Profile;

            var written = _serialization.WriteValue<CruciformComponent>(before, alwaysWrite: true);
            var reloaded = _serialization.Read<CruciformComponent>(written);

            Assert.That(reloaded.Holiness, Is.EqualTo(holiness).Within(1e-9));
            Assert.That(reloaded.EverActivated, Is.EqualTo(ever));
            Assert.That(reloaded.Profile, Is.EqualTo(profile));
            Assert.That(reloaded.Active, Is.EqualTo(before.Active));

            ExtractRecoverable(body, implant);

            var extracted = SComp<CruciformComponent>(implant);
            Assert.That(extracted.Holiness, Is.EqualTo(holiness).Within(1e-9));
            Assert.That(extracted.EverActivated, Is.True);
            Assert.That(extracted.Profile, Is.EqualTo(Preacher));
            Assert.That(extracted.Active, Is.False);
            Assert.That(extracted.ImplantedEntity, Is.Null);
            // MaxHoliness is derived/view state; absolute resource and role fields persist.
            Assert.That(holiness, Is.LessThan(max));
        });
    }

    [Test]
    public async Task ActiveBearerContributesAccessTags()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var body = SSpawnAtPosition(HumanProto, map.GridCoords);
            Implant(body);

            var inactiveTags = _access.FindAccessTags(body);
            Assert.That(inactiveTags.Contains(FollowerAccess), Is.False,
                "Inactive implant must not grant NT access.");

            Assert.That(_cruciform.Activate(body), Is.True);
            var activeTags = _access.FindAccessTags(body);
            Assert.That(activeTags.Contains(FollowerAccess), Is.True);

            Assert.That(_cruciform.Deactivate(body), Is.True);
            var deactivated = _access.FindAccessTags(body);
            Assert.That(deactivated.Contains(FollowerAccess), Is.False);
        });
    }

    [Test]
    public async Task HolinessRegeneratesWithElapsedTime()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var body = SSpawnAtPosition(HumanProto, map.GridCoords);
            var implant = Implant(body);
            Assert.That(_cruciform.Activate(body), Is.True);
            Assert.That(_cruciform.TrySpend(body, 5d), Is.True);

            var before = _cruciform.GetHoliness(body);
            var component = SComp<CruciformComponent>(implant);
            component.LastHolinessUpdate = _timing.CurTime - TimeSpan.FromMinutes(1);

            var after = _cruciform.GetHoliness(body);
            Assert.That(after, Is.GreaterThan(before));
            Assert.That(after, Is.LessThanOrEqualTo(_cruciform.GetMaximumHoliness(body)));
            // Base disciple regen is 1 holiness/minute with default Cog/righteous/channeling.
            Assert.That(after - before, Is.EqualTo(1d).Within(0.05d));
        });
    }

    [Test]
    public async Task DeathDeactivatesAndReimplantationCanResume()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var body = SSpawnAtPosition(HumanProto, map.GridCoords);
            var implant = Implant(body);
            Assert.That(_cruciform.Activate(body), Is.True);
            Assert.That(SComp<CruciformComponent>(implant).Active, Is.True);

            _mobState.ChangeMobState(body, MobState.Dead);
            Assert.That(SComp<CruciformComponent>(implant).Active, Is.False);
            Assert.That(_cruciform.IsActiveBearer(body), Is.False);

            ExtractRecoverable(body, implant);
            Assert.That(SComp<CruciformComponent>(implant).EverActivated, Is.True);

            // Fresh living body receives the previously activated implant and resumes.
            var body2 = SSpawnAtPosition(HumanProto, map.GridCoords);
            _implants.ForceImplant(body2, implant);
            Assert.That(SComp<CruciformBearerComponent>(body2).Cruciform, Is.EqualTo(implant));
            Assert.That(SComp<CruciformComponent>(implant).Active, Is.True,
                "Ever-activated implant on a living body resumes without a second Activate fill.");
            Assert.That(_cruciform.IsActiveBearer(body2), Is.True);
        });
    }

    private EntityUid Implant(EntityUid body)
    {
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(SComp<CruciformBearerComponent>(body).Cruciform, Is.EqualTo(implant));
        return implant!.Value;
    }

    /// <summary>
    /// Recoverable extraction: container remove without ForceRemove (which deletes).
    /// </summary>
    private void ExtractRecoverable(EntityUid body, EntityUid implant)
    {
        var installed = SComp<ImplantedComponent>(body);
        Assert.That(_containers.Remove(implant, installed.ImplantContainer), Is.True);
    }
}
