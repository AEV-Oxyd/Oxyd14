using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Oxyd.Economy;
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Server.Cargo.Components;
using Content.Shared._Oxyd.Economy;
using Content.Shared._Oxyd.Skills;
using Content.Shared.Cargo.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Materials;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.Economy;

[TestFixture]
public sealed class OxydAccountsDbAndArtTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    [Test]
    public async Task AccountsDbCreateSuspendAdjust()
    {
        var server = Pair.Server;
        await server.WaitAssertion(() =>
        {
            var eco = server.System<OxydEconomySystem>();
            var cargo = eco.CreateAccount("Cargo Department", "Cargo", 5000,
                null, 0, true, OxydEconomyConstants.EmployerCargo, canMakeAccounts: true);

            Assert.That(eco.TryCreateFundedAccount("HopHire", 500, cargo.AccountNumber, "test",
                out var created, out var err), Is.True, err);
            Assert.That(created, Is.Not.Null);
            Assert.That(created!.Balance, Is.EqualTo(500));
            Assert.That(cargo.Balance, Is.EqualTo(4500));

            Assert.That(eco.TrySetSuspended(created.AccountNumber, true), Is.True);
            Assert.That(eco.TryTransfer(created.AccountNumber, cargo.AccountNumber, 10, "x", "t", out _), Is.False);
            Assert.That(eco.TrySilentAdjust(created.AccountNumber, 50, "silent", "AccountsDB"), Is.True);
            Assert.That(created.Balance, Is.EqualTo(550));

            Assert.That(eco.TryRevokePayroll(created.AccountNumber, cargo.AccountNumber, "AccountsDB", out _), Is.True);
            Assert.That(created.Balance, Is.EqualTo(0));
            Assert.That(cargo.Balance, Is.EqualTo(5050));
        });
    }

    [Test]
    public async Task ArtistBenchRequiresMaterialsAndConsumesInsight()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var mats = server.System<SharedMaterialStorageSystem>();
            var artist = entMan.SpawnEntity("MobHuman", map.MapCoords);
            var sanity = entMan.EnsureComponent<SanityComponent>(artist);
            sanity.Insight = 80f;

            var benchUid = entMan.SpawnEntity("OxydArtistBench", map.MapCoords);
            var benchComp = entMan.GetComponent<OxydArtistBenchComponent>(benchUid);
            Entity<OxydArtistBenchComponent> bench = (benchUid, benchComp);
            var sys = server.System<OxydArtistBenchSystem>();

            Assert.That(sys.TryCreateArtwork(bench, artist, 50f, out var err, out _, out _), Is.False);
            Assert.That(err, Does.Contain("Need"));

            SeedBenchMaterials(mats, benchUid);

            Assert.That(sys.TryCreateArtwork(bench, artist, 50f, out err, out var price, out var used), Is.True, err);
            // Non-artist dumps all insight (≥ min).
            Assert.That(used, Is.EqualTo(80f).Within(0.01f));
            Assert.That(price, Is.EqualTo(50 + 160));
            Assert.That(sanity.Insight, Is.EqualTo(0f).Within(0.01f));

            foreach (var mat in benchComp.SuitableMaterials)
                Assert.That(mats.GetMaterialAmount(benchUid, mat), Is.EqualTo(80)); // 100 - 20

            EntityUid? found = null;
            var query = entMan.EntityQueryEnumerator<OxydArtworkComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                found = uid;
                break;
            }

            Assert.That(found, Is.Not.Null);
            Assert.That(entMan.GetComponent<StaticPriceComponent>(found!.Value).Price, Is.EqualTo(price));
            Assert.That(entMan.GetComponent<OverrideSellComponent>(found.Value).OverrideAccount.Id,
                Is.EqualTo(OxydEconomyConstants.EmployerGuild));

            entMan.DeleteEntity(found.Value);
            entMan.DeleteEntity(benchUid);
            entMan.DeleteEntity(artist);
        });
    }

    [Test]
    public async Task ArtistPerkChoosesInsightAndUsesOddityWeights()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var mats = server.System<SharedMaterialStorageSystem>();
            var slots = server.System<ItemSlotsSystem>();

            var artist = entMan.SpawnEntity("MobHuman", map.MapCoords);
            entMan.EnsureComponent<OxydArtistPerkComponent>(artist);
            var sanity = entMan.EnsureComponent<SanityComponent>(artist);
            sanity.Insight = 90f;
            var skills = entMan.EnsureComponent<MobSkillComponent>(artist);

            var benchUid = entMan.SpawnEntity("OxydArtistBench", map.MapCoords);
            var benchComp = entMan.GetComponent<OxydArtistBenchComponent>(benchUid);
            Entity<OxydArtistBenchComponent> bench = (benchUid, benchComp);
            SeedBenchMaterials(mats, benchUid);

            var oddity = entMan.SpawnEntity("OxydArtworkOddity", map.MapCoords);
            var od = entMan.EnsureComponent<OddityComponent>(oddity);
            od.giving = new Dictionary<ProtoId<SkillPrototype>, int>
            {
                ["Cog"] = 6,
                ["Bio"] = 6,
                ["Mec"] = 1,
            };
            Assert.That(slots.TryInsert(benchUid, benchComp.OdditySlotId, oddity, null), Is.True);

            var sys = server.System<OxydArtistBenchSystem>();
            Assert.That(sys.TryCreateArtwork(bench, artist, 55f, out var err, out var price, out var used, out var kind),
                Is.True, err);
            Assert.That(used, Is.EqualTo(55f).Within(0.01f));
            Assert.That(sanity.Insight, Is.EqualTo(35f).Within(0.01f));
            Assert.That(price, Is.EqualTo(50 + 110));
            Assert.That(kind, Is.Not.Empty);

            // Artist perk: no skill sacrifice (base skills untouched if they were zeroed).
            // Ensure MobSkill was initialized; sacrifice would have written negatives.
            if (skills.skills.TryGetValue("Cog", out var cog))
                Assert.That(cog[0], Is.GreaterThanOrEqualTo(0));

            EntityUid? found = null;
            var query = entMan.EntityQueryEnumerator<OxydArtworkComponent>();
            while (query.MoveNext(out var uid, out var art))
            {
                if (uid == oddity)
                    continue;
                found = uid;
                Assert.That(art.ArtKind, Is.EqualTo(kind));
                break;
            }

            Assert.That(found, Is.Not.Null);
            entMan.DeleteEntity(found!.Value);
            entMan.DeleteEntity(benchUid);
            entMan.DeleteEntity(artist);
        });
    }

    [Test]
    public async Task NonArtistSacrificesSkills()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var mats = server.System<SharedMaterialStorageSystem>();

            var artist = entMan.SpawnEntity("MobHumanOxyd", map.MapCoords);
            var sanity = entMan.GetComponent<SanityComponent>(artist);
            sanity.Insight = 50f;
            var skills = entMan.GetComponent<MobSkillComponent>(artist);

            // Give a known baseline so sacrifice is observable.
            foreach (var key in skills.skills.Keys.ToList())
                skills.skills[key][0] = 20;

            var benchUid = entMan.SpawnEntity("OxydArtistBench", map.MapCoords);
            Entity<OxydArtistBenchComponent> bench = (benchUid, entMan.GetComponent<OxydArtistBenchComponent>(benchUid));
            SeedBenchMaterials(mats, benchUid);

            var sys = server.System<OxydArtistBenchSystem>();
            Assert.That(sys.TryCreateArtwork(bench, artist, 50f, out var err, out _, out var used), Is.True, err);
            Assert.That(used, Is.EqualTo(50f).Within(0.01f));

            var total = skills.skills.Values.Sum(v => v[0]);
            Assert.That(total, Is.LessThan(20 * skills.skills.Count));

            entMan.DeleteEntity(benchUid);
            entMan.DeleteEntity(artist);
        });
    }

    private static void SeedBenchMaterials(SharedMaterialStorageSystem mats, EntityUid bench)
    {
        foreach (var mat in new[] { "Wood", "Steel", "Glass", "Plasteel", "Plastic" })
            Assert.That(mats.TryChangeMaterialAmount(bench, mat, 100), Is.True, mat);
    }
}
