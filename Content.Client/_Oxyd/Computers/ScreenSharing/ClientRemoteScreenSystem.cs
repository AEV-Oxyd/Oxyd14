using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.ScreenSharing;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;

namespace Content.Client._Oxyd.Computers.ScreenSharing;

public sealed partial class ClientRemoteScreenSystem : SharedRemoteScreenSystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<UserInterfaceComponent, ActivateInWorldEvent>(OnActivateInWorld, before: new[] { typeof(SharedInteractionSystem) });
        SubscribeLocalEvent<UserInterfaceComponent, GetVerbsEvent<ActivationVerb>>(OnGetActivationVerbs, before: new[] { typeof(SharedInteractionSystem) });
    }

    private void OnActivateInWorld(EntityUid uid, UserInterfaceComponent comp, ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        var targetUid = uid;
        if (TryComp<Content.Shared._Oxyd.Computers.EmbeddedComputerHostComponent>(uid, out var host) &&
            host.ComputerEntity is { } compUid && Exists(compUid))
        {
            targetUid = compUid;
        }

        if (RemoteScreenView.RemoteBuiTarget.TryBringTargetToScreen(targetUid))
        {
            args.Handled = true;
        }
    }

    private void OnGetActivationVerbs(EntityUid uid, UserInterfaceComponent comp, GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var targetUid = uid;
        if (TryComp<Content.Shared._Oxyd.Computers.EmbeddedComputerHostComponent>(uid, out var host) &&
            host.ComputerEntity is { } compUid && Exists(compUid))
        {
            targetUid = compUid;
        }

        if (RemoteScreenView.RemoteBuiTarget.TryGetForEntity(targetUid, out var target) && !target.IsPoppedOut)
        {
            var verb = new ActivationVerb
            {
                Text = Loc.GetString("ui-verb-toggle-open"),
                Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/open.svg.192dpi.png")),
                Act = () => target.PullWindowOnScreen()
            };
            args.Verbs.Add(verb);
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        PipScreenSurface.BeginUiFrame();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        PipScreenSurface.FlushDeferredDisposals();
    }
}
