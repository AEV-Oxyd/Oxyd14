using Content.Server._Oxyd.Economy;
using Content.Shared._Oxyd.Ntos;
using Content.Shared.CartridgeLoader;

namespace Content.Server._Oxyd.Ntos;

public sealed partial class OxydEmailCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridges = default!;
    [Dependency] private OxydEmailSystem _email = default!;


    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydEmailCartridgeComponent, CartridgeUiReadyEvent>(OnReady);
        SubscribeLocalEvent<OxydEmailCartridgeComponent, CartridgeMessageEvent>(OnMessage);
    }

    private void OnReady(EntityUid uid, OxydEmailCartridgeComponent component, CartridgeUiReadyEvent args)
        => Push(args.Loader);

    private void OnMessage(EntityUid uid, OxydEmailCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not OxydEmailUiMessageEvent msg)
            return;

        var loader = GetEntity(args.LoaderUid);
        string? error = null;
        string? status = null;

        switch (msg.Action)
        {
            case OxydEmailUiAction.Register:
                if (_email.TryRegister(msg.Login, msg.Password, out error))
                {
                    _email.TryLogin(loader, msg.Login, msg.Password, out _);
                    status = "Registered & logged in";
                }
                break;
            case OxydEmailUiAction.Login:
                if (_email.TryLogin(loader, msg.Login, msg.Password, out error))
                    status = "Logged in";
                break;
            case OxydEmailUiAction.Logout:
                _email.Logout(loader);
                if (TryComp<OxydEmailUiComponent>(loader, out var loUi))
                    loUi.OpenMessageId = null;
                status = "Logged out";
                break;
            case OxydEmailUiAction.Open:
                if (_email.TryGetSession(loader, out var loginOpen))
                {
                    EnsureComp<OxydEmailUiComponent>(loader).OpenMessageId = msg.MessageId;
                    _email.MarkRead(loginOpen, msg.MessageId);
                }
                else
                    error = "Not logged in";
                break;
            case OxydEmailUiAction.Send:
                if (!_email.TryGetSession(loader, out var from))
                    error = "Not logged in";
                else if (!_email.TrySend(from, msg.To, msg.Subject, msg.Body, out error))
                    error ??= "Send failed";
                else
                    status = "Sent";
                break;
            case OxydEmailUiAction.Delete:
                if (!_email.TryGetSession(loader, out var delLogin))
                    error = "Not logged in";
                else if (_email.TryDelete(delLogin, msg.MessageId))
                {
                    if (TryComp<OxydEmailUiComponent>(loader, out var delUi))
                        delUi.OpenMessageId = null;
                    status = "Deleted";
                }
                else
                    error = "Not found";
                break;
        }

        Push(loader, error, status);
    }

    private void Push(EntityUid loader, string? error = null, string? status = null)
    {
        var state = new OxydEmailUiState { Error = error, Status = status };
        if (_email.TryGetSession(loader, out var login))
        {
            state.LoggedIn = true;
            state.Login = login;
            foreach (var m in _email.GetInbox(login))
            {
                state.Inbox.Add(new OxydEmailListEntry
                {
                    Id = m.Id,
                    From = m.From,
                    Subject = m.Subject,
                    Read = m.Read,
                });
            }

            if (TryComp<OxydEmailUiComponent>(loader, out var ui) && ui.OpenMessageId is { } oid)
            {
                var detail = _email.GetMessage(login, oid);
                if (detail != null)
                {
                    state.Active = new OxydEmailDetail
                    {
                        Id = detail.Id,
                        From = detail.From,
                        To = detail.To,
                        Subject = detail.Subject,
                        Body = detail.Body,
                    };
                }
            }
        }

        _cartridges.UpdateCartridgeUiState(loader, state);
    }
}
