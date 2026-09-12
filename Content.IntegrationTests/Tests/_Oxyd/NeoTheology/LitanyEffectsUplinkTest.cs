using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server.Atmos.Components;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Implants;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// NtUplink packet: Knowledge reports the hidden uplink's telecrystals and Bounty opens the store
/// (Eris <c>rituals/inquisitor.dm:291-327</c>). The uplink lives on the inquisitor's cruciform
/// (Eris <c>modules.dm:30-53</c>) and its balance survives a module swap.
/// </summary>
[TestOf(typeof(NtUplinkSystem))]
public sealed class LitanyEffectsUplinkTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly ProtoId<CoreModulePrototype> UplinkModule = "OxydNtModuleUplink";
    private static readonly ProtoId<LitanyPrototype> Knowledge = "OxydLitanyKnowledge";
    private static readonly ProtoId<LitanyPrototype> Bounty = "OxydLitanyBounty";
    private static readonly ProtoId<CurrencyPrototype> Telecrystal = "Telecrystal";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly CoreModuleSystem _modules = default!;
    [SidedDependency(Side.Server)] private readonly NtUplinkSystem _uplink = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly UserInterfaceSystem _ui = default!;
    [SidedDependency(Side.Server)] private readonly SatiationSystem _satiation = default!;

    [Test]
    public async Task Knowledge_FirstUse_CreatesUplinkWithFifteenTelecrystals()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid cruciform = default;

        await Server.WaitAssertion(() =>
        {
            caster = PrepareCaster(map.GridCoords);
            cruciform = SComp<CruciformBearerComponent>(caster).Cruciform!.Value;

            var begin = _litany.TryBeginLitany(caster, Knowledge, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Knowledge begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_uplink.TryGetStore(cruciform, out var store), Is.True,
                "Knowledge must create the hidden uplink.");
            var storeComp = SComp<StoreComponent>(store!.Value);
            Assert.That(storeComp.Balance[Telecrystal], Is.EqualTo(FixedPoint2.New(15)),
                "A fresh uplink starts with 15 telecrystals (Eris modules.dm:31).");
        });
    }

    [Test]
    public async Task Bounty_OpensTheUplinkStore()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid cruciform = default;

        await Server.WaitAssertion(() =>
        {
            caster = PrepareCaster(map.GridCoords);
            cruciform = SComp<CruciformBearerComponent>(caster).Cruciform!.Value;

            var begin = _litany.TryBeginLitany(caster, Bounty, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Bounty begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_uplink.TryGetStore(cruciform, out var store), Is.True,
                "Bounty must create the hidden uplink.");
            Assert.That(_ui.GetActors(store!.Value, StoreUiKey.Key), Does.Contain(caster),
                "Bounty must open the hidden uplink interface for the caster.");
        });
    }

    [Test]
    public async Task UplinkBalance_SurvivesModuleSwap()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var caster = PrepareCaster(map.GridCoords);
            var cruciform = SComp<CruciformBearerComponent>(caster).Cruciform!.Value;
            var component = SComp<CruciformComponent>(cruciform);

            Assert.That(_uplink.TryGetUplink(cruciform, out var uplink), Is.True,
                "The inquisitor rank must install the uplink module.");
            var store = _uplink.GetOrCreateStore(caster, cruciform, uplink!);
            SComp<StoreComponent>(store).Balance[Telecrystal] = FixedPoint2.New(7);

            Assert.That(_modules.TryRemove(cruciform, component, UplinkModule), Is.True);
            Assert.That(_uplink.TryGetStore(cruciform, out _), Is.False,
                "Uninstalling the module must destroy the store.");
            Assert.That(_uplink.TryGetUplink(cruciform, out _), Is.False,
                "Uninstalling the module must gate the uplink.");

            Assert.That(_modules.TryInstall(cruciform, component, UplinkModule), Is.True);
            Assert.That(_uplink.TryGetUplink(cruciform, out var reinstalled), Is.True);
            var reopened = _uplink.GetOrCreateStore(caster, cruciform, reinstalled!);
            Assert.That(SComp<StoreComponent>(reopened).Balance[Telecrystal], Is.EqualTo(FixedPoint2.New(7)),
                "The telecrystal balance must survive a module swap.");
        });
    }

    /// <summary>
    /// A living inquisitor with the test actor flag and a full cruciform, so its rank modules
    /// (including the uplink module) are installed.
    /// </summary>
    private EntityUid PrepareCaster(EntityCoordinates coords)
    {
        _litany.TestingClearAvailabilityOverrides();
        _litany.TestingClearActors();
        var body = SSpawnAtPosition(HumanProto, coords);
        _litany.TestingTreatAsActor(body);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);

        var component = SComp<CruciformComponent>(implant!.Value);
        _cruciform.MakeInquisitor(implant.Value, component);
        component.Holiness = component.MaxHoliness;
        StabilizeNeeds(body);
        return body;
    }

    /// <summary>
    /// Removes the environment drift the other litany suites also strip: satiation decay damage and
    /// the vacuum test map's barotrauma so a ~1-2 s cast cannot kill the participants.
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
