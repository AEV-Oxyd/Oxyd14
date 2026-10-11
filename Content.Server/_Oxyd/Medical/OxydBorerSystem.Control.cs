using Content.Server.Administration;
using Content.Server.Chat.Managers;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Server.Chat.Systems;
using Content.Server.Mind;
using Content.Shared._Oxyd.Medical;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Jittering;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Round-2 Eris borer kit: assume_control (mind drives the host while the host's mind
/// is parked in a captive-brain entity inside the borer — Eris host_brain), release /
/// resist control, talk_host / say_host / whisper_host / psychic_whisper / commune,
/// read_mind / write_mind (cellular damage + confusion; no stat/language system to copy),
/// hide (draw-depth toggle), and evolution exp levels (Eris borer_exp/level_up).
/// </summary>
public sealed partial class OxydBorerSystem
{
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private QuickDialogSystem _quickDialog = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;


    // ------------------------------------------------------------------
    // Assume Control (Eris assume_control): 30s channel, then mind swap
    // ------------------------------------------------------------------

    private void TryStartAssumeControl(EntityUid borer, OxydBorerComponent comp, EntityUid host)
    {
        if (comp.Host != host || comp.Controlling || _mobs.IsDead(borer))
            return;

        if (comp.Docile)
        {
            _popup.PopupEntity(Loc.GetString("oxyd-borer-docile"), borer, borer);
            return;
        }

        if (_mobs.IsDead(host))
        {
            _popup.PopupEntity(Loc.GetString("oxyd-borer-control-dead"), borer, borer);
            return;
        }

        if (!_mind.TryGetMind(borer, out _, out _))
        {
            _popup.PopupEntity(Loc.GetString("oxyd-borer-control-no-mind"), borer, borer);
            return;
        }

        _popup.PopupEntity(Loc.GetString("oxyd-borer-control-start"), borer, borer);

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, borer, comp.AssumeControlDelay,
            new OxydBorerAssumeControlDoAfterEvent(), borer, target: host)
        {
            BreakOnMove = false,
            BreakOnDamage = false,
            NeedHand = false,
        });
    }

    private void OnAssumeControlDone(EntityUid uid, OxydBorerComponent comp, OxydBorerAssumeControlDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;
        args.Handled = true;

        if (comp.Host is not { } host || comp.Controlling || _mobs.IsDead(uid) || _mobs.IsDead(host))
            return;

        if (!_mind.TryGetMind(uid, out var borerMindId, out _))
            return;

        // Park the host's mind in a captive-brain entity inside the borer, then move the
        // borer's mind into the host body (Eris: host_brain.ckey = host.ckey; host.ckey = src.ckey).
        // Order matters: evict the host mind FIRST or TransferTo ghosts it out.
        var captive = Spawn(comp.CaptiveBrainPrototype);
        comp.CaptiveContainer = _container.EnsureContainer<Container>(uid, OxydBorerComponent.CaptiveContainerId);
        _container.Insert(captive, comp.CaptiveContainer);

        var captiveComp = EnsureComp<OxydBorerCaptiveComponent>(captive);
        captiveComp.Host = host;
        captiveComp.Borer = uid;
        Dirty(captive, captiveComp);

        if (_mind.TryGetMind(host, out var hostMindId, out _))
            _mind.TransferTo(hostMindId, captive);

        _mind.TransferTo(borerMindId, host);

        comp.Controlling = true;
        comp.HostBrain = captive;
        Dirty(uid, comp);

        _adminLogger.Add(LogType.Mind, LogImpact.High,
            $"{ToPrettyString(uid):user} assumed control of {ToPrettyString(host):target} (mind swap)");
        _popup.PopupEntity(Loc.GetString("oxyd-borer-control-done"), uid, uid);
        _popup.PopupEntity(Loc.GetString("oxyd-borer-control-done-host"), host, host, PopupType.LargeCaution);
    }

    /// <summary>Eris detach(): move the borer's mind back into the borer entity and the
    /// captive mind back into the host body, then delete the captive brain.</summary>
    private void DetachControl(EntityUid borer, OxydBorerComponent comp, EntityUid host)
    {
        if (!comp.Controlling)
            return;

        comp.Controlling = false;
        var captive = comp.HostBrain;
        comp.HostBrain = null;
        Dirty(borer, comp);
        _adminLogger.Add(LogType.Mind, LogImpact.Medium,
            $"{ToPrettyString(borer):user} released control of {ToPrettyString(host):target}");

        // The mind currently owning the host body is the borer's — move it home first.
        if (_mind.TryGetMind(host, out var bodyMindId, out _))
            _mind.TransferTo(bodyMindId, borer);

        if (captive is { } c && !Deleted(c))
        {
            if (TryComp<OxydBorerCaptiveComponent>(c, out var cc))
            {
                cc.Host = null;
                cc.Borer = null;
                Dirty(c, cc);
            }

            if (_mind.TryGetMind(c, out var captiveMindId, out _))
            {
                if (!TerminatingOrDeleted(host))
                    _mind.TransferTo(captiveMindId, host);
                else
                    _mind.TransferTo(captiveMindId, null); // host gone — ghost out like death
            }

            QueueDel(c);
        }
    }

    /// <summary>Eris release_control: the borer voluntarily releases the host body.</summary>
    private void TryReleaseControl(EntityUid borer, OxydBorerComponent comp, EntityUid host)
    {
        if (!comp.Controlling)
            return;

        _popup.PopupEntity(Loc.GetString("oxyd-borer-release-control"), host, host);
        if (comp.HostBrain is { } captive && !Deleted(captive))
            _popup.PopupEntity(Loc.GetString("oxyd-borer-release-control-captive"), captive, captive, PopupType.LargeCaution);

        DetachControl(borer, comp, host);
    }

    /// <summary>Eris captive_brain process_resist(): the captive forces the borer out
    /// after ~25-30s.</summary>
    private void TryStartResist(EntityUid captive, OxydBorerCaptiveComponent comp)
    {
        if (comp.Borer is not { } borer || comp.Host is not { } host)
            return;

        var (min, max) = (25.0, 30.0);
        if (TryComp<OxydBorerComponent>(borer, out var bc))
        {
            min = bc.ResistMinDelay.TotalSeconds;
            max = bc.ResistMaxDelay.TotalSeconds;
        }
        var delay = TimeSpan.FromSeconds(_random.NextFloat((float) min, (float) max));

        _popup.PopupEntity(Loc.GetString("oxyd-borer-resist-start"), captive, captive);
        _popup.PopupEntity(Loc.GetString("oxyd-borer-resist-start-host"), host, host, PopupType.MediumCaution);

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, captive, delay,
            new OxydBorerResistDoAfterEvent(), captive, target: captive)
        {
            BreakOnMove = false,
            BreakOnDamage = false,
            NeedHand = false,
        });
    }

    private void OnResistDone(EntityUid uid, OxydBorerCaptiveComponent comp, OxydBorerResistDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;
        args.Handled = true;

        if (comp.Borer is not { } borer || comp.Host is not { } host ||
            !TryComp<OxydBorerComponent>(borer, out var borerComp) || !borerComp.Controlling)
            return;

        _popup.PopupEntity(Loc.GetString("oxyd-borer-resist-done"), uid, uid, PopupType.LargeCaution);
        _popup.PopupEntity(Loc.GetString("oxyd-borer-resist-done-borer"), host, host, PopupType.LargeCaution);
        DetachControl(borer, borerComp, host);
    }

    /// <summary>If the captive entity dies while holding a mind, the mind goes back to the
    /// host body (or ghosts if the host is gone too).</summary>
    private void OnCaptiveTerminating(EntityUid uid, OxydBorerCaptiveComponent comp, EntityTerminatingEvent args)
    {
        if (!_mind.TryGetMind(uid, out var captiveMindId, out _))
            return;

        if (comp.Host is { } host && !TerminatingOrDeleted(host))
            _mind.TransferTo(captiveMindId, host);
        else
            _mind.TransferTo(captiveMindId, null);
    }

    // ------------------------------------------------------------------
    // Comm verbs (Eris say/whisper_host, talk_host, psychic_whisper, commune,
    // captive_brain say) — text via quick dialog, delivery via chat channel
    // ------------------------------------------------------------------

    /// <summary>Open a text box on the entity's controlling session.</summary>
    private void AskText(EntityUid who, string title, string prompt, Action<string> onOk)
    {
        if (!_players.TryGetSessionByEntity(who, out var session))
        {
            _popup.PopupEntity(Loc.GetString("oxyd-borer-no-mind"), who, who);
            return;
        }

        _quickDialog.OpenDialog<string>(session, title, prompt,
            text =>
            {
                if (string.IsNullOrWhiteSpace(text))
                    return;
                onOk(text.Trim());
            });
    }

    /// <summary>Eris to_chat(): a private line in the recipient's chat panel.</summary>
    private void TellMind(EntityUid from, EntityUid to, string message)
    {
        if (_players.TryGetSessionByEntity(to, out var session))
            _chatManager.ChatMessageToOne(ChatChannel.Whisper, message, message, from, false, session.Channel);
    }

    /// <summary>Eris borer say() while nested: words drop into the host's mind.</summary>
    private void TrySpeakToHost(EntityUid borer, OxydBorerComponent comp, EntityUid host)
    {
        if (comp.Host != host || comp.Docile || _mobs.IsDead(borer))
            return;

        AskText(borer, Loc.GetString("oxyd-borer-dialog-speak-title"), Loc.GetString("oxyd-borer-dialog-speak-prompt"),
            text =>
            {
                // The dialog can be answered long after it opened — re-verify.
                if (comp.Host != host || comp.Docile || _mobs.IsDead(borer) || TerminatingOrDeleted(host))
                    return;
                var escaped = FormattedMessage.EscapeText(text);
                TellMind(borer, borer, Loc.GetString("oxyd-borer-speak-self", ("text", escaped), ("host", host)));
                TellMind(borer, host, Loc.GetString("oxyd-borer-speak-host", ("text", escaped)));
                _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"{ToPrettyString(borer):user} borer-said to {ToPrettyString(host):target}: {escaped}");
            });
    }

    /// <summary>Eris say_host/whisper_host: the borer forces speech out of the host body.</summary>
    private void TryForceHostSpeech(EntityUid borer, OxydBorerComponent comp, EntityUid host, InGameICChatType type)
    {
        if (comp.Host != host || comp.Docile || _mobs.IsDead(borer) || _mobs.IsDead(host))
            return;

        var title = type == InGameICChatType.Whisper
            ? Loc.GetString("oxyd-borer-dialog-whisper-host-title")
            : Loc.GetString("oxyd-borer-dialog-say-host-title");

        AskText(borer, title, Loc.GetString("oxyd-borer-dialog-say-host-prompt"),
            text =>
            {
                if (comp.Host != host || comp.Docile || _mobs.IsDead(borer) || _mobs.IsDead(host))
                    return;
                _chat.TrySendInGameICMessage(host, text, type, hideChat: false, ignoreActionBlocker: true);
                _adminLogger.Add(LogType.Chat, LogImpact.Medium,
                    $"{ToPrettyString(borer):user} forced {ToPrettyString(host):target} to {type}: {text}");
            });
    }

    /// <summary>Eris psychic_whisper: silent message to a nearby victim's mind.</summary>
    private void TryPsychicWhisper(EntityUid user, EntityUid victim)
    {
        AskText(user, Loc.GetString("oxyd-borer-dialog-psychic-title"), Loc.GetString("oxyd-borer-dialog-psychic-prompt"),
            text =>
            {
                if (TerminatingOrDeleted(victim) || TerminatingOrDeleted(user))
                    return;
                var escaped = FormattedMessage.EscapeText(text);
                TellMind(user, victim, Loc.GetString("oxyd-borer-psychic-target", ("text", escaped)));
                TellMind(user, user, Loc.GetString("oxyd-borer-psychic-self", ("text", escaped), ("target", victim)));
                _adminLogger.Add(LogType.Chat, LogImpact.Medium,
                    $"{ToPrettyString(user):user} psychic-whispered {ToPrettyString(victim):target}: {escaped}");
            });
    }

    /// <summary>Eris commune: alien thought projection + nosebleed flavor.</summary>
    private void TryCommune(EntityUid borer, EntityUid user, EntityUid victim)
    {
        AskText(user, Loc.GetString("oxyd-borer-dialog-commune-title"), Loc.GetString("oxyd-borer-dialog-commune-prompt"),
            text =>
            {
                if (TerminatingOrDeleted(borer) || TerminatingOrDeleted(user) ||
                    TerminatingOrDeleted(victim) || _mobs.IsDead(borer) ||
                    !TryComp<OxydBorerComponent>(borer, out var comp) ||
                    !_transform.InRange(user, victim, SharedInteractionSystem.InteractionRange))
                    return;
                var escaped = FormattedMessage.EscapeText(text);
                TellMind(borer, victim, Loc.GetString("oxyd-borer-commune-target", ("text", escaped)));
                TellMind(borer, user, Loc.GetString("oxyd-borer-commune-self", ("text", escaped), ("target", victim)));
                // Eris: H.drip_blood(1) — a trickle of blood from the nose.
                _damage.TryChangeDamage(victim,
                    new DamageSpecifier(_proto.Index(comp.CellularDamage), 1f),
                    ignoreResistances: true);
                TellMind(borer, victim, Loc.GetString("oxyd-borer-commune-nosebleed"));
                _adminLogger.Add(LogType.Chat, LogImpact.Medium,
                    $"{ToPrettyString(borer):user} communed with {ToPrettyString(victim):target}: {escaped}");
            });
    }

    /// <summary>Eris talk_host (controlling borer → captive mind).</summary>
    private void TryTalkToCaptive(EntityUid borer, OxydBorerComponent comp, EntityUid host)
    {
        if (!comp.Controlling)
            return;

        if (comp.HostBrain is not { } captive || Deleted(captive))
        {
            _popup.PopupEntity(Loc.GetString("oxyd-borer-no-captive"), host, host);
            return;
        }

        AskText(host, Loc.GetString("oxyd-borer-dialog-captive-title"), Loc.GetString("oxyd-borer-dialog-captive-prompt"),
            text =>
            {
                // Control may have been released while the dialog was open.
                if (!comp.Controlling || comp.HostBrain is not { } cap || Deleted(cap))
                    return;
                var escaped = FormattedMessage.EscapeText(text);
                TellMind(host, host, Loc.GetString("oxyd-borer-captive-self", ("text", escaped)));
                TellMind(host, cap, Loc.GetString("oxyd-borer-captive-echo", ("text", escaped)));
                _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"{ToPrettyString(host):user} borer-talked to captive {ToPrettyString(cap):target}: {escaped}");
            });
    }

    /// <summary>Eris captive_brain say(): the captive whispers into the host's mind.</summary>
    private void TryCaptiveWhisper(EntityUid captive, OxydBorerCaptiveComponent comp)
    {
        if (comp.Host is not { } host || Deleted(host))
            return;

        AskText(captive, Loc.GetString("oxyd-borer-dialog-whisper-title"), Loc.GetString("oxyd-borer-dialog-whisper-prompt"),
            text =>
            {
                // The host may be gone (released/deleted) by the time the dialog is answered.
                if (comp.Host is not { } cur || TerminatingOrDeleted(cur))
                    return;
                var escaped = FormattedMessage.EscapeText(text);
                TellMind(captive, captive, Loc.GetString("oxyd-borer-whisper-self", ("text", escaped)));
                TellMind(captive, cur, Loc.GetString("oxyd-borer-whisper-host", ("text", escaped)));
                _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"{ToPrettyString(captive):user} captive-whispered {ToPrettyString(cur):target}: {escaped}");
            });
    }

    // ------------------------------------------------------------------
    // Read/Write Mind (Eris read_mind/write_mind — stat/language copying has no
    // Oxyd equivalent; the mental strain + exp gain carry over)
    // ------------------------------------------------------------------

    private void TryReadMind(EntityUid borer, OxydBorerComponent comp, EntityUid host)
    {
        if (comp.Host != host || comp.Docile || _mobs.IsDead(borer))
            return;

        _damage.TryChangeDamage(host,
            new DamageSpecifier(_proto.Index(comp.CellularDamage), comp.ReadMindDamage),
            ignoreResistances: true);
        _jitter.DoJitter(host, comp.MindEffectDuration, true);

        _popup.PopupEntity(Loc.GetString("oxyd-borer-read-self"), borer, borer);
        _popup.PopupEntity(Loc.GetString("oxyd-borer-read-host"), host, host, PopupType.MediumCaution);
        AddExp(borer, comp, comp.ReadMindExp);
    }

    private void TryWriteMind(EntityUid borer, OxydBorerComponent comp, EntityUid host)
    {
        if (comp.Host != host || comp.Docile || _mobs.IsDead(borer))
            return;

        _damage.TryChangeDamage(host,
            new DamageSpecifier(_proto.Index(comp.CellularDamage), comp.WriteMindDamage),
            ignoreResistances: true);
        _jitter.DoJitter(host, comp.MindEffectDuration, true);

        _popup.PopupEntity(Loc.GetString("oxyd-borer-write-self"), borer, borer);
        _popup.PopupEntity(Loc.GetString("oxyd-borer-write-host"), host, host, PopupType.MediumCaution);
        AddExp(borer, comp, comp.WriteMindExp);
    }

    // ------------------------------------------------------------------
    // Hide (Eris hide(): renders beneath floor objects)
    // ------------------------------------------------------------------

    private void TryToggleHide(EntityUid borer, OxydBorerComponent comp)
    {
        if (comp.Host != null || _mobs.IsDead(borer))
            return;

        comp.Hidden = !comp.Hidden;
        Dirty(borer, comp);

        var appearance = EnsureComp<AppearanceComponent>(borer);
        _appearance.SetData(borer, OxydBorerVisuals.Hidden, comp.Hidden, appearance);

        _popup.PopupEntity(
            Loc.GetString(comp.Hidden ? "oxyd-borer-hide-on" : "oxyd-borer-hide-off"),
            borer, borer);
    }

    // ------------------------------------------------------------------
    // Evolution (Eris borer_add_exp / update_borer_level / level_up)
    // ------------------------------------------------------------------

    private void AddExp(EntityUid borer, OxydBorerComponent comp, int amount)
    {
        comp.BorerExp += amount;
        var level = comp.BorerLevel;
        while (level < comp.Levels.Count && comp.BorerExp >= comp.Levels[level].Threshold)
            level++;

        if (level != comp.BorerLevel)
        {
            for (var l = comp.BorerLevel; l < level; l++)
            {
                var tier = comp.Levels[l];
                comp.ProducedReagents.AddRange(tier.Reagents);
                // Eris level_up: max_chemicals += level*10; max_inhost = max*5.
                comp.MaxChemicals += tier.ChemBonus;
            }

            comp.BorerLevel = level;
            comp.MaxChemicalsInHost = comp.MaxChemicals * 5f;

            _popup.PopupEntity(Loc.GetString("oxyd-borer-level-up", ("level", level)), borer, borer);
        }

        Dirty(borer, comp);
    }
}
