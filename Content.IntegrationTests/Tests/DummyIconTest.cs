#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests
{
    [TestFixture]
    public sealed class DummyIconTest : GameTest
    {
        private static readonly EntProtoId CruciformProto = "OxydNtCruciform";

        [Test]
        public async Task Test()
        {
            var pair = Pair;
            var client = pair.Client;
            var prototypeManager = client.ResolveDependency<IPrototypeManager>();
            var resourceCache = client.ResolveDependency<IResourceCache>();
            var spriteSys = client.System<SpriteSystem>();

            await client.WaitAssertion(() =>
            {
                foreach (var proto in prototypeManager.EnumeratePrototypes<EntityPrototype>())
                {
                    if (proto.HideSpawnMenu || proto.Abstract || pair.IsTestPrototype(proto) || !proto.Components.ContainsKey("Sprite"))
                        continue;

                    Assert.DoesNotThrow(() =>
                    {
                        var _ = spriteSys.GetPrototypeTextures(proto).ToList();
                    }, "Prototype {0} threw an exception when getting its textures.",
                        proto.ID);
                }
            });
        }

        [Test]
        public async Task CruciformIconLoadsForSpawnMenu()
        {
            var client = Pair.Client;
            var prototypeManager = client.ResolveDependency<IPrototypeManager>();
            var spriteSys = client.System<SpriteSystem>();

            await client.WaitAssertion(() =>
            {
                var cruciform = prototypeManager.Index<EntityPrototype>(CruciformProto);
                Assert.That(cruciform.HideSpawnMenu, Is.False,
                    "The cruciform must appear in the spawn menu.");
                Assert.That(cruciform.Components.ContainsKey("Sprite"), Is.True,
                    "The cruciform must have a sprite for the spawn menu.");
                Assert.DoesNotThrow(() => spriteSys.GetPrototypeTextures(cruciform).ToList(),
                    "The cruciform spawn-menu sprite must load.");
            });
        }
    }
}
