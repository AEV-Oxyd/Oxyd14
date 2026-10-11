using Content.Shared.Body;
using Content.Shared.Roles;
using Content.Server.Roles;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.DetailExaminable;
using Content.Shared._Oxyd;
using Content.Shared._Oxyd.Skills;
using Robust.Shared.Timing;
using Content.Shared.Forensics.Components;
using Content.Shared.Ghost.Components;
using Content.Shared.Mobs.Systems;
using Content.Server.Cloning;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Robust.Shared.Physics.Components;
using Robust.Shared.Serialization.Manager;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.Medical;
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

    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private NtUplinkSystem _uplink = default!;
    [Dependency] private CruciformSystem _cruciform = default!;
    [Dependency] private NeoTheologyWorldSystem _world = default!;
    [Dependency] private SharedMindSystem _minds = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private CloningPodSystem _cloning = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private HumanoidProfileSystem _profiles = default!;
    [Dependency] private SharedVisualBodySystem _visualBody = default!;
    [Dependency] private SharedSkillSystem _skills = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedRoleSystem _roles = default!;

    [SubscribeLocalEvent]
    private void OnModuleInstalled(EntityUid cruciform, CruciformComponent comp, ref CoreModuleInstalledEvent args)
    {
        if (args.Module == CloningModule)
            WriteSnapshot(cruciform, comp);
        else if (args.Module == UplinkModule)
            _uplink.OnUplinkInstalled(cruciform);
        else if (args.Module == NeoTheologyPrototypes.ObeyModule)
            ActivateObey(cruciform, comp);
    }

    [SubscribeLocalEvent]
    private void OnModuleUninstalled(EntityUid cruciform, CruciformComponent comp, ref CoreModuleUninstalledEvent args)
    {
        if (args.Module == CloningModule)
            WriteSnapshot(cruciform, comp);
        else if (args.Module == UplinkModule)
            _uplink.OnUplinkUninstalled(cruciform);
        else if (args.Module == NeoTheologyPrototypes.ObeyModule && comp.ImplantedEntity is { } body &&
            _minds.TryGetMind(body, out var mind, out _))
            _roles.MindRemoveRole<NeoTheologyObeyRoleComponent>(mind);
    }

    [SubscribeLocalEvent]
    private void OnObeyBriefing(Entity<NeoTheologyObeyRoleComponent> ent, ref GetBriefingEvent args)
    {
        args.Append(Loc.GetString("oxyd-nt-obey-laws", ("commander", ent.Comp.Commander)));
    }

    public void ActivateObey(EntityUid cruciform, CruciformComponent comp)
    {
        if (!comp.Active || comp.ImplantedEntity is not { } body ||
            !comp.CoreUpgrades.TryGetValue(NeoTheologyPrototypes.ObeyModule, out var item) ||
            !TryComp<CruciformCoreUpgradeComponent>(item, out var kit) ||
            !_minds.TryGetMind(body, out var mind, out var mindComp))
            return;
        if (!_roles.MindHasRole<NeoTheologyObeyRoleComponent>(mind))
            _roles.MindAddRole(mind, NeoTheologyPrototypes.ObeyRole, mindComp);
        if (_roles.MindHasRole<NeoTheologyObeyRoleComponent>(mind, out var role))
            Comp<NeoTheologyObeyRoleComponent>(role.Value).Commander = kit.Commander;
    }

    /// <summary>Restore the saved mind, never replace it with the prepared body's identity.</summary>
    [SubscribeLocalEvent]
    private void OnReincarnation(Entity<CruciformBearerComponent> ent, ref LitanyReincarnationEvent args)
    {
        if (!_cruciform.TryGetCruciformEntity(ent.Owner, out var cruciform, out var comp) ||
            comp.Active || !comp.EverActivated || !comp.InstalledModules.Contains(CloningModule) ||
            _mobState.IsDead(ent.Owner) || HasComp<GodbloodMutationComponent>(ent.Owner) ||
            !TryComp<CruciformSoulComponent>(cruciform, out var soul) || !soul.HasSnapshot ||
            soul.Profile is null || !HasComp<HumanoidProfileComponent>(ent.Owner) ||
            soul.Dna is null || !TryComp<DnaComponent>(ent.Owner, out var dna) || dna.DNA != soul.Dna ||
            soul.MindId is not { } mindId || !TryComp<MindComponent>(mindId, out var mind) ||
            !TryComp<MindContainerComponent>(ent.Owner, out var holder) ||
            (holder.Mind is { } occupying && occupying != mindId))
            return;

        if (mind.OwnedEntity is { } owned && owned != ent.Owner && !TerminatingOrDeleted(owned) &&
            !HasComp<GhostComponent>(owned) && !_mobState.IsDead(owned))
            return;

        args.Handled = true;
        if (args.ValidateOnly)
            return;

        _visualBody.ApplyProfileTo(ent.Owner, soul.Profile);
        _profiles.ApplyProfileTo(ent.Owner, soul.Profile);
        _metadata.SetEntityName(ent.Owner, soul.Name);
        RestoreBodyState(ent.Owner, soul);
        _minds.TransferTo(mindId, ent.Owner, ghostCheckOverride: true, mind: mind);
        _minds.UnVisit(mindId, mind);
        _cloning.ClonesWaitingForMind.Remove(mind);
        soul.PreparedBody = null;
        // The identity now belongs to this body, so activation may safely refresh it.
        soul.SourceBody = ent.Owner;
        args.Handled = _cruciform.Activate(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnMindAdded(Entity<CruciformBearerComponent> ent, ref MindAddedMessage args)
    {
        if (_cruciform.TryGetCruciform(ent.Owner, out var cruciform, out var comp))
        {
            WriteSnapshot(cruciform, comp);
            ActivateObey(cruciform, comp);
            _world.AssignObjectives(ent.Owner);
        }
    }

    /// <summary>
    /// Records the wearer's identity on the cruciform. A cruciform with nobody in it never
    /// clobbers an existing snapshot, so a soul written while alive survives the body's death.
    /// </summary>
    public bool WriteSnapshot(EntityUid cruciform, CruciformComponent comp, bool atDeath = false)
    {
        if (comp.ImplantedEntity is not { } body || !TryComp<HumanoidProfileComponent>(body, out var humanoid))
            return false;

        var soul = EnsureComp<CruciformSoulComponent>(cruciform);
        // Never overwrite another soul merely because its implant entered a new body.
        if (soul.HasSnapshot && soul.SourceBody != body)
            return false;
        if (soul.HasSnapshot && _mobState.IsDead(body) && !atDeath)
            return true; // Corpse alteration must not rewrite the identity saved at death.

        soul.HasSnapshot = true;
        soul.SourceBody = body;
        soul.Dna = TryComp<DnaComponent>(body, out var dna) ? dna.DNA : null;
        soul.Fingerprint = TryComp<FingerprintComponent>(body, out var prints) ? prints.Fingerprint : null;
        soul.Name = MetaData(body).EntityName;
        soul.AtheistMutation = HasComp<AtheistMutationComponent>(body);
        soul.HolyLight = HasComp<HolyLightComponent>(body);
        soul.FlavorText = TryComp<DetailExaminableComponent>(body, out var flavor) ? flavor.Content : null;
        var bloodReference = TryComp<BloodstreamComponent>(body, out var blood) ? blood.BloodReferenceSolution : null;
        soul.BloodReference = bloodReference?.Clone();
        soul.BaseSkills.Clear();
        soul.SkillBuffs.Clear();
        if (TryComp<MobSkillComponent>(body, out var skills))
        {
            foreach (var (skill, values) in skills.skills)
                if (values.Length > 0)
                    soul.BaseSkills[skill] = values[0];
            foreach (var (skill, sources) in skills.buffSources)
                foreach (var (source, buffs) in sources)
                    foreach (var buff in buffs)
                        if (buff.expires > _timing.CurTime)
                            soul.SkillBuffs.Add(new SoulSkillBuff
                            {
                                Skill = skill, Source = source, Amount = buff.amount, Expires = buff.expires,
                            });
        }
        soul.ChosenLanguage = null;
        soul.Speaking.Clear();
        soul.Understanding.Clear();
        if (TryComp<LanguageKnowledgeComponent>(body, out var languages))
        {
            soul.ChosenLanguage = languages.chosen;
            soul.Speaking.UnionWith(languages.speaking);
            soul.Understanding.UnionWith(languages.understanding);
        }
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

    /// <summary>Shared by vessel growth and soul transfer; copying never aliases the implant's snapshot.</summary>
    public void RestoreBodyState(EntityUid body, CruciformSoulComponent soul)
    {
        if (soul.BaseSkills.Count > 0)
        {
            var skills = EnsureComp<MobSkillComponent>(body);
            skills.buffSources.Clear();
            foreach (var (skill, value) in soul.BaseSkills)
                skills.skills[skill] = new[] { value, 0 };
            foreach (var buff in soul.SkillBuffs)
                if (buff.Expires > _timing.CurTime)
                    _skills.AddBuff((body, skills), buff.Source, buff.Amount, buff.Skill,
                        buff.Expires == TimeSpan.MaxValue ? null : buff.Expires - _timing.CurTime);
            _skills.RecalculateBuffs((body, skills));
        }
        if (soul.ChosenLanguage is { } chosen)
        {
            var languages = EnsureComp<LanguageKnowledgeComponent>(body);
            languages.chosen = chosen;
            languages.speaking = new(soul.Speaking);
            languages.understanding = new(soul.Understanding);
            Dirty(body, languages);
        }
        if (soul.BloodReference is { } blood && TryComp<BloodstreamComponent>(body, out var bloodstream))
            _bloodstream.ChangeBloodReagents((body, bloodstream), blood.Clone());
        if (soul.FlavorText is { } flavor)
        {
            var details = EnsureComp<DetailExaminableComponent>(body);
            details.Content = flavor;
            Dirty(body, details);
        }
        if (soul.HolyLight)
            EnsureComp<HolyLightComponent>(body);
        else
            RemComp<HolyLightComponent>(body);
        if (soul.AtheistMutation)
            EnsureComp<AtheistMutationComponent>(body);
        else
            RemComp<AtheistMutationComponent>(body);
    }
}
