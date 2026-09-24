using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Implants;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// Phase 1 attachment slot: an installed upgrade rides the same derivation as profile ∪
/// modules inside <c>RecomputeProfile</c>, and removing it restores the exact prior values.
/// The upgrade component is built in-code so this stays a test of the slot, not of YAML.
/// </summary>
[TestOf(typeof(CruciformUpgradeSystem))]
public sealed class CruciformUpgradeTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly EntProtoId UpgradeCarrierProto = "SheetSteel1";
    private static readonly ProtoId<LitanySetPrototype> UpgradeOnlySet = "OxydLitanyPriest";
    private const float UpgradeHolinessDelta = 20f;

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly CruciformUpgradeSystem _upgrades = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;

    [Test]
    public async Task InstallingUpgradeRaisesMaxHolinessByDelta()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var (cruciform, comp, upgradeItem) = SetupUpgrade(map.GridCoords);
            var before = comp.MaxHoliness;

            Assert.That(comp.UnlockedSets, Does.Not.Contain(UpgradeOnlySet));

            Assert.That(_upgrades.TryInstallUpgrade(cruciform, comp, upgradeItem), Is.True);

            Assert.That(comp.MaxHoliness, Is.EqualTo(before + UpgradeHolinessDelta).Within(0.001));
            Assert.That(comp.UnlockedSets, Does.Contain(UpgradeOnlySet));

            Assert.That(_upgrades.TryInstallUpgrade(cruciform, comp, upgradeItem), Is.False,
                "The slot holds one attachment at a time.");
        });
    }

    [Test]
    public async Task UninstallingUpgradeRestoresExactPriorMaxHoliness()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var (cruciform, comp, upgradeItem) = SetupUpgrade(map.GridCoords);
            var before = comp.MaxHoliness;

            Assert.That(_upgrades.TryInstallUpgrade(cruciform, comp, upgradeItem), Is.True);
            Assert.That(_upgrades.TryUninstallUpgrade(cruciform, comp), Is.True);

            Assert.That(comp.MaxHoliness, Is.EqualTo(before));
            Assert.That(comp.UnlockedSets, Does.Not.Contain(UpgradeOnlySet));
            Assert.That(comp.Upgrade, Is.Null);
        });
    }

    private (EntityUid Cruciform, CruciformComponent Comp, EntityUid UpgradeItem) SetupUpgrade(EntityCoordinates coords)
    {
        var body = SSpawnAtPosition(HumanProto, coords);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null, "The test body must accept a cruciform implant.");
        var cruciform = implant!.Value;

        var item = SSpawnAtPosition(UpgradeCarrierProto, coords);
        var upgrade = SEntMan.AddComponent<CruciformUpgradeComponent>(item);
        upgrade.MaxHolinessDelta = UpgradeHolinessDelta;
        upgrade.LitanySets = new List<ProtoId<LitanySetPrototype>> { UpgradeOnlySet };

        return (cruciform, SComp<CruciformComponent>(cruciform), item);
    }
}
