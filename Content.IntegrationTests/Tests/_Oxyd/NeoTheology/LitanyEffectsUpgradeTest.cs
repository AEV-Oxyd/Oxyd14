using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Server.Atmos.Components;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Implants;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P4.8 attachments: InstallUpgrade moves the upgrade resting on the altar into the target's
/// cruciform (raising MaxHoliness by its delta); UninstallUpgrade returns that same item to the
/// altar and reverts the derived stats exactly.
/// </summary>
[TestOf(typeof(CruciformUpgradeSystem))]
public sealed class LitanyEffectsUpgradeTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly EntProtoId AltarProto = "OxydNtAltar";
    private static readonly EntProtoId UpgradeProto = "OxydNtUpgradeFaithsShield";

    private static readonly ProtoId<LitanyPrototype> InstallUpgrade = "OxydLitanyInstallUpgrade";
    private static readonly ProtoId<LitanyPrototype> UninstallUpgrade = "OxydLitanyUninstallUpgrade";

    private const float UpgradeHolinessDelta = 25f;

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly CruciformUpgradeSystem _upgrades = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly SharedContainerSystem _containers = default!;
    [SidedDependency(Side.Server)] private readonly AltarSystem _altar = default!;
    [SidedDependency(Side.Server)] private readonly SatiationSystem _satiation = default!;

    [Test]
    public async Task InstallUpgrade_MovesTheAltarUpgradeIntoTheCruciformAndRaisesMaxHoliness()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid follower = default;
        EntityUid upgrade = default;
        double before = 0;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin);
            follower = SpawnFollower(origin.Offset(new Vector2(1f, 0f)));
            SSpawnAtPosition(AltarProto, origin.Offset(new Vector2(1f, 0.7f)));
            upgrade = SpawnAltarUpgrade(origin.Offset(new Vector2(1f, 1f)));

            Assert.That(_cruciform.TryGetCruciform(follower, out _, out var component), Is.True);
            before = component.MaxHoliness;
            Assert.That(_containers.IsEntityInContainer(upgrade), Is.False,
                "Setup: the upgrade must start loose on the altar.");

            var begin = _litany.TryBeginLitany(caster, InstallUpgrade, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "InstallUpgrade begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_cruciform.TryGetCruciform(follower, out _, out var component), Is.True);
            Assert.That(component.Upgrade, Is.EqualTo(upgrade),
                "The very same altar item must be attached, not a copy.");
            Assert.That(_containers.IsEntityInContainer(upgrade), Is.True,
                "The installed attachment must leave the altar (Eris forceMove(_cruciform)).");
            Assert.That(component.MaxHoliness, Is.EqualTo(before + UpgradeHolinessDelta).Within(0.001),
                "The installed attachment must raise MaxHoliness by its delta.");
        });
    }

    [Test]
    public async Task UninstallUpgrade_ReturnsTheItemToTheAltarAndRevertsStatsExactly()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid follower = default;
        EntityUid altar = default;
        EntityUid upgrade = default;
        double before = 0;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin);
            follower = SpawnFollower(origin.Offset(new Vector2(1f, 0f)));
            altar = SSpawnAtPosition(AltarProto, origin.Offset(new Vector2(1f, 0.7f)));
            upgrade = SpawnAltarUpgrade(origin.Offset(new Vector2(1f, 1f)));

            Assert.That(_cruciform.TryGetCruciform(follower, out var cruciform, out var component), Is.True);
            before = component.MaxHoliness;

            Assert.That(_upgrades.TryInstallUpgrade(cruciform, component, upgrade), Is.True,
                "Setup: the attachment must install.");
            Assert.That(_containers.IsEntityInContainer(upgrade), Is.True,
                "Setup: an installed attachment leaves the altar.");
            Assert.That(component.MaxHoliness, Is.EqualTo(before + UpgradeHolinessDelta).Within(0.001),
                "Setup: the delta must be live before the removal.");

            var begin = _litany.TryBeginLitany(caster, UninstallUpgrade, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "UninstallUpgrade begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_cruciform.TryGetCruciform(follower, out _, out var component), Is.True);
            Assert.That(component.Upgrade, Is.Null, "The slot must be free again.");
            Assert.That(component.MaxHoliness, Is.EqualTo(before).Within(0.001),
                "Uninstall must revert the derived stats exactly, not approximately.");
            Assert.That(_containers.IsEntityInContainer(upgrade), Is.False,
                "The attachment must return to the world.");

            Assert.That(_altar.TryFindItemOnAltar<CruciformUpgradeComponent>(altar, out var found), Is.True,
                "The attachment must land back on the altar's turf (Eris forceMove(get_turf(wearer))).");
            Assert.That(found, Is.EqualTo(upgrade), "It must be the same item, not a respawn.");
        });
    }

    /// <summary>Centre of the tile at <paramref name="gridCoords"/> so tile math is unambiguous.</summary>
    private static EntityCoordinates TileCentre(EntityCoordinates gridCoords)
        => gridCoords.Offset(new Vector2(0.5f, 0.5f));

    /// <summary>A living disciple with the test actor flag and a full cruciform.</summary>
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

    /// <summary>A living active bearer: the follower an attachment is installed on.</summary>
    private EntityUid SpawnFollower(EntityCoordinates coords)
    {
        var body = SSpawnAtPosition(HumanProto, coords);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);
        StabilizeNeeds(body);
        return body;
    }

    /// <summary>The YAML upgrade entity carrying a test MaxHoliness delta, loose on the altar.</summary>
    private EntityUid SpawnAltarUpgrade(EntityCoordinates coords)
    {
        var item = SSpawnAtPosition(UpgradeProto, coords);
        SComp<CruciformUpgradeComponent>(item).MaxHolinessDelta = UpgradeHolinessDelta;
        return item;
    }

    /// <summary>
    /// Removes the environment drift the other litany suites also strip: satiation decay damage
    /// and the vacuum test map's barotrauma so a ~1-2 s cast cannot kill the participants.
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
