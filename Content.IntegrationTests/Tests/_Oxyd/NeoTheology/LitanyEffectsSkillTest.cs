using System;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.Skills;
using Content.Shared.Implants;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P4.6: the short-boost litanies (Grace of Perseverance, To Uphold the Holy Word) buff
/// every eligible hearer with a timed unique skill buff. Recasting the same chant
/// refreshes that unique source instead of stacking; expiry removes the boost.
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanyEffectsSkillTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId SkillHumanProto = "MobHumanOxyd";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Preacher = "OxydNtPreacher";
    private static readonly ProtoId<LitanyPrototype> GraceOfPerseverance = "OxydLitanyGraceOfPerseverance";
    private static readonly ProtoId<LitanyPrototype> UpholdHolyWord = "OxydLitanyUpholdHolyWord";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly IGameTiming _timing = default!;

    [Test]
    public async Task GraceOfPerseverance_BuffsHearersOnce_Refreshes_Expires()
    {
        await AssertSkillBoost(
            GraceOfPerseverance,
            ("Mec", 10), ("Cog", 10), ("Bio", 10));
    }

    [Test]
    public async Task UpholdHolyWord_BuffsHearersOnce_Refreshes_Expires()
    {
        await AssertSkillBoost(
            UpholdHolyWord,
            ("Rob", 10), ("Tgh", 10), ("Vig", 10));
    }

    /// <summary>
    /// Casts <paramref name="litany"/> at one visible hearer and asserts: the buff lands at
    /// the right amount with a litany-length expiry, a recast refreshes the same unique
    /// source (still exactly one entry), and forcing the expiry clears the derived boost.
    /// </summary>
    private async Task AssertSkillBoost(
        ProtoId<LitanyPrototype> litany,
        params (string Skill, int Amount)[] expected)
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid hearer = default;

        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();
            _litany.TestingClearActors();

            var origin = TileCentre(map.GridCoords);
            caster = SpawnBearer(origin);
            _litany.TestingTreatAsActor(caster);
            hearer = SSpawnAtPosition(SkillHumanProto, origin.Offset(new Vector2(2f, 0f)));
            Assert.That(SEntMan.HasComponent<MobSkillComponent>(hearer), Is.True,
                "The skill test body must carry MobSkill.");

            var begin = _litany.TryBeginLitany(caster, litany, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? $"{litany.Id} begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            var skills = SComp<MobSkillComponent>(hearer);
            foreach (var (skill, amount) in expected)
            {
                Assert.That(skills.skills[skill][1], Is.EqualTo(amount),
                    $"{litany.Id} must grant +{amount} {skill}.");
                Assert.That(skills.buffSources[skill][litany.Id], Has.Count.EqualTo(1),
                    $"{litany.Id} must register exactly one unique buff source per skill.");
                var remaining = skills.buffSources[skill][litany.Id][0].expires - _timing.CurTime;
                Assert.That(remaining.TotalSeconds, Is.EqualTo(900).Within(5),
                    $"{litany.Id} must apply the litany's effectDuration as the buff expiry.");
            }

            Assert.That(SComp<MobSkillComponent>(caster).skills[expected[0].Skill][1], Is.EqualTo(0),
                "The caster does not hear their own chant and must not be buffed.");
        });

        // Recast: refresh the same unique source, do not stack. Drop the shared
        // OxydNtShortBoost global cooldown and refund the spent holiness first
        // (the resource, like the cooldown, is not what this test exercises).
        await Pair.RunTicksSync(120);
        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearCooldowns();
            _cruciform.Refund(caster, 50);
            var begin = _litany.TryBeginLitany(caster, litany, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? $"{litany.Id} recast begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            var skills = SComp<MobSkillComponent>(hearer);
            foreach (var (skill, amount) in expected)
            {
                Assert.That(skills.buffSources[skill][litany.Id], Has.Count.EqualTo(1),
                    "A recast must refresh the unique buff, not stack a second entry.");
                Assert.That(skills.skills[skill][1], Is.EqualTo(amount));
            }
        });

        // Expiry removes the buff and recomputes the derived boost to zero.
        await Server.WaitAssertion(() =>
        {
            var skills = SComp<MobSkillComponent>(hearer);
            foreach (var (skill, _) in expected)
                skills.buffSources[skill][litany.Id][0].expires = TimeSpan.Zero;
        });

        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var skills = SComp<MobSkillComponent>(hearer);
            foreach (var (skill, _) in expected)
            {
                Assert.That(skills.buffSources[skill][litany.Id], Is.Empty, "An expired buff must be removed.");
                Assert.That(skills.skills[skill][1], Is.EqualTo(0), "Expiry must clear the derived boost.");
            }
        });
    }

    /// <summary>Centre of the tile at <paramref name="gridCoords"/> so tile math is unambiguous.</summary>
    private static EntityCoordinates TileCentre(EntityCoordinates gridCoords)
        => gridCoords.Offset(new Vector2(0.5f, 0.5f));

    /// <summary>A skill-capable human with an active cruciform and the Priest litany set.</summary>
    private EntityUid SpawnBearer(EntityCoordinates coords)
    {
        var body = SSpawnAtPosition(SkillHumanProto, coords);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);
        Assert.That(_cruciform.TrySetProfile(body, Preacher), Is.True,
            "Short boosts require the Priest litany set.");
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
