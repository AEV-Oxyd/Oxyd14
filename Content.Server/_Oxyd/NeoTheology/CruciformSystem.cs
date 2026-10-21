using System.Linq;
using Content.Server.GameTicking;
using Content.Server._Oxyd.Framework.ViewCalc;
using Content.Shared._Oxyd.Medical;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared._Oxyd.Skills;
using Content.Shared.Access;
using Content.Shared.Mind.Components;
using Content.Shared.Metabolism;
using Content.Shared.Access.Components;
using Content.Shared.GameTicking;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
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
    [Dependency] private MobStateSystem _mobStates = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private CoreModuleSystem _modules = default!;
    [Dependency] private SharedSubdermalImplantSystem _implants = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private CoreModuleBehaviorSystem _souls = default!;
    [Dependency] private NeoTheologyWorldSystem _world = default!;
    [Dependency] private NeoTheologyFoundationSystem _foundation = default!;

    /// <summary>
    /// Epiphany bridge: the shared litany effect cannot call this server system, so it
    /// raises <see cref="LitanyActivateCruciformEvent"/> on the target body. <c>Handled</c>
    /// stays false when the target has no installed, inactive cruciform.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnLitanyActivateCruciform(Entity<CruciformBearerComponent> ent, ref LitanyActivateCruciformEvent args)
    {
        args.Handled = Activate(ent.Owner);
    }

    /// <summary>
    /// Conversion-role bridge (Confirmation, Ordination, Omission, Excommunication): the effect
    /// cannot call <see cref="MakeRank"/> itself, so it raises <see cref="LitanySetRankEvent"/> on
    /// the target. Profile and rank modules are swapped together; the revision bump keeps the
    /// litany UI in step.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnLitanySetRank(Entity<CruciformBearerComponent> ent, ref LitanySetRankEvent args)
    {
        if (!TryGetCruciformEntity(ent.Owner, out var cruciform, out var component))
            return;

        // Confirmation changes specialization, not priest/inquisitor rank or clearance.
        MakeSpecialization(cruciform, component, args.Profile);
        Dirty(cruciform, component);
        BumpRevision(ent.Owner, ent.Comp);
        args.Handled = true;
    }

    [SubscribeLocalEvent]
    private void OnLitanySetClearance(Entity<CruciformBearerComponent> ent, ref LitanySetClearanceEvent args)
    {
        if (!TryGetCruciformEntity(ent.Owner, out var cruciform, out var component) || !component.Active)
            return;

        component.Clearance = args.Clearance;
        Dirty(cruciform, component);
        BumpRevision(ent.Owner, ent.Comp);
        args.Handled = true;
    }

    [SubscribeLocalEvent]
    private void OnLitanyRemoveSpecialization(Entity<CruciformBearerComponent> ent, ref LitanyRemoveSpecializationEvent args)
    {
        if (!TryGetCruciformEntity(ent.Owner, out var cruciform, out var component))
            return;

        foreach (var module in SpecializationModules)
            _modules.TryRemove(cruciform, component, module);
        if (!component.InstalledModules.Contains(NeoTheologyPrototypes.PriestModule) &&
            !component.InstalledModules.Contains(NeoTheologyPrototypes.InquisitorModule))
            component.Profile = NeoTheologyPrototypes.DiscipleProfile;

        RecomputeProfile(cruciform, component);
        Dirty(cruciform, component);
        BumpRevision(ent.Owner, ent.Comp);
        args.Handled = true;
    }

    /// <summary>
    /// Adoption bridge: raised on the target, who has no bearer component yet, so this
    /// subscription is a local broadcast rather than a component one. <c>Handled</c> stays false
    /// when the body already carries a cruciform.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnLitanyGrantCruciform(ref LitanyGrantCruciformEvent args)
    {
        args.Handled = GrantCruciform(args.Target, args.Profile);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<CruciformComponent, SubdermalImplantComponent>();
        while (query.MoveNext(out var cruciform, out var component, out var implant))
        {
            // Holiness accrues lazily — settled by GetHoliness/TrySpend/Refund and the
            // state transitions that change the rate — so the per-tick pass only keeps
            // the elapsed-time anchor sane for bearers that cannot accrue and runs
            // purity on its own schedule.
            if (!component.Active || implant.ImplantedEntity is not { } body ||
                !TryGetLinkedBearer(body, cruciform, out _) || _mobStates.IsDead(body))
            {
                component.LastHolinessUpdate = _timing.CurTime;
                continue;
            }

            if (_timing.CurTime >= component.NextPurity && !HasComp<GodbloodMutationComponent>(body))
            {
                component.NextPurity = _timing.CurTime + component.PurityInterval;
                _foundation.Purify(body, cleanseMutation: true);
            }

            // Followers moving in or out of channeling range do not raise the activity
            // event, so recount the roster on a slow schedule (Eris counts in-view
            // followers, matching the range lookup used everywhere else).
            if (component is { Active: true, Channeling: true } &&
                _timing.CurTime >= component.NextRosterCheck)
            {
                component.NextRosterCheck = _timing.CurTime + component.RosterCheckInterval;
                RecomputeRegeneration(component);
            }
        }
    }

    private void SetActive(EntityUid implant, CruciformComponent comp, bool active)
    {
        if (comp.Active == active)
            return;
        comp.Active = active;
        var changed = new CruciformActivityChangedEvent(comp.ImplantedEntity, active);
        RaiseLocalEvent(implant, ref changed, broadcast: true);
    }

    [SubscribeLocalEvent]
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

    /// <summary>
    /// The aura upgrade needs view ticks. The martyr upgrade needs a death marker.
    /// Neither can live on the implant, so the body carries them; rebuild the pair whenever the
    /// implant lands or leaves, or the upgrade slot changes.
    /// </summary>
    public void RefreshUpgradeBehaviors(EntityUid body, CruciformComponent component)
    {
        var desired = component.Upgrade is { } upgrade && TryComp<CruciformUpgradeAuraComponent>(upgrade, out var aura)
            ? aura.Radius
            : 0f;
        if (desired > 0f || TryComp<ViewTickerComponent>(body, out _))
        {
            var ticker = EnsureComp<ViewTickerComponent>(body);
            if (TryComp<CruciformBearerComponent>(body, out var bearer))
            {
                ticker.range += desired - bearer.AppliedViewRange;
                bearer.AppliedViewRange = desired;
            }
        }

        if (component.ImplantedEntity == body &&
            component.Upgrade is { } martyr && HasComp<CruciformUpgradeMartyrComponent>(martyr))
            EnsureComp<CruciformMartyrArmedComponent>(body);
        else
            RemComp<CruciformMartyrArmedComponent>(body);
    }

    [SubscribeLocalEvent]
    private void OnImplanted(Entity<CruciformComponent> ent, ref ImplantImplantedEvent args)
    {
        if (args.Implant != ent.Owner)
            return;

        var body = args.Implanted;
        if (HasAnotherCruciform(body, ent.Owner))
        {
            // OnInsertAttempt already rejects the duplicate before insert completes.
            // Reaching this still means the implant landed; leave it inert rather than
            // nesting a container mutation inside the implanted event.
            return;
        }

        ent.Comp.ImplantedEntity = body;
        ent.Comp.LastHolinessUpdate = _timing.CurTime;

        var bearer = EnsureComp<CruciformBearerComponent>(body);
        bearer.Cruciform = ent.Owner;
        BumpRevision(body, bearer);

        // Implantation is inert, including saved implants. Reincarnation restores the saved soul.
        SetActive(ent.Owner, ent.Comp, false); // An implanted saved soul waits for Reincarnation.
        RecomputeProfile(ent.Owner, ent.Comp);
        Dirty(ent);
        Dirty(body, bearer);
        _world.RecordConversion(body);

        // A stored speed upgrade resumes with the reimplanted cruciform.
        _movement.RefreshMovementSpeedModifiers(body);

        // An already-installed aura or martyr upgrade resumes with the reimplanted cruciform.
        RefreshUpgradeBehaviors(body, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnRemoved(Entity<CruciformComponent> ent, ref ImplantRemovedEvent args)
    {
        if (args.Implant != ent.Owner)
            return;

        var body = args.Implanted;
        AdvanceHoliness(ent, body);
        _souls.WriteSnapshot(ent.Owner, ent.Comp);
        SetActive(ent.Owner, ent.Comp, false);
        ent.Comp.ImplantedEntity = null;
        ent.Comp.LastHolinessUpdate = _timing.CurTime;
        Dirty(ent);
        RefreshUpgradeBehaviors(body, ent.Comp);

        if (TryComp<CruciformBearerComponent>(body, out var bearer) && bearer.Cruciform == ent.Owner)
        {
            bearer.Cruciform = null;
            bearer.PendingRequestId = null;
            BumpRevision(body, bearer);
        }

        // The speed upgrade lives on the cruciform; the body must lose the multiplier now.
        _movement.RefreshMovementSpeedModifiers(body);

        // Eternal Brotherhood's HUD lives on the body; Eris loses the module with the implant.
        RemComp<NtDiscipleHudComponent>(body);
    }

    [SubscribeLocalEvent]
    private void OnCruciformTerminating(Entity<CruciformComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.ImplantedEntity is not { } body || !TryComp<CruciformBearerComponent>(body, out var bearer))
            return;

        SetActive(ent.Owner, ent.Comp, false);
        if (bearer.Cruciform == ent.Owner)
        {
            bearer.Cruciform = null;
            bearer.PendingRequestId = null;
            BumpRevision(body, bearer);
        }

        _movement.RefreshMovementSpeedModifiers(body);
        RemComp<NtDiscipleHudComponent>(body);
    }

    [SubscribeLocalEvent]
    private void OnBearerTerminating(Entity<CruciformBearerComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.Cruciform is not { } cruciform || !TryComp<CruciformComponent>(cruciform, out var component))
            return;

        AdvanceHoliness((cruciform, component), ent.Owner);
        SetActive(cruciform, component, false);
        component.ImplantedEntity = null;
        component.LastHolinessUpdate = _timing.CurTime;
        Dirty(cruciform, component);
    }

    [SubscribeLocalEvent]
    private void OnMobStateChanged(Entity<CruciformBearerComponent> ent, ref MobStateChangedEvent args)
    {
        if (ent.Comp.Cruciform is not { } cruciform || !TryComp<CruciformComponent>(cruciform, out var component))
            return;

        if (!TryGetLinkedBearer(ent.Owner, cruciform, out _))
            return;

        AdvanceHoliness((cruciform, component), ent.Owner);
        if (args.NewMobState == MobState.Dead)
        {
            _souls.WriteSnapshot(cruciform, component, atDeath: true);
            SetActive(cruciform, component, false);
            // Plan §5.2: death deactivates and cancels in-flight casts.
            ent.Comp.PendingRequestId = null;
        }
        else if (args.NewMobState == MobState.Alive && component.EverActivated &&
            (!TryComp<CruciformSoulComponent>(cruciform, out var soul) || !soul.HasSnapshot ||
                soul.SourceBody == ent.Owner))
            SetActive(cruciform, component, true);

        component.LastHolinessUpdate = _timing.CurTime;
        RecomputeRegeneration(component);
        Dirty(cruciform, component);
        BumpRevision(ent.Owner, ent.Comp);
    }

    /// <summary>
    /// Cognition feeds <see cref="CruciformComponent.RegenerationPerSecond"/>: settle the
    /// bearer's holiness then re-derive the stored rate when skills change.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnSkillsRecalculated(Entity<CruciformBearerComponent> ent, ref SkillsRecalculatedEvent args)
    {
        if (!TryGetCruciformEntity(ent.Owner, out var cruciform, out var component))
            return;

        AdvanceHoliness((cruciform, component), ent.Owner);
        RecomputeRegeneration(component);
        Dirty(cruciform, component);
    }

    /// <summary>
    /// A follower's activation change can move it in or out of a channeler's roster, so any
    /// channeling cruciforms in range re-derive their rate (kept as a range lookup, never a
    /// station-wide query).
    /// </summary>
    [SubscribeLocalEvent]
    private void OnActivityChangedRefreshRoster(ref CruciformActivityChangedEvent args)
    {
        if (args.Body is not { } body || GetRules()?.ChannelingFollowerRange is not > 0f)
            return;

        var range = GetRules()!.ChannelingFollowerRange;
        foreach (var (_, comp) in _lookup.GetEntitiesInRange<CruciformComponent>(
                     Transform(body).Coordinates, range))
        {
            if (comp is { Active: true, Channeling: true })
                RecomputeRegeneration(comp);
        }
    }

    [SubscribeLocalEvent]
    private void OnGetAccessTags(Entity<CruciformBearerComponent> ent, ref GetAccessTagsEvent args)
    {
        if (ent.Comp.Cruciform is not { } cruciform || !TryGetLinkedBearer(ent.Owner, cruciform, out var component) || !component.Active)
            return;

        if (!TryGetConfiguredProfile(component.Profile, GetRules(), out var profile))
            return;

        foreach (var access in profile.AccessPrivileges)
        {
            if (!MeetsAccessClearance(access, component.Clearance))
                continue;
            args.Tags.Add(access);
        }

        foreach (var moduleId in component.InstalledModules)
        {
            if (!ProtoMan.TryIndex(moduleId, out CoreModulePrototype? module) || module == null)
                continue;

            foreach (var level in module.Access)
            {
                if (!MeetsAccessClearance(level, component.Clearance))
                    continue;
                args.Tags.Add(level);
            }
        }
    }

    [SubscribeLocalEvent]
    private void OnReagentMetabolized(Entity<CruciformBearerComponent> ent, ref ReagentMetabolizedEvent args)
    {
        if (!TryGetCruciformEntity(ent.Owner, out var implant, out var comp))
            return;

        var penalty = 0f;
        if (args.Stage == NeoTheologyPrototypes.BloodstreamStage && args.Reagent.Group == NeoTheologyPrototypes.NarcoticsReagentGroup)
            penalty = 0.5f;
        else if (args.Stage == NeoTheologyPrototypes.DigestionStage && args.Reagent.ID != NeoTheologyPrototypes.CahorsReagent.Id &&
            args.Reagent.Metabolisms?.Metabolisms.TryGetValue(args.Stage, out var metabolism) == true &&
            metabolism.Metabolites?.ContainsKey(NeoTheologyPrototypes.EthanolReagent) == true)
            penalty = 0.1f;

        if (penalty <= 0)
            return;
        comp.RighteousLife = Math.Max(0f, comp.RighteousLife - penalty);
        RecomputeRegeneration(comp);
        Dirty(implant, comp);
    }

    [SubscribeLocalEvent]
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
        if (_mobStates.IsDead(body) || HasComp<GodbloodMutationComponent>(body))
            return false;
        if (TryComp<CruciformSoulComponent>(cruciform, out var saved) && saved.HasSnapshot &&
            saved.SourceBody != body)
            return false;

        // Epiphany: the first awakening gets the bell + flash; re-activations after a
        // revive stay quiet.
        var firstActivation = !component.EverActivated;
        component.EverActivated = true;

        _modules.TryInstall(cruciform, component, NeoTheologyPrototypes.BaseModule);
        _modules.TryInstall(cruciform, component, NeoTheologyPrototypes.CloningModule);
        _souls.WriteSnapshot(cruciform, component);
        // Eris cruciform.dm:94-122 — installed activatable upgrades convert on activation.
        ApplyActivationModules(cruciform, component);
        SetActive(cruciform, component, true);
        _souls.ActivateObey(cruciform, component);

        if (component.Holiness <= GetDebitTolerance())
            component.Holiness = component.MaxHoliness;
        component.LastHolinessUpdate = _timing.CurTime;
        RecomputeProfile(cruciform, component);
        Dirty(cruciform, component);
        BumpRevision(body);

        if (firstActivation)
            Spawn(NeoTheologyPrototypes.EpiphanyFlashEffect, Transform(body).Coordinates);

        return true;
    }

    /// <summary>
    /// Activates the conversion module supplied by an installed ascension kit. Initiation
    /// does not create a free kit; the removable core-upgrade item owns the module.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnLitanyInitiation(Entity<CruciformBearerComponent> ent, ref LitanyInitiationEvent args)
    {
        if (!TryGetCruciformEntity(ent.Owner, out var cruciform, out var component) || !component.Active)
            return;

        // Eris guards the perform: the target must not already be a preacher.
        if (_modules.HasModule(component, NeoTheologyPrototypes.PriestModule) || _modules.HasModule(component, NeoTheologyPrototypes.InquisitorModule))
            return;

        if (!_modules.HasModule(component, NeoTheologyPrototypes.PriestConvertModule) ||
            !ApplyActivationModules(cruciform, component))
            return;

        Dirty(cruciform, component);
        BumpRevision(ent.Owner, ent.Comp);
        args.Handled = true;
    }

    /// <summary>
    /// The one writer for activatable-module conversion: <see cref="Activate"/> runs it for a whole
    /// cruciform activation and the Initiation bridge for a lone ascension kit. InstalledModules is
    /// mutated by <see cref="MakeRank"/>, so it iterates a snapshot.
    /// </summary>
    private bool ApplyActivationModules(EntityUid cruciform, CruciformComponent component)
    {
        var converted = false;
        foreach (var moduleId in component.InstalledModules.ToArray())
        {
            if (!ProtoMan.TryIndex(moduleId, out CoreModulePrototype? module) || module.ActivationProfile is not { } profile)
                continue;

            MakeRank(cruciform, component, profile);
            converted = true;
        }

        return converted;
    }

    /// <summary>
    /// Spawns, implants and activates a cruciform on <paramref name="body"/> with the given
    /// profile. No-op when the body is already a bearer, so job respawns and admin healing
    /// cannot double-implant.
    /// </summary>
    public bool GrantCruciform(EntityUid body, ProtoId<NeoTheologyProfilePrototype> profile)
    {
        if (TerminatingOrDeleted(body) ||
            _mobStates.IsDead(body) || HasComp<GodbloodMutationComponent>(body) ||
            TryComp<CruciformBearerComponent>(body, out _) ||
            !TryGetConfiguredProfile(profile, GetRules(), out var grantedProfile))
            return false;

        if (_implants.AddImplant(body, NeoTheologyPrototypes.CruciformEnt) is not { } implant)
            return false;

        if (!TryComp<CruciformComponent>(implant, out var comp))
            return false;

        comp.EverActivated = true;
        comp.LastHolinessUpdate = _timing.CurTime;
        // Channeling is a grant-time perk flag, not something rank changes re-derive:
        // TrySetProfile must not toggle it (ceremony channeling bonus semantics).
        comp.Channeling = grantedProfile.CanChannel;
        MakeRank(implant, comp, profile);
        _modules.TryInstall(implant, comp, NeoTheologyPrototypes.CloningModule);
        _souls.WriteSnapshot(implant, comp);
        SetActive(implant, comp, true);

        // Parity with Activate(): run the module conversion and obey-kit activation.
        // A fresh implant has neither, so both no-op today.
        ApplyActivationModules(implant, comp);
        _souls.ActivateObey(implant, comp);

        // Same semantics as Activate(): a freshly granted cruciform starts full.
        if (comp.Holiness <= GetDebitTolerance())
            comp.Holiness = comp.MaxHoliness;

        Dirty(implant, comp);
        BumpRevision(body);
        return true;
    }

    public bool Deactivate(EntityUid body)
    {
        if (!TryGetCruciformEntity(body, out var cruciform, out var component) || !component.Active)
            return false;

        AdvanceHoliness((cruciform, component), body);
        SetActive(cruciform, component, false);
        component.LastHolinessUpdate = _timing.CurTime;
        RecomputeRegeneration(component);
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
        MakeRank(cruciform, component, profileId);
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
        return TryGetCruciformEntity(body, out _, out var component)
            ? component.RegenerationPerSecond
            : 0;
    }

    /// <summary>
    /// Eris <c>make_*()</c> ported as profile + module swaps. Removing the previous rank's
    /// modules is required — otherwise ordination would stack inquisitor capacity onto priest.
    /// </summary>
    public void MakeRank(EntityUid cruciform, CruciformComponent comp, ProtoId<NeoTheologyProfilePrototype> profile)
    {
        var rules = GetRules();
        if (rules == null || !TryGetConfiguredProfile(profile, rules, out var next))
            return;
        var holiness = comp.Holiness;
        var specializations = comp.InstalledModules.Where(module => SpecializationModules.Contains(module)).ToArray();
        foreach (var role in rules.Profiles)
        {
            if (!TryGetConfiguredProfile(role, rules, out var previous))
                continue;
            foreach (var module in previous.StartingModules)
                _modules.TryRemove(cruciform, comp, module);
        }

        foreach (var module in SpecializationModules)
            _modules.TryRemove(cruciform, comp, module);
        comp.Profile = profile;

        foreach (var module in next.StartingModules)
            _modules.TryInstall(cruciform, comp, module);

        if (next.Clearance > comp.Clearance)
            comp.Clearance = next.Clearance;
        if (!next.IsSpecialization)
        {
            foreach (var module in specializations)
                _modules.TryInstall(cruciform, comp, module);
        }

        RecomputeProfile(cruciform, comp);
        // Intermediate module removals must not clamp a promotion to the temporary base capacity.
        comp.Holiness = NeoTheologyHoliness.ClampResource(holiness, comp.MaxHoliness);
        if (comp.ImplantedEntity is { } body)
            _world.AssignObjectives(body);
    }

    private void MakeSpecialization(EntityUid cruciform, CruciformComponent comp, ProtoId<NeoTheologyProfilePrototype> profile)
    {
        var rules = GetRules();
        if (!TryGetConfiguredProfile(profile, rules, out var next) || !next.IsSpecialization)
            return;

        foreach (var module in SpecializationModules)
            _modules.TryRemove(cruciform, comp, module);
        foreach (var module in next.StartingModules.Where(module => SpecializationModules.Contains(module)))
            _modules.TryInstall(cruciform, comp, module);
        if (!TryGetConfiguredProfile(comp.Profile, rules, out var current) ||
            current.Clearance < NeoTheologyClearance.Clergy)
            comp.Profile = profile;
        RecomputeProfile(cruciform, comp);
        if (comp.ImplantedEntity is { } body)
            _world.AssignObjectives(body);
    }

    private bool MeetsAccessClearance(ProtoId<AccessLevelPrototype> access, NeoTheologyClearance clearance)
    {
        var rules = GetRules();
        return rules == null ||
               !rules.RequiredClearance.TryGetValue(access, out var required) ||
               clearance >= required;
    }

    /// <summary>Modules implied by a profile id. One table, no switch statements elsewhere.</summary>
    private static readonly ProtoId<CoreModulePrototype>[] SpecializationModules =
    [
        NeoTheologyPrototypes.AcolyteModule,
        NeoTheologyPrototypes.AgrolyteModule,
        NeoTheologyPrototypes.CustodianModule,
    ];

    private double AdvanceHoliness(Entity<CruciformComponent> ent, EntityUid body)
    {
        var now = _timing.CurTime;
        if (ent.Comp.LastHolinessUpdate == default)
            ent.Comp.LastHolinessUpdate = now;

        var elapsed = now - ent.Comp.LastHolinessUpdate;
        ent.Comp.LastHolinessUpdate = now;
        if (elapsed <= TimeSpan.Zero || !ent.Comp.Active || _mobStates.IsDead(body))
            return ent.Comp.Holiness;

        // RegenerationPerSecond is stored state recomputed by the transitions that change
        // its inputs (RecomputeProfile, module/upgrade/profile/cognition/aura/follower
        // changes); the settle path itself stays allocation-free and undirtied — the client
        // extrapolates from the networked (Holiness, LastHolinessUpdate, RegenerationPerSecond).
        var seconds = elapsed.TotalSeconds;
        if (double.IsFinite(seconds) && seconds > 0)
            ent.Comp.Holiness = NeoTheologyHoliness.ClampResource(
                ent.Comp.Holiness + ent.Comp.RegenerationPerSecond * seconds,
                ent.Comp.MaxHoliness);

        return ent.Comp.Holiness;
    }

    /// <summary>
    /// Full structural rebuild: litany sets, capacity, and the regeneration rate. Call when
    /// the module set, upgrade, or profile changes. Holiness-only ticks use
    /// <see cref="RecomputeRegeneration"/> instead.
    /// </summary>
    public void RecomputeProfile(EntityUid cruciform, CruciformComponent component)
    {
        var rules = GetRules();
        var hasProfile = TryGetConfiguredProfile(component.Profile, rules, out var profile);

        var capacity = hasProfile ? profile.CruciformCapacity : rules?.DefaultCruciformCapacity ?? 0;

        component.UnlockedSets.Clear();

        foreach (var moduleId in component.InstalledModules)
        {
            if (!ProtoMan.TryIndex(moduleId, out CoreModulePrototype? module) || module == null)
                continue;

            foreach (var set in module.LitanySets)
                component.UnlockedSets.Add(set);

            capacity *= module.MaxHolinessMultiplier;
        }

        // Runtime grants (the Crusade rite) survive the recompute; Eris keeps them in
        // known_rituals, which no module change clears.
        foreach (var set in component.GrantedSets)
            component.UnlockedSets.Add(set);

        // Installed attachment: same derivation path as profile ∪ modules, so it can never
        // be clobbered by a later recompute and uninstall lands on the exact prior value.
        if (component.Upgrade is { } upgradeItem &&
            TryComp<CruciformUpgradeComponent>(upgradeItem, out var upgrade))
        {
            foreach (var set in upgrade.LitanySets)
                component.UnlockedSets.Add(set);

            capacity += upgrade.MaxHolinessDelta;
        }

        component.MaxHoliness = capacity;
        RecomputeRegeneration(component);
        component.Holiness = NeoTheologyHoliness.ClampResource(component.Holiness, component.MaxHoliness);
    }

    /// <summary>
    /// Refreshes only the holiness regeneration rate — the live inputs (cognition, righteous
    /// life, channeling roster, module/upgrade/aura multipliers) without rebuilding litany
    /// sets or capacity.
    /// </summary>
    public void RecomputeRegeneration(CruciformComponent component)
    {
        var rules = GetRules();
        var hasProfile = TryGetConfiguredProfile(component.Profile, rules, out var profile);
        var body = component.ImplantedEntity;

        var regenMultiplier = hasProfile ? profile.RegenerationMultiplier : 1d;
        foreach (var moduleId in component.InstalledModules)
        {
            if (ProtoMan.TryIndex(moduleId, out CoreModulePrototype? module) && module != null)
                regenMultiplier += module.RegenMultiplierDelta;
        }

        if (component.Upgrade is { } upgradeItem &&
            TryComp<CruciformUpgradeComponent>(upgradeItem, out var upgrade))
            regenMultiplier += upgrade.RegenMultiplierDelta;

        regenMultiplier += component.EnergyMiracles;

        // Auras (the obelisk) ride the same derivation, so an aura pulse can neither compound on
        // itself nor be lost when a module changes.
        regenMultiplier *= component.RegenerationMultiplier;

        var cognitive = 0;
        if (body is { } skillBody && TryComp<MobSkillComponent>(skillBody, out var skills) && skills.skills.TryGetValue(NeoTheologySkills.Cognition, out var cog) && cog.Length > 0)
            cognitive = cog[0] + (cog.Length > 1 ? cog[1] : 0);

        // Source regeneration starts at 20/min; rank modifiers are installed-module deltas,
        // not a second multiplication by capacity.
        component.RegenerationPerSecond = hasProfile && rules != null && body is { } regenBody
            ? NeoTheologyHoliness.RegenerationPerSecond(
                cognitive,
                component.RighteousLife,
                component.Channeling && profile.CanChannel,
                component.Channeling ? CountEligibleChannelingFollowers(regenBody) : 0,
                rules.BaseHolinessPerMinute,
                regenMultiplier)
            : 0d;
    }

    private int CountEligibleChannelingFollowers(EntityUid source)
    {
        var range = GetRules()?.ChannelingFollowerRange ?? 0f;
        if (range <= 0f)
            return 0;

        var count = 0;
        foreach (var (uid, component) in _lookup.GetEntitiesInRange<CruciformComponent>(
                     Transform(source).Coordinates, range))
        {
            if (!component.Active ||
                !TryComp<SubdermalImplantComponent>(uid, out var implant) ||
                implant.ImplantedEntity is not { } body ||
                _mobStates.IsDead(body) ||
                !TryGetLinkedBearer(body, uid, out _) ||
                !ProtoMan.TryIndex(component.Profile, out NeoTheologyProfilePrototype? profile) ||
                !profile.CountsAsChannelingFollower)
                continue;

            count++;
        }

        return count;
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

    /// <summary>Selected rules prototype, re-resolved when prototypes reload.</summary>
    private NeoTheologyRulesPrototype? _rules;

    public NeoTheologyRulesPrototype? GetRules()
    {
        return _rules;
    }

    [SubscribeLocalEvent]
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<NeoTheologyRulesPrototype>())
            _rules = ResolveRules();
    }

    private NeoTheologyRulesPrototype? ResolveRules()
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

    public override void Initialize()
    {
        base.Initialize();
        _rules = ResolveRules();
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
