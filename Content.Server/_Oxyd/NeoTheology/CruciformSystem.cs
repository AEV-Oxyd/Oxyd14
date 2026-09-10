using System.Linq;
using Content.Server.GameTicking;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.Skills;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.GameTicking;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Station;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// Owns the relationship between a physical cruciform implant and its current body,
/// together with the server-side holiness and role profile. No gameplay authority is
/// granted by a bare bearer component.
/// </summary>
public sealed partial class CruciformSystem : SharedCruciformSystem
{
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private MobStateSystem _mobStates = default!;
    [Dependency] private SharedStationSystem _stations = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private readonly CoreModuleSystem _modules = default!;
    [Dependency] private readonly SharedSubdermalImplantSystem _implants = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CruciformComponent, ContainerGettingInsertedAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<CruciformComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<CruciformComponent, ImplantRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<CruciformComponent, EntityTerminatingEvent>(OnCruciformTerminating);
        SubscribeLocalEvent<CruciformBearerComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<CruciformBearerComponent, EntityTerminatingEvent>(OnBearerTerminating);
        SubscribeLocalEvent<CruciformBearerComponent, GetAccessTagsEvent>(OnGetAccessTags);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<CruciformComponent, SubdermalImplantComponent>();
        while (query.MoveNext(out var cruciform, out var component, out var implant))
        {
            if (!component.Active || implant.ImplantedEntity is not { } body)
            {
                component.LastHolinessUpdate = _timing.CurTime;
                continue;
            }

            if (!TryGetLinkedBearer(body, cruciform, out _) || _mobStates.IsDead(body))
            {
                component.LastHolinessUpdate = _timing.CurTime;
                continue;
            }

            AdvanceHoliness((cruciform, component), body);
        }
    }

    private void OnInsertAttempt(Entity<CruciformComponent> ent, ref ContainerGettingInsertedAttemptEvent args)
    {
        if (args.Container.ID != ImplanterComponent.ImplantSlotId)
            return;

        var body = args.Container.Owner;
        if (!HasAnotherCruciform(body, ent.Owner))
            return;

        // Reject before insert completes. Nested Remove during ImplantImplantedEvent
        // trips container metadata asserts; cancelling leaves the implant recoverable.
        args.Cancel();
        if (TryComp<SubdermalImplantComponent>(ent.Owner, out var implant))
        {
            implant.ImplantedEntity = null;
            Dirty(ent.Owner, implant);
        }
    }

    private void OnImplanted(Entity<CruciformComponent> ent, ref ImplantImplantedEvent args)
    {
        if (args.Implant != ent.Owner)
            return;

        var body = args.Implanted;
        if (HasAnotherCruciform(body, ent.Owner))
        {
            // Defensive fallback if insert somehow bypassed OnInsertAttempt. Do not
            // Remove synchronously here — that nests container mutations. Defer.
            var rejected = ent.Owner;
            Timer.Spawn(0, () => TryRemoveDuplicateDeferred(rejected));
            return;
        }

        ent.Comp.ImplantedEntity = body;
        ent.Comp.LastHolinessUpdate = _timing.CurTime;

        var bearer = EnsureComp<CruciformBearerComponent>(body);
        bearer.Cruciform = ent.Owner;
        BumpRevision(body, bearer);

        // Initial installation is inert. A previously activated implant may resume
        // on the same living body after extraction/reimplantation.
        ent.Comp.Active = ent.Comp.EverActivated && !_mobStates.IsDead(body);
        RecomputeProfile(ent.Owner, ent.Comp);
        Dirty(ent);
        Dirty(body, bearer);
    }

    private void OnRemoved(Entity<CruciformComponent> ent, ref ImplantRemovedEvent args)
    {
        if (args.Implant != ent.Owner)
            return;

        var body = args.Implanted;
        AdvanceHoliness(ent, body);
        ent.Comp.Active = false;
        ent.Comp.ImplantedEntity = null;
        ent.Comp.LastHolinessUpdate = _timing.CurTime;
        Dirty(ent);

        if (TryComp<CruciformBearerComponent>(body, out var bearer) && bearer.Cruciform == ent.Owner)
        {
            bearer.Cruciform = null;
            bearer.PendingRequestId = null;
            BumpRevision(body, bearer);
        }
    }

    private void OnCruciformTerminating(Entity<CruciformComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.ImplantedEntity is not { } body || !TryComp<CruciformBearerComponent>(body, out var bearer))
            return;

        if (bearer.Cruciform == ent.Owner)
        {
            bearer.Cruciform = null;
            bearer.PendingRequestId = null;
            BumpRevision(body, bearer);
        }
    }

    private void OnBearerTerminating(Entity<CruciformBearerComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.Cruciform is not { } cruciform || !TryComp<CruciformComponent>(cruciform, out var component))
            return;

        AdvanceHoliness((cruciform, component), ent.Owner);
        component.Active = false;
        component.ImplantedEntity = null;
        component.LastHolinessUpdate = _timing.CurTime;
        Dirty(cruciform, component);
    }

    private void OnMobStateChanged(Entity<CruciformBearerComponent> ent, ref MobStateChangedEvent args)
    {
        if (ent.Comp.Cruciform is not { } cruciform || !TryComp<CruciformComponent>(cruciform, out var component))
            return;

        if (!TryGetLinkedBearer(ent.Owner, cruciform, out _))
            return;

        AdvanceHoliness((cruciform, component), ent.Owner);
        if (args.NewMobState == MobState.Dead)
        {
            component.Active = false;
            // Plan §5.2: death deactivates and cancels in-flight casts.
            ent.Comp.PendingRequestId = null;
        }
        else if (args.NewMobState == MobState.Alive && component.EverActivated)
            component.Active = true;

        component.LastHolinessUpdate = _timing.CurTime;
        RecomputeProfile(cruciform, component);
        Dirty(cruciform, component);
        BumpRevision(ent.Owner, ent.Comp);
    }

    public void OnGetAccessTags(Entity<CruciformBearerComponent> ent, ref GetAccessTagsEvent args)
    {
        if (ent.Comp.Cruciform is not { } cruciform || !TryGetLinkedBearer(ent.Owner, cruciform, out var component) || !component.Active)
            return;

        if (!TryGetConfiguredProfile(component.Profile, GetRules(), out var profile))
            return;

        foreach (var access in profile.AccessPrivileges)
            args.Tags.Add(access);

        foreach (var moduleId in component.InstalledModules)
        {
            if (!ProtoMan.TryIndex(moduleId, out CoreModulePrototype? module) || module == null)
                continue;

            foreach (var level in module.Access)
                args.Tags.Add(level);
        }
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent ev)
    {
        var query = EntityQueryEnumerator<CruciformBearerComponent>();
        while (query.MoveNext(out var uid, out var bearer))
        {
            bearer.PersonalCooldowns.Clear();
            bearer.PendingRequestId = null;
            BumpRevision(uid, bearer);
        }
    }

    public bool Activate(EntityUid body)
    {
        if (!TryGetCruciformEntity(body, out var cruciform, out var component))
            return false;
        if (component.Active)
            return false;

        component.EverActivated = true;
        component.Active = true;

        // Eris cruciform.dm:94-122 — an activatable module (priest_convert) converts on activation.
        // InstalledModules is mutated by MakeRank, so iterate a snapshot.
        foreach (var moduleId in component.InstalledModules.ToArray())
        {
            if (!ProtoMan.TryIndex(moduleId, out CoreModulePrototype? module) || module.ActivationProfile is not { } profile)
                continue;

            MakeRank(cruciform, component, profile);
        }

        if (component.Holiness <= GetDebitTolerance())
            component.Holiness = component.MaxHoliness;
        component.LastHolinessUpdate = _timing.CurTime;
        RecomputeProfile(cruciform, component);
        Dirty(cruciform, component);
        BumpRevision(body);
        return true;
    }

    /// <summary>
    /// Spawns, implants and activates a cruciform on <paramref name="body"/> with the given
    /// profile. No-op when the body is already a bearer, so job respawns and admin healing
    /// cannot double-implant.
    /// </summary>
    public bool GrantCruciform(EntityUid body, ProtoId<NeoTheologyProfilePrototype> profile)
    {
        if (TryComp<CruciformBearerComponent>(body, out _))
            return false;

        if (_implants.AddImplant(body, "OxydNtCruciform") is not { } implant)
            return false;

        if (!TryComp<CruciformComponent>(implant, out var comp))
            return false;

        comp.EverActivated = true;
        comp.Active = true;
        comp.LastHolinessUpdate = _timing.CurTime;
        MakeRank(implant, comp, profile);

        // Same semantics as Activate(): a freshly granted cruciform starts full.
        if (comp.Holiness <= GetDebitTolerance())
            comp.Holiness = comp.MaxHoliness;

        return true;
    }

    public bool Deactivate(EntityUid body)
    {
        if (!TryGetCruciformEntity(body, out var cruciform, out var component) || !component.Active)
            return false;

        AdvanceHoliness((cruciform, component), body);
        component.Active = false;
        component.LastHolinessUpdate = _timing.CurTime;
        RecomputeProfile(cruciform, component);
        Dirty(cruciform, component);
        BumpRevision(body);
        return true;
    }

    public bool TrySpend(EntityUid body, double amount)
    {
        if (amount < 0 || !double.IsFinite(amount) || !TryGetCruciformEntity(body, out var cruciform, out var component) || !component.Active)
            return false;

        AdvanceHoliness((cruciform, component), body);
        if (!NeoTheologyHoliness.CanAfford(component.Holiness, amount, GetDebitTolerance()))
            return false;

        component.Holiness = NeoTheologyHoliness.Normalize(component.Holiness - amount, GetDebitTolerance());
        Dirty(cruciform, component);
        return true;
    }

    public void Refund(EntityUid body, double amount)
    {
        if (amount <= 0 || !double.IsFinite(amount) || !TryGetCruciformEntity(body, out var cruciform, out var component))
            return;

        AdvanceHoliness((cruciform, component), body);
        component.Holiness = NeoTheologyHoliness.ClampResource(component.Holiness + amount, component.MaxHoliness);
        Dirty(cruciform, component);
    }

    public bool TrySetProfile(EntityUid body, ProtoId<NeoTheologyProfilePrototype> profileId)
    {
        if (!TryGetCruciformEntity(body, out var cruciform, out var component) ||
            component.Profile == profileId || !TryGetConfiguredProfile(profileId, GetRules(), out _))
            return false;

        AdvanceHoliness((cruciform, component), body);
        component.Profile = profileId;
        RecomputeProfile(cruciform, component);
        Dirty(cruciform, component);
        BumpRevision(body);
        return true;
    }

    public double GetMaximumHoliness(EntityUid body)
    {
        return TryGetCruciformEntity(body, out _, out var component) ? component.MaxHoliness : 0;
    }

    public double GetHoliness(EntityUid body)
    {
        return TryGetCruciformEntity(body, out var cruciform, out var component)
            ? AdvanceHoliness((cruciform, component), body)
            : 0;
    }

    public double GetRegenerationPerSecond(EntityUid body)
    {
        return TryGetCruciformEntity(body, out _, out var component) ? component.RegenerationPerSecond : 0;
    }

    /// <summary>
    /// Eris <c>make_*()</c> ported as profile + module swaps. Removing the previous rank's
    /// modules is required — otherwise ordination would stack inquisitor capacity onto priest.
    /// </summary>
    public void MakeRank(EntityUid cruciform, CruciformComponent comp, ProtoId<NeoTheologyProfilePrototype> profile)
    {
        if (RankModules.TryGetValue(comp.Profile, out var previous))
        {
            foreach (var module in previous)
                _modules.TryRemove(cruciform, comp, module);
        }

        comp.Profile = profile;

        if (RankModules.TryGetValue(profile, out var modules))
        {
            foreach (var module in modules)
                _modules.TryInstall(cruciform, comp, module);
        }

        RecomputeProfile(cruciform, comp);
    }

    public void MakeCommon(EntityUid c, CruciformComponent comp)
        => MakeRank(c, comp, "OxydNtDisciple");

    public void MakePriest(EntityUid c, CruciformComponent comp)
        => MakeRank(c, comp, "OxydNtPreacher");

    public void MakeInquisitor(EntityUid c, CruciformComponent comp)
        => MakeRank(c, comp, "OxydNtInquisitor");

    public void MakeAcolyte(EntityUid c, CruciformComponent comp)
        => MakeRank(c, comp, "OxydNtAcolyte");

    public void MakeCustodian(EntityUid c, CruciformComponent comp)
        => MakeRank(c, comp, "OxydNtCustodian");

    public void MakeAgrolyte(EntityUid c, CruciformComponent comp)
        => MakeRank(c, comp, "OxydNtAgrolyte");

    /// <summary>Modules implied by a profile id. One table, no switch statements elsewhere.</summary>
    private static readonly Dictionary<ProtoId<NeoTheologyProfilePrototype>, ProtoId<CoreModulePrototype>[]> RankModules =
        new()
        {
            ["OxydNtDisciple"] = new ProtoId<CoreModulePrototype>[] { "OxydNtModuleBase" },
            ["OxydNtAcolyte"] = new ProtoId<CoreModulePrototype>[] { "OxydNtModuleBase", "OxydNtModuleAcolyte" },
            ["OxydNtAgrolyte"] = new ProtoId<CoreModulePrototype>[] { "OxydNtModuleBase", "OxydNtModuleAgrolyte" },
            ["OxydNtCustodian"] = new ProtoId<CoreModulePrototype>[] { "OxydNtModuleBase", "OxydNtModuleCustodian" },
            ["OxydNtPreacher"] = new ProtoId<CoreModulePrototype>[] { "OxydNtModuleBase", "OxydNtModuleAcolyte", "OxydNtModulePriest" },
            ["OxydNtInquisitor"] = new ProtoId<CoreModulePrototype>[] { "OxydNtModuleBase", "OxydNtModuleAcolyte", "OxydNtModulePriest", "OxydNtModuleInquisitor", "OxydNtModuleRedLight" },
        };

    private double AdvanceHoliness(Entity<CruciformComponent> ent, EntityUid body)
    {
        var now = _timing.CurTime;
        if (ent.Comp.LastHolinessUpdate == default)
            ent.Comp.LastHolinessUpdate = now;

        var elapsed = now - ent.Comp.LastHolinessUpdate;
        ent.Comp.LastHolinessUpdate = now;
        if (elapsed <= TimeSpan.Zero || !ent.Comp.Active || _mobStates.IsDead(body))
            return ent.Comp.Holiness;

        var seconds = elapsed.TotalSeconds;
        if (double.IsFinite(seconds) && seconds > 0)
            ent.Comp.Holiness = NeoTheologyHoliness.ClampResource(
                ent.Comp.Holiness + ent.Comp.RegenerationPerSecond * seconds,
                ent.Comp.MaxHoliness);

        Dirty(ent);
        return ent.Comp.Holiness;
    }

    public void RecomputeProfile(EntityUid cruciform, CruciformComponent component)
    {
        var rules = GetRules();
        var hasProfile = TryGetConfiguredProfile(component.Profile, rules, out var profile);
        var body = component.ImplantedEntity;

        var capacity = hasProfile ? profile.CruciformCapacity : 50d;
        var regenMultiplier = hasProfile ? profile.RegenerationMultiplier : 1d;

        component.UnlockedSets.Clear();

        if (hasProfile)
        {
            foreach (var set in profile.LitanySets)
                component.UnlockedSets.Add(set);
        }

        foreach (var moduleId in component.InstalledModules)
        {
            if (!ProtoMan.TryIndex(moduleId, out CoreModulePrototype? module) || module == null)
                continue;

            foreach (var set in module.LitanySets)
                component.UnlockedSets.Add(set);

            capacity *= module.MaxHolinessMultiplier;
            regenMultiplier += module.RegenMultiplierDelta;
        }

        // Installed attachment: same derivation path as profile ∪ modules, so it can never
        // be clobbered by a later recompute and uninstall lands on the exact prior value.
        if (component.Upgrade is { } upgradeItem &&
            TryComp<CruciformUpgradeComponent>(upgradeItem, out var upgrade))
        {
            foreach (var set in upgrade.LitanySets)
                component.UnlockedSets.Add(set);

            capacity += upgrade.MaxHolinessDelta;
            regenMultiplier += upgrade.RegenMultiplierDelta;
        }

        // Auras (the obelisk) ride the same derivation, so an aura pulse can neither compound on
        // itself nor be lost when a module changes.
        regenMultiplier *= component.RegenerationMultiplier;

        component.MaxHoliness = capacity;

        var cognitive = 0;
        if (body is { } skillBody && TryComp<MobSkillComponent>(skillBody, out var skills) && skills.skills.TryGetValue("Cog", out var cog) && cog.Length > 0)
            cognitive = cog[0] + (cog.Length > 1 ? cog[1] : 0);

        // The shared regen helper already folds righteous life, cognition and channeling in;
        // module deltas ride the multiplier. Multiplying by capacity here would be 50x the
        // rate the existing lifecycle test pins for a disciple (1/min), so it is not applied.
        component.RegenerationPerSecond = hasProfile && rules != null && body is { } regenBody
            ? NeoTheologyHoliness.RegenerationPerSecond(
                cognitive,
                component.RighteousLife,
                component.Channeling && profile.CanChannel,
                CountEligibleChannelingFollowers(regenBody),
                rules.BaseHolinessPerMinute,
                regenMultiplier)
            : 0d;

        component.Holiness = NeoTheologyHoliness.ClampResource(component.Holiness, component.MaxHoliness);
    }

    private int CountEligibleChannelingFollowers(EntityUid source)
    {
        var count = 0;
        var query = EntityQueryEnumerator<CruciformComponent, SubdermalImplantComponent>();
        while (query.MoveNext(out var uid, out var component, out var implant))
        {
            if (!component.Active || implant.ImplantedEntity is not { } body ||
                !TryGetConfiguredProfile(component.Profile, GetRules(), out var profile) ||
                !profile.CountsAsChannelingFollower)
                continue;
            if (body == source)
                continue;
            if (SameStationOrMap(source, body) && TryGetLinkedBearer(body, uid, out _))
                count++;
        }

        return count;
    }

    private bool SameStationOrMap(EntityUid left, EntityUid right)
    {
        var leftStation = _stations.GetOwningStation(left);
        var rightStation = _stations.GetOwningStation(right);
        if (leftStation != null && rightStation != null)
            return leftStation == rightStation;
        if (leftStation == null && rightStation == null)
            return Transform(left).MapID == Transform(right).MapID;
        return false;
    }

    private void BumpRevision(EntityUid body, CruciformBearerComponent? bearer = null)
    {
        if (bearer == null && !TryComp(body, out bearer))
            return;

        bearer.UiRevision++;
        Dirty(body, bearer);
    }

    private bool HasAnotherCruciform(EntityUid body, EntityUid except)
    {
        if (!TryComp<ImplantedComponent>(body, out var installed))
            return false;
        foreach (var entity in installed.ImplantContainer.ContainedEntities)
        {
            if (entity == except || !HasComp<CruciformComponent>(entity))
                continue;
            return true;
        }

        return false;
    }

    private void TryRemoveDuplicateDeferred(EntityUid rejected)
    {
        if (TerminatingOrDeleted(rejected))
            return;
        if (!TryComp<SubdermalImplantComponent>(rejected, out var implant) || implant.ImplantedEntity is not { } body)
            return;
        if (!TryComp<ImplantedComponent>(body, out var installed))
            return;
        if (!installed.ImplantContainer.ContainedEntities.Contains(rejected))
            return;
        if (!HasAnotherCruciform(body, rejected))
            return;

        // Recoverable rejection: container remove without ForceRemove (which deletes).
        _containers.Remove(rejected, installed.ImplantContainer);
    }

    public NeoTheologyRulesPrototype? GetRules()
    {
        NeoTheologyRulesPrototype? selected = null;
        foreach (var rules in ProtoMan.EnumeratePrototypes<NeoTheologyRulesPrototype>())
        {
            if (!rules.Selected)
                continue;
            if (selected != null)
                return null;
            selected = rules;
        }

        return selected;
    }

    public double GetDebitTolerance()
    {
        var rules = GetRules();
        return rules is { DebitTolerance: var tolerance } && double.IsFinite(tolerance) && tolerance >= 0
            ? tolerance
            : 0d;
    }

    private bool TryGetConfiguredProfile(
        ProtoId<NeoTheologyProfilePrototype> profileId,
        NeoTheologyRulesPrototype? rules,
        out NeoTheologyProfilePrototype profile)
    {
        profile = null!;
        if (rules == null || !rules.Profiles.Contains(profileId))
            return false;

        if (ProtoMan.TryIndex(profileId, out NeoTheologyProfilePrototype? configuredProfile) && configuredProfile != null)
        {
            profile = configuredProfile;
            return true;
        }

        return false;
    }
}
