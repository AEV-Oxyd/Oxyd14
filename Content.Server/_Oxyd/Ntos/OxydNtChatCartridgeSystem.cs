using System.Linq;
using Content.Shared._Oxyd.Ntos;
using Content.Shared.CartridgeLoader;

namespace Content.Server._Oxyd.Ntos;

public sealed partial class OxydNtChatCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridges = default!;
    [Dependency] private readonly OxydNtChatSystem _chat = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydNtChatCartridgeComponent, CartridgeUiReadyEvent>(OnReady);
        SubscribeLocalEvent<OxydNtChatCartridgeComponent, CartridgeMessageEvent>(OnMessage);
    }

    private void OnReady(EntityUid uid, OxydNtChatCartridgeComponent component, CartridgeUiReadyEvent args)
        => Push(args.Loader);

    private void OnMessage(EntityUid uid, OxydNtChatCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not OxydNtChatUiMessageEvent msg)
            return;

        var loader = GetEntity(args.LoaderUid);
        string? error = null;
        string? status = null;

        switch (msg.Action)
        {
            case OxydNtChatUiAction.SetName:
                _chat.GetOrSetName(loader, msg.Text);
                status = "Name set";
                break;
            case OxydNtChatUiAction.Join:
                if (!_chat.TryJoin(loader, msg.ChannelId, out error))
                    error ??= "Join failed";
                else
                    status = "Joined";
                break;
            case OxydNtChatUiAction.Leave:
                _chat.Leave(loader);
                status = "Left";
                break;
            case OxydNtChatUiAction.Create:
                var ch = _chat.Create(loader, msg.Text);
                status = $"Created {ch.Title}";
                break;
            case OxydNtChatUiAction.Speak:
                if (!_chat.TrySpeak(loader, msg.Text, out error))
                    error ??= "Send failed";
                break;
        }

        Push(loader, error, status);
    }

    private void Push(EntityUid loader, string? error = null, string? status = null)
    {
        var state = new OxydNtChatUiState
        {
            Username = _chat.GetOrSetName(loader),
            Error = error,
            Status = status,
        };

        foreach (var ch in _chat.ListChannels().OrderBy(c => c.Title))
        {
            state.Channels.Add(new OxydChatChannelEntry
            {
                Id = ch.Id,
                Title = ch.Title,
                Users = ch.Clients.Count,
            });
        }

        var joined = _chat.GetJoined(loader);
        state.ActiveChannelId = joined;
        if (joined is { } cid && _chat.GetChannel(cid) is { } active)
        {
            state.ActiveChannelTitle = active.Title;
            state.Messages = active.Messages.ToList();
        }

        _cartridges.UpdateCartridgeUiState(loader, state);
    }
}
