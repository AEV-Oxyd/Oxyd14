using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server.Atmos.Components;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Implants;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// Milestone 4 Packet B: Relief + SoulHunger medical effect contracts.
/// Asserts authoritative component state after cast completion (not mere handler invocation).
/// Packet B handlers (Relief, SoulHunger) are Implemented and catalog-enabled.
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanyEffectsMedicalTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly ProtoId<DamageTypePrototype> BluntDamage = "Blunt";
    private static readonly ProtoId<DamageTypePrototype> HeatDamage = "Heat";
    private static readonly ProtoId<DamageTypePrototype> ShockDamage = "Shock";
    private static readonly ProtoId<LitanyPrototype> Relief = "OxydLitanyRelief";
    private static readonly ProtoId<LitanyPrototype> SoulHunger = "OxydLitanySoulHunger";
    private static readonly ProtoId<LitanyPrototype> HandOfMercy = "OxydLitanyHandOfMercy";
    private static readonly ProtoId<LitanyPrototype> AbsolutionOfWounds = "OxydLitanyAbsolutionOfWounds";
    private static readonly ProtoId<LitanyPrototype> Convalescence = "OxydLitanyConvalescence";
    private static readonly ProtoId<LitanyPrototype> Succour = "OxydLitanySuccour";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Agrolyte = "OxydNtAgrolyte";
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
        LitanyEffectKind.GraceOfPerseverance,
        LitanyEffectKind.UpholdHolyWord,
        LitanyEffectKind.Revelation,
        LitanyEffectKind.Epiphany,
        LitanyEffectKind.DivineBlessing,
        LitanyEffectKind.Commitment,
        LitanyEffectKind.Deprivation,
    ];

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly IPrototypeManager _prototypes = default!;
    [SidedDependency(Side.Server)] private readonly DamageableSystem _damageable = default!;
    [SidedDependency(Side.Server)] private readonly SatiationSystem _satiation = default!;
    [SidedDependency(Side.Server)] private readonly IGameTiming _timing = default!;

    [Test]
    public async Task Relief_HealsBluntAndHeat_NoStatusEffect()
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default;
        FixedPoint2 bluntBefore = default;
        FixedPoint2 heatBefore = default;

        await Server.WaitAssertion(() =>
        {
            body = PrepareCaster(map.GridCoords, Relief);
            var proto = _prototypes.Index(Relief);
            Assert.That(proto.Cost, Is.EqualTo(20));
            Assert.That(proto.IgnoreStuttering, Is.True);
            var heal = proto.Effects.OfType<LitanyHealEffect>().Single();
            Assert.That(heal.Damage.DamageDict.TryGetValue("Blunt", out var blunt) && blunt < FixedPoint2.Zero,
                Is.True,
                "Relief must heal through a negative Blunt damage value.");

            // Seed Blunt + Heat so the heal has something to remove.
            _damageable.SetDamage(body, new DamageSpecifier
            {
                DamageDict =
                {
                    ["Blunt"] = FixedPoint2.New(10),
                    ["Heat"] = FixedPoint2.New(8),
                },
            });

            StabilizeNeeds(body);
            bluntBefore = DamageOf(body, "Blunt");
            heatBefore = DamageOf(body, "Heat");
            Assert.That(bluntBefore, Is.GreaterThan(FixedPoint2.Zero), "Blunt seed missing");
            Assert.That(heatBefore, Is.GreaterThan(FixedPoint2.Zero), "Heat seed missing");

            var begin = _litany.TryBeginLitany(body, Relief, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Relief begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            var bluntHealed = bluntBefore.Float() - DamageOf(body, "Blunt").Float();
            var heatHealed = heatBefore.Float() - DamageOf(body, "Heat").Float();
            Assert.That(bluntHealed, Is.EqualTo(5f).Within(0.75f),
                "Relief must heal ~5 Blunt via the negative damage specifier.");
            Assert.That(heatHealed, Is.EqualTo(5f).Within(0.75f),
                "Relief must heal ~5 Heat via the negative damage specifier.");
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(30).Within(0.01),
                "Disciple 50 capacity - Relief 20 cost once.");
        });
    }

    [Test]
    public async Task Relief_FourTransactionOutcomes()
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default;
        string successRequestId = null!;
        double holinessAfterSuccess = 0;

        // 1) Success once + charge once
        await Server.WaitAssertion(() =>
        {
            body = PrepareCaster(map.GridCoords, Relief);
            var before = _cruciform.GetHoliness(body);
            var begin = _litany.TryBeginLitany(body, Relief, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "success begin failed");
            Assert.That(begin.RequestId, Is.Not.Null);
            successRequestId = begin.RequestId!;
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(before),
                "Charge must wait until successful completion.");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            holinessAfterSuccess = _cruciform.GetHoliness(body);
            Assert.That(holinessAfterSuccess, Is.EqualTo(30).Within(0.01),
                "Disciple 50 capacity − Relief 20 cost once.");
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(SComp<CruciformBearerComponent>(body).PendingRequestId, Is.Null);
        });

        // 4) Duplicate completion no-op (stale request completion after commit)
        await Server.WaitAssertion(() =>
        {
            RaiseStaleLitanyCompletion(body, successRequestId!);
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(holinessAfterSuccess).Within(0.01),
                "Duplicate completion must not charge again.");
        });

        // 2) Invalid begin no-op
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            Assert.That(_cruciform.TrySpend(body, _cruciform.GetHoliness(body)), Is.True);
            var before = _cruciform.GetHoliness(body);
            var begin = _litany.TryBeginLitany(body, Relief, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.False);
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(before));
        });

        // 3) Interrupted delay refund (cancel before commit → no charge / no new effect)
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            _cruciform.Refund(body, 50);
            ClearPersonalCooldown(body, Relief.Id);
        });
        await Server.WaitAssertion(() =>
        {
            var before = _cruciform.GetHoliness(body);
            var begin = _litany.TryBeginLitany(body, Relief, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "interrupt begin failed");
            Assert.That(begin.RequestId, Is.Not.Null);

            var cancel = _litany.TryCancelLitany(body, begin.RequestId!);
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(SComp<CruciformBearerComponent>(body).PendingRequestId, Is.Null);
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(before).Within(0.01),
                "Interrupted cast must refund / never charge.");
            Assert.That(cancel.Success, Is.False); // cancel returns Fail("cancelled") by M3 contract
        });
    }

    [Test]
    public async Task SoulHunger_Adds100HungerClamped_Paired5Heat_NoThirstOrFood()
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default;
        float hungerBefore = 0;
        float thirstBefore = 0;
        int edibleBefore = 0;
        FixedPoint2 heatBefore = default;

        await Server.WaitAssertion(() =>
        {
            body = PrepareCaster(map.GridCoords, SoulHunger);
            var proto = _prototypes.Index(SoulHunger);
            Assert.That(proto.Cost, Is.EqualTo(50));
            var soulHungerEffect = proto.Effects.OfType<LitanySoulHungerEffect>().Single();
            Assert.That(soulHungerEffect.Damage.DamageDict["Heat"], Is.GreaterThan(FixedPoint2.Zero));

            var sat = SEntity<SatiationComponent>(body);
            Assert.That(sat.Comp.Has(SatiationSystem.Hunger), Is.True);
            Assert.That(sat.Comp.Has(SatiationSystem.Thirst), Is.True);

            // Room for +100 with clamp-to-max coverage: max is 200 for NormalSatiationHunger.
            _satiation.SetValue(sat, SatiationSystem.Hunger, 120f);
            hungerBefore = _satiation.GetValueOrNull(sat, SatiationSystem.Hunger)!.Value;
            thirstBefore = _satiation.GetValueOrNull(sat, SatiationSystem.Thirst)!.Value;
            Assert.That(hungerBefore, Is.EqualTo(120f).Within(0.5f));

            heatBefore = DamageOf(body, "Heat");
            edibleBefore = CountEdibles();

            var begin = _litany.TryBeginLitany(body, SoulHunger, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "SoulHunger begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            var sat = SEntity<SatiationComponent>(body);
            var hungerAfter = _satiation.GetValueOrNull(sat, SatiationSystem.Hunger)!.Value;
            var thirstAfter = _satiation.GetValueOrNull(sat, SatiationSystem.Thirst)!.Value;
            var max = _satiation.GetMaximumValue(sat, SatiationSystem.Hunger)!.Value;

            Assert.That(hungerAfter, Is.EqualTo(Math.Min(hungerBefore + 100f, max)).Within(1f),
                "SoulHunger must add +100 Hunger clamped to max via SatiationSystem.");
            Assert.That(thirstAfter, Is.EqualTo(thirstBefore).Within(1f),
                "SoulHunger must not restore thirst.");

            var heatAfter = DamageOf(body, "Heat");
            var heatDelta = heatAfter.Float() - heatBefore.Float();
            Assert.That(heatDelta, Is.EqualTo(5f * Math.Max(1f, _damageable.UniversalAllDamageModifier)).Within(0.75f),
                $"SoulHunger must apply paired ~5 Heat. Delta={heatDelta}, univ={_damageable.UniversalAllDamageModifier}");

            Assert.That(CountEdibles(), Is.EqualTo(edibleBefore),
                "SoulHunger must not spawn food / edible entities.");
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(0).Within(0.01),
                "SoulHunger cost 50 on disciple capacity 50.");
        });

        // Clamp branch: near-max hunger → still clamps, does not exceed max.
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            ClearPersonalCooldown(body, SoulHunger.Id);
            _cruciform.Refund(body, 50);
            var sat = SEntity<SatiationComponent>(body);
            var max = _satiation.GetMaximumValue(sat, SatiationSystem.Hunger)!.Value;
            _satiation.SetValue(sat, SatiationSystem.Hunger, (float)max - 30f);
            var thirst = _satiation.GetValueOrNull(sat, SatiationSystem.Thirst)!.Value;

            var begin = _litany.TryBeginLitany(body, SoulHunger, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "clamp begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            var sat = SEntity<SatiationComponent>(body);
            var max = _satiation.GetMaximumValue(sat, SatiationSystem.Hunger)!.Value;
            var hunger = _satiation.GetValueOrNull(sat, SatiationSystem.Hunger)!.Value;
            Assert.That(hunger, Is.EqualTo(max).Within(1f),
                "SoulHunger near max must clamp to MaximumValue.");
        });
    }

    [Test]
    public async Task SoulHunger_RejectsFullHungerOrMissingHungerOrHeatApplyFail_NoCharge()
    {
        var map = await Pair.CreateTestMap();

        // Full hunger → reject at validate, no charge
        await Server.WaitAssertion(() =>
        {
            var fullBody = PrepareCaster(map.GridCoords, SoulHunger);
            var sat = SEntity<SatiationComponent>(fullBody);
            var max = _satiation.GetMaximumValue(sat, SatiationSystem.Hunger)!.Value;
            _satiation.SetValue(sat, SatiationSystem.Hunger, (float)max);
            var before = _cruciform.GetHoliness(fullBody);

            var begin = _litany.TryBeginLitany(fullBody, SoulHunger, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.False, "Full hunger must reject before charge.");
            Assert.That(begin.Reason?.Id, Is.EqualTo("oxyd-litany-no-hunger"));
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(_cruciform.GetHoliness(fullBody), Is.EqualTo(before));
            Assert.That(DamageOf(fullBody, "Heat"), Is.EqualTo(FixedPoint2.Zero));
        });

        // Missing Hunger / Satiation → reject at validate, no charge
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            var missingBody = PrepareCaster(map.GridCoords, SoulHunger);
            Assert.That(SEntMan.RemoveComponent<SatiationComponent>(missingBody), Is.True);
            var before = _cruciform.GetHoliness(missingBody);

            var begin = _litany.TryBeginLitany(missingBody, SoulHunger, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.False, "Missing Hunger must reject before charge.");
            Assert.That(begin.Reason?.Id, Is.EqualTo("oxyd-litany-no-hunger"));
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(_cruciform.GetHoliness(missingBody), Is.EqualTo(before));
            Assert.That(DamageOf(missingBody, "Heat"), Is.EqualTo(FixedPoint2.Zero));
        });

        // Cannot apply paired Heat (no Damageable) → reject at validate, no charge
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            var heatFailBody = PrepareCaster(map.GridCoords, SoulHunger);
            var sat = SEntity<SatiationComponent>(heatFailBody);
            _satiation.SetValue(sat, SatiationSystem.Hunger, 80f);
            var hungerSnap = _satiation.GetValueOrNull(sat, SatiationSystem.Hunger)!.Value;
            Assert.That(SEntMan.RemoveComponent<DamageableComponent>(heatFailBody), Is.True);
            var before = _cruciform.GetHoliness(heatFailBody);

            var begin = _litany.TryBeginLitany(heatFailBody, SoulHunger, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.False,
                "Missing Damageable must reject SoulHunger before charge (paired Heat cannot apply).");
            Assert.That(begin.Reason?.Id, Is.EqualTo("oxyd-litany-no-effect"));
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(_cruciform.GetHoliness(heatFailBody), Is.EqualTo(before));
            Assert.That(_satiation.GetValueOrNull(sat, SatiationSystem.Hunger)!.Value, Is.EqualTo(hungerSnap).Within(1f));
        });
    }

    [Test]
    public async Task SoulHunger_FourTransactionOutcomes()
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default;
        string successRequestId = null!;
        double holinessAfterSuccess = 0;
        float hungerAfterSuccess = 0;
        FixedPoint2 heatAfterSuccess = default;

        // 1) Success once + charge once
        await Server.WaitAssertion(() =>
        {
            body = PrepareCaster(map.GridCoords, SoulHunger);
            var sat = SEntity<SatiationComponent>(body);
            _satiation.SetValue(sat, SatiationSystem.Hunger, 50f);

            var before = _cruciform.GetHoliness(body);
            var begin = _litany.TryBeginLitany(body, SoulHunger, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "success begin failed");
            successRequestId = begin.RequestId!;
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(before));
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            holinessAfterSuccess = _cruciform.GetHoliness(body);
            Assert.That(holinessAfterSuccess, Is.EqualTo(0).Within(0.01));
            var sat = SEntity<SatiationComponent>(body);
            hungerAfterSuccess = _satiation.GetValueOrNull(sat, SatiationSystem.Hunger)!.Value;
            Assert.That(hungerAfterSuccess, Is.EqualTo(150f).Within(1f));
            heatAfterSuccess = DamageOf(body, "Heat");
            Assert.That(heatAfterSuccess.Float(), Is.EqualTo(5f * Math.Max(1f, _damageable.UniversalAllDamageModifier)).Within(0.75f));
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
        });

        // 4) Duplicate completion no-op
        await Server.WaitAssertion(() =>
        {
            RaiseStaleLitanyCompletion(body, successRequestId!);
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(holinessAfterSuccess).Within(0.01));
            var sat = SEntity<SatiationComponent>(body);
            Assert.That(_satiation.GetValueOrNull(sat, SatiationSystem.Hunger)!.Value, Is.EqualTo(hungerAfterSuccess).Within(1f));
            AssertFixed(DamageOf(body, "Heat"), heatAfterSuccess);
        });

        // 2) Invalid begin no-op
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            var before = _cruciform.GetHoliness(body);
            var sat = SEntity<SatiationComponent>(body);
            var hunger = _satiation.GetValueOrNull(sat, SatiationSystem.Hunger)!.Value;
            var begin = _litany.TryBeginLitany(body, SoulHunger, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.False, "Zero holiness must fail begin.");
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(before));
            Assert.That(_satiation.GetValueOrNull(sat, SatiationSystem.Hunger)!.Value, Is.EqualTo(hunger).Within(1f));
        });

        // 3) Interrupted delay refund
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            _cruciform.Refund(body, 50);
            ClearPersonalCooldown(body, SoulHunger.Id);
            var sat = SEntity<SatiationComponent>(body);
            _satiation.SetValue(sat, SatiationSystem.Hunger, 40f);
            var hungerBefore = _satiation.GetValueOrNull(sat, SatiationSystem.Hunger)!.Value;
            var heatBefore = DamageOf(body, "Heat");
            var before = _cruciform.GetHoliness(body);

            var begin = _litany.TryBeginLitany(body, SoulHunger, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "interrupt begin failed");
            _litany.TryCancelLitany(body, begin.RequestId!);

            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(before).Within(0.01));
            Assert.That(_satiation.GetValueOrNull(sat, SatiationSystem.Hunger)!.Value, Is.EqualTo(hungerBefore).Within(1f));
            AssertFixed(DamageOf(body, "Heat"), heatBefore);
        });
    }

    [Test]
    public async Task Catalog_PacketCAvailableIncludesMedical()
    {
        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();

            // Packet C supersedes Packet B: medical handlers remain Implemented alongside social;
            // P4.3 adds the door handler.
            Assert.That(
                LitanyHandlerCatalog.Implemented,
                Is.EquivalentTo(PacketCImplemented),
                "Implemented must match the enabled catalog handlers.");

            Assert.That(_prototypes.Index(Relief).IsAvailable, Is.True);
            Assert.That(_prototypes.Index(SoulHunger).IsAvailable, Is.True);
            Assert.That(_prototypes.Index(Relief).Enabled, Is.True);
            Assert.That(_prototypes.Index(SoulHunger).Enabled, Is.True);
        });
    }


    // P4.5: the four medical litanies heal through their negative damage blocks. The heal
    // applies through the casting body; adjacent helper bodies exist only where the target
    // mode requires a recipient (HandOfMercy / Absolution: adjacent living, Succour: adjacent
    // active follower). Seeds exceed the budget but stay below the mob's 100-damage critical
    // threshold, so the asserted delta proves the heal is capped: the Brute-group entry spends
    // 20 across Blunt/Slash, not 20 per subtype.
    [Test]
    public async Task HandOfMercy_HealsBlunt10Heat10() =>
        await AssertLitanyHealsExactDelta(HandOfMercy, Agrolyte, adjacentLiving: true, adjacentFollower: false,
            ("Blunt", 20f, 10f), ("Heat", 20f, 10f));

    [Test]
    public async Task AbsolutionOfWounds_HealsBrute20Heat20Asphyxiation40() =>
        await AssertLitanyHealsExactDelta(AbsolutionOfWounds, Agrolyte, adjacentLiving: true, adjacentFollower: false,
            ("Blunt", 11f, 10f), ("Slash", 11f, 10f), ("Heat", 22f, 20f), ("Asphyxiation", 41f, 40f));

    [Test]
    public async Task Convalescence_HealsBrute20Heat20Asphyxiation40() =>
        await AssertLitanyHealsExactDelta(Convalescence, Inquisitor, adjacentLiving: false, adjacentFollower: false,
            ("Blunt", 11f, 10f), ("Slash", 11f, 10f), ("Heat", 22f, 20f), ("Asphyxiation", 41f, 40f));

    [Test]
    public async Task Succour_HealsBrute20Heat20Asphyxiation40() =>
        await AssertLitanyHealsExactDelta(Succour, Inquisitor, adjacentLiving: false, adjacentFollower: true,
            ("Blunt", 11f, 10f), ("Slash", 11f, 10f), ("Heat", 22f, 20f), ("Asphyxiation", 41f, 40f));

    /// <summary>
    /// Seeds each listed damage type above its heal budget, casts <paramref name="litany"/>
    /// and asserts the exact numeric damage delta per type (not just "health improved").
    /// </summary>
    private async Task AssertLitanyHealsExactDelta(
        ProtoId<LitanyPrototype> litany,
        ProtoId<NeoTheologyProfilePrototype> profile,
        bool adjacentLiving,
        bool adjacentFollower,
        params (string Type, float Seed, float Heal)[] expected)
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default;
        var before = new Dictionary<string, float>();

        await Server.WaitAssertion(() =>
        {
            body = PrepareCaster(map.GridCoords, litany);
            Assert.That(_cruciform.TrySetProfile(body, profile), Is.True,
                $"{litany.Id} requires the {profile.Id} profile's litany set.");

            if (adjacentLiving || adjacentFollower)
            {
                var neighbour = SSpawnAtPosition(HumanProto, map.GridCoords.Offset(new Vector2(1f, 0f)));
                if (adjacentFollower)
                {
                    Assert.That(_implants.AddImplant(neighbour, CruciformProto), Is.Not.Null);
                    Assert.That(_cruciform.Activate(neighbour), Is.True);
                }
            }

            var seed = new DamageSpecifier();
            foreach (var (type, amount, _) in expected)
                seed.DamageDict[type] = FixedPoint2.New(amount);
            _damageable.SetDamage(body, seed);

            StabilizeNeeds(body);
            foreach (var (type, _, _) in expected)
            {
                before[type] = DamageOf(body, type).Float();
                Assert.That(before[type], Is.GreaterThan(0f), $"{litany.Id} seed missing for {type}.");
            }

            var begin = _litany.TryBeginLitany(body, litany, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? $"{litany.Id} begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            var observed = new Dictionary<string, float>();
            var after = new Dictionary<string, float>();
            foreach (var (type, _, _) in expected)
            {
                after[type] = DamageOf(body, type).Float();
                observed[type] = before[type] - after[type];
            }

            var healSpec = string.Join(", ",
                _prototypes.Index(litany).Effects.OfType<LitanyHealEffect>()
                    .SelectMany(heal => heal.Damage.DamageDict)
                    .Select(kv => $"{kv.Key}={kv.Value}"));

            foreach (var (type, _, heal) in expected)
            {
                Assert.That(observed[type], Is.EqualTo(heal).Within(Math.Max(1f, heal * 0.1f)),
                    $"{litany.Id} must heal {heal} {type}; observed {observed[type]}. " +
                    $"Before=[{string.Join(", ", before.Select(kv => $"{kv.Key}:{kv.Value:F2}"))}] " +
                    $"After=[{string.Join(", ", after.Select(kv => $"{kv.Key}:{kv.Value:F2}"))}] Spec=[{healSpec}]");
            }
        });
    }

    /// <summary>
    /// Prevent environment drift from skewing medical damage asserts during waits:
    /// satiation decay damage is removed, and the vacuum test map's 1 Hz barotrauma
    /// damage is disabled so a long cast (Succour's extra delay) cannot outrun the heal.
    /// </summary>
    private void StabilizeNeeds(EntityUid body)
    {
        if (SEntMan.TryGetComponent(body, out SatiationComponent satiation))
        {
            var sat = new Entity<SatiationComponent>(body, satiation);
            // Keep Hunger mid-range so SoulHunger tests can still add nutrition.
            if (_satiation.GetMaximumValue(sat, SatiationSystem.Hunger) is { } maxH)
                _satiation.SetValue(sat, SatiationSystem.Hunger, Math.Min(80f, (float)maxH));
            if (_satiation.GetMaximumValue(sat, SatiationSystem.Thirst) is { } maxT)
                _satiation.SetValue(sat, SatiationSystem.Thirst, (float)maxT);
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

    private static void AssertFixed(FixedPoint2 actual, FixedPoint2 expected, string message = "")
    {
        Assert.That(actual.Float(), Is.EqualTo(expected.Float()).Within(1.0f), message);
    }

    private EntityUid PrepareCaster(EntityCoordinates coords, ProtoId<LitanyPrototype> litany)
    {
        _litany.TestingClearAvailabilityOverrides();
        _litany.TestingClearActors();
        _litany.TestingSetAvailabilityOverride(litany.Id, true);
        var body = SSpawnAtPosition(HumanProto, coords);
        _litany.TestingTreatAsActor(body);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);
        Assert.That(_cruciform.GetHoliness(body), Is.GreaterThanOrEqualTo(_prototypes.Index(litany).Cost));
        StabilizeNeeds(body);
        return body;
    }

    private void ClearPersonalCooldown(EntityUid body, string cooldownKey)
    {
        var bearer = SComp<CruciformBearerComponent>(body);
        bearer.PersonalCooldowns.Remove(cooldownKey);
    }

    private int CountEdibles()
    {
        var count = 0;
        var query = SEntMan.EntityQueryEnumerator<EdibleComponent>();
        while (query.MoveNext(out _, out _))
            count++;
        return count;
    }

    /// <summary>
    /// Replays a litany DoAfter completion for a request that should already be gone / committed.
    /// OnLitanyDoAfter returns immediately when the request is absent, before touching DoAfter state.
    /// Must not debit or re-apply effects.
    /// </summary>
    private void RaiseStaleLitanyCompletion(EntityUid body, string requestId)
    {
        SEntMan.EventBus.RaiseLocalEvent(body, new LitanyDoAfterEvent(requestId));
    }

    private async Task AdvancePastCast()
    {
        // Succour carries an extra 4 s delay on top of the chant; 150 iterations leave
        // ample headroom at the default tick period and exit as soon as the cast settles.
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
