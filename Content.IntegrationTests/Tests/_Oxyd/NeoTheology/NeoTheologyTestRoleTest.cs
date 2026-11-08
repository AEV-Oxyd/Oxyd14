using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Humanoid.Components;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

public sealed class NeoTheologyTestRoleTest : GameTest
{
    private static readonly string[] Profiles =
    [
        "Disciple", "Acolyte", "Agrolyte", "Custodian", "Preacher", "Inquisitor",
    ];

    [Test]
    public async Task TestGhostRolesReceiveTheirCruciformAndBible()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var minds = SEntMan.System<SharedMindSystem>();
            var inventory = SEntMan.System<InventorySystem>();
            var ghostRoles = SEntMan.System<GhostRoleSystem>();

            foreach (var profile in Profiles)
            {
                var markerId = $"RandomHumanoidSpawnerOxydNt{profile}";
                var settingsId = $"OxydNtLitanyTest{profile}";
                var marker = SSpawnAtPosition(new EntProtoId(markerId), map.GridCoords);
                Assert.That(STryComp<RandomHumanoidSpawnerComponent>(marker, out var spawner), Is.True);
                Assert.That(spawner!.SettingsPrototypeId, Is.EqualTo(settingsId));

                EntityUid? body = null;
                var roles = SEntMan.EntityQueryEnumerator<NeoTheologyTestRoleComponent>();
                while (roles.MoveNext(out var uid, out var role))
                {
                    if (role.Profile.Id == $"OxydNt{profile}")
                        body = uid;
                }

                Assert.That(body, Is.Not.Null, $"The {profile} marker must spawn a test role.");
                var spawned = body!.Value;
                Assert.That(STryComp<GhostRoleComponent>(spawned, out var ghostRole), Is.True);
                Assert.That(ghostRole!.RoleName, Does.Contain(profile).IgnoreCase);
                Assert.That(ghostRoles.GhostRoles.Any(role => role.Owner == spawned), Is.True,
                    $"The {profile} role must appear in the ghost-role list.");
                Assert.That(STryComp<GhostTakeoverAvailableComponent>(spawned, out _), Is.True);
                Assert.That(STryComp<CruciformBearerComponent>(spawned, out _), Is.False);
                Assert.That(inventory.TryGetSlotEntity(spawned, "belt", out var book), Is.True);
                Assert.That(STryComp<LitanyBookComponent>(book!.Value, out _), Is.True);

                var mind = minds.CreateMind(null);
                minds.TransferTo(mind, spawned);
                Assert.That(ghostRole!.Taken, Is.True);
                Assert.That(STryComp<CruciformBearerComponent>(spawned, out var bearer), Is.True);
                var cruciform = SComp<CruciformComponent>(bearer!.Cruciform!.Value);
                Assert.That(cruciform.Active, Is.True);
                Assert.That(cruciform.Profile.Id, Is.EqualTo($"OxydNt{profile}"));
                Assert.That(cruciform.UnlockedSets, Is.Not.Empty);
            }
        });
    }
}
