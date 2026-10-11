using Content.Server.Body.Components;
using Content.Server.DoAfter;
using Content.Shared._Oxyd.Medical;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Chat;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Verbs;
using Content.Shared.Medical;
using Robust.Server.Containers;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Ports Eris mob/living/simple_animal/borer (borer.dm + borer_powers.dm), scoped to its
/// non-mind-control kit: a small slug that infests a humanoid through the ear canal,
/// lives in a container inside the host, and spends its regenerating "chemicals" pool on
/// secreting reagents into the host's bloodstream, paralyzing adjacent victims, and
/// reproduction. Verbs instead of a BUI, mirroring the Eris verb lists:
/// Infest / Paralyze Victim (standalone), Secrete / Reproduce / Release Host (in host).
/// </summary>
public sealed partial class OxydBorerSystem : EntitySystem
{
    [Dependency] private ContainerSystem _container = default!;
    [Dependency] private DoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private VomitSystem _vomit = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    /// <summary>Eris "sugar" — host blood sugar makes the borer docile.</summary>
    private static readonly ProtoId<Content.Shared.Chemistry.Reagent.ReagentPrototype> Sugar = "Sugar";

    public override void Initialize()
    {
        // Standalone abilities: alt verbs on the potential victim/host, shown to nearby borers
        // (BodyComponent+InteractionVerb is already claimed by OxydSurgerySystem).
        SubscribeLocalEvent<BodyComponent, GetVerbsEvent<AlternativeVerb>>(AddTargetVerbs);
        // In-host abilities: self verbs on the borer (it cannot click the host from inside).
        SubscribeLocalEvent<OxydBorerComponent, GetVerbsEvent<InnateVerb>>(AddInHostVerbs);

        SubscribeLocalEvent<OxydBorerComponent, OxydBorerInfestDoAfterEvent>(OnInfestDone);
        SubscribeLocalEvent<OxydBorerComponent, OxydBorerReleaseDoAfterEvent>(OnReleaseDone);
        SubscribeLocalEvent<OxydBorerComponent, MobStateChangedEvent>(OnBorerStateChanged);
        SubscribeLocalEvent<OxydBorerComponent, ComponentShutdown>(OnBorerShutdown);
        SubscribeLocalEvent<OxydBorerComponent, OxydBorerAssumeControlDoAfterEvent>(OnAssumeControlDone);
        SubscribeLocalEvent<OxydBorerHostComponent, GetVerbsEvent<InnateVerb>>(AddControlVerbs);
        SubscribeLocalEvent<OxydBorerHostComponent, MobStateChangedEvent>(OnHostStateChanged);
        SubscribeLocalEvent<OxydBorerHostComponent, EntRemovedFromContainerMessage>(OnBorerRemoved);
        SubscribeLocalEvent<OxydBorerHostComponent, EntityTerminatingEvent>(OnHostTerminating);
        SubscribeLocalEvent<OxydBorerCaptiveComponent, GetVerbsEvent<InnateVerb>>(AddCaptiveVerbs);
        SubscribeLocalEvent<OxydBorerCaptiveComponent, OxydBorerResistDoAfterEvent>(OnResistDone);
        SubscribeLocalEvent<OxydBorerCaptiveComponent, EntityTerminatingEvent>(OnCaptiveTerminating);
    }

    // ------------------------------------------------------------------
    // Verbs
    // ------------------------------------------------------------------

    private void AddTargetVerbs(EntityUid uid, BodyComponent comp, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || _mobs.IsDead(args.User) || args.User == uid)
            return;

        // A host whose borer is in control (its mind drives this body) gets the
        // psychic verbs — Eris abilities_in_control (psychic_whisper / commune).
        if (TryComp<OxydBorerHostComponent>(args.User, out var userHost) &&
            userHost.Borer is { } driver &&
            TryComp<OxydBorerComponent>(driver, out var driverComp) &&
            driverComp.Controlling)
        {
            AddPsychicVerbs(args, driver, driverComp, uid);
            return;
        }

        // Only a living, free-roaming borer adjacent to the victim sees these.
        if (!TryComp<OxydBorerComponent>(args.User, out var borer) || borer.Host != null)
            return;

        // Eris standalone abilities also include commune/psychic whisper.
        AddPsychicVerbs(args, args.User, borer, uid);

        var target = uid;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("oxyd-borer-verb-infest"),
            Act = () => TryStartInfest(args.User, borer, target),
            Priority = -5,
        });

        var paralyzeDisabled = _timing.CurTime < borer.LastParalyze + borer.ParalyzeCooldown;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("oxyd-borer-verb-paralyze"),
            Act = () => TryParalyze(args.User, borer, target),
            Disabled = paralyzeDisabled,
            Message = paralyzeDisabled ? Loc.GetString("oxyd-borer-paralyze-cooldown") : null,
            Priority = -6,
        });
    }

    private void AddInHostVerbs(EntityUid uid, OxydBorerComponent comp, GetVerbsEvent<InnateVerb> args)
    {
        if (args.User != uid || _mobs.IsDead(uid))
            return;

        var category = new VerbCategory(Loc.GetString("oxyd-borer-verb-category"), null);

        // Standalone abilities (Eris abilities_standalone extras): Hide.
        if (comp.Host == null)
        {
            args.Verbs.Add(new InnateVerb
            {
                Text = Loc.GetString(comp.Hidden ? "oxyd-borer-verb-unhide" : "oxyd-borer-verb-hide"),
                Category = category,
                Act = () => TryToggleHide(uid, comp),
            });
            return;
        }

        // In-control borers drive the host body; their verbs move to the host.
        if (comp.Controlling)
            return;

        var host = comp.Host.Value;

        foreach (var reagent in comp.ProducedReagents)
        {
            var name = _proto.TryIndex(reagent, out var proto) ? proto.LocalizedName : reagent.Id;
            var id = reagent;
            args.Verbs.Add(new InnateVerb
            {
                Text = Loc.GetString("oxyd-borer-verb-secrete", ("reagent", name)),
                Category = category,
                Act = () => TrySecrete(uid, comp, host, id),
            });
        }

        args.Verbs.Add(new InnateVerb
        {
            Text = Loc.GetString("oxyd-borer-verb-reproduce"),
            Category = category,
            Act = () => TryReproduce(uid, comp, host),
        });

        args.Verbs.Add(new InnateVerb
        {
            Text = Loc.GetString("oxyd-borer-verb-assume-control"),
            Category = category,
            Act = () => TryStartAssumeControl(uid, comp, host),
        });

        args.Verbs.Add(new InnateVerb
        {
            Text = Loc.GetString("oxyd-borer-verb-read-mind"),
            Category = category,
            Act = () => TryReadMind(uid, comp, host),
        });

        args.Verbs.Add(new InnateVerb
        {
            Text = Loc.GetString("oxyd-borer-verb-write-mind"),
            Category = category,
            Act = () => TryWriteMind(uid, comp, host),
        });

        args.Verbs.Add(new InnateVerb
        {
            Text = Loc.GetString("oxyd-borer-verb-speak-to-host"),
            Category = category,
            Act = () => TrySpeakToHost(uid, comp, host),
        });

        args.Verbs.Add(new InnateVerb
        {
            Text = Loc.GetString("oxyd-borer-verb-say-host"),
            Category = category,
            Act = () => TryForceHostSpeech(uid, comp, host, InGameICChatType.Speak),
        });

        args.Verbs.Add(new InnateVerb
        {
            Text = Loc.GetString("oxyd-borer-verb-whisper-host"),
            Category = category,
            Act = () => TryForceHostSpeech(uid, comp, host, InGameICChatType.Whisper),
        });

        args.Verbs.Add(new InnateVerb
        {
            Text = Loc.GetString("oxyd-borer-verb-release"),
            Category = category,
            Act = () => TryStartRelease(uid, comp, host),
        });
    }

    // ------------------------------------------------------------------
    // Infest (Eris: 2.5s do_mob, x3 with headgear or NT implant)
    // ------------------------------------------------------------------

    private void TryStartInfest(EntityUid borer, OxydBorerComponent comp, EntityUid host)
    {
        if (comp.Host != null || _mobs.IsDead(borer))
            return;

        if (_mobs.IsDead(host))
        {
            _popup.PopupEntity(Loc.GetString("oxyd-borer-infest-dead"), borer, borer);
            return;
        }

        // Eris: cannot infest someone already infested.
        if (HasComp<OxydBorerHostComponent>(host))
        {
            _popup.PopupEntity(Loc.GetString("oxyd-borer-infest-occupied"), borer, borer);
            return;
        }

        var delay = comp.InfestDelay;

        // Eris: NT disciple nanofiber mesh implant slows entry.
        if (HasComp<CruciformBearerComponent>(host))
        {
            delay *= comp.ArmoredInfestMultiplier;
            _popup.PopupEntity(Loc.GetString("oxyd-borer-infest-implant"), borer, borer);
        }

        // Eris: covered headgear takes time to work around (non-removable headgear blocks).
        if (_inventory.TryGetSlotEntity(host, "head", out var headgear) &&
            !_inventory.CanUnequip(host, host, "head", out _))
        {
            _popup.PopupEntity(Loc.GetString("oxyd-borer-infest-protected"), borer, borer);
            return;
        }
        else if (headgear != null)
        {
            delay *= comp.ArmoredInfestMultiplier;
        }

        _popup.PopupEntity(Loc.GetString("oxyd-borer-infest-start-host"), host, host, PopupType.MediumCaution);
        _popup.PopupEntity(Loc.GetString("oxyd-borer-infest-start", ("host", host)), borer, borer);

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, borer, delay,
            new OxydBorerInfestDoAfterEvent(), borer, target: host)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        });
    }

    private void OnInfestDone(EntityUid uid, OxydBorerComponent comp, OxydBorerInfestDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } host || comp.Host != null)
            return;
        args.Handled = true;

        if (HasComp<OxydBorerHostComponent>(host) || _mobs.IsDead(uid))
            return;

        var hostComp = EnsureComp<OxydBorerHostComponent>(host);
        hostComp.BorerContainer ??= _container.EnsureContainer<Container>(host, OxydBorerHostComponent.BorerContainerId);
        if (!_container.Insert(uid, hostComp.BorerContainer))
            return;

        comp.Host = host;
        hostComp.Borer = uid;
        Dirty(uid, comp);
        Dirty(host, hostComp);

        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(uid):user} infested {ToPrettyString(host):target}");
        _popup.PopupEntity(Loc.GetString("oxyd-borer-infest-done", ("host", host)), uid, uid);
        _popup.PopupEntity(Loc.GetString("oxyd-borer-infest-done-host"), host, host, PopupType.LargeCaution);
    }

    // ------------------------------------------------------------------
    // In-host abilities
    // ------------------------------------------------------------------

    private void TrySecrete(EntityUid borer, OxydBorerComponent comp, EntityUid host, ProtoId<Content.Shared.Chemistry.Reagent.ReagentPrototype> reagent)
    {
        if (comp.Host != host || _mobs.IsDead(borer))
            return;

        if (comp.Docile)
        {
            _popup.PopupEntity(Loc.GetString("oxyd-borer-docile"), borer, borer);
            return;
        }

        if (comp.Chemicals < comp.SecreteCost)
        {
            _popup.PopupEntity(Loc.GetString("oxyd-borer-not-enough-chems"), borer, borer);
            return;
        }

        if (!_solutions.TryGetSolution(host, BloodstreamComponent.DefaultBloodSolutionName,
                out var bloodEnt, out var blood))
            return;

        if (!_solutions.TryAddReagent(bloodEnt.Value, reagent, comp.SecreteAmount))
            return;

        comp.Chemicals -= comp.SecreteCost;
        Dirty(borer, comp);
        _adminLogger.Add(LogType.ForceFeed, LogImpact.Medium,
            $"{ToPrettyString(borer):user} secreted {comp.SecreteAmount}u of {reagent} into {ToPrettyString(host):target}");

        var name = _proto.TryIndex(reagent, out var proto) ? proto.LocalizedName : reagent.Id;
        _popup.PopupEntity(Loc.GetString("oxyd-borer-secreted",
            ("reagent", name), ("amount", blood.GetTotalPrototypeQuantity(reagent).Float())), borer, borer);
    }

    private void TryReproduce(EntityUid borer, OxydBorerComponent comp, EntityUid host)
    {
        if (comp.Host != host || _mobs.IsDead(borer))
            return;

        if (comp.Docile)
        {
            _popup.PopupEntity(Loc.GetString("oxyd-borer-docile"), borer, borer);
            return;
        }

        var cost = MathF.Round(comp.MaxChemicalsInHost * comp.ReproduceFraction);
        if (comp.Chemicals < cost)
        {
            _popup.PopupEntity(Loc.GetString("oxyd-borer-reproduce-poor", ("cost", cost)), borer, borer);
            return;
        }

        comp.Chemicals -= cost;
        comp.HasReproduced = true;
        Dirty(borer, comp);

        // Eris: host heaves violently, expelling vomit and a wriggling young.
        _vomit.Vomit(host, force: true);
        Spawn(comp.ReproducePrototype, Transform(host).Coordinates);
        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(borer):user} reproduced inside {ToPrettyString(host):target}");
        _popup.PopupEntity(Loc.GetString("oxyd-borer-reproduce-host"), host, host, PopupType.LargeCaution);
        _popup.PopupEntity(Loc.GetString("oxyd-borer-reproduce"), borer, borer);
    }

    // ------------------------------------------------------------------
    // Control-state verbs — on the HOST entity while the borer's mind drives it
    // (Eris abilities_in_control: release_control / talk_host / spawn_larvae)
    // ------------------------------------------------------------------

    private void AddControlVerbs(EntityUid uid, OxydBorerHostComponent comp, GetVerbsEvent<InnateVerb> args)
    {
        if (args.User != uid ||
            comp.Borer is not { } borer ||
            !TryComp<OxydBorerComponent>(borer, out var borerComp) ||
            !borerComp.Controlling)
            return;

        var category = new VerbCategory(Loc.GetString("oxyd-borer-verb-category"), null);

        args.Verbs.Add(new InnateVerb
        {
            Text = Loc.GetString("oxyd-borer-verb-release-control"),
            Category = category,
            Act = () => TryReleaseControl(borer, borerComp, uid),
        });

        args.Verbs.Add(new InnateVerb
        {
            Text = Loc.GetString("oxyd-borer-verb-talk-captive"),
            Category = category,
            Act = () => TryTalkToCaptive(borer, borerComp, uid),
        });

        // Eris abilities_in_control also includes spawn_larvae (same reproduce).
        args.Verbs.Add(new InnateVerb
        {
            Text = Loc.GetString("oxyd-borer-verb-reproduce"),
            Category = category,
            Act = () => TryReproduce(borer, borerComp, uid),
        });
    }

    /// <summary>Captive brain verbs (Eris captive_brain say + process_resist).</summary>
    private void AddCaptiveVerbs(EntityUid uid, OxydBorerCaptiveComponent comp, GetVerbsEvent<InnateVerb> args)
    {
        if (args.User != uid)
            return;

        var category = new VerbCategory(Loc.GetString("oxyd-borer-verb-category"), null);

        args.Verbs.Add(new InnateVerb
        {
            Text = Loc.GetString("oxyd-borer-verb-captive-whisper"),
            Category = category,
            Act = () => TryCaptiveWhisper(uid, comp),
        });

        args.Verbs.Add(new InnateVerb
        {
            Text = Loc.GetString("oxyd-borer-verb-captive-resist"),
            Category = category,
            Act = () => TryStartResist(uid, comp),
        });
    }

    /// <summary>Psychic Whisper + Commune on a victim — usable by a free borer or a
    /// controlled host (Eris commune / psychic_whisper).</summary>
    private void AddPsychicVerbs(GetVerbsEvent<AlternativeVerb> args, EntityUid borerEnt,
        OxydBorerComponent borerComp, EntityUid victim)
    {
        var category = new VerbCategory(Loc.GetString("oxyd-borer-verb-category"), null);
        var user = args.User;
        var target = victim;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("oxyd-borer-verb-psychic-whisper"),
            Category = category,
            Act = () => TryPsychicWhisper(user, target),
            Priority = -7,
        });

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("oxyd-borer-verb-commune"),
            Category = category,
            Act = () => TryCommune(borerEnt, user, target),
            Priority = -8,
        });
    }

    private void TryStartRelease(EntityUid borer, OxydBorerComponent comp, EntityUid host)
    {
        if (comp.Host != host || _mobs.IsDead(borer))
            return;

        if (comp.Docile)
        {
            _popup.PopupEntity(Loc.GetString("oxyd-borer-docile"), borer, borer);
            return;
        }

        _popup.PopupEntity(Loc.GetString("oxyd-borer-release-start"), borer, borer);
        if (!_mobs.IsDead(host))
            _popup.PopupEntity(Loc.GetString("oxyd-borer-release-start-host"), host, host, PopupType.MediumCaution);

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, borer, comp.ReleaseDelay,
            new OxydBorerReleaseDoAfterEvent(), borer, target: host)
        {
            BreakOnMove = false,
            BreakOnDamage = false,
            NeedHand = false,
        });
    }

    private void OnReleaseDone(EntityUid uid, OxydBorerComponent comp, OxydBorerReleaseDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;
        args.Handled = true;

        if (comp.Host is not { } host)
            return;

        LeaveHost(uid, comp, host);

        _popup.PopupEntity(Loc.GetString("oxyd-borer-release-done", ("host", host)), uid, uid);
        _popup.PopupEntity(Loc.GetString("oxyd-borer-release-done-host"), host, host, PopupType.LargeCaution);
    }

    /// <summary>Ejects the borer onto the host's turf and clears both sides of the link.</summary>
    private void LeaveHost(EntityUid borer, OxydBorerComponent comp, EntityUid host)
    {
        // Eris release_host(): detach control first so both minds go home.
        if (comp.Controlling)
            DetachControl(borer, comp, host);

        var coords = Transform(host).Coordinates;
        if (TryComp<OxydBorerHostComponent>(host, out var hostComp))
        {
            if (hostComp.BorerContainer.Contains(borer))
                _container.Remove(borer, hostComp.BorerContainer);
            hostComp.Borer = null;
            RemComp<OxydBorerHostComponent>(host);
        }

        comp.Host = null;
        comp.Docile = false;
        Dirty(borer, comp);
        _transform.SetCoordinates(borer, coords);
    }

    // ------------------------------------------------------------------
    // Paralyze Victim (Eris: 1min cooldown, 10 chemicals, fear freezes limbs)
    // ------------------------------------------------------------------

    private void TryParalyze(EntityUid borer, OxydBorerComponent comp, EntityUid victim)
    {
        if (_mobs.IsDead(borer) || comp.Host != null)
            return;

        var now = _timing.CurTime;
        if (now < comp.LastParalyze + comp.ParalyzeCooldown)
        {
            var left = (int) (comp.LastParalyze + comp.ParalyzeCooldown - now).TotalSeconds;
            _popup.PopupEntity(Loc.GetString("oxyd-borer-paralyze-cooldown-in", ("seconds", left)), borer, borer);
            return;
        }

        if (comp.Chemicals < comp.ParalyzeCost)
        {
            _popup.PopupEntity(Loc.GetString("oxyd-borer-not-enough-chems"), borer, borer);
            return;
        }

        if (_mobs.IsDead(victim) || HasComp<OxydBorerHostComponent>(victim))
            return;

        comp.Chemicals -= comp.ParalyzeCost;
        comp.LastParalyze = now;
        Dirty(borer, comp);
        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(borer):user} paralyzed {ToPrettyString(victim):target}");

        _stun.TryUpdateParalyzeDuration(victim, comp.ParalyzeDuration);
        _stun.TryKnockdown(victim, comp.ParalyzeDuration, force: true);

        _popup.PopupEntity(Loc.GetString("oxyd-borer-paralyze", ("victim", victim)), borer, borer);
        _popup.PopupEntity(Loc.GetString("oxyd-borer-paralyze-victim"), victim, victim, PopupType.LargeCaution);
    }

    // ------------------------------------------------------------------
    // Lifecycle / upkeep
    // ------------------------------------------------------------------

    private void OnBorerStateChanged(EntityUid uid, OxydBorerComponent comp, MobStateChangedEvent args)
    {
        // Eris death(): detach control, then a dead borer falls out of its host.
        if (args.NewMobState == MobState.Dead && comp.Host is { } host)
        {
            if (comp.Controlling)
                DetachControl(uid, comp, host);
            LeaveHost(uid, comp, host);
        }
    }

    private void OnBorerShutdown(EntityUid uid, OxydBorerComponent comp, ComponentShutdown args)
    {
        if (comp.Host is { } host && comp.Controlling)
            DetachControl(uid, comp, host);

        if (comp.Host is { } h && TryComp<OxydBorerHostComponent>(h, out var hostComp))
        {
            hostComp.Borer = null;
            RemCompDeferred<OxydBorerHostComponent>(h);
        }
        comp.Host = null;
    }

    private void OnHostStateChanged(EntityUid uid, OxydBorerHostComponent comp, MobStateChangedEvent args)
    {
        // Eris host_death(): control stops instantly; the borer stays inside the corpse.
        if (args.NewMobState == MobState.Dead && comp.Borer is { } borer)
        {
            if (TryComp<OxydBorerComponent>(borer, out var borerComp) && borerComp.Controlling)
            {
                DetachControl(borer, borerComp, uid);
                _popup.PopupEntity(Loc.GetString("oxyd-borer-control-stopped"), borer, borer, PopupType.LargeCaution);
            }
            _popup.PopupEntity(Loc.GetString("oxyd-borer-host-died"), borer, borer, PopupType.LargeCaution);
        }
    }

    /// <summary>Borer pulled out of the host container by anything other than LeaveHost
    /// (admin verbs, other container ops) — keep both sides consistent.</summary>
    private void OnBorerRemoved(EntityUid uid, OxydBorerHostComponent comp, EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != OxydBorerHostComponent.BorerContainerId)
            return;

        var borer = args.Entity;
        comp.Borer = null;
        RemCompDeferred<OxydBorerHostComponent>(uid);

        if (TryComp<OxydBorerComponent>(borer, out var borerComp))
        {
            borerComp.Host = null;
            borerComp.Docile = false;
            Dirty(borer, borerComp);
        }
    }

    /// <summary>Gibbed/deleted hosts free the borer instead of deleting it with them.</summary>
    private void OnHostTerminating(EntityUid uid, OxydBorerHostComponent comp, EntityTerminatingEvent args)
    {
        if (comp.Borer is { } borer && !TerminatingOrDeleted(borer))
        {
            _container.Remove(borer, comp.BorerContainer);
            if (TryComp<OxydBorerComponent>(borer, out var borerComp))
            {
                borerComp.Host = null;
                borerComp.Docile = false;
                Dirty(borer, borerComp);
            }
        }
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<OxydBorerComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out var comp, out var mobState))
        {
            var now = _timing.CurTime;
            if (now < comp.NextUpdate)
                continue;
            comp.NextUpdate = now + TimeSpan.FromSeconds(comp.UpdateInterval);

            if (mobState.CurrentState == MobState.Dead)
                continue;

            // Eris Life(): chems regen to the out-of-host cap; inside a host to the larger
            // in-host cap, plus brute regen and the sugar-docility check.
            var max = comp.Host != null ? comp.MaxChemicalsInHost : comp.MaxChemicals;
            var regen = comp.Host != null ? comp.ChemRegenInHostPerSecond : comp.ChemRegenPerSecond;
            if (comp.Chemicals < max)
            {
                comp.Chemicals = MathF.Min(max, comp.Chemicals + regen);
                Dirty(uid, comp);
            }

            if (comp.Host is not { } host)
                continue;

            // Regenerate while nested (Eris adjustBruteLoss(-1) per Life tick).
            _damage.TryChangeDamage(uid,
                new DamageSpecifier(_proto.Index(comp.HostRegenDamageType), -comp.HostRegenBrutePerSecond),
                ignoreResistances: true);

            // Sugar in the host's bloodstream sedates the borer (docile) until it metabolizes away.
            var docile = _solutions.TryGetSolution(host, BloodstreamComponent.DefaultBloodSolutionName,
                             out var bloodEnt, out var blood) &&
                         blood.GetTotalPrototypeQuantity(Sugar) > 0;

            if (docile == comp.Docile)
                continue;

            comp.Docile = docile;
            Dirty(uid, comp);
            _popup.PopupEntity(
                Loc.GetString(docile ? "oxyd-borer-docile-on" : "oxyd-borer-docile-off"),
                uid, uid);

            // Eris process_host(): docile forced-release of a controlled host.
            if (docile && comp.Controlling)
            {
                _popup.PopupEntity(Loc.GetString("oxyd-borer-docile-control"), host, host, PopupType.LargeCaution);
                DetachControl(uid, comp, host);
            }
        }
    }
}
