using Content.Shared.Body;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Robust.Shared.Physics.Components;
using Robust.Shared.Serialization.Manager;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Robust.Server.Player;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// The behaviour side of <see cref="CoreModulePrototype"/>: modules that are genuinely special
/// subscribe to the lifecycle events instead of the module just being data.
/// </summary>
/// <remarks>
/// Currently two modules: the cloning module writes the wearer's soul onto the cruciform on both
/// install and uninstall (Eris <c>datum/core_module/cruciform/cloning</c>), and the uplink module
/// hands off to <see cref="NtUplinkSystem"/> (Eris <c>datum/core_module/cruciform/uplink</c>).
/// </remarks>
public sealed partial class CoreModuleBehaviorSystem : EntitySystem
{
    private static readonly ProtoId<CoreModulePrototype> CloningModule = "OxydNtModuleCloning";
    private static readonly ProtoId<CoreModulePrototype> UplinkModule = "OxydNtModuleUplink";

    [Dependency] private readonly ISerializationManager _serialization = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly NtUplinkSystem _uplink = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CruciformComponent, CoreModuleInstalledEvent>(OnModuleInstalled);
        SubscribeLocalEvent<CruciformComponent, CoreModuleUninstalledEvent>(OnModuleUninstalled);
        SubscribeLocalEvent<CruciformBearerComponent, LitanyWriteSoulSnapshotEvent>(OnLitanyWriteSoulSnapshot);
    }

    private void OnModuleInstalled(EntityUid cruciform, CruciformComponent comp, ref CoreModuleInstalledEvent args)
    {
        if (args.Module == CloningModule)
            WriteSnapshot(cruciform, comp);
        else if (args.Module == UplinkModule)
            _uplink.OnUplinkInstalled(cruciform);
    }

    private void OnModuleUninstalled(EntityUid cruciform, CruciformComponent comp, ref CoreModuleUninstalledEvent args)
    {
        if (args.Module == CloningModule)
            WriteSnapshot(cruciform, comp);
        else if (args.Module == UplinkModule)
            _uplink.OnUplinkUninstalled(cruciform);
    }

    /// <summary>
    /// Reincarnation bridge (Eris <c>rituals/base.dm:258-310</c>): the litany refreshes the
    /// stored soul from the living wearer onto their installed cruciform. Raised on the body, so
    /// the handler resolves the implant from the bearer link first.
    /// </summary>
    private void OnLitanyWriteSoulSnapshot(EntityUid body, CruciformBearerComponent bearer, ref LitanyWriteSoulSnapshotEvent args)
    {
        if (bearer.Cruciform is not { } cruciform ||
            !TryComp<CruciformComponent>(cruciform, out var comp))
        {
            return;
        }

        args.Handled = WriteSnapshot(cruciform, comp);
    }

    /// <summary>
    /// Records the wearer's identity on the cruciform. A cruciform with nobody in it never
    /// clobbers an existing snapshot, so a soul written while alive survives the body's death.
    /// </summary>
    public bool WriteSnapshot(EntityUid cruciform, CruciformComponent comp)
    {
        if (comp.ImplantedEntity is not { } body || !TryComp<HumanoidProfileComponent>(body, out var humanoid))
            return false;

        var soul = EnsureComp<CruciformSoulComponent>(cruciform);
        soul.HasSnapshot = true;
        soul.Name = MetaData(body).EntityName;
        soul.MindId = null;
        soul.Ckey = null;
        soul.BiomassCost = TryComp<PhysicsComponent>(body, out var physics)
            ? Math.Max(1, (int) Math.Round(physics.FixturesMass))
            : 100;

        // Read the body, not the selected lobby character.
        var profile = HumanoidCharacterProfile.DefaultWithSpecies(humanoid.Species, humanoid.Sex)
            .WithAge(humanoid.Age).WithGender(humanoid.Gender).WithVoice(humanoid.Voice);
        profile.Name = soul.Name;
        var organs = EntityQueryEnumerator<OrganComponent>();
        while (organs.MoveNext(out var organUid, out var organ))
        {
            if (organ.Body != body || organ.Category is not { } category)
                continue;

            if (TryComp<VisualOrganComponent>(organUid, out var visual))
            {
                var layer = visual.Layer;
                if (layer.Equals(HumanoidVisualLayers.Chest))
                    profile.Appearance.SkinColor = visual.Profile.SkinColor;
                if (layer.Equals(HumanoidVisualLayers.Eyes))
                    profile.Appearance.EyeColor = visual.Profile.EyeColor;
            }

            if (TryComp<VisualOrganMarkingsComponent>(organUid, out var markings))
                profile.Appearance.Markings[category] = _serialization.CreateCopy(markings.Markings, notNullableOverride: true);
        }
        soul.Profile = profile;

        if (TryComp<MindContainerComponent>(body, out var container) &&
            container.Mind is { } mindId &&
            TryComp<MindComponent>(mindId, out var mind))
        {
            soul.MindId = mindId;

            if (mind.UserId is { } user && _player.TryGetSessionById(user, out var session))
            {
                soul.Ckey = session.Name;
            }
        }

        return true;
    }
}
