using Content.Server.Preferences.Managers;
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
/// Currently one module: the cloning module writes the wearer's soul onto the cruciform on both
/// install and uninstall (Eris <c>datum/core_module/cruciform/cloning</c>).
/// </remarks>
public sealed partial class CoreModuleBehaviorSystem : EntitySystem
{
    private static readonly ProtoId<CoreModulePrototype> CloningModule = "OxydNtModuleCloning";

    [Dependency] private readonly IServerPreferencesManager _preferences = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

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
    }

    private void OnModuleUninstalled(EntityUid cruciform, CruciformComponent comp, ref CoreModuleUninstalledEvent args)
    {
        if (args.Module == CloningModule)
            WriteSnapshot(cruciform, comp);
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
        if (comp.ImplantedEntity is not { } body)
            return false;

        var soul = EnsureComp<CruciformSoulComponent>(cruciform);
        soul.HasSnapshot = true;
        soul.Name = MetaData(body).EntityName;

        if (TryComp<MindContainerComponent>(body, out var container) &&
            container.Mind is { } mindId &&
            TryComp<MindComponent>(mindId, out var mind))
        {
            soul.MindId = mindId;

            if (mind.CharacterName is { Length: > 0 } characterName)
                soul.Name = characterName;

            if (mind.UserId is { } user && _player.TryGetSessionById(user, out var session))
            {
                soul.Ckey = session.Name;

                if (_preferences.TryGetCachedPreferences(user, out var prefs))
                    soul.Profile = prefs.SelectedCharacter;
            }
        }

        return true;
    }
}
