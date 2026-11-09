using Content.Client.Power.APC.UI;
using Content.Client.UserInterface.StateRouting;
using Content.Shared.Access.Systems;
using Content.Shared.APC;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Player;

namespace Content.Client.Power.APC
{
    [UsedImplicitly]
    public sealed class ApcBoundUserInterface : RoutedBoundUserInterface
    {
        [ViewVariables]
        private ApcMenu? _menu;

        public ApcBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();
            _menu = this.CreateWindow<ApcMenu>();
            SetStateScopeRoot(_menu);
            _menu.SetEntity(Owner);
            _menu.OnBreaker += BreakerPressed;

            var hasAccess = false;
            if (PlayerManager.LocalEntity != null)
            {
                var accessReader = EntMan.System<AccessReaderSystem>();
                hasAccess = accessReader.IsAllowed((EntityUid)PlayerManager.LocalEntity, Owner);
            }
            _menu?.SetAccessEnabled(hasAccess);

            if (_menu != null && Content.Client._Oxyd.Computers.ScreenSharing.RemoteScreenView.RemoteBuiTarget.TryGetForEntity(Owner, out var remoteTarget) && !remoteTarget.IsPoppedOut)
            {
                var stash = Content.Client._Oxyd.Computers.ScreenSharing.RemoteScreenView.GetOrCreateOffscreenStash(Robust.Shared.IoC.IoCManager.Resolve<Robust.Client.UserInterface.IUserInterfaceManager>());
                _menu.Parent?.RemoveChild(_menu);
                stash.AddChild(_menu);
                Robust.Client.UserInterface.Controls.LayoutContainer.SetPosition(_menu, System.Numerics.Vector2.Zero);
            }
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);

            var castState = (ApcBoundInterfaceState) state;
            _menu?.UpdateState(castState);
        }

        public void BreakerPressed()
        {
            SendMessage(new ApcToggleMainBreakerMessage());
        }
    }
}
