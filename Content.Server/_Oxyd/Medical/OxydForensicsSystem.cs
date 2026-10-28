using System.Linq;
using System.Text;
using Content.Server.Chat.Managers;
using Content.Server.DoAfter;
using Content.Shared._Oxyd.Medical;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Chat;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Ports the Eris forensic handhelds: the autopsy scanner (objects/items/weapons/autopsy.dm)
/// that prints a paper report on a cadaver's wounds, and the mass spectrometer
/// (devices/scanners/mass_scpectrometer.dm) that reads out a container's or a patient's
/// reagents as a private message to the user.
/// </summary>
public sealed partial class OxydForensicsSystem : EntitySystem
{
    [Dependency] private DoAfterSystem _doAfter = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private OxydWoundSystem _wounds = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;

    private static readonly EntProtoId PaperProto = "Paper";

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydAutopsyScannerComponent, AfterInteractEvent>(OnAutopsyInteract);
        SubscribeLocalEvent<OxydAutopsyScannerComponent, OxydAutopsyDoAfterEvent>(OnAutopsyDoAfter);
        SubscribeLocalEvent<OxydMassSpectrometerComponent, AfterInteractEvent>(OnSpectrometerInteract);
    }

    // ---------------------------------------------------------------------------
    // Autopsy scanner (Eris autopsy.dm). Eris accumulates per-weapon wound data over
    // repeated limb scans; the SS14 port snapshots the whole body in one do_after.
    // ---------------------------------------------------------------------------

    private void OnAutopsyInteract(Entity<OxydAutopsyScannerComponent> uid, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        args.Handled = TryAutopsyScan(args.User, uid, target);
    }

    /// <summary>Item-side autopsy entry point (Eris autopsy.dm). Returns whether the
    /// click was handled.</summary>
    public bool TryAutopsyScan(EntityUid user, Entity<OxydAutopsyScannerComponent> uid, EntityUid target)
    {
        if (!HasComp<MobStateComponent>(target) || !HasComp<DamageableComponent>(target))
            return false;

        if (!_mobs.IsDead(target))
        {
            _popup.PopupEntity(Loc.GetString("oxyd-autopsy-not-dead"), uid, user);
            return true;
        }

        _popup.PopupEntity(Loc.GetString("oxyd-autopsy-scan-start"), uid, user);
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, uid.Comp.ScanDelay,
                new OxydAutopsyDoAfterEvent(), uid, target: target, used: uid)
        {
            NeedHand = true,
            BreakOnMove = true,
        });
        return true;
    }

    private void OnAutopsyDoAfter(Entity<OxydAutopsyScannerComponent> uid, ref OxydAutopsyDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target || TerminatingOrDeleted(target))
            return;

        args.Handled = true;
        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(args.User):user} performed an autopsy on {ToPrettyString(target):target}");
        PrintReport(uid, args.User, target);
    }

    /// <summary>Eris print_data(): build the autopsy text and spit out a paper.</summary>
    private void PrintReport(EntityUid scanner, EntityUid user, EntityUid target)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.GetString("oxyd-autopsy-report-title"));
        sb.AppendLine(Loc.GetString("oxyd-autopsy-report-subject", ("name", Name(target))));

        // Time-of-death estimate from the patient's mind record (player corpses only).
        if (_mind.TryGetMind(target, out _, out var mind) && mind.TimeOfDeath is { } tod)
        {
            var deadFor = _timing.RealTime - tod;
            sb.AppendLine(Loc.GetString("oxyd-autopsy-report-tod",
                ("time", $"{(int) Math.Max(0, deadFor.TotalMinutes)}")));
        }

        // Damage breakdown by type (DamageableComponent) — the Eris weapon rows reduce
        // to damage types in SS14.
        if (TryComp<DamageableComponent>(target, out var dmg))
        {
            sb.AppendLine(Loc.GetString("oxyd-autopsy-report-damage"));
            var any = false;
            foreach (var (type, amount) in _damage.GetPositiveDamage((target, dmg)).DamageDict)
            {
                if (amount <= 0)
                    continue;
                var tname = _prototypes.TryIndex<DamageTypePrototype>(type, out var dt)
                    ? dt.LocalizedName
                    : type.Id;
                sb.AppendLine(Loc.GetString("oxyd-autopsy-report-damage-line",
                    ("type", tname), ("amount", (int) amount.Float())));
                any = true;
            }
            if (!any)
                sb.AppendLine(Loc.GetString("oxyd-autopsy-report-damage-none"));
        }

        // Per-limb wound section from the ported organ wound model.
        var organs = _wounds.GetOrgans(target);
        var external = organs.Where(o => OxydWoundSystem.IsExternal(o.Organ)).ToList();
        if (external.Count > 0)
        {
            sb.AppendLine(Loc.GetString("oxyd-autopsy-report-wounds"));
            foreach (var (orgUid, _, surg) in external)
            {
                var flags = new List<string>();
                if (surg.OrganDamage > 0)
                    flags.Add(Loc.GetString("oxyd-autopsy-flag-damage",
                        ("amount", (int) surg.OrganDamage)));
                if (surg.Fractured)
                    flags.Add(Loc.GetString("oxyd-autopsy-flag-fracture"));
                if (surg.Incision != OxydIncisionStage.None)
                    flags.Add(Loc.GetString("oxyd-autopsy-flag-incision"));
                if (surg.WoundBleedRate > 0)
                    flags.Add(Loc.GetString("oxyd-autopsy-flag-bleeding"));
                if (surg.EmbeddedItems.Count > 0)
                    flags.Add(Loc.GetString("oxyd-autopsy-flag-embedded",
                        ("count", surg.EmbeddedItems.Count)));

                sb.AppendLine(flags.Count > 0
                    ? Loc.GetString("oxyd-autopsy-report-wound-line",
                        ("organ", Name(orgUid)), ("notes", string.Join(", ", flags)))
                    : Loc.GetString("oxyd-autopsy-report-wound-intact", ("organ", Name(orgUid))));
            }
        }

        // Trace chemicals: reagents in the bloodstream (Eris chemtraces).
        if (_solutions.TryGetSolution(target, BloodstreamComponent.DefaultBloodSolutionName,
                out _, out var blood) && blood.Contents.Count > 0)
        {
            sb.AppendLine(Loc.GetString("oxyd-autopsy-report-chems"));
            foreach (var (reagent, qty) in blood.Contents)
            {
                var rname = _prototypes.TryIndex<ReagentPrototype>(reagent.Prototype, out var p)
                    ? p.LocalizedName
                    : (string) reagent.Prototype;
                sb.AppendLine(Loc.GetString("oxyd-autopsy-report-chem-line",
                    ("reagent", rname), ("amount", (int) Math.Ceiling(qty.Float()))));
            }
        }

        var paper = Spawn(PaperProto, Transform(user).Coordinates);
        _metaData.SetEntityName(paper,
            Loc.GetString("oxyd-autopsy-paper-name", ("name", Name(target))));
        _paper.SetContent(paper, sb.ToString());
        _hands.PickupOrDrop(user, paper);
        _popup.PopupEntity(Loc.GetString("oxyd-autopsy-printed"), scanner, user);
    }

    // ---------------------------------------------------------------------------
    // Mass spectrometer (Eris mass_scpectrometer.dm + scanners/reagents.dm): private
    // readout of every solution carried by the target (beakers, mob bloodstream, ...).
    // ---------------------------------------------------------------------------

    private void OnSpectrometerInteract(Entity<OxydMassSpectrometerComponent> uid, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        var lines = new List<string>();
        foreach (var (_, solEnt) in _solutions.EnumerateSolutions(target))
        {
            foreach (var (reagent, qty) in solEnt.Comp.Solution.Contents)
            {
                var rname = _prototypes.TryIndex<ReagentPrototype>(reagent.Prototype, out var p)
                    ? p.LocalizedName
                    : (string) reagent.Prototype;
                lines.Add(uid.Comp.Detailed
                    ? Loc.GetString("oxyd-massspec-line-units",
                        ("reagent", rname), ("units", Math.Round(qty.Float(), 2)))
                    : rname);
            }
        }

        args.Handled = true;
        var msg = lines.Count == 0
            ? Loc.GetString("oxyd-massspec-none", ("target", Name(target)))
            : Loc.GetString("oxyd-massspec-header", ("target", Name(target)))
              + "\n" + string.Join("\n", lines);

        // Eris user.show_message(scan_data): readout visible to the scanning user only.
        if (TryComp<ActorComponent>(args.User, out var actor))
            _chat.ChatMessageToOne(ChatChannel.Server, msg, msg, EntityUid.Invalid, false,
                actor.PlayerSession.Channel);
        else
            _popup.PopupEntity(msg, uid, args.User);
    }
}
