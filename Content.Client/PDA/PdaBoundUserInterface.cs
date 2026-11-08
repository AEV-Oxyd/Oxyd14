using System.Linq;
using Content.Client._Oxyd.Computers.Cartridges;
using Content.Client._Oxyd.Computers.ScreenSharing;
using Content.Client.CartridgeLoader;
using Content.Client.UserInterface.Controls;
using Content.Shared.CartridgeLoader;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.PDA;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.PDA
{
    [UsedImplicitly]
    public sealed class PdaBoundUserInterface : CartridgeLoaderBoundUserInterface
    {
        private readonly PdaSystem _pdaSystem;

        [ViewVariables]
        private PdaMenu? _menu;

        private PdaUpdateState? _lastState;
        private OsUiFragment? _osFragment;

        public PdaBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
            _pdaSystem = EntMan.System<PdaSystem>();
        }

        protected override void Open()
        {
            base.Open();

            if (_menu == null)
            {
                CreateMenu();
            }
            else
            {
                if (RemoteScreenView.RemoteBuiTarget.TryBringTargetToScreen(Owner))
                    return;

                _menu.Parent?.RemoveChild(_menu);
                _menu.Visible = true;
                _menu.OpenCenteredLeft();
            }
        }

        private void CreateMenu()
        {
            if (_menu != null)
                return;

            _menu = this.CreateWindowCenteredLeft<PdaMenu>();
            SetStateScopeRoot(_menu);

            var borderColorComponent = GetBorderColorComponent();
            if (borderColorComponent != null)
            {
                _menu.BorderColor = borderColorComponent.BorderColor;
                _menu.AccentHColor = borderColorComponent.AccentHColor;
                _menu.AccentVColor = borderColorComponent.AccentVColor;
            }

            if (_lastState != null)
            {
                _menu.UpdateState(_lastState);
            }
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            if (state is PdaUpdateState pdaState)
                _lastState = pdaState;

            if (_menu == null)
                CreateMenu();

            if (_menu != null && state is PdaUpdateState updateState)
            {
                if (!_menu.IsEmbedded)
                    _menu.ScreenSurface.DeviceGuid = updateState.DeviceGuid;
                _menu.UpdateState(updateState);

                if (updateState.OsState != null && _osFragment != null)
                {
                    _osFragment.UpdateState(updateState.OsState);
                }
                else if (updateState.CartridgeState is Content.Shared._Oxyd.Computers.OsUiState osState && _osFragment != null)
                {
                    _osFragment.UpdateState(osState);
                }
            }

            base.UpdateState(state);
        }

        protected override void AttachCartridgeUI(Control cartridgeUIFragment, string? title)
        {
            if (_menu == null)
                CreateMenu();

            if (_menu == null)
                return;

            if (cartridgeUIFragment is OsUiFragment osFrag)
            {
                _osFragment = osFrag;
                if (!_menu.ProgramView.Children.Contains(osFrag))
                {
                    osFrag.Parent?.RemoveChild(osFrag);
                    _menu.ProgramView.RemoveAllChildren();
                    _menu.ProgramView.AddChild(osFrag);
                }

                if (_lastState?.OsState != null)
                    osFrag.UpdateState(_lastState.OsState);
                else if (_lastState?.CartridgeState is Content.Shared._Oxyd.Computers.OsUiState osState)
                    osFrag.UpdateState(osState);
            }
            else if (_osFragment != null)
            {
                if (!_menu.ProgramView.Children.Contains(_osFragment))
                {
                    _osFragment.Parent?.RemoveChild(_osFragment);
                    _menu.ProgramView.RemoveAllChildren();
                    _menu.ProgramView.AddChild(_osFragment);
                }
                var activeNetUid = _activeProgram.HasValue ? EntMan.GetNetEntity(_activeProgram.Value) : (Robust.Shared.GameObjects.NetEntity?)null;
                _osFragment.AttachProgram(cartridgeUIFragment, title, activeNetUid);
            }
            else
            {
                _menu.ProgramView.RemoveAllChildren();
                cartridgeUIFragment.Parent?.RemoveChild(cartridgeUIFragment);
                _menu.ProgramView.AddChild(cartridgeUIFragment);
            }
        }

        protected override void DetachCartridgeUI(Control cartridgeUIFragment)
        {
            if (_menu is null)
                return;

            if (cartridgeUIFragment is OsUiFragment)
            {
                cartridgeUIFragment.Parent?.RemoveChild(cartridgeUIFragment);
                _osFragment = null;
            }
            else if (_osFragment != null)
            {
                _osFragment.DetachProgram(cartridgeUIFragment);
            }
            else
            {
                _menu.ProgramView.RemoveAllChildren();
            }
        }

        protected override void UpdateAvailablePrograms(List<(EntityUid, CartridgeComponent)> programs)
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _menu?.Dispose();
                _menu = null;
                _osFragment = null;
            }
        }

        private PdaBorderColorComponent? GetBorderColorComponent()
        {
            return EntMan.GetComponentOrNull<PdaBorderColorComponent>(Owner);
        }
    }
}
