using System;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.Medical;
using Content.Server.Stack;
using Content.Shared._Oxyd.Medical;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using ServerContainers = Robust.Server.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Oxyd.Medical;

/// <summary>
/// Q3: self-contained coverage for the Moebius medical half — autodoc credit charge
/// (B1), IV drip attach do-after + overflow (B2), resuscitator window/organ/damage
/// refusals (B4/B5), borer mind round-trip, surgery unique-category guard, and
/// organ decay pausing inside OxydOrganStasis containers.
/// </summary>
public sealed class MoebiusMedicalTest : GameTest
{
    private static readonly EntProtoId HumanProto = "MobHumanOxyd";
    private static readonly EntProtoId AutodocProto = "OxydMedicalAutodoc";
    private static readonly EntProtoId IvDripProto = "OxydMedicalIvDrip";
    private static readonly EntProtoId BorerProto = "OxydBorer";
    private static readonly EntProtoId CashProto = "SpaceCash";
    private static readonly EntProtoId BeakerProto = "Beaker";
    private static readonly EntProtoId TorsoOrganProto = "OrganHumanTorso";
    private static readonly EntProtoId AppendixOrganProto = "OrganHumanAppendix";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly OxydWoundSystem _wounds = default!;
    [SidedDependency(Side.Server)] private readonly SharedMindSystem _minds = default!;
    [SidedDependency(Side.Server)] private readonly MobStateSystem _mobState = default!;
    [SidedDependency(Side.Server)] private readonly MobThresholdSystem _threshold = default!;
    [SidedDependency(Side.Server)] private readonly DamageableSystem _damage = default!;
    [SidedDependency(Side.Server)] private readonly SharedSolutionContainerSystem _solutions = default!;
    [SidedDependency(Side.Server)] private readonly ServerContainers.ContainerSystem _container = default!;
    [SidedDependency(Side.Server)] private readonly StackSystem _stack = default!;
    [SidedDependency(Side.Server)] private readonly IGameTiming _timing = default!;
    [SidedDependency(Side.Server)] private readonly SharedHandsSystem _hands = default!;
    [SidedDependency(Side.Server)] private readonly DetachableOrganSystem _detach = default!;

    private Content.Shared.DoAfter.DoAfter FakeDoAfter(DoAfterEvent ev, EntityUid user,
        EntityUid? target, EntityUid? used = null)
    {
        return new Content.Shared.DoAfter.DoAfter(0,
            new DoAfterArgs(SEntMan, user, 1f, ev, user, target: target, used: used),
            _timing.CurTime);
    }

    private static EntityCoordinates TileCentre(EntityCoordinates gridCoords)
        => gridCoords.Offset(new Vector2(0.5f, 0.5f));

    /// <summary>B1: the autodoc charge consumes SpaceCash across stack boundaries —
    /// a balance below the cost refuses the run and keeps the stacks, a balance at
    /// the cost consumes the partial stack and leaves the remainder.</summary>
    [Test]
    public async Task AutodocChargePartialStacks()
    {
        var map = await Pair.CreateTestMap();
        var origin = TileCentre(map.GridCoords);
        EntityUid autodoc = default, patient = default, small = default, big = default;

        await Server.WaitAssertion(() =>
        {
            autodoc = SSpawnAtPosition(AutodocProto, origin);
            patient = SSpawnAtPosition(HumanProto, origin);
            Assert.That(_container.Insert(patient,
                _container.EnsureContainer<ContainerSlot>(autodoc, OxydAutodocComponent.BodyContainerId)),
                Is.True, "Patient must sit in the autodoc body slot.");

            // A damaged patient yields a scan note; the charge path is what is under test.
            var comp = SComp<OxydAutodocComponent>(autodoc);
            comp.Notes.Add(new OxydAutodocPatchnote
            {
                Scanned = { OxydAutodocOp.Damage },
                Picked = { OxydAutodocOp.Damage },
            });

            var credit = _container.EnsureContainer<Container>(autodoc, OxydAutodocComponent.CreditsContainerId);
            small = SSpawnAtPosition(CashProto, origin);
            _stack.SetCount((small, SComp<StackComponent>(small)), 500);
            Assert.That(_container.Insert(small, credit), Is.True);

            // 500 < 800 (OxydAutodocOp.Damage cost): refused, stack untouched.
            SEntMan.EventBus.RaiseLocalEvent(autodoc, new OxydAutodocProcessAllMessage());
            Assert.That(comp.Operating, Is.False, "Charge below cost must refuse the run.");
            Assert.That(SComp<StackComponent>(small).Count, Is.EqualTo(500));

            // Add a second stack: 500 + 1000 = 1500 vs cost 800 — partial stack
            // is consumed entirely, the large one loses exactly the cost.
            big = SSpawnAtPosition(CashProto, origin);
            _stack.SetCount((big, SComp<StackComponent>(big)), 1000);
            Assert.That(_container.Insert(big, credit), Is.True);
            SEntMan.EventBus.RaiseLocalEvent(autodoc, new OxydAutodocProcessAllMessage());

            Assert.That(comp.Operating, Is.True, "Charge at/above cost must start the run.");
            Assert.That(SComp<StackComponent>(big).Count, Is.EqualTo(700),
                "Charge must leave the remainder of the last stack (1000 - (800 - 500)).");
        });

        // ReduceCount queues the empty stack for deletion on the next tick.
        await Pair.RunTicksSync(1);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.Deleted(small), Is.True,
                "The smaller stack must be consumed by ReduceCount."));
    }

    /// <summary>B2: the IV attaches only through the attach do-after event (a cancelled
    /// one does nothing), and an inject tick against a full bloodstream loses no
    /// reagent — the transfer is capped by the destination's free volume.</summary>
    [Test]
    public async Task IvAttachRequiresDoAfterAndOverflowLosesNothing()
    {
        var map = await Pair.CreateTestMap();
        var origin = TileCentre(map.GridCoords);
        EntityUid drip = default, patient = default, surgeon = default, beaker = default;

        await Server.WaitAssertion(() =>
        {
            drip = SSpawnAtPosition(IvDripProto, origin);
            patient = SSpawnAtPosition(HumanProto, origin);
            surgeon = SSpawnAtPosition(HumanProto, origin.Offset(new Vector2(1f, 0f)));
            beaker = SSpawnAtPosition(BeakerProto, origin);
            Assert.That(_container.Insert(beaker,
                _container.EnsureContainer<ContainerSlot>(drip, OxydIvDripComponent.BeakerContainerId)),
                Is.True);

            var comp = SComp<OxydIvDripComponent>(drip);
            Assert.That(comp.AttachedTo, Is.Null, "Fresh drip is unattached.");

            // Dragging the drip onto the patient starts a do-after; it must not
            // attach instantly — the link only happens on do-after completion.
            var drag = new DragDropTargetEvent(surgeon, drip);
            SEntMan.EventBus.RaiseLocalEvent(patient, ref drag);
            Assert.That(comp.AttachedTo, Is.Null, "Attach must wait on the do-after.");

            // Completed do-after: attached.
            var done = new OxydIvAttachDoAfterEvent();
            done.DoAfter = FakeDoAfter(done, surgeon, patient, drip);
            SEntMan.EventBus.RaiseLocalEvent(drip, done);
            Assert.That(comp.AttachedTo, Is.EqualTo(SEntMan.GetNetEntity(patient)),
                "The completed attach do-after must link the drip to the patient.");

            // Fill the beaker and top the bloodstream to capacity.
            Assert.That(_solutions.TryGetSolution(beaker, "beaker", out var beakerEnt, out var beakerSol));
            Assert.That(_solutions.TryAddReagent(beakerEnt.Value, "Water", 50), Is.True);
            Assert.That(_solutions.TryGetSolution(patient,
                BloodstreamComponent.DefaultBloodSolutionName, out var bloodEnt, out var blood));
            blood.MaxVolume = FixedPoint2.New(300);
            blood.Volume = FixedPoint2.New(300);

            comp.NextTick = _timing.CurTime;
        });

        await Pair.RunTicksSync(90); // 3s > TickInterval: at least one inject tick fires.

        await Server.WaitAssertion(() =>
        {
            Assert.That(_solutions.TryGetSolution(beaker, "beaker", out var beakerEnt, out var beakerSol));
            Assert.That(_solutions.TryGetSolution(patient,
                BloodstreamComponent.DefaultBloodSolutionName, out _, out var blood));
            Assert.That(beakerSol.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(50)),
                "A full bloodstream must reject the transfer — nothing is lost from the beaker.");
        });
    }

    /// <summary>B4/B5: the resuscitator effect revives only inside the ToD window with
    /// viable heart+brain and total damage below the dead threshold.</summary>
    [Test]
    public async Task ResuscitatorWindowOrganAndDamageGates()
    {
        var map = await Pair.CreateTestMap();
        var origin = TileCentre(map.GridCoords);

        EntityUid SpawnDead(Vector2 off)
        {
            var body = SSpawnAtPosition(HumanProto, origin.Offset(off));
            _mobState.ChangeMobState(body, MobState.Dead);
            return body;
        }

        void Apply(EntityUid body)
        {
            var ev = new EntityEffectEvent<Resuscitate>(new Resuscitate(), 1f, null);
            SEntMan.EventBus.RaiseLocalEvent(body, ref ev);
        }

        // 1. Happy path: dead now, organs viable -> revived to Critical.
        await Server.WaitAssertion(() =>
        {
            var body = SpawnDead(new Vector2(0f, 0f));
            Apply(body);
            Assert.That(_mobState.IsCritical(body), Is.True,
                "A fresh dead body with viable heart+brain must revive to Critical.");
        });

        // 2. Outside the window: ToD 20 min ago -> stays dead.
        await Server.WaitAssertion(() =>
        {
            var body = SpawnDead(new Vector2(3f, 0f));
            var tod = SComp<OxydTimeOfDeathComponent>(body);
            tod.DiedAt = _timing.CurTime - TimeSpan.FromMinutes(20);
            Apply(body);
            Assert.That(_mobState.IsDead(body), Is.True,
                "A body past the revive window must not revive.");
        });

        // 3. Missing heart -> stays dead.
        await Server.WaitAssertion(() =>
        {
            var body = SpawnDead(new Vector2(6f, 0f));
            var heart = _wounds.GetOrgans(body).Find(o =>
                o.Organ.Category is { } c && c.Id == "Heart");
            Assert.That(heart.Organ, Is.Not.Null, "MobHumanOxyd must have a heart organ.");
            SEntMan.DeleteEntity(heart.Uid);
            Apply(body);
            Assert.That(_mobState.IsDead(body), Is.True,
                "A body with no heart must not revive.");
        });

        // 4. Total damage >= dead threshold -> stays dead (even with the asphyx cap).
        await Server.WaitAssertion(() =>
        {
            var body = SpawnDead(new Vector2(9f, 0f));
            Assert.That(_threshold.TryGetDeadThreshold(body, out var th), Is.True);
            var spec = new DamageSpecifier { DamageDict = { ["Blunt"] = th.Value.Float() + 50 } };
            _damage.TryChangeDamage(body, spec, ignoreResistances: true);
            Apply(body);
            Assert.That(_mobState.IsDead(body), Is.True,
                "A body too mangled to hold life must stay dead, not flicker Critical.");
        });
    }

    /// <summary>Borer assume -> release -> host-death: the borer's and host's minds
    /// return to their own bodies on every release path, with no lost or duplicated
    /// mind (assume swaps via the captive-brain entity, detach swaps back).</summary>
    [Test]
    public async Task BorerAssumeReleaseAndHostDeathRestoreMinds()
    {
        var map = await Pair.CreateTestMap();
        var origin = TileCentre(map.GridCoords);
        EntityUid borer = default, host = default;
        EntityUid borerMind = default, hostMind = default;

        await Server.WaitAssertion(() =>
        {
            borer = SSpawnAtPosition(BorerProto, origin);
            host = SSpawnAtPosition(HumanProto, origin);

            borerMind = _minds.CreateMind(null, "borer").Owner;
            hostMind = _minds.CreateMind(null, "host").Owner;
            _minds.TransferTo(borerMind, borer);
            _minds.TransferTo(hostMind, host);

            // Infest do-after completion links the two.
            var infest = new OxydBorerInfestDoAfterEvent();
            infest.DoAfter = FakeDoAfter(infest, borer, host);
            SEntMan.EventBus.RaiseLocalEvent(borer, infest);
            var comp = SComp<OxydBorerComponent>(borer);
            Assert.That(comp.Host, Is.EqualTo(host));
            Assert.That(SComp<OxydBorerHostComponent>(host).Borer, Is.EqualTo(borer));
        });

        // Assume control: borer mind drives the host, host mind parks in the captive.
        await Server.WaitAssertion(() =>
        {
            var assume = new OxydBorerAssumeControlDoAfterEvent();
            assume.DoAfter = FakeDoAfter(assume, borer, host);
            SEntMan.EventBus.RaiseLocalEvent(borer, assume);
            var comp = SComp<OxydBorerComponent>(borer);
            Assert.That(comp.Controlling, Is.True, "Assume completion must set Controlling.");
            Assert.That(_minds.TryGetMind(host, out var m1, out _) && m1 == borerMind,
                "The borer's mind must be driving the host body.");
            Assert.That(_minds.TryGetMind(comp.HostBrain!.Value, out var m2, out _) && m2 == hostMind,
                "The host's mind must be parked in the captive brain.");
        });

        // Release: both minds home.
        await Server.WaitAssertion(() =>
        {
            var release = new OxydBorerReleaseDoAfterEvent();
            release.DoAfter = FakeDoAfter(release, borer, host);
            SEntMan.EventBus.RaiseLocalEvent(borer, release);
            Assert.That(_minds.TryGetMind(host, out var m1, out _) && m1 == hostMind,
                "Release must restore the host's own mind.");
            Assert.That(_minds.TryGetMind(borer, out var m2, out _) && m2 == borerMind,
                "Release must return the borer's mind to the borer.");
            Assert.That(SComp<OxydBorerComponent>(borer).HostBrain, Is.Null);
            Assert.That(SComp<OxydBorerComponent>(borer).Controlling, Is.False);
        });

        // Re-infest + re-assume, then kill the host: host-death also detaches control.
        await Server.WaitAssertion(() =>
        {
            var infest = new OxydBorerInfestDoAfterEvent();
            infest.DoAfter = FakeDoAfter(infest, borer, host);
            SEntMan.EventBus.RaiseLocalEvent(borer, infest);
            var assume = new OxydBorerAssumeControlDoAfterEvent();
            assume.DoAfter = FakeDoAfter(assume, borer, host);
            SEntMan.EventBus.RaiseLocalEvent(borer, assume);
            Assert.That(SComp<OxydBorerComponent>(borer).Controlling, Is.True);

            _mobState.ChangeMobState(host, MobState.Dead);

            Assert.That(_minds.TryGetMind(borer, out var m1, out _) && m1 == borerMind,
                "Host death must return the borer's mind to the borer.");
            Assert.That(_minds.TryGetMind(host, out var m2, out _) && m2 == hostMind,
                "Host death must return the host's mind to the dead body.");
            Assert.That(SComp<OxydBorerComponent>(borer).Controlling, Is.False);
        });
    }

    /// <summary>Q9/transplant: an AttachOrgan attempt on a category the body already
    /// has never inserts a second organ of that category (the can_add_item unique-tag
    /// check), whatever the success roll lands.</summary>
    [Test]
    public async Task SurgeryUniqueCategoryGuard()
    {
        var map = await Pair.CreateTestMap();
        var origin = TileCentre(map.GridCoords);
        EntityUid patient = default, surgeon = default, spareTorso = default, target = default;

        await Server.WaitAssertion(() =>
        {
            patient = SSpawnAtPosition(HumanProto, origin);
            surgeon = SSpawnAtPosition(HumanProto, origin.Offset(new Vector2(1f, 0f)));
            spareTorso = SSpawnAtPosition(TorsoOrganProto, origin);

            // Open site: retracted incision on the patient's own torso.
            var torso = _wounds.GetOrgans(patient).Find(o =>
                o.Organ.Category is { } c && c.Id == "Torso");
            Assert.That(torso.Organ, Is.Not.Null, "MobHumanOxyd must have a torso organ.");
            target = torso.Uid;
            Assert.That(SEntMan.HasComponent<OxydOrganSurgeryComponent>(target), Is.True);
            var surg = SComp<OxydOrganSurgeryComponent>(target);
            surg.Incision = OxydIncisionStage.Retracted;
            SEntMan.Dirty(target, surg);

            var sessions = SEntMan.EnsureComponent<OxydSurgerySessionComponent>(patient);
            sessions.Sessions[surgeon] = new OxydSurgerySession { Tool = spareTorso };

            var torsosBefore = _wounds.GetOrgans(patient)
                .FindAll(o => o.Organ.Category is { } c && c.Id == "Torso").Count;
            Assert.That(torsosBefore, Is.EqualTo(1));

            var msg = new OxydSurgerySelectStepMessage(SEntMan.GetNetEntity(target), OxydSurgeryStep.AttachOrgan)
            {
                Actor = surgeon,
            };
            SEntMan.EventBus.RaiseLocalEvent(patient, msg);
        });

        await Pair.RunTicksSync(300); // outlast the 5s step do-after.

        await Server.WaitAssertion(() =>
        {
            var torsos = _wounds.GetOrgans(patient)
                .FindAll(o => o.Organ.Category is { } c && c.Id == "Torso");
            Assert.That(torsos, Has.Count.EqualTo(1),
                "The unique-category guard must refuse a second Torso-category organ.");
            Assert.That(SEntMan.Deleted(spareTorso), Is.False,
                "The refused transplant item must not be consumed.");
            Assert.That(_container.TryGetContainingContainer(spareTorso, out var held) && held.Owner == patient,
                Is.False, "The duplicate organ must not end up inside the body.");
        });
    }

    /// <summary>P2: organ decay is suspended while the organ sits inside an entity
    /// carrying OxydOrganStasis (freezer/stasis bag) — the decay tick advances
    /// NextDecay but never adds damage.</summary>
    [Test]
    public async Task OrganDecayPausesInStasis()
    {
        var map = await Pair.CreateTestMap();
        var origin = TileCentre(map.GridCoords);
        EntityUid organ = default, holder = default;

        await Server.WaitAssertion(() =>
        {
            organ = SSpawnAtPosition(AppendixOrganProto, origin);
            holder = SEntMan.SpawnEntity(null, origin);
            SEntMan.EnsureComponent<OxydOrganStasisComponent>(holder);
            Assert.That(_container.Insert(organ,
                _container.EnsureContainer<Container>(holder, "body")), Is.True);
        });

        await Pair.RunTicksSync(1200); // ~40s: several decay ticks under either timing.

        await Server.WaitAssertion(() =>
        {
            var surg = SComp<OxydOrganSurgeryComponent>(organ);
            Assert.That(surg.NextDecay, Is.GreaterThan(TimeSpan.Zero),
                "The decay loop must have advanced the organ's schedule at least once.");
            Assert.That(surg.OrganDamage, Is.EqualTo(0f),
                "An organ inside stasis must take zero decay damage.");
            Assert.That(surg.Decayed, Is.False);
        });
    }

    /// <summary>Q3: severing a limb (the head) packs its child organs into the
    /// detached-body shell, and re-attaching the shell through the AttachOrgan
    /// step restores them to the patient's body — organ ownership (OrganComponent.Body)
    /// resolves back to the patient and the empty shell is consumed.</summary>
    [Test]
    public async Task LimbReattachRestoresOrgans()
    {
        var map = await Pair.CreateTestMap();
        var origin = TileCentre(map.GridCoords);
        EntityUid patient = default, surgeon = default, torso = default,
            head = default, brain = default, shell = default;

        await Server.WaitAssertion(() =>
        {
            patient = SSpawnAtPosition(HumanProto, origin);
            surgeon = SSpawnAtPosition(HumanProto, origin.Offset(new Vector2(1f, 0f)));

            // The AttachOrgan roll is quality(100) - difficulty(35) = 65% — a laser
            // scalpel in hand makes the loop converge in a few attempts.
            var laser = SSpawnAtPosition("OxydSurgicalScalpelLaser", origin);
            Assert.That(_hands.TryForcePickupAnyHand(surgeon, laser), Is.True);

            var organs = _wounds.GetOrgans(patient);
            torso = organs.Find(o => o.Organ.Category is { } c && c.Id == "Torso").Uid;
            head = organs.Find(o => o.Organ.Category is { } c && c.Id == "Head").Uid;
            brain = organs.Find(o => o.Organ.Category is { } c && c.Id == "Brain").Uid;
            Assert.Multiple(() =>
            {
                Assert.That(torso.IsValid(), Is.True, "MobHumanOxyd must have a torso organ.");
                Assert.That(head.IsValid(), Is.True, "MobHumanOxyd must have a head organ.");
                Assert.That(brain.IsValid(), Is.True, "MobHumanOxyd must have a brain organ.");
            });

            // Open site for the reattach step.
            var surg = SComp<OxydOrganSurgeryComponent>(torso);
            surg.Incision = OxydIncisionStage.Retracted;
            SEntMan.Dirty(torso, surg);

            // Sever the head: it and its child organs (brain, eyes, tongue, ears)
            // must leave the patient inside the detached-body shell.
            shell = _detach.Detach(head) ?? EntityUid.Invalid;
            Assert.Multiple(() =>
            {
                Assert.That(shell.IsValid(), Is.True,
                    "Detaching the head must spawn a detached-body shell.");
                Assert.That(_container.TryGetContainer(shell, BodyComponent.ContainerID,
                        out var shellBox) && shellBox.ContainedEntities.Contains(brain), Is.True,
                    "The brain must travel with the severed head.");
                Assert.That(_wounds.GetOrgans(patient).Any(o => o.Uid == brain), Is.False,
                    "The brain must leave the patient's body.");
            });
        });

        // Step attempts: each is a 5s do-after; on a failed roll the organ takes
        // malpractice damage and the shell survives for the next try.
        for (var attempt = 0; attempt < 8 && SEntMan.EntityExists(shell); attempt++)
        {
            await Server.WaitAssertion(() =>
            {
                var sessions = SEntMan.EnsureComponent<OxydSurgerySessionComponent>(patient);
                sessions.Sessions[surgeon] = new OxydSurgerySession { Tool = shell };
                var msg = new OxydSurgerySelectStepMessage(
                    SEntMan.GetNetEntity(torso), OxydSurgeryStep.AttachOrgan)
                {
                    Actor = surgeon,
                };
                SEntMan.EventBus.RaiseLocalEvent(patient, msg);
            });

            await Pair.RunTicksSync(300);
        }

        await Server.WaitAssertion(() =>
        {
            var organs = _wounds.GetOrgans(patient);
            Assert.Multiple(() =>
            {
                Assert.That(organs.Any(o => o.Uid == brain), Is.True,
                    "The brain must be restored to the patient's body.");
                Assert.That(organs.Any(o => o.Uid == head), Is.True,
                    "The head must be restored to the patient's body.");
                Assert.That(SComp<OrganComponent>(brain).Body, Is.EqualTo(patient),
                    "The restored brain must be owned by the patient again.");
                Assert.That(SEntMan.Deleted(shell), Is.True,
                    "The emptied detached-body shell must be consumed.");
            });
        });
    }
}
