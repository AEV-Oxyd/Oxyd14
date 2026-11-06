using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// Phase 1 job → profile wiring: spawning into a job listed in the rules prototype's
/// <c>jobProfiles</c> installs that rank's cruciform and rank modules.
/// </summary>
public sealed class NeoTheologyJobTest : GameTest
{
    private static readonly EntProtoId HumanProto = "MobHuman";

    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;

    [Test]
    public async Task SpawningIntoChaplainJobGrantsPreacherCruciform()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var mob = SSpawnAtPosition(HumanProto, map.GridCoords);

            var ev = new PlayerSpawnCompleteEvent(
                mob,
                ServerSession!,
                "Chaplain",
                lateJoin: false,
                silent: false,
                joinOrder: 1,
                EntityUid.Invalid,
                HumanoidCharacterProfile.DefaultWithSpecies());

            SEntMan.EventBus.RaiseEvent(EventSource.Local, ev);

            Assert.That(STryComp<CruciformBearerComponent>(mob, out var bearer), Is.True,
                "A chaplain must receive a cruciform bearer component.");
            Assert.That(bearer!.Cruciform, Is.Not.Null, "The bearer component must link a cruciform.");

            var comp = SComp<CruciformComponent>(bearer.Cruciform!.Value);
            Assert.That(comp.Active, Is.True, "A job-granted cruciform starts active.");
            Assert.That(comp.Profile.Id, Is.EqualTo("OxydNtPreacher"));
            Assert.That(comp.UnlockedSets, Is.Not.Empty);

            Assert.That(_cruciform.GrantCruciform(mob, "OxydNtPreacher"), Is.False,
                "A body that already wears a cruciform must not be granted a second one.");
        });
    }
}
