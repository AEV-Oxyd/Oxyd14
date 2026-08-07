using System.Linq;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.IdentityManagement;
using Content.Shared.Radio;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server.Chat.Systems;

public sealed partial class ChatSystem
{
    private void SendEntitySpeak(
        MessageData msg,
        string? nameOverride,
        bool hideLog = false,
        bool ignoreActionBlocker = false
        )
    {
        if (!_actionBlocker.CanSpeak(msg.speaker) && !ignoreActionBlocker)
            return;

        var message = TransformSpeech(msg.speaker, msg.raw);

        if (message.Length == 0)
            return;

        var speech = GetSpeechVerb(msg.speaker, message);

        // get the entity's apparent name (if no override provided).
        string name;
        if (nameOverride != null)
        {
            name = nameOverride;
        }
        else
        {
            var nameEv = new TransformSpeakerNameEvent(msg.speaker, Name(msg.speaker));
            RaiseLocalEvent(msg.speaker, nameEv);
            name = nameEv.VoiceName;
            // Check for a speech verb override
            if (nameEv.SpeechVerb != null && ProtoMan.Resolve(nameEv.SpeechVerb, out var proto))
                speech = proto;
        }

        name = FormattedMessage.EscapeText(name);
        var k = MessageData.hashFromList(msg.containedLanguages);
        var finalMessages = new Dictionary<int, string[]>();
        finalMessages[k] = new string[2];
        finalMessages[k][0] = message;
        finalMessages[k][1] = Loc.GetString(speech.Bold ? "chat-manager-entity-say-bold-wrap-message" : "chat-manager-entity-say-wrap-message",
            ("entityName", name),
            ("verb", Loc.GetString(_random.Pick(speech.SpeechVerbStrings))),
            ("fontType", speech.FontId),
            ("fontSize", speech.FontSize),
            ("message", FormattedMessage.EscapeText(message)));
        
        foreach (var (session, data) in GetRecipients(msg.speaker, VoiceRange))
        {
            var usingKey = k;
            var entRange = MessageRangeCheck(session, data, msg.range);
            if (entRange == MessageRangeCheckResult.Disallowed)
                continue;
            if (session.AttachedEntity is not { Valid: true } playerEntity)
                continue;
            if (langquery.TryComp(playerEntity, out var lang))
            {
                usingKey = msg.GetLanguageHash(lang.understanding);
                if (!finalMessages.ContainsKey(usingKey))
                {
                    finalMessages[usingKey] = new string[2];
                    finalMessages[usingKey][0] = msg.GetMessage(lang.understanding, out _);
                    finalMessages[usingKey][1] = Loc.GetString(speech.Bold ? "chat-manager-entity-say-bold-wrap-message" : "chat-manager-entity-say-wrap-message",
                        ("entityName", name),
                        ("verb", Loc.GetString(_random.Pick(speech.SpeechVerbStrings))),
                        ("fontType", speech.FontId),
                        ("fontSize", speech.FontSize),
                        ("message", FormattedMessage.EscapeText(finalMessages[usingKey][0])));
                }
            }
            var entHideChat = entRange == MessageRangeCheckResult.HideChat;
            _chatManager.ChatMessageToOne(ChatChannel.Local, finalMessages[usingKey][0], finalMessages[usingKey][1], msg.speaker, entHideChat, session.Channel);
        }

        _replay.RecordServerMessage(new ChatMessage(ChatChannel.Local, message, finalMessages[k][1], GetNetEntity(msg.speaker), null, MessageRangeHideChatForReplay(msg.range)));
        

        var ev = new EntitySpokeEvent(msg.speaker, message, null, null);
        RaiseLocalEvent(msg.speaker, ev, true);

        // To avoid logging any messages sent by entities that are not players, like vendors, cloning, etc.
        // Also doesn't log if hideLog is true.
        if (!HasComp<ActorComponent>(msg.speaker) || hideLog)
            return;

        if (msg.raw == message)
        {
            if (name != Name(msg.speaker))
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Say from {msg.speaker} as {name}: {msg.raw}.");
            else
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Say from {msg.speaker}: {msg.raw}.");
        }
        else
        {
            if (name != Name(msg.speaker))
                _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"Say from {msg.speaker} as {name}, original: {msg.raw}, transformed: {message}.");
            else
                _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"Say from {msg.speaker}, original: {msg.raw}, transformed: {message}.");
        }
    }

    private void SendEntityWhisper(
        MessageData msg,
        RadioChannelPrototype? channel,
        string? nameOverride,
        bool hideLog = false,
        bool ignoreActionBlocker = false
        )
    {
        if (!_actionBlocker.CanSpeak(msg.speaker) && !ignoreActionBlocker)
            return;

        var message = TransformSpeech(msg.speaker, FormattedMessage.RemoveMarkupOrThrow(msg.raw));
        if (message.Length == 0)
            return;

        var obfuscatedMessage = ObfuscateMessageReadability(message, 0.2f);

        // get the entity's name by visual identity (if no override provided).
        string nameIdentity = FormattedMessage.EscapeText(nameOverride ?? Identity.Name(msg.speaker, EntityManager));
        // get the entity's name by voice (if no override provided).
        string name;
        if (nameOverride != null)
        {
            name = nameOverride;
        }
        else
        {
            var nameEv = new TransformSpeakerNameEvent(msg.speaker, Name(msg.speaker));
            RaiseLocalEvent(msg.speaker, nameEv);
            name = nameEv.VoiceName;
        }
        name = FormattedMessage.EscapeText(name);
        // generate and hold all 3 possible message for each language-combination we encounter , SPCR 2026
        var finalMessages = new Dictionary<int, string[]> ();
        // raw text key.
        var k = MessageData.hashFromList(msg.containedLanguages);
        finalMessages[k] = new string[3];
        finalMessages[k][0] = Loc.GetString("chat-manager-entity-whisper-wrap-message", ("entityName", name), ("message", FormattedMessage.EscapeText(message)));
        finalMessages[k][1] = Loc.GetString("chat-manager-entity-whisper-wrap-message", ("entityName", nameIdentity), ("message", FormattedMessage.EscapeText(obfuscatedMessage)));
        finalMessages[k][2] = Loc.GetString("chat-manager-entity-whisper-unknown-wrap-message", ("message", FormattedMessage.EscapeText(obfuscatedMessage)));

        foreach (var (session, data) in GetRecipients(msg.speaker, WhisperMuffledRange))
        {
            var usingKey = k;
            EntityUid listener;

            if (session.AttachedEntity is not { Valid: true } playerEntity)
                continue;
            if (MessageRangeCheck(session, data, msg.range) != MessageRangeCheckResult.Full)
                continue; // Won't get logged to chat, and ghosts are too far away to see the pop-up, so we just won't send it to them.

            listener = session.AttachedEntity.Value;
            if (langquery.TryComp(listener, out var lang))
            {
                var hash = msg.GetLanguageHash(lang.understanding);
                if (!finalMessages.ContainsKey(hash))
                {
                    var rawProcessed = TransformSpeech(msg.speaker, FormattedMessage.RemoveMarkupOrThrow(msg.GetMessage(lang.understanding, out _)));
                    var rawObfuscate = ObfuscateMessageReadability(rawProcessed, 0.2f);
                    finalMessages[hash] = new string[3];    
                    finalMessages[hash][0] = Loc.GetString("chat-manager-entity-whisper-wrap-message", ("entityName", name), ("message", FormattedMessage.EscapeText(rawProcessed)));
                    finalMessages[hash][1] = Loc.GetString("chat-manager-entity-whisper-wrap-message", ("entityName", nameIdentity), ("message", FormattedMessage.EscapeText(rawObfuscate)));
                    finalMessages[hash][2] = Loc.GetString("chat-manager-entity-whisper-unknown-wrap-message", ("message", FormattedMessage.EscapeText(rawObfuscate)));
                }
                usingKey = hash;
            }
            if (data.Range <= WhisperClearRange || data.Observer)
                _chatManager.ChatMessageToOne(ChatChannel.Whisper, message, finalMessages[usingKey][0], msg.speaker, false, session.Channel);
            //If listener is too far, they only hear fragments of the message
            else if (_examineSystem.InRangeUnOccluded(msg.speaker, listener, WhisperMuffledRange))
                _chatManager.ChatMessageToOne(ChatChannel.Whisper, obfuscatedMessage, finalMessages[usingKey][1], msg.speaker, false, session.Channel);
            //If listener is too far and has no line of sight, they can't identify the whisperer's identity
            else
                _chatManager.ChatMessageToOne(ChatChannel.Whisper, obfuscatedMessage, finalMessages[usingKey][2], msg.speaker, false, session.Channel);

        }

        _replay.RecordServerMessage(new ChatMessage(ChatChannel.Whisper, message, finalMessages[k][0], GetNetEntity(msg.speaker), null, MessageRangeHideChatForReplay(msg.range)));

        var ev = new EntitySpokeEvent(msg.speaker, message, channel, obfuscatedMessage);
        RaiseLocalEvent(msg.speaker, ev, true);
        if (!hideLog)
            if (msg.raw == message)
            {
                if (name != Name(msg.speaker))
                    _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Whisper from {msg.speaker} as {name}: {msg.raw}.");
                else
                    _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Whisper from {msg.speaker}: {msg.raw}.");
            }
            else
            {
                if (name != Name(msg.speaker))
                    _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"Whisper from {msg.speaker} as {name}, original: {msg.raw}, transformed: {message}.");
                else
                    _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"Whisper from {msg.speaker}, original: {msg.raw}, transformed: {message}.");
            }
    }

    protected override void SendEntityEmote(
        EntityUid source,
        string action,
        ChatTransmitRange range,
        string? nameOverride,
        bool hideLog = false,
        bool checkEmote = true,
        bool ignoreActionBlocker = false,
        NetUserId? author = null
        )
    {
        if (!_actionBlocker.CanEmote(source) && !ignoreActionBlocker)
            return;

        // get the entity's apparent name (if no override provided).
        var ent = Identity.Entity(source, EntityManager);
        string name = FormattedMessage.EscapeText(nameOverride ?? Name(ent));

        // Emotes use Identity.Name, since it doesn't actually involve your voice at all.
        var wrappedMessage = Loc.GetString("chat-manager-entity-me-wrap-message",
            ("entityName", name),
            ("entity", ent),
            ("message", FormattedMessage.RemoveMarkupOrThrow(action)));

        if (checkEmote &&
            !TryEmoteChatInput(source, action))
            return;

        SendInVoiceRange(ChatChannel.Emotes, action, wrappedMessage, source, range, author);
        if (!hideLog)
            if (name != Name(source))
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Emote from {source} as {name}: {action}");
            else
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Emote from {source}: {action}");
    }

    // ReSharper disable once InconsistentNaming
    private void SendLOOC(EntityUid source, ICommonSession player, string message, bool hideChat)
    {
        var name = FormattedMessage.EscapeText(Identity.Name(source, EntityManager));

        if (_adminManager.IsAdmin(player))
        {
            if (!_adminLoocEnabled) return;
        }
        else if (!_loocEnabled) return;

        // If crit player LOOC is disabled, don't send the message at all.
        if (!_critLoocEnabled && _mobStateSystem.IsCritical(source))
            return;

        var wrappedMessage = Loc.GetString("chat-manager-entity-looc-wrap-message",
            ("entityName", name),
            ("message", FormattedMessage.EscapeText(message)));

        SendInVoiceRange(ChatChannel.LOOC, message, wrappedMessage, source, hideChat ? ChatTransmitRange.HideChat : ChatTransmitRange.Normal, player.UserId);
        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"LOOC from {source}: {message}");
    }

    private void SendDeadChat(EntityUid source, ICommonSession player, string message, bool hideChat)
    {
        if (!_adminManager.IsAdmin(player) && !_deadChatEnabled)
            return;

        var clients = GetDeadChatClients();
        var playerName = Name(source);
        string wrappedMessage;
        if (_adminManager.IsAdmin(player))
        {
            wrappedMessage = Loc.GetString("chat-manager-send-admin-dead-chat-wrap-message",
                ("adminChannelName", Loc.GetString("chat-manager-admin-channel-name")),
                ("userName", player.Channel.UserName),
                ("message", FormattedMessage.EscapeText(message)));
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Admin dead chat from {source}: {message}");
        }
        else
        {
            wrappedMessage = Loc.GetString("chat-manager-send-dead-chat-wrap-message",
                ("deadChannelName", Loc.GetString("chat-manager-dead-channel-name")),
                ("playerName", (playerName)),
                ("message", FormattedMessage.EscapeText(message)));
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Dead chat from {source}: {message}");
        }

        _chatManager.ChatMessageToMany(ChatChannel.Dead, message, wrappedMessage, source, hideChat, true, clients.ToList(), author: player.UserId);
    }
}
