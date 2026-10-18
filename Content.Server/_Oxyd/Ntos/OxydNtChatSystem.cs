using System.Linq;
using Content.Shared._Oxyd.Ntos;
using Robust.Shared.Map;

namespace Content.Server._Oxyd.Ntos;

/// <summary>Eris NTNRC-style channel chat shared across PDAs.
/// Reviewer round-2: channel/name/join state lives on a lazily-spawned nullspace
/// entity (OxydBankSystem pattern) so chat resets each round, bounded by caps.</summary>
public sealed partial class OxydNtChatSystem : EntitySystem
{
    // Reviewer round-2 caps: bounded chat storage per round.
    private const int MaxChannels = 32;
    private const int MaxMessages = 80;
    private const int MaxMessage = 1024;
    private const int MaxTitle = 64;

    private EntityUid? _stateEnt;

    /// <summary>Find or create the round-scoped chat state entity (nullspace).</summary>
    private OxydNtChatStateComponent GetState()
    {
        if (_stateEnt is { } cached && !Deleted(cached))
            return Comp<OxydNtChatStateComponent>(cached);
        var q = EntityQueryEnumerator<OxydNtChatStateComponent>();
        while (q.MoveNext(out var uid, out var comp))
        {
            _stateEnt = uid;
            return comp;
        }
        _stateEnt = Spawn(null, MapCoordinates.Nullspace);
        var state = AddComp<OxydNtChatStateComponent>(_stateEnt.Value);
        EnsureGeneral(state);
        return state;
    }

    public string GetOrSetName(EntityUid loader, string? requested = null)
    {
        var state = GetState();
        if (!string.IsNullOrWhiteSpace(requested))
            state.Names[loader] = requested.Trim();
        else if (!state.Names.ContainsKey(loader))
            state.Names[loader] = $"User{state.Names.Count + 100}";

        return state.Names[loader];
    }

    public IReadOnlyCollection<OxydChatChannel> ListChannels() => GetState().Channels.Values;

    public bool TryJoin(EntityUid loader, int channelId, out string? error)
    {
        error = null;
        var state = GetState();
        if (!state.Channels.TryGetValue(channelId, out var ch))
        {
            error = "Channel missing";
            return false;
        }

        Leave(loader);
        ch.Clients.Add(loader);
        state.Joined[loader] = channelId;
        AddMessage(ch, $"* {GetOrSetName(loader)} joined");
        return true;
    }

    public void Leave(EntityUid loader)
    {
        var state = GetState();
        if (state.Joined.TryGetValue(loader, out var id) && id is { } cid && state.Channels.TryGetValue(cid, out var ch))
        {
            ch.Clients.Remove(loader);
            AddMessage(ch, $"* {GetOrSetName(loader)} left");
        }

        state.Joined.Remove(loader);
    }

    public bool TryCreate(EntityUid loader, string title, out OxydChatChannel? channel)
    {
        var state = GetState();
        EnsureGeneral(state);
        // Reviewer cap: bounded channel count.
        if (state.Channels.Count >= MaxChannels)
        {
            channel = null;
            return false;
        }
        var ch = new OxydChatChannel
        {
            Id = state.NextChannelId++,
            Title = string.IsNullOrWhiteSpace(title) ? $"Channel {state.NextChannelId}" : title.Trim(),
        };
        if (ch.Title.Length > MaxTitle)
            ch.Title = ch.Title[..MaxTitle];
        state.Channels[ch.Id] = ch;
        TryJoin(loader, ch.Id, out _);
        channel = ch;
        return true;
    }

    public bool TrySpeak(EntityUid loader, string message, out string? error)
    {
        error = null;
        var state = GetState();
        if (!state.Joined.TryGetValue(loader, out var id) || id is not { } cid || !state.Channels.TryGetValue(cid, out var ch))
        {
            error = "Not in a channel";
            return false;
        }

        message = message.Trim();
        if (string.IsNullOrEmpty(message))
        {
            error = "Empty message";
            return false;
        }

        // Reviewer cap: message text truncated to a sane size.
        if (message.Length > MaxMessage)
            message = message[..MaxMessage];
        AddMessage(ch, $"{GetOrSetName(loader)}: {message}");
        return true;
    }

    public int? GetJoined(EntityUid loader)
        => GetState().Joined.TryGetValue(loader, out var id) ? id : null;

    public OxydChatChannel? GetChannel(int id)
        => GetState().Channels.TryGetValue(id, out var ch) ? ch : null;

    private static void AddMessage(OxydChatChannel ch, string message)
    {
        // Reviewer cap: bounded scrollback per channel.
        ch.Messages.Add(message);
        if (ch.Messages.Count > MaxMessages)
            ch.Messages.RemoveRange(0, ch.Messages.Count - MaxMessages);
    }

    private void EnsureGeneral(OxydNtChatStateComponent state)
    {
        if (state.Channels.Values.Any(c => c.Title == "General"))
            return;

        var ch = new OxydChatChannel { Id = state.NextChannelId++, Title = "General" };
        state.Channels[ch.Id] = ch;
    }
}
