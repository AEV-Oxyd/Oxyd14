using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._Oxyd.Medical;
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Server.Atmos.Components;
using Content.Server.Body.Components;
using Content.Shared._Oxyd.Medical;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Buckle;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.EntityEffects;
using Content.Shared.EntityEffects.Effects.StatusEffects;
using Content.Shared.Inventory;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Nutrition.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

[TestOf(typeof(PainSystem))]
public sealed class LitanyMedicalDependenciesTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, DummyTicker = false };

    [TestCase("OxydLitanyRelief", "OxydNtAngelsBalm", 15, true)]
    [TestCase("OxydLitanyHandOfMercy", "OxydNtDeusBlessing", 15, false)]
    [TestCase("OxydLitanyAbsolutionOfWounds", "OxydNtHolyInaprovaline", 10, false)]
    public async Task HolyDoseGoesToThePatientWithoutInstantWoundHealing(string litanyId, string reagent, int dose, bool self)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var caster = Patient(map.GridCoords);
            var patient = self ? caster : Patient(map.GridCoords.Offset(Vector2.UnitX));
            var damage = SEntMan.System<DamageableSystem>();
            damage.SetDamage(patient, new DamageSpecifier { DamageDict = { ["Blunt"] = 20 } });
            var effects = SEntMan.System<LitanyEffectSystem>();
            var litany = Server.ProtoMan.Index<LitanyPrototype>(litanyId);
            Assert.That(effects.TryValidateEffects(caster, litany, out var failure, [patient]), Is.True, failure?.Id);
            Assert.That(effects.TryApplyEffects(caster, litany, [patient]), Is.True);
            Assert.That(BloodAmount(patient, reagent), Is.EqualTo(dose));
            Assert.That(damage.GetAllDamage(patient).DamageDict["Blunt"].Float(), Is.EqualTo(20));
            if (!self)
                Assert.That(BloodAmount(caster, reagent), Is.Zero);
            if (litanyId == "OxydLitanyAbsolutionOfWounds")
                Assert.That(BloodAmount(patient, "OxydNtHolyDexalin"), Is.EqualTo(10));
        });
    }

    [Test]
    public async Task PainRecoversAndAnalgesicsDoNotStackWithTheSameSource()
    {
        var map = await Pair.CreateTestMap();
        EntityUid patient = default;
        await Server.WaitAssertion(() =>
        {
            patient = Patient(map.GridCoords);
            var pain = SEntMan.System<PainSystem>();
            var atonement = new LitanyPainEvent(patient, 50, false);
            SEntMan.EventBus.RaiseLocalEvent(patient, ref atonement);
            Assert.That(atonement.Handled, Is.True);
            Assert.That(SComp<StaminaComponent>(patient).StaminaDamage, Is.Zero);
            Assert.That(SComp<PainComponent>(patient).CurrentPain, Is.EqualTo(66.5f).Within(0.01));
            var movement = new RefreshMovementSpeedModifiersEvent();
            SEntMan.EventBus.RaiseLocalEvent(patient, movement);
            Assert.That(movement.WalkSpeedModifier, Is.EqualTo(0.8f));
            pain.SuppressPain(patient, "test", 25, 2);
            pain.SuppressPain(patient, "test", 25, 2);
            Assert.That(SComp<PainComponent>(patient).CurrentPain, Is.EqualTo(41.5f).Within(0.01));
            Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(patient).Float(), Is.Zero);
            SEntMan.System<SharedEntityEffectsSystem>().ApplyEffect(patient, new ModifyStatusEffect
            {
                EffectProto = "StatusEffectPainNumbness",
                Time = TimeSpan.FromSeconds(2),
            });
        });
        await RunSeconds(0.1f);
        await Server.WaitAssertion(() => Assert.That(SComp<PainComponent>(patient).CurrentPain, Is.Zero));
        await RunSeconds(3f);
        await Server.WaitAssertion(() =>
        {
            var state = SComp<PainComponent>(patient);
            Assert.That(state.Analgesics, Is.Empty);
            Assert.That(state.TemporaryPain, Is.LessThan(50));
            Assert.That(state.CurrentPain, Is.GreaterThan(60));
            Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(patient).Float(), Is.Zero);
        });
    }

    [Test]
    public async Task MetabolismCreatesDependenceAndPurgingAdvancesRecoveryWithoutDetox()
    {
        var map = await Pair.CreateTestMap();
        EntityUid patient = default;
        await Server.WaitAssertion(() =>
        {
            patient = Patient(map.GridCoords);
            AddReagent(patient, "Nicotine", 20);
        });
        await RunSeconds(1.2f);
        await Server.WaitAssertion(() =>
        {
            var addiction = SComp<AddictionComponent>(patient);
            var dependence = addiction.Reagents["Nicotine"];
            Assert.That(dependence.Progress, Is.EqualTo(-15));
            Assert.That(dependence.PeakDose.Float(), Is.EqualTo(20));
            var before = BloodAmount(patient, "Nicotine");
            var purge = new LitanyPurgeAddictionEvent(patient, false);
            SEntMan.EventBus.RaiseLocalEvent(patient, ref purge);
            Assert.That(purge.Handled, Is.True);
            Assert.That(dependence.Progress, Is.Zero);
            Assert.That(BloodAmount(patient, "Nicotine"), Is.EqualTo(before));
            Assert.That(SComp<PainComponent>(patient).Analgesics["WordsOfPurging"].Strength, Is.EqualTo(15));

            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(patient, BloodstreamComponent.DefaultBloodSolutionName,
                out var blood, out var solution), Is.True);
            foreach (var content in solution.Contents.ToArray())
            {
                if (content.Reagent.Prototype == "Nicotine")
                    solutions.RemoveReagent(blood.Value, content.Reagent, content.Quantity);
            }
            addiction.UpdateInterval = 0.1f;
            addiction.UpdateRemaining = 0.1f;
            dependence.Progress = 49;
        });
        await RunSeconds(0.2f);
        await Server.WaitAssertion(() => Assert.That(SComp<AddictionComponent>(patient).Reagents, Is.Empty));
    }

    [TestCase("OxydNtAngelsBalm", 90)]
    [TestCase("OxydNtDeusBlessing", 90)]
    [TestCase("OxydNtHolyInaprovaline", 60)]
    [TestCase("OxydNtHolyDexalin", 30)]
    public async Task HolyMedicinesMetabolizeAndOverdoseOnlyAboveTheSourceLimit(string reagent, int limit)
    {
        var map = await Pair.CreateTestMap();
        EntityUid safe = default;
        EntityUid overdose = default;
        await Server.WaitAssertion(() =>
        {
            safe = Patient(map.GridCoords);
            overdose = Patient(map.GridCoords.Offset(Vector2.UnitX));
            AddReagent(safe, reagent, limit);
            AddReagent(overdose, reagent, limit + 1);
        });
        await RunSeconds(1.2f);
        await Server.WaitAssertion(() =>
        {
            var damage = SEntMan.System<DamageableSystem>();
            Assert.That(BloodAmount(safe, reagent), Is.LessThan(limit));
            Assert.That(BloodAmount(overdose, reagent), Is.LessThan(limit + 1));
            damage.GetAllDamage(safe).DamageDict.TryGetValue("Poison", out var safePoison);
            damage.GetAllDamage(overdose).DamageDict.TryGetValue("Poison", out var overdosePoison);
            Assert.That(safePoison.Float(), Is.Zero);
            Assert.That(overdosePoison.Float(), Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task BiologicalRejectionBlocksOnlyTheSourceRestrictedRites()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = Patient(map.GridCoords);
            SEntMan.AddComponent<AtheistMutationComponent>(patient);
            SEntMan.EnsureComponent<SanityComponent>(patient);
            var effects = SEntMan.System<LitanyEffectSystem>();
            foreach (var id in new[] { "OxydLitanyHandOfMercy", "OxydLitanyWordsOfPurging", "OxydLitanyRevelation" })
            {
                var litany = Server.ProtoMan.Index<LitanyPrototype>(id);
                Assert.That(effects.TryValidateEffects(patient, litany, out var failure, [patient]), Is.False, id);
                Assert.That(failure?.Id, Is.EqualTo("oxyd-litany-biological-rejection"), id);
            }
            var relief = Server.ProtoMan.Index<LitanyPrototype>("OxydLitanyRelief");
            Assert.That(effects.TryValidateEffects(patient, relief, out _, [patient]), Is.True);
        });
    }

    [Test]
    public async Task RoboticPrototypesUseNativeOrgans()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            foreach (var part in new[] { "Arm", "Hand", "Leg", "Foot" })
            foreach (var side in new[] { "Left", "Right" })
            {
                var organ = SSpawnAtPosition($"OxydOrganRobotic{part}{side}", map.GridCoords);
                Assert.That(SEntMan.HasComponent<RoboticOrganComponent>(organ), Is.True);
                Assert.That(SEntMan.HasComponent<DetachableOrganComponent>(organ), Is.True);
                Assert.That(SComp<OrganComponent>(organ).Category?.Id, Is.EqualTo(part + side));
            }
        });
    }

    [Test]
    public async Task RoboticRejectionPreservesNaturalOrgansAndTheDetachedEntity()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = Patient(map.GridCoords);
            var organs = SEntMan.EntityQuery<OrganComponent>().Where(o => o.Body == patient).ToArray();
            var hand = organs.Single(o => o.Category == "HandLeft");
            SEntMan.AddComponent<RoboticOrganComponent>(hand.Owner);
            Assert.That(SEntMan.System<RoboticOrganSystem>().Reject(patient), Is.EqualTo(1));
            Assert.That(SEntMan.EntityExists(hand.Owner), Is.True);
            Assert.That(hand.Body, Is.Not.EqualTo(patient));
            foreach (var natural in organs.Where(o => o.Owner != hand.Owner))
                Assert.That(natural.Body, Is.EqualTo(patient));
            Assert.That(SEntMan.System<RoboticOrganSystem>().Reject(patient), Is.Zero);
        });
    }

    [TestCase("ClothingUniformJumpsuitColorGrey", "jumpsuit")]
    [TestCase("ClothingHandsGlovesColorBlack", "pocket1")]
    public async Task AltarProceduresRequireTheActualUndressedOccupant(string clothingId, string slot)
    {
        var map = await Pair.CreateMachineTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = Patient(map.GridCoords);
            var altar = SSpawnAtPosition("OxydNtAltar", map.GridCoords);
            var effects = SEntMan.System<LitanyEffectSystem>();
            Assert.That(effects.TryGetProcedureAltar(patient, true, out _, out _), Is.False);
            var buckle = SEntMan.System<SharedBuckleSystem>();
            Assert.That(buckle.TryBuckle(patient, null, altar), Is.True);
            Assert.That(effects.TryGetProcedureAltar(patient, true, out var actual, out _), Is.True);
            Assert.That(actual, Is.EqualTo(altar));
            var clothing = SSpawnAtPosition(clothingId, map.GridCoords);
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(patient, clothing, slot, force: true), Is.True);
            Assert.That(effects.TryGetProcedureAltar(patient, true, out _, out var reason), Is.False);
            Assert.That(reason?.Id, Is.EqualTo("oxyd-litany-procedure-clothing"));
            Assert.That(effects.TryGetProcedureAltar(patient, false, out _, out _), Is.True,
                "Eris requires clothing removal for installation, but not upgrade removal.");
            buckle.Unbuckle(patient, null);
            Assert.That(effects.TryGetProcedureAltar(patient, false, out _, out _), Is.False);
        });
    }

    [Test]
    public async Task RejectionEjectsPermanentForeignImplantsWithoutDeletingThemOrTheCruciform()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = Patient(map.GridCoords);
            var implants = SEntMan.System<SharedSubdermalImplantSystem>();
            var cruciform = implants.AddImplant(patient, "OxydNtCruciform")!.Value;
            var foreign = implants.AddImplant(patient, "TrackingImplant")!.Value;
            SComp<SubdermalImplantComponent>(foreign).Permanent = true;
            var reject = new LitanyRejectForeignBodyEvent(patient, false);
            SEntMan.EventBus.RaiseLocalEvent(patient, ref reject);
            Assert.That(reject.Handled, Is.True);
            Assert.That(SEntMan.EntityExists(foreign), Is.True);
            Assert.That(SComp<SubdermalImplantComponent>(foreign).ImplantedEntity, Is.Null);
            Assert.That(SComp<SubdermalImplantComponent>(cruciform).ImplantedEntity, Is.EqualTo(patient));
            Assert.That(SComp<CruciformBearerComponent>(patient).Cruciform, Is.EqualTo(cruciform));
            Assert.That(SEntMan.System<DamageableSystem>().GetAllDamage(patient).DamageDict["Blunt"].Float(), Is.EqualTo(20));
        });
    }

    [TestCase("OxydNtHolyInaprovaline", 15)]
    [TestCase("OxydNtHolyDexalin", 0)]
    public async Task HolyMedicineTreatsOxygenDamageThroughMetabolism(string reagent, int analgesia)
    {
        var map = await Pair.CreateTestMap();
        EntityUid patient = default;
        await Server.WaitAssertion(() =>
        {
            patient = Patient(map.GridCoords);
            SEntMan.System<DamageableSystem>().SetDamage(patient,
                new DamageSpecifier { DamageDict = { ["Blunt"] = 100, ["Asphyxiation"] = 20 } });
            AddReagent(patient, reagent, 10);
        });
        await RunSeconds(1.2f);
        await Server.WaitAssertion(() =>
        {
            var damage = SEntMan.System<DamageableSystem>().GetAllDamage(patient).DamageDict;
            Assert.That(damage["Asphyxiation"].Float(), Is.LessThan(20));
            Assert.That(damage["Blunt"].Float(), Is.EqualTo(100));
            Assert.That(SComp<PainComponent>(patient).CurrentPain, Is.EqualTo(100 - analgesia));
        });
    }

    private EntityUid Patient(EntityCoordinates coordinates)
    {
        var patient = SSpawnAtPosition("MobHuman", coordinates);
        SEntMan.RemoveComponent<BarotraumaComponent>(patient);
        SEntMan.RemoveComponent<SatiationDamageComponent>(patient);
        SEntMan.RemoveComponent<PassiveDamageComponent>(patient);
        SEntMan.RemoveComponent<RespiratorComponent>(patient);
        return patient;
    }

    private float BloodAmount(EntityUid patient, string reagent)
    {
        Assert.That(SEntMan.System<SharedSolutionContainerSystem>().TryGetSolution(patient,
            BloodstreamComponent.DefaultBloodSolutionName, out _, out var blood), Is.True);
        return blood.GetTotalPrototypeQuantity(reagent).Float();
    }

    private void AddReagent(EntityUid patient, string reagent, int amount)
    {
        var solutions = SEntMan.System<SharedSolutionContainerSystem>();
        Assert.That(solutions.TryGetSolution(patient, BloodstreamComponent.DefaultBloodSolutionName,
            out var blood, out _), Is.True);
        Assert.That(solutions.TryAddReagent(blood.Value, reagent, amount), Is.True);
    }
}
