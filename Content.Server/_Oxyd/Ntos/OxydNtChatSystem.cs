using System.Linq;

namespace Content.Server._Oxyd.Ntos;

/// <summary>Eris NTNRC-style channel chat shared across PDAs.</summary>
public sealed partial class OxydNtChatSystem : EntitySystem
{
    private int _nextChannelId = 1;
    private readonly Dictionary<int, OxydChatChannel> _channels = new();
    private readonly Dictionary<EntityUid, string> _names = new();
    private readonly Dictionary<EntityUid, int?> _joined = new();

    public override void Initialize()
    {
        base.Initialize();
        EnsureGeneral();
    }

    public string GetOrSetName(EntityUid loader, string? requested = null)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            _names[loader] = requested.Trim();
        else if (!_names.ContainsKey(loader))
            _names[loader] = $"User{_names.Count + 100}";

        return _names[loader];
    }

    public IReadOnlyCollection<OxydChatChannel> ListChannels() => _channels.Values;

    public bool TryJoin(EntityUid loader, int channelId, out string? error)
    {
        error = null;
        if (!_channels.TryGetValue(channelId, out var ch))
        {
            error = "Channel missing";
            return false;
        }

        Leave(loader);
        ch.Clients.Add(loader);
        _joined[loader] = channelId;
        ch.Messages.Add($"* {GetOrSetName(loader)} joined");
        return true;
    }

    public void Leave(EntityUid loader)
    {
        if (_joined.TryGetValue(loader, out var id) && id is { } cid && _channels.TryGetValue(cid, out var ch))
        {
            ch.Clients.Remove(loader);
            ch.Messages.Add($"* {GetOrSetName(loader)} left");
        }

        _joined.Remove(loader);
    }

    public OxydChatChannel Create(EntityUid loader, string title)
    {
        EnsureGeneral();
        var ch = new OxydChatChannel
        {
            Id = _nextChannelId++,
            Title = string.IsNullOrWhiteSpace(title) ? $"Channel {_nextChannelId}" : title.Trim(),
        };
        _channels[ch.Id] = ch;
        TryJoin(loader, ch.Id, out _);
        return ch;
    }

    public bool TrySpeak(EntityUid loader, string message, out string? error)
    {
        error = null;
        if (!_joined.TryGetValue(loader, out var id) || id is not { } cid || !_channels.TryGetValue(cid, out var ch))
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

        ch.Messages.Add($"{GetOrSetName(loader)}: {message}");
        if (ch.Messages.Count > 80)
            ch.Messages.RemoveRange(0, ch.Messages.Count - 80);
        return true;
    }

    public int? GetJoined(EntityUid loader)
        => _joined.TryGetValue(loader, out var id) ? id : null;

    public OxydChatChannel? GetChannel(int id)
        => _channels.TryGetValue(id, out var ch) ? ch : null;

    private void EnsureGeneral()
    {
        if (_channels.Values.Any(c => c.Title == "General"))
            return;

        var ch = new OxydChatChannel { Id = _nextChannelId++, Title = "General" };
        _channels[ch.Id] = ch;
    }
}

public sealed class OxydChatChannel
{
    public int Id;
    public string Title = string.Empty;
    public HashSet<EntityUid> Clients = new();
    public List<string> Messages = new();
}
