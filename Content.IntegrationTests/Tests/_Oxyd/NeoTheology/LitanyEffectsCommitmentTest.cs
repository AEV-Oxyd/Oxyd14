using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server.Atmos.Components;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P4.2 foundation: Commitment attaches the loose altar cruciform to a living human (25 Blunt,
/// left inert) and Deprivation rips the same implant out of a dead bearer (15 Blunt first; the
/// cruciform survives and lands on the corpse's tile). Asserts authoritative component state.
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanyEffectsCommitmentTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly EntProtoId AltarProto = "OxydNtAltar";
    private static readonly ProtoId<LitanyPrototype> Commitment = "OxydLitanyCommitment";
    private static readonly ProtoId<LitanyPrototype> Deprivation = "OxydLitanyDeprivation";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly SharedContainerSystem _containers = default!;
    [SidedDependency(Side.Server)] private readonly SharedTransformSystem _xform = default!;
    [SidedDependency(Side.Server)] private readonly MobStateSystem _mobState = default!;
    [SidedDependency(Side.Server)] private readonly DamageableSystem _damageable = default!;
    [SidedDependency(Side.Server)] private readonly SatiationSystem _satiation = default!;

    [Test]
    public async Task Commitment_ImplantsTheLooseAltarCruciformAndDeals25Blunt()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid target = default;
        EntityUid cruciform = default;
        FixedPoint2 bluntBefore = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin);
            target = SSpawnAtPosition(HumanProto, origin.Offset(new Vector2(1f, 0f)));
            StabilizeNeeds(target);

            // The loose implant rests beside the altar; the altar is beside the target.
            SSpawnAtPosition(AltarProto, origin.Offset(new Vector2(1f, 0.7f)));
            cruciform = SSpawnAtPosition(CruciformProto, origin.Offset(new Vector2(1f, 1f)));

            bluntBefore = DamageOf(target, "Blunt");
            var begin = _litany.TryBeginLitany(caster, Commitment, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Commitment begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(cruciform), Is.True,
                "Commitment must implant the existing item, never respawn it.");
            Assert.That(_cruciform.TryGetCruciformEntity(target, out var linked, out var component), Is.True,
                "The target must now resolve an installed cruciform.");
            Assert.That(linked, Is.EqualTo(cruciform), "The very same loose implant must be installed.");
            Assert.That(component.Active, Is.False, "A committed cruciform lands inert (Epiphany activates it).");
            Assert.That(component.EverActivated, Is.False);

            var installed = SComp<ImplantedComponent>(target);
            Assert.That(installed.ImplantContainer.ContainedEntities, Does.Contain(cruciform),
                "The implant must sit in the body's implant container.");

            var cruciformXform = SEntMan.GetComponent<TransformComponent>(cruciform);
            Assert.That(cruciformXform.ParentUid, Is.EqualTo(target),
                "The loose floor item must no longer be loose.");

            var dealt = DamageOf(target, "Blunt").Float() - bluntBefore.Float();
            Assert.That(dealt, Is.EqualTo(25f).Within(0.75f),
                "Commitment must deal 25 total Blunt (one port operation).");
        });

        // Back-to-back begin: the body already carries a cruciform, so a recast is refused
        // before anything is consumed.
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            var begin = _litany.TryBeginLitany(caster, Commitment, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.False, "A second Commitment on the same body must fail.");
            Assert.That(begin.Reason?.Id, Is.EqualTo("oxyd-litany-commitment-has-cruciform"));
        });
    }

    [Test]
    public async Task Deprivation_ExtractsTheCruciformFromADeadBearerOntoTheFloor()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid corpse = default;
        EntityUid cruciform = default;
        FixedPoint2 bluntBefore = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin);
            corpse = SSpawnAtPosition(HumanProto, origin.Offset(new Vector2(1f, 0f)));
            StabilizeNeeds(corpse);

            var implant = _implants.AddImplant(corpse, CruciformProto);
            Assert.That(implant, Is.Not.Null, "Setup: the victim must receive a cruciform.");
            cruciform = implant!.Value;
            Assert.That(_cruciform.Activate(corpse), Is.True, "Setup: the victim's cruciform must be active.");

            _mobState.ChangeMobState(corpse, MobState.Dead);
            Assert.That(_mobState.IsDead(corpse), Is.True, "Setup: the bearer must be dead.");

            bluntBefore = DamageOf(corpse, "Blunt");
            var begin = _litany.TryBeginLitany(caster, Deprivation, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Deprivation begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(cruciform), Is.True,
                "Deprivation must never delete the implant (no ForceRemove).");
            Assert.That(_cruciform.TryGetCruciformEntity(corpse, out _, out _), Is.False,
                "The corpse must no longer resolve an installed cruciform.");
            Assert.That(SComp<CruciformBearerComponent>(corpse).Cruciform, Is.Null,
                "The bearer link must be detached.");

            var installed = SComp<ImplantedComponent>(corpse);
            Assert.That(installed.ImplantContainer.ContainedEntities, Does.Not.Contain(cruciform),
                "The cruciform must leave the implant container.");
            Assert.That(SComp<SubdermalImplantComponent>(cruciform).ImplantedEntity, Is.Null);
            var component = SComp<CruciformComponent>(cruciform);
            Assert.That(component.ImplantedEntity, Is.Null);
            Assert.That(component.Active, Is.False);

            // Loose on the corpse's tile (the fixture's single-tile grid deparents off-tile
            // entities to the map, so compare world positions rather than grid parentage).
            var cruciformXform = SEntMan.GetComponent<TransformComponent>(cruciform);
            Assert.That(cruciformXform.ParentUid, Is.Not.EqualTo(corpse),
                "The cruciform must not stay parented to the corpse.");
            Assert.That(_containers.IsEntityInContainer(cruciform), Is.False,
                "The cruciform must be a loose, examinable world item.");
            var corpseCoords = _xform.GetMapCoordinates(corpse);
            var itemCoords = _xform.GetMapCoordinates(cruciform);
            Assert.That(itemCoords.MapId, Is.EqualTo(corpseCoords.MapId),
                "The item must land on the corpse's map.");
            Assert.That((itemCoords.Position - corpseCoords.Position).Length(), Is.LessThan(0.1f),
                "The item must land on the corpse's tile.");

            var dealt = DamageOf(corpse, "Blunt").Float() - bluntBefore.Float();
            Assert.That(dealt, Is.EqualTo(15f).Within(0.75f),
                "Deprivation must deal 15 Blunt before the extraction.");
        });

        // Back-to-back begin: no installed cruciform remains, so the retry is refused with no
        // further damage (the 15 Blunt is applied after validation, never before).
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            var before = DamageOf(corpse, "Blunt");
            var begin = _litany.TryBeginLitany(caster, Deprivation, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.False, "A second Deprivation on a stripped corpse must fail.");
            Assert.That(begin.Reason?.Id, Is.EqualTo("oxyd-litany-no-cruciform"));
            Assert.That(DamageOf(corpse, "Blunt").Float(), Is.EqualTo(before.Float()).Within(0.75f),
                "A refused Deprivation must not damage the corpse.");
        });
    }

    /// <summary>Centre of the tile at <paramref name="gridCoords"/> so tile math is unambiguous.</summary>
    private static EntityCoordinates TileCentre(EntityCoordinates gridCoords)
        => gridCoords.Offset(new Vector2(0.5f, 0.5f));

    /// <summary>A living human disciple with an active cruciform and the test actor flag.</summary>
    private EntityUid PrepareCaster(EntityCoordinates coords)
    {
        _litany.TestingClearAvailabilityOverrides();
        _litany.TestingClearActors();
        var body = SSpawnAtPosition(HumanProto, coords);
        _litany.TestingTreatAsActor(body);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);
        StabilizeNeeds(body);
        return body;
    }

    /// <summary>
    /// Removes the environment drift the medical suite also strips: satiation decay damage and
    /// the vacuum test map's barotrauma so a ~1-2 s cast cannot pollute the damage deltas.
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

    private FixedPoint2 DamageOf(EntityUid body, string type)
    {
        if (!SEntMan.TryGetComponent(body, out DamageableComponent damageable))
            return FixedPoint2.Zero;
        var positive = _damageable.GetPositiveDamage((body, damageable));
        return positive.DamageDict.TryGetValue(type, out var value) ? value : FixedPoint2.Zero;
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
