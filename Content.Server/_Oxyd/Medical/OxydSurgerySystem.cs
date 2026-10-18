using System.Linq;
using Content.Server.DoAfter;
using Content.Shared._Oxyd.Medical;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Damage.Systems;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Robust.Shared.GameObjects;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Medical.Healing;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Standing;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Ports Eris's organ-based surgery (modules/surgery). A surgical tool clicked on a patient
/// attempts the matching step directly (Eris do_surgery tool-quality dispatch); when no step
/// applies the organ/step window opens on a nullspace proxy. Patients not lying on an
/// operating surface only allow surface steps (Eris CAN_OPERATE_STANDING), and every step
/// rolls quality minus difficulty like Eris's FAILCHANCE_* checks.
/// </summary>
public sealed partial class OxydSurgerySystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private DoAfterSystem _doAfter = default!;
    [Dependency] private OxydWoundSystem _wounds = default!;
    [Dependency] private DetachableOrganSystem _detach = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedStackSystem _stacks = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;

    private const string ImplantContainerId = "oxyd-surgery-implants";
    private static readonly ProtoId<TagPrototype> CableCoilTag = "CableCoil";

    private record struct OrganEntry(EntityUid Uid, OrganComponent Organ, OxydOrganSurgeryComponent Surg)
    {
        public bool Valid => Uid.IsValid();
    }

    public override void Initialize()
    {
        SubscribeLocalEvent<BodyComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<BodyComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<BodyComponent, OxydSurgeryDoAfterEvent>(OnPatientStepDone);
        SubscribeLocalEvent<OxydSurgerySessionComponent, OxydSurgerySelectStepMessage>(OnSelectStep);
        SubscribeLocalEvent<OxydSurgerySessionComponent, BoundUIClosedEvent>(OnUiClosed);
        SubscribeLocalEvent<BodyComponent, GetVerbsEvent<InteractionVerb>>(OnGetSurgeryVerbs);
    }

    // Context-menu mirror of the click path so the window is reachable without pixel clicks
    // (also the standard SS14 affordance for interactions like this).
    private void OnGetSurgeryVerbs(EntityUid uid, BodyComponent comp, GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !IsOperable(uid))
            return;

        // A surgical tool in any hand qualifies (args.Using is only the active one).
        EntityUid? surgicalTool = null;
        var hasFreeHand = _hands.CountFreeHands(args.User) > 0;
        foreach (var held in _hands.EnumerateHeld(args.User))
        {
            if (TryComp<OxydSurgeryToolComponent>(held, out var tool) && tool.Tools != OxydSurgeryTool.None)
                surgicalTool ??= held;
        }
        var bareHandsOnOpenSite = hasFreeHand
            && FindOrgan(uid, o => o.Surg.Incision != OxydIncisionStage.None).Valid;
        if (surgicalTool == null && !bareHandsOnOpenSite)
            return;

        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("oxyd-surgery-verb"),
            Act = () => OpenSurgeryUi(args.User, uid, surgicalTool),
        });
    }

    // Sole InteractUsingEvent subscriber on BodyComponent (the bus allows one per comp+event):
    // dispatch by what's being used on the patient. Health/autopsy scanners route through
    // their own item-side AfterInteractEvent handlers instead.
    private void OnInteractUsing(EntityUid uid, BodyComponent comp, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // Cable coil repairs robotic burns (Eris wires on robotic organs) and carries no
        // surgery component of its own.
        if (_tag.HasTag(args.Used, CableCoilTag))
        {
            if (!IsOperable(uid))
                return;
            args.Handled = TryDirectStep(args.User, uid, args.Used, null, OxydSurgeryTool.CableCoil);
            return;
        }

        if (TryComp<OxydSurgeryToolComponent>(args.Used, out var tool) && tool.Tools != OxydSurgeryTool.None)
        {
            if (!IsOperable(uid))
            {
                if (TryStandingStep(args.User, uid, args.Used, tool))
                    args.Handled = true;
                else if (!IsHealingKit(tool))
                {
                    args.Handled = true;
                    _popup.PopupEntity(Loc.GetString("oxyd-surgery-not-operable",
                        ("patient", Name(uid))), args.User, args.User);
                }
                // else leave unhandled so the stack heals normally.
                return;
            }

            if (TryDirectStep(args.User, uid, args.Used, tool, tool.Tools))
            {
                args.Handled = true;
                return;
            }

            // Kits with no surgical target fall through to normal topical healing.
            if (IsHealingKit(tool))
                return;

            args.Handled = true;
            OpenSurgeryUi(args.User, uid, args.Used);
            return;
        }

        // Healing items without a surgical role keep their normal topical behaviour.
        if (HasComp<HealingComponent>(args.Used))
            return;

        if (!IsOperable(uid))
            return;

        // A small item goes into a retracted cavity (Eris w_class check) — not IDs,
        // guns or handcuffs. An organ item or detached-limb shell is a transplant
        // (Eris attach_or_replace_organ), not a cavity implant — AttachOrgan still
        // validates Incision/decay itself.
        var cavity = FindOrgan(uid, o =>
            o.Surg is { Incision: OxydIncisionStage.Retracted } && OxydWoundSystem.IsExternal(o.Organ));
        if (cavity.Valid && (IsTransplantItem(args.Used) || CanImplant(args.Used)))
        {
            args.Handled = true;
            var step = IsTransplantItem(args.Used)
                ? OxydSurgeryStep.AttachOrgan
                : OxydSurgeryStep.InsertItem;
            RunStep(args.User, uid, cavity.Uid, step, args.Used, null);
            return;
        }

        // Only surgical-relevant items take over the click (transplants and
        // w_class-small implants) — cuffs, zipties, IDs and everything else keep
        // their normal interaction on the body.
        if (!IsTransplantItem(args.Used) && !CanImplant(args.Used))
            return;

        if (FindOrgan(uid, o => o.Surg.Incision != OxydIncisionStage.None).Valid)
        {
            args.Handled = true;
            OpenSurgeryUi(args.User, uid, args.Used);
        }
    }

    // Eris opens the organ window when clicking a patient with an open incision, bare hands too.
    private void OnInteractHand(EntityUid uid, BodyComponent comp, InteractHandEvent args)
    {
        if (args.Handled || !IsOperable(uid))
            return;

        if (!FindOrgan(uid, o => o.Surg.Incision != OxydIncisionStage.None).Valid)
            return;

        args.Handled = true;
        OpenSurgeryUi(args.User, uid, null);
    }

    /// <summary>Eris can_operate: full surgery needs the patient lying down or buckled
    /// (op table, bed, chair). Otherwise only CAN_OPERATE_STANDING surface steps run.
    /// Self-surgery is always standing-only.</summary>
    private bool IsOperable(EntityUid patient)
    {
        if (_standing.IsDown(patient))
            return true;
        if (TryComp<BuckleComponent>(patient, out var buckle) && buckle.Buckled)
            return true;
        // An operating table under the patient counts even without buckling (resting on it).
        var coords = Transform(patient).Coordinates;
        return _lookup.GetEntitiesInRange<OxydOperatingTableComponent>(coords, 0.4f).Any();
    }

    private static bool IsHealingKit(OxydSurgeryToolComponent tool) =>
        (tool.Tools & (OxydSurgeryTool.TraumaKit | OxydSurgeryTool.BurnKit)) != 0;

    /// <summary>Only small items may be packed into a body cavity (Eris w_class check).</summary>
    private bool CanImplant(EntityUid item) =>
        TryComp<ItemComponent>(item, out var ic) && OxydItemSizes.Implantable.Contains(ic.Size);

    private List<OrganEntry> GetOrganEntries(EntityUid body) =>
        _wounds.GetOrgans(body).Select(o => new OrganEntry(o.Uid, o.Organ, o.Surgery)).ToList();

    private OrganEntry FindOrgan(EntityUid body, Func<OrganEntry, bool> pred)
    {
        foreach (var o in GetOrganEntries(body))
        {
            if (pred(o))
                return o;
        }
        return default;
    }

    // Eris standing mode: shrapnel removal and cautery only (surgery.dm CAN_OPERATE_STANDING).
    private bool TryStandingStep(EntityUid user, EntityUid patient, EntityUid toolEnt, OxydSurgeryToolComponent tool)
    {
        if ((tool.Tools & (OxydSurgeryTool.Scalpel | OxydSurgeryTool.Saw | OxydSurgeryTool.Drill)) != 0)
        {
            var embedded = FindOrgan(patient, o => o.Surg.EmbeddedItems.Count > 0);
            if (embedded.Valid)
            {
                RunStep(user, patient, embedded.Uid, OxydSurgeryStep.ExtractShrapnel, toolEnt, tool);
                return true;
            }
        }

        if ((tool.Tools & OxydSurgeryTool.Cautery) != 0)
        {
            var wounded = FindOrgan(patient, o =>
                OxydWoundSystem.IsExternal(o.Organ) &&
                (o.Surg.OrganDamage > 0 || o.Surg.WoundBleedRate > 0 || o.Surg.Incision != OxydIncisionStage.None));
            if (wounded.Valid)
            {
                RunStep(user, patient, wounded.Uid, OxydSurgeryStep.CloseWounds, toolEnt, tool);
                return true;
            }
        }

        return false;
    }

    // Eris do_surgery: map the clicked tool straight onto the applicable step for the best organ.
    private bool TryDirectStep(EntityUid user, EntityUid patient, EntityUid toolEnt,
        OxydSurgeryToolComponent? tool, OxydSurgeryTool tools)
    {
        var organs = GetOrganEntries(patient);

        OrganEntry FirstExternal(Func<OxydOrganSurgeryComponent, bool> pred) =>
            organs.FirstOrDefault(o => OxydWoundSystem.IsExternal(o.Organ) && !o.Surg.Robotic && pred(o.Surg));

        OrganEntry FirstRobotic(Func<OxydOrganSurgeryComponent, bool> pred) =>
            organs.FirstOrDefault(o => o.Surg.Robotic && pred(o.Surg));

        OxydSurgeryStep? step = null;
        EntityUid organ = default;

        void Pick(OrganEntry o, OxydSurgeryStep s)
        {
            if (o.Valid && step == null)
            {
                organ = o.Uid;
                step = s;
            }
        }

        if ((tools & OxydSurgeryTool.CableCoil) != 0)
            Pick(FirstRobotic(s => s is { Incision: not OxydIncisionStage.None, OrganDamage: > 0 }),
                OxydSurgeryStep.RoboFixBurn);

        if (step == null && (tools & OxydSurgeryTool.Screwdriver) != 0)
        {
            Pick(FirstRobotic(s => s.Incision != OxydIncisionStage.None), OxydSurgeryStep.RoboClose);
            Pick(FirstRobotic(s => s.Incision == OxydIncisionStage.None), OxydSurgeryStep.RoboOpen);
        }

        if (step == null && (tools & OxydSurgeryTool.Welder) != 0)
            Pick(FirstRobotic(s => s is { Incision: not OxydIncisionStage.None, OrganDamage: > 0 }),
                OxydSurgeryStep.RoboFixBrute);

        if (step == null && (tools & (OxydSurgeryTool.TraumaKit | OxydSurgeryTool.BurnKit)) != 0)
        {
            // Eris fix_organ/fix_brute: works through any open site on the most damaged organ.
            if (organs.Any(o => o.Surg.Incision != OxydIncisionStage.None))
            {
                var damaged = organs.Where(o => o.Surg.OrganDamage > 0)
                    .OrderByDescending(o => o.Surg.OrganDamage)
                    .FirstOrDefault();
                if (damaged.Valid)
                {
                    organ = damaged.Uid;
                    step = OxydSurgeryStep.FixOrgan;
                }
            }
        }

        if (step == null && (tools & OxydSurgeryTool.Scalpel) != 0)
        {
            Pick(FirstExternal(s => s.Incision == OxydIncisionStage.None), OxydSurgeryStep.CutOpen);
            Pick(FirstExternal(s => s.Incision == OxydIncisionStage.Retracted && s.EmbeddedItems.Count > 0),
                OxydSurgeryStep.RemoveEmbedded);
        }

        if (step == null && (tools & OxydSurgeryTool.Retractor) != 0)
            Pick(FirstExternal(s => s.Incision == OxydIncisionStage.Open), OxydSurgeryStep.RetractSkin);

        if (step == null && (tools & OxydSurgeryTool.Hemostat) != 0)
        {
            Pick(FirstExternal(s => s is { Incision: not OxydIncisionStage.None, Clamped: false }),
                OxydSurgeryStep.FixBleeding);
            Pick(FirstExternal(s => s is { Incision: OxydIncisionStage.Retracted, EmbeddedItems.Count: > 0 }),
                OxydSurgeryStep.RemoveEmbedded);
        }

        if (step == null && (tools & OxydSurgeryTool.Cautery) != 0)
            Pick(FirstExternal(s => s.Incision != OxydIncisionStage.None), OxydSurgeryStep.Cauterize);

        if (step == null && (tools & OxydSurgeryTool.BoneSetter) != 0)
        {
            Pick(FirstExternal(s => s is { Incision: OxydIncisionStage.Retracted, Fractured: true }),
                OxydSurgeryStep.MendBone);
            Pick(FirstExternal(s => s is { Incision: OxydIncisionStage.Retracted, Fractured: false }),
                OxydSurgeryStep.BreakBone);
        }

        if (step == null && (tools & OxydSurgeryTool.BoneGel) != 0)
            Pick(FirstExternal(s => s is { Incision: OxydIncisionStage.Retracted, Fractured: false }),
                OxydSurgeryStep.FixBone);

        if (step == null && (tools & OxydSurgeryTool.Saw) != 0)
            Pick(FirstExternal(s => s.Incision == OxydIncisionStage.Retracted), OxydSurgeryStep.Amputate);

        if (step == null)
            return false;

        RunStep(user, patient, organ, step.Value, toolEnt, tool);
        return true;
    }

    // ----- shared step execution (direct click + UI selection) -----

    private static float StepDuration(OxydSurgeryStep step) => step switch
    {
        OxydSurgeryStep.DiagnoseWound => 0.5f,
        OxydSurgeryStep.CutOpen => 3f,
        OxydSurgeryStep.Cauterize or OxydSurgeryStep.CloseWounds => 3f,
        OxydSurgeryStep.FixBleeding => 2.5f,
        OxydSurgeryStep.RetractSkin => 2f,
        OxydSurgeryStep.MendBone or OxydSurgeryStep.FixBone => 4f,
        OxydSurgeryStep.Amputate or OxydSurgeryStep.DetachOrgan => 6f,
        OxydSurgeryStep.RemoveEmbedded or OxydSurgeryStep.RemoveItem => 3f,
        OxydSurgeryStep.ExtractShrapnel => 6f,
        OxydSurgeryStep.InsertItem or OxydSurgeryStep.AttachOrgan => 5f,
        OxydSurgeryStep.FixOrgan => 4f,
        OxydSurgeryStep.RoboOpen or OxydSurgeryStep.RoboClose => 2f,
        OxydSurgeryStep.RoboFixBrute or OxydSurgeryStep.RoboFixBurn => 4f,
        _ => 3f,
    };

    /// <summary>Eris FAILCHANCE_* per step: subtracted from tool quality for the success roll.</summary>
    private static int StepDifficulty(OxydSurgeryStep step) => step switch
    {
        OxydSurgeryStep.DiagnoseWound => 0,
        OxydSurgeryStep.RetractSkin or OxydSurgeryStep.RoboOpen or OxydSurgeryStep.RoboClose => 10,
        OxydSurgeryStep.Cauterize or OxydSurgeryStep.CloseWounds or OxydSurgeryStep.FixBleeding => 15,
        OxydSurgeryStep.CutOpen => 20,
        OxydSurgeryStep.RemoveEmbedded or OxydSurgeryStep.RemoveItem or OxydSurgeryStep.InsertItem
            or OxydSurgeryStep.ExtractShrapnel => 20,
        OxydSurgeryStep.BreakBone or OxydSurgeryStep.MendBone or OxydSurgeryStep.FixBone
            or OxydSurgeryStep.RoboFixBrute or OxydSurgeryStep.RoboFixBurn => 25,
        OxydSurgeryStep.FixOrgan => 15,
        OxydSurgeryStep.Amputate or OxydSurgeryStep.DetachOrgan => 30,
        OxydSurgeryStep.AttachOrgan => 35,
        _ => 20,
    };

    /// <summary>Pain inflicted by an attempt (Eris inflict_agony).</summary>
    private static float StepAgony(OxydSurgeryStep step) => step switch
    {
        OxydSurgeryStep.DiagnoseWound or OxydSurgeryStep.RoboOpen or OxydSurgeryStep.RoboClose
            or OxydSurgeryStep.RoboFixBrute or OxydSurgeryStep.RoboFixBurn => 0f,
        OxydSurgeryStep.Amputate or OxydSurgeryStep.DetachOrgan or OxydSurgeryStep.BreakBone => 20f,
        _ => 10f,
    };

    /// <summary>Starts a surgery step DoAfter; the completion event lands on the patient.</summary>
    private void RunStep(EntityUid surgeon, EntityUid patient, EntityUid organ, OxydSurgeryStep step,
        EntityUid? tool, OxydSurgeryToolComponent? toolComp)
    {
        var surg = EnsureComp<OxydOrganSurgeryComponent>(organ);
        // Reviewer: one operation per organ at a time — a step already running on
        // this organ blocks a second surgeon's step on it.
        if (surg.RunningStep != null)
        {
            _popup.PopupEntity("That organ is already being operated on.", surgeon, surgeon);
            return;
        }
        // Reviewer: the tool must still be held when the step starts.
        if (tool is { } held && !_hands.IsHolding(surgeon, held))
        {
            _popup.PopupEntity("You're not holding that tool anymore.", surgeon, surgeon);
            return;
        }
        var speed = toolComp?.Speed ?? 1f;
        var ev = new OxydSurgeryDoAfterEvent(GetNetEntity(patient), GetNetEntity(organ), step,
            tool is { } t ? GetNetEntity(t) : NetEntity.Invalid)
        {
            SelfSurgery = surgeon == patient,
        };

        var doArgs = new DoAfterArgs(EntityManager, surgeon, TimeSpan.FromSeconds(StepDuration(step) * speed), ev,
            patient, patient, tool)
        {
            BreakOnMove = true,
            MovementThreshold = 0.5f,
            BreakOnDamage = true,
            DamageThreshold = 5,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(doArgs))
        {
            surg.RunningStep = step;
            Announce(surgeon, patient, "oxyd-medical-surgery-start", organ);
            PushAllStates(patient);
        }
    }

    // UI path. The message is raised on the patient hosting the BUI; the session is
    // looked up by the actor who clicked.
    private void OnSelectStep(EntityUid patient, OxydSurgerySessionComponent comp, OxydSurgerySelectStepMessage args)
    {
        if (!comp.Sessions.TryGetValue(args.Actor, out var s))
            return;

        var organ = GetEntity(args.Organ);
        if (!TryComp<OrganComponent>(organ, out var organComp) || organComp.Body != patient)
            return;

        var surg = EnsureComp<OxydOrganSurgeryComponent>(organ);
        // Reviewer: re-validate the held tool when the step actually starts —
        // the session's recorded tool may have been dropped since the window opened.
        if (s.Tool is { } rec && !_hands.IsHolding(args.Actor, rec))
            s.Tool = HeldStepItem(args.Actor);
        var tools = ToolFlagsOf(s.Tool);
        if (!AvailableSteps(tools, s.Tool, organComp, surg,
                s.Tool is { } held && IsTransplantItem(held),
                s.Tool is { } h2 && CanImplant(h2)).Contains(args.Step))
            return;

        RunStep(args.Actor, patient, organ, args.Step, s.Tool,
            s.Tool is { } t ? CompOrNull<OxydSurgeryToolComponent>(t) : null);
    }

    /// <summary>First held item usable for a step: a surgical tool, else a
    /// transplant organ/limb, else an implantable item.</summary>
    private EntityUid? HeldStepItem(EntityUid surgeon)
    {
        EntityUid? transplant = null, implant = null;
        foreach (var held in _hands.EnumerateHeld(surgeon))
        {
            if (TryComp<OxydSurgeryToolComponent>(held, out var tc) && tc.Tools != OxydSurgeryTool.None)
                return held;
            if (transplant == null && IsTransplantItem(held))
                transplant = held;
            if (implant == null && CanImplant(held))
                implant = held;
        }
        return transplant ?? implant;
    }

    /// <summary>Surgical tool flags across everything the surgeon holds (cable coil included).</summary>
    private OxydSurgeryTool HeldToolFlags(EntityUid surgeon)
    {
        var flags = OxydSurgeryTool.None;
        foreach (var held in _hands.EnumerateHeld(surgeon))
            flags |= ToolFlagsOf(held);
        return flags;
    }

    private OxydSurgeryTool ToolFlagsOf(EntityUid? tool)
    {
        if (tool is not { } t)
            return OxydSurgeryTool.None;
        var flags = TryComp<OxydSurgeryToolComponent>(t, out var c) ? c.Tools : OxydSurgeryTool.None;
        if (_tag.HasTag(t, CableCoilTag))
            flags |= OxydSurgeryTool.CableCoil;
        return flags;
    }

    // DoAfter landing on the patient.
    private void OnPatientStepDone(EntityUid uid, BodyComponent comp, OxydSurgeryDoAfterEvent args)
    {
        var organ = GetEntity(args.Organ);
        if (TryComp<OxydOrganSurgeryComponent>(organ, out var running))
            running.RunningStep = null;

        if (!args.Cancelled)
            CompleteStep(args, args.User, uid);

        // Refresh any surgery windows open on this patient so progress
        // shows up without closing and reopening.
        PushAllStates(uid);
    }

    /// <summary>Eris try_surgery_step: roll quality minus difficulty (plus self-surgery penalty),
    /// run end_step on success or fail_step malpractice on failure.</summary>
    private void CompleteStep(OxydSurgeryDoAfterEvent args, EntityUid surgeon, EntityUid patient)
    {
        var organ = GetEntity(args.Organ);
        if (!TryComp<OrganComponent>(organ, out var organComp) || organComp.Body != patient)
            return;

        var surg = EnsureComp<OxydOrganSurgeryComponent>(organ);
        var tool = args.Tool == NetEntity.Invalid ? (EntityUid?) null : GetEntity(args.Tool);
        // Reviewer: re-validate the held tool at completion — it may have been
        // dropped mid-DoAfter, and a dropped item must not drive the result.
        if (tool is { } t && !_hands.IsHolding(surgeon, t))
            tool = null;
        var needed = ToolForStep(args.Step);
        // Diagnose is free on an undiagnosed organ (AvailableSteps adds it bare);
        // on a diagnosed one it rides the scalpel flag like the UI gate does.
        if (args.Step == OxydSurgeryStep.DiagnoseWound && !surg.Diagnosed)
            needed = OxydSurgeryTool.None;
        var itemStep = args.Step is OxydSurgeryStep.InsertItem or OxydSurgeryStep.AttachOrgan;
        if ((itemStep && tool == null) ||
            (needed != OxydSurgeryTool.None && (HeldToolFlags(surgeon) & needed) == 0))
        {
            _popup.PopupEntity("You're not holding the right tool anymore.", surgeon, surgeon);
            return;
        }
        var toolComp = tool is { } t2 ? CompOrNull<OxydSurgeryToolComponent>(t2) : null;

        // The clicked item carries no quality when it isn't a surgical tool
        // (e.g. a held organ) — Eris rolls the required tool's quality instead,
        // so take the best surgical quality across the surgeon's hands.
        var quality = toolComp?.Quality ?? BestHeldToolQuality(surgeon);
        var chance = Math.Clamp(quality - StepDifficulty(args.Step) - (args.SelfSurgery ? 20 : 0), 5, 95);
        var success = _random.Prob(chance / 100f);

        InflictAgony(patient, args.Step);

        if (!success)
        {
            FailStep(args.Step, surgeon, patient, organ, surg);
            return;
        }

        var invalid = ApplyStep(args.Step, surgeon, patient, organ, organComp, surg, tool);
        if (invalid)
            _popup.PopupEntity(Loc.GetString("oxyd-medical-surgery-fail"), surgeon, surgeon);
        else
            Announce(surgeon, patient, "oxyd-medical-surgery-success", organ);
    }

    /// <summary>Eris after_attempted_step pain (scaled down; no stat system to key off).</summary>
    private void InflictAgony(EntityUid patient, OxydSurgeryStep step)
    {
        if (!_mobs.IsAlive(patient) || !TryComp<PainComponent>(patient, out var pain))
            return;
        pain.TemporaryPain += StepAgony(step) * pain.TemporaryPainMultiplier;
    }

    /// <summary>Eris fail_step: malpractice — organ takes damage, the site keeps bleeding,
    /// and the patient gets a spike of pain.</summary>
    private void FailStep(OxydSurgeryStep step, EntityUid surgeon, EntityUid patient,
        EntityUid organ, OxydOrganSurgeryComponent surg)
    {
        OxydWoundSystem.AddOrganDamage(surg, 8f);
        if (surg.Incision != OxydIncisionStage.None)
            surg.Clamped = false;
        Dirty(organ, surg);

        _damage.TryChangeDamage(patient, new DamageSpecifier { DamageDict = { ["Slash"] = 6 } },
            ignoreResistances: true);
        if (TryComp<PainComponent>(patient, out var pain))
            pain.TemporaryPain += 15;

        var text = Loc.GetString("oxyd-medical-surgery-malpractice",
            ("organ", Name(organ)), ("patient", Name(patient)));
        _popup.PopupEntity(text, surgeon, surgeon);
        if (patient != surgeon && _mobs.IsAlive(patient))
            _popup.PopupEntity(text, patient, patient);
    }

    /// <summary>Eris end_step effects. Returns true when the step's preconditions no longer hold.</summary>
    private bool ApplyStep(OxydSurgeryStep step, EntityUid surgeon, EntityUid patient, EntityUid organ,
        OrganComponent organComp, OxydOrganSurgeryComponent surg, EntityUid? tool)
    {
        switch (step)
        {
            case OxydSurgeryStep.DiagnoseWound:
                surg.Diagnosed = true;
                _popup.PopupEntity(Diagnose(organComp, surg), surgeon, surgeon);
                break;
            case OxydSurgeryStep.CutOpen:
                surg.Incision = OxydIncisionStage.Open;
                surg.Clamped = false;
                break;
            case OxydSurgeryStep.RetractSkin:
                if (surg.Incision != OxydIncisionStage.Open) return true;
                surg.Incision = OxydIncisionStage.Retracted;
                break;
            case OxydSurgeryStep.FixBleeding:
                if (surg.Incision == OxydIncisionStage.None) return true;
                surg.Clamped = true;
                break;
            case OxydSurgeryStep.Cauterize:
                surg.Incision = OxydIncisionStage.None;
                surg.Clamped = true;
                surg.WoundBleedRate = 0;
                _damage.TryChangeDamage(patient, new DamageSpecifier { DamageDict = { ["Heat"] = 2 } },
                    ignoreResistances: true);
                break;
            case OxydSurgeryStep.CloseWounds:
                // Standing cautery: seals surface bleeding without an incision.
                surg.Clamped = true;
                surg.WoundBleedRate = 0;
                OxydWoundSystem.ReduceOrganDamage(surg, 10);
                _damage.TryChangeDamage(patient, new DamageSpecifier { DamageDict = { ["Heat"] = 3 } },
                    ignoreResistances: true);
                break;
            case OxydSurgeryStep.MendBone:
            case OxydSurgeryStep.FixBone:
                if (!surg.Fractured || surg.Incision != OxydIncisionStage.Retracted) return true;
                surg.Fractured = false;
                OxydWoundSystem.ReduceOrganDamage(surg, 15);
                if (TryComp<PainComponent>(patient, out var pain))
                    pain.TemporaryPain += 12;
                break;
            case OxydSurgeryStep.BreakBone:
                if (surg.Incision != OxydIncisionStage.Retracted) return true;
                surg.Fractured = true;
                surg.Splinted = false;
                break;
            case OxydSurgeryStep.RemoveEmbedded:
            case OxydSurgeryStep.RemoveItem:
            case OxydSurgeryStep.ExtractShrapnel:
                if (surg.EmbeddedItems.Count == 0)
                    return true;
                if (step != OxydSurgeryStep.ExtractShrapnel && surg.Incision == OxydIncisionStage.None)
                    return true;
                ExtractEmbedded(patient, surg);
                break;
            case OxydSurgeryStep.InsertItem:
                if (tool is not { } item || surg.Incision != OxydIncisionStage.Retracted ||
                    surg.EmbeddedItems.Count >= OxydOrganSurgeryComponent.ImplantCavityMax ||
                    !CanImplant(item))
                    return true;
                var cavity = _container.EnsureContainer<Container>(organ, ImplantContainerId);
                if (!_container.Insert(item, cavity))
                    return true;
                surg.EmbeddedItems.Add(GetNetEntity(item));
                _adminLogger.Add(LogType.Action, LogImpact.Medium,
                    $"{ToPrettyString(surgeon):user} implanted {ToPrettyString(item)} into {ToPrettyString(patient):target}'s {Name(organ)} cavity");
                break;
            case OxydSurgeryStep.Amputate:
            case OxydSurgeryStep.DetachOrgan:
                if (!OxydWoundSystem.IsExternal(organComp) && surg.Incision != OxydIncisionStage.Retracted)
                    return true;
                _detach.Detach(organ);
                _adminLogger.Add(LogType.Action, LogImpact.High,
                    $"{ToPrettyString(surgeon):user} removed {ToPrettyString(organ):target} from {ToPrettyString(patient):target} via {step}");
                _damage.TryChangeDamage(patient,
                    new DamageSpecifier { DamageDict = { ["Slash"] = 25 } }, ignoreResistances: true);
                break;
            case OxydSurgeryStep.AttachOrgan:
                if (surg.Incision != OxydIncisionStage.Retracted) return true;
                if (tool is { } heldOrgan && CompOrNull<OxydOrganSurgeryComponent>(heldOrgan) is { Decayed: true })
                {
                    _popup.PopupEntity(Loc.GetString("oxyd-surgery-organ-decayed"), surgeon, surgeon);
                    return true;
                }
                if (!TryAttach(patient, tool, surgeon)) return true;
                _adminLogger.Add(LogType.Action, LogImpact.Medium,
                    $"{ToPrettyString(surgeon):user} attached {ToPrettyString(tool!.Value):target} to {ToPrettyString(patient):target}");
                break;
            case OxydSurgeryStep.FixOrgan:
                if (surg.OrganDamage <= 0) return true;
                // Kits work through an open site; stacks consume a charge only on success
                // (Eris tool.use(1) in end_step).
                if (!FindOrgan(patient, o => o.Surg.Incision != OxydIncisionStage.None).Valid)
                    return true;
                if (tool is { } kit && TryComp<StackComponent>(kit, out var stack))
                    _stacks.ReduceCount((kit, stack), 1);
                surg.BruteDamage = 0;
                surg.BurnDamage = 0;
                surg.OrganDamage = 0;
                break;
            case OxydSurgeryStep.RoboOpen:
                if (!surg.Robotic || surg.Incision != OxydIncisionStage.None) return true;
                surg.Incision = OxydIncisionStage.Open;
                break;
            case OxydSurgeryStep.RoboClose:
                if (!surg.Robotic || surg.Incision == OxydIncisionStage.None) return true;
                surg.Incision = OxydIncisionStage.None;
                surg.Clamped = true;
                break;
            case OxydSurgeryStep.RoboFixBrute:
            case OxydSurgeryStep.RoboFixBurn:
                if (!surg.Robotic || surg.Incision == OxydIncisionStage.None || surg.OrganDamage <= 0)
                    return true;
                if (step == OxydSurgeryStep.RoboFixBrute)
                {
                    surg.BruteDamage = Math.Max(0, surg.BruteDamage - 15);
                    surg.OrganDamage = surg.BruteDamage + surg.BurnDamage;
                }
                else
                {
                    surg.BurnDamage = Math.Max(0, surg.BurnDamage - 15);
                    surg.OrganDamage = surg.BruteDamage + surg.BurnDamage;
                    if (tool is { } coil && TryComp<StackComponent>(coil, out var coilStack))
                        _stacks.ReduceCount((coil, coilStack), 1);
                }
                break;
        }

        Dirty(organ, surg);
        return false;
    }

    private void ExtractEmbedded(EntityUid patient, OxydOrganSurgeryComponent surg)
    {
        var net = surg.EmbeddedItems[^1];
        surg.EmbeddedItems.RemoveAt(surg.EmbeddedItems.Count - 1);
        var item = GetEntity(net);
        _container.TryRemoveFromContainer(item);
        _transform.SetCoordinates(item, Transform(patient).Coordinates);
    }

    /// <summary>Best OxydSurgeryTool quality in the surgeon's hands (60 bare).</summary>
    private int BestHeldToolQuality(EntityUid surgeon)
    {
        var best = 60;
        foreach (var held in _hands.EnumerateHeld(surgeon))
        {
            if (TryComp<OxydSurgeryToolComponent>(held, out var tool) && tool.Quality > best)
                best = tool.Quality;
        }
        return best;
    }

    /// <summary>Organ item or a detached-limb shell carrying organs.</summary>
    private bool IsTransplantItem(EntityUid item)
        => HasComp<OrganComponent>(item) || HasComp<BodyComponent>(item);

    private bool TryAttach(EntityUid body, EntityUid? heldItem, EntityUid surgeon)
    {
        if (heldItem is not { } item)
            return false;

        // Severed limb/head: the detached-body shell carries the limb's organs.
        // Reversing DetachableOrganSystem.Detach — move each organ into the
        // patient's body_organs, then the empty shell is consumed (Eris
        // attach_organ restores the limb, it doesn't bag it in the torso).
        if (TryComp<BodyComponent>(item, out _) &&
            _container.TryGetContainer(item, BodyComponent.ContainerID, out var limbContainer) &&
            _container.TryGetContainer(body, BodyComponent.ContainerID, out var patientContainer))
        {
            var containedAll = limbContainer.ContainedEntities;
            if (containedAll.Count > 0 && containedAll.All(o =>
                    CompOrNull<OxydOrganSurgeryComponent>(o) is { Decayed: true }))
            {
                _popup.PopupEntity(Loc.GetString("oxyd-surgery-organ-decayed"), surgeon, surgeon);
                return false;
            }

            var moved = false;
            foreach (var contained in containedAll.ToArray())
            {
                // Patient still has that part (Eris can_add_item unique tag) — skip.
                if (TryComp<OrganComponent>(contained, out var containedOrgan) &&
                    containedOrgan.Category is { } containedCategory &&
                    FindOrgan(body, o => o.Organ.Category == containedCategory).Valid)
                    continue;

                if (_container.Insert(contained, patientContainer, force: true))
                    moved = true;
            }

            if (moved && limbContainer.ContainedEntities.Count == 0)
                QueueDel(item);

            return moved;
        }

        // Eris dismemberment parity: external organs (severed limbs) re-attach
        // through the same body container as internal organs.
        if (!TryComp<OrganComponent>(item, out var organ))
            return false;

        // Eris can_add_item: one organ per unique tag — detach the old one first.
        if (organ.Category is { } category &&
            FindOrgan(body, o => o.Organ.Category == category).Valid)
        {
            _popup.PopupEntity(Loc.GetString("oxyd-surgery-organ-already-present",
                ("organ", Name(item))), surgeon, surgeon);
            return false;
        }

        if (!_container.TryGetContainer(body, BodyComponent.ContainerID, out var container))
            return false;

        return _container.Insert(item, container, force: true);
    }

    // ----- UI -----

    /// <summary>The BUI lives on the patient; each surgeon that opens it gets a session
    /// entry on the patient's OxydSurgerySessionComponent.</summary>
    private void OpenSurgeryUi(EntityUid surgeon, EntityUid patient, EntityUid? tool)
    {
        EnsureComp<UserInterfaceComponent>(patient);
        if (!_ui.HasUi(patient, OxydSurgeryUiKey.Key))
        {
            _ui.SetUi(patient, OxydSurgeryUiKey.Key,
                new InterfaceData("OxydSurgeryBoundUserInterface", SharedInteractionSystem.InteractionRange));
        }

        var sessions = EnsureComp<OxydSurgerySessionComponent>(patient);
        sessions.Sessions[surgeon] = new OxydSurgerySession
        {
            Tool = tool,
            SelfSurgery = surgeon == patient,
        };
        _ui.OpenUi(patient, OxydSurgeryUiKey.Key, surgeon);
        PushState(patient, surgeon);
    }

    private void OnUiClosed(EntityUid patient, OxydSurgerySessionComponent comp, BoundUIClosedEvent args)
    {
        comp.Sessions.Remove(args.Actor);
        if (comp.Sessions.Count == 0)
            RemComp<OxydSurgerySessionComponent>(patient);
    }

    /// <summary>Refresh every window currently open on this patient.</summary>
    private void PushAllStates(EntityUid patient)
    {
        if (!TryComp<OxydSurgerySessionComponent>(patient, out var comp))
            return;

        foreach (var surgeon in comp.Sessions.Keys.ToArray())
            {
            PushState(patient, surgeon);
        }
    }

    /// <summary>Push the actor's state — via a per-actor message since the patient-hosted
    /// BUI shares its UI state between every surgeon with a window open.</summary>
    private void PushState(EntityUid patient, EntityUid surgeon)
    {
        if (!TryComp<OxydSurgerySessionComponent>(patient, out var comp) ||
            !comp.Sessions.TryGetValue(surgeon, out var s))
            return;

        var state = new OxydSurgeryState
        {
            PatientName = Name(patient),
            HeldTools = ToolFlagsOf(s.Tool),
            HeldItemName = s.Tool is { } t ? Name(t) : string.Empty,
            SelfSurgery = s.SelfSurgery,
            StandingOnly = !IsOperable(patient),
        };

        // Eris owner_oxyloss: airloss group drives the Oxygen bar on respiratory organs.
        if (TryComp<DamageableComponent>(patient, out var dmg))
        {
            var spec = _damage.GetPositiveDamage((patient, dmg));
            state.OwnerOxyLoss = OxydDamageTypes.Sum(spec, OxydDamageTypes.Airloss);
        }

        // Blood bars read the body's bloodstream (Eris organ.current_blood / max_blood_storage).
        float bloodLevel = 0f, bloodMax = 0f;
        if (_solutions.TryGetSolution(patient, BloodstreamComponent.DefaultBloodSolutionName,
                out _, out var blood))
        {
            bloodLevel = blood.Volume.Float();
            bloodMax = blood.MaxVolume.Float();
        }

        foreach (var (orgUid, organ, surg) in _wounds.GetOrgans(patient))
        {
            var external = OxydWoundSystem.IsExternal(organ);
            var efficiency = Math.Clamp(
                (OxydOrganSurgeryComponent.OrganMaxDamage - surg.OrganDamage) /
                OxydOrganSurgeryComponent.OrganMaxDamage * 100f, 0f, 100f);
            var steps = AvailableSteps(state.HeldTools, s.Tool, organ, surg,
                s.Tool is { } held && IsTransplantItem(held),
                s.Tool is { } h2 && CanImplant(h2));
            var entry = new OxydSurgeryOrganEntry
            {
                Organ = GetNetEntity(orgUid),
                Name = Name(orgUid),
                Robotic = surg.Robotic,
                External = external,
                Incision = surg.Incision,
                Clamped = surg.Clamped,
                Fractured = surg.Fractured,
                Splinted = surg.Splinted,
                OrganDamage = surg.OrganDamage,
                BruteDamage = surg.BruteDamage,
                BurnDamage = surg.BurnDamage,
                EmbeddedCount = surg.EmbeddedItems.Count,
                Diagnosed = surg.Diagnosed,
                MaxDamage = OxydOrganSurgeryComponent.OrganMaxDamage,
                CavityMax = OxydOrganSurgeryComponent.ImplantCavityMax,
                AvailableSteps = steps,
                RunningStep = surg.RunningStep,
                // Eris organ.is_open(): organic organs need a retracted incision, robotic an open panel.
                Open = surg.Robotic ? surg.Incision != OxydIncisionStage.None
                                    : surg.Incision == OxydIncisionStage.Retracted,
                Efficiency = efficiency,
                // Eris limb cards list their internal process types; organs list themselves.
                Processes = external
                    ? new List<string> { "Bone", "Muscle", "Nerves" }
                    : new List<string> { Name(orgUid) },
                StoredBlood = bloodLevel,
                MaxBlood = bloodMax,
                // Respiratory/brain organs expose the patient's oxygen bar (Eris BP_BRAIN organs).
                ShowOxygen = organ.Category is { } cat &&
                             (cat.Id == OxydOrganCategories.Brain ||
                              cat.Id == OxydOrganCategories.Lungs ||
                              cat.Id == OxydOrganCategories.Heart),
            };

            foreach (var net in surg.EmbeddedItems)
            {
                var item = GetEntity(net);
                if (item.IsValid())
                    entry.ModNames.Add(Name(item));
            }

            if (surg.Diagnosed)
            {
                entry.Wounds = BuildWounds(surg, steps);
                entry.WoundCount = entry.Wounds.Count;
            }

            state.Organs.Add(entry);
        }

        _ui.SetUiState(patient, OxydSurgeryUiKey.Key, state);
        _ui.ServerSendUiMessage(patient, OxydSurgeryUiKey.Key,
            new OxydSurgeryStateMessage { State = state }, surgeon);
    }

    /// <summary>Synthesises the internal view's wound cards from the organ's surgical state
    /// (Eris wounddatums: each wound has a type, severity, and treatment list).</summary>
    private List<OxydSurgeryWoundEntry> BuildWounds(OxydOrganSurgeryComponent surg,
        List<OxydSurgeryStep> steps)
    {
        var list = new List<OxydSurgeryWoundEntry>();
        bool Fixable(OxydSurgeryStep s) => steps.Contains(s);

        if (surg is { Incision: not OxydIncisionStage.None, Clamped: false })
        {
            list.Add(new OxydSurgeryWoundEntry
            {
                Name = Loc.GetString("oxyd-surgery-wound-incision"),
                Severity = 1,
                SeverityMax = 2,
                Treatments = Loc.GetString("oxyd-surgery-treat-incision"),
                FixStep = Fixable(OxydSurgeryStep.FixBleeding) ? OxydSurgeryStep.FixBleeding
                          : Fixable(OxydSurgeryStep.Cauterize) ? OxydSurgeryStep.Cauterize
                          : OxydSurgeryStep.FixBleeding,
            });
        }

        if (surg.WoundBleedRate > 0 && surg.Incision == OxydIncisionStage.None)
        {
            list.Add(new OxydSurgeryWoundEntry
            {
                Name = Loc.GetString("oxyd-surgery-wound-bleeding"),
                Severity = 1,
                SeverityMax = 2,
                Treatments = Loc.GetString("oxyd-surgery-treat-cauterise"),
                FixStep = OxydSurgeryStep.CloseWounds,
            });
        }

        if (surg.Fractured)
        {
            list.Add(new OxydSurgeryWoundEntry
            {
                Name = Loc.GetString("oxyd-surgery-wound-fracture"),
                Severity = surg.Splinted ? 1 : 2,
                SeverityMax = 2,
                Treatments = Loc.GetString("oxyd-surgery-treat-fracture"),
                FixStep = OxydSurgeryStep.MendBone,
            });
        }

        if (surg.EmbeddedItems.Count > 0)
        {
            list.Add(new OxydSurgeryWoundEntry
            {
                Name = Loc.GetString("oxyd-surgery-wound-embedded"),
                Severity = surg.EmbeddedItems.Count,
                SeverityMax = OxydOrganSurgeryComponent.ImplantCavityMax,
                Treatments = Loc.GetString("oxyd-surgery-treat-embedded"),
                FixStep = OxydSurgeryStep.RemoveEmbedded,
            });
        }

        // Damage pools become internal-trauma / burn-tissue / mechanical-damage cards.
        if (surg.BruteDamage > 0)
        {
            list.Add(new OxydSurgeryWoundEntry
            {
                Name = Loc.GetString(surg.Robotic
                    ? "oxyd-surgery-wound-mech"
                    : "oxyd-surgery-wound-brute"),
                Severity = (int) MathF.Ceiling(surg.BruteDamage / 10f),
                SeverityMax = (int) MathF.Ceiling(OxydOrganSurgeryComponent.OrganMaxDamage / 10f),
                Treatments = Loc.GetString(surg.Robotic
                    ? "oxyd-surgery-treat-robo"
                    : "oxyd-surgery-treat-trauma"),
                FixStep = surg.Robotic ? OxydSurgeryStep.RoboFixBrute : OxydSurgeryStep.FixOrgan,
            });
        }

        if (surg.BurnDamage > 0)
        {
            list.Add(new OxydSurgeryWoundEntry
            {
                Name = Loc.GetString(surg.Robotic
                    ? "oxyd-surgery-wound-short"
                    : "oxyd-surgery-wound-burn"),
                Severity = (int) MathF.Ceiling(surg.BurnDamage / 10f),
                SeverityMax = (int) MathF.Ceiling(OxydOrganSurgeryComponent.OrganMaxDamage / 10f),
                Treatments = Loc.GetString(surg.Robotic
                    ? "oxyd-surgery-treat-coil"
                    : "oxyd-surgery-treat-burn"),
                FixStep = surg.Robotic ? OxydSurgeryStep.RoboFixBurn : OxydSurgeryStep.FixOrgan,
            });
        }

        return list;
    }

    private static OxydSurgeryTool ToolForStep(OxydSurgeryStep step) => step switch
    {
        OxydSurgeryStep.CutOpen or OxydSurgeryStep.DetachOrgan or OxydSurgeryStep.DiagnoseWound
            or OxydSurgeryStep.ExtractShrapnel => OxydSurgeryTool.Scalpel,
        OxydSurgeryStep.RetractSkin => OxydSurgeryTool.Retractor,
        OxydSurgeryStep.FixBleeding or OxydSurgeryStep.RemoveEmbedded or OxydSurgeryStep.RemoveItem
            => OxydSurgeryTool.Hemostat,
        OxydSurgeryStep.Cauterize or OxydSurgeryStep.CloseWounds => OxydSurgeryTool.Cautery,
        OxydSurgeryStep.MendBone or OxydSurgeryStep.BreakBone => OxydSurgeryTool.BoneSetter,
        OxydSurgeryStep.FixBone => OxydSurgeryTool.BoneGel,
        OxydSurgeryStep.Amputate => OxydSurgeryTool.Saw,
        OxydSurgeryStep.AttachOrgan => OxydSurgeryTool.Cautery, // Eris: attach_organ needs QUALITY_CAUTERIZING
        OxydSurgeryStep.RoboOpen or OxydSurgeryStep.RoboClose => OxydSurgeryTool.Screwdriver,
        OxydSurgeryStep.RoboFixBrute => OxydSurgeryTool.Welder,
        OxydSurgeryStep.RoboFixBurn => OxydSurgeryTool.CableCoil,
        OxydSurgeryStep.FixOrgan => OxydSurgeryTool.TraumaKit | OxydSurgeryTool.BurnKit,
        _ => OxydSurgeryTool.None,
    };

    private List<OxydSurgeryStep> AvailableSteps(OxydSurgeryTool tools, EntityUid? heldItem,
        OrganComponent organ, OxydOrganSurgeryComponent surg, bool heldOrgan = false,
        bool implantable = false)
    {
        var steps = new List<OxydSurgeryStep>();
        var external = OxydWoundSystem.IsExternal(organ);

        void Add(OxydSurgeryStep step)
        {
            if ((tools & ToolForStep(step)) != 0)
                steps.Add(step);
        }

        if (surg.Robotic)
        {
            if (surg.Incision == OxydIncisionStage.None)
                Add(OxydSurgeryStep.RoboOpen);
            else
            {
                if (surg.OrganDamage > 0)
                {
                    Add(OxydSurgeryStep.RoboFixBrute);
                    Add(OxydSurgeryStep.RoboFixBurn);
                }
                Add(OxydSurgeryStep.RoboClose);
            }
            return steps;
        }

        switch (surg.Incision)
        {
            case OxydIncisionStage.None:
                if (external)
                    Add(OxydSurgeryStep.CutOpen);
                break;
            case OxydIncisionStage.Open:
                if (!surg.Clamped)
                    Add(OxydSurgeryStep.FixBleeding);
                Add(OxydSurgeryStep.RetractSkin);
                Add(OxydSurgeryStep.Cauterize);
                break;
            case OxydIncisionStage.Retracted:
                if (!surg.Clamped)
                    Add(OxydSurgeryStep.FixBleeding);
                Add(OxydSurgeryStep.Cauterize);
                if (surg.Fractured && !surg.Splinted)
                {
                    Add(OxydSurgeryStep.MendBone);
                    Add(OxydSurgeryStep.FixBone);
                }
                if (external && !surg.Fractured)
                    Add(OxydSurgeryStep.BreakBone);
                if (surg.EmbeddedItems.Count > 0)
                {
                    Add(OxydSurgeryStep.RemoveEmbedded);
                    Add(OxydSurgeryStep.RemoveItem);
                }
                // Cavity work accepts an implantable non-surgical item, until full
                // (Eris w_class check).
                if (external && implantable && tools == OxydSurgeryTool.None &&
                    surg.EmbeddedItems.Count < OxydOrganSurgeryComponent.ImplantCavityMax)
                    steps.Add(OxydSurgeryStep.InsertItem);
                if (!external)
                    Add(OxydSurgeryStep.DetachOrgan);
                if (external)
                    Add(OxydSurgeryStep.Amputate);
                // Eris transplant / limb reattachment: the held organ is placed
                // into the open site and a cauterizing tool seals it — offer the
                // combined step at any retracted site when an organ is held.
                if (heldOrgan)
                    steps.Add(OxydSurgeryStep.AttachOrgan);
                break;
        }

        if (surg.OrganDamage > 0 && (tools & (OxydSurgeryTool.TraumaKit | OxydSurgeryTool.BurnKit)) != 0)
            steps.Add(OxydSurgeryStep.FixOrgan);

        // Eris diagnose: any surgeon can probe an undiagnosed organ (no tool needed);
        // on a diagnosed organ it's the scalpel's wound examine.
        if (!surg.Diagnosed)
            steps.Add(OxydSurgeryStep.DiagnoseWound);
        else
            Add(OxydSurgeryStep.DiagnoseWound);
        return steps;
    }

    private string Diagnose(OrganComponent organ, OxydOrganSurgeryComponent surg)
    {
        var parts = new List<string>();
        if (surg.Fractured)
            parts.Add(Loc.GetString("oxyd-medical-diagnose-fracture"));
        if (surg is { Incision: not OxydIncisionStage.None, Clamped: false })
            parts.Add(Loc.GetString("oxyd-medical-diagnose-bleeding"));
        if (surg.EmbeddedItems.Count > 0)
            parts.Add(Loc.GetString("oxyd-medical-diagnose-embedded", ("count", surg.EmbeddedItems.Count)));
        if (surg.OrganDamage > 0)
            parts.Add(Loc.GetString("oxyd-medical-diagnose-damage", ("amount", (int) surg.OrganDamage)));
        if (parts.Count == 0)
            parts.Add(Loc.GetString("oxyd-medical-diagnose-healthy"));
        return string.Join(" ", parts);
    }

    private void Announce(EntityUid surgeon, EntityUid patient, string loc, EntityUid organ)
    {
        var text = Loc.GetString(loc, ("organ", Name(organ)), ("patient", Name(patient)));
        _popup.PopupEntity(text, surgeon, surgeon);
        if (patient != surgeon && _mobs.IsAlive(patient))
            _popup.PopupEntity(text, patient, patient);
    }
}

