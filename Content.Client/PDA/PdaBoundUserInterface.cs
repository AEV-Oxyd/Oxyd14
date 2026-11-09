using System.Linq;
using Content.Client._Oxyd.Computers.Cartridges;
using Content.Client._Oxyd.Computers.ScreenSharing;
using Content.Client.CartridgeLoader;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers;
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
        private EmbeddedOsUiFragment? _embeddedOsFragment;

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
                if (RemoteScreenView.RemoteBuiTarget.TryGetForEntity(Owner, out var target) && !target.IsPoppedOut)
                {
                    var stash = RemoteScreenView.GetOrCreateOffscreenStash(IoCManager.Resolve<IUserInterfaceManager>());
                    _menu.Parent?.RemoveChild(_menu);
                    stash.AddChild(_menu);
                    Robust.Client.UserInterface.Controls.LayoutContainer.SetPosition(_menu, System.Numerics.Vector2.Zero);
                }
                else
                {
                    _menu.Visible = true;
                    _menu.OpenCenteredLeft();
                }
            }
        }

        private void CreateMenu()
        {
            if (_menu != null)
                return;

            _menu = this.CreateWindowCenteredLeft<PdaMenu>();
            SetStateScopeRoot(_menu);

            ApplyThematicStyle(_lastState);

            if (_lastState != null)
            {
                _menu.UpdateState(_lastState);
            }

            if (RemoteScreenView.RemoteBuiTarget.TryGetForEntity(Owner, out var remoteTarget) && !remoteTarget.IsPoppedOut)
            {
                var stash = RemoteScreenView.GetOrCreateOffscreenStash(IoCManager.Resolve<IUserInterfaceManager>());
                _menu.Parent?.RemoveChild(_menu);
                stash.AddChild(_menu);
                Robust.Client.UserInterface.Controls.LayoutContainer.SetPosition(_menu, System.Numerics.Vector2.Zero);
            }
        }

        private void ApplyThematicStyle(PdaUpdateState? state)
        {
            if (_menu == null)
                return;

            EntityUid? hostUid = null;
            if (EntMan.TryGetComponent<OxydComputerComponent>(Owner, out var oxydComp) && oxydComp.HostMachine != null)
                hostUid = oxydComp.HostMachine;
            else if (EntMan.TryGetComponent<TransformComponent>(Owner, out var xform) && xform.ParentUid.IsValid())
                hostUid = xform.ParentUid;

            EmbeddedComputerHostComponent? hostComp = null;
            if (hostUid != null)
                EntMan.TryGetComponent(hostUid.Value, out hostComp);

            var borderColorComp = GetBorderColorComponent();

            var borderColor = hostComp?.BorderColor ?? borderColorComp?.BorderColor;
            var accentH = hostComp?.AccentHColor ?? borderColorComp?.AccentHColor;
            var accentV = hostComp?.AccentVColor ?? borderColorComp?.AccentVColor;
            var title = hostComp?.Title;
            var subtitle = hostComp?.Subtitle;

            if (state?.Template == ComputerUiTemplate.Embedded)
            {
                var hostName = hostUid != null && EntMan.TryGetComponent<MetaDataComponent>(hostUid.Value, out var meta)
                    ? meta.EntityName
                    : null;

                if (string.IsNullOrEmpty(title))
                {
                    title = !string.IsNullOrEmpty(hostName) ? hostName.ToUpperInvariant() : "EMBEDDED CONTROLLER";
                }

                if (string.IsNullOrEmpty(subtitle))
                {
                    subtitle = " | Subsystem Terminal";
                }

                borderColor ??= "#00D4FF";
            }
            else
            {
                title ??= "NTOS 1.4™";
                subtitle ??= " | Portable Terminal";
            }

            if (!string.IsNullOrEmpty(borderColor))
            {
                _menu.BorderColor = borderColor;
                _menu.OsTitleLabel.FontColorOverride = Color.FromHex(borderColor, Color.FromHex("#00D4FF"));
            }

            if (!string.IsNullOrEmpty(accentH))
                _menu.AccentHColor = accentH;

            if (!string.IsNullOrEmpty(accentV))
                _menu.AccentVColor = accentV;

            if (!string.IsNullOrEmpty(title))
                _menu.OsTitleLabel.Text = title;

            if (!string.IsNullOrEmpty(subtitle))
                _menu.OsSubtitleLabel.Text = subtitle;
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            if (state is PdaUpdateState pdaState)
                _lastState = pdaState;

            if (_menu == null)
                CreateMenu();

            if (_menu != null && state is PdaUpdateState updateState)
            {
                ApplyThematicStyle(updateState);

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
                else if (updateState.CartridgeState is Content.Shared._Oxyd.Computers.EmbeddedOsUiState embState && _embeddedOsFragment != null)
                {
                    _embeddedOsFragment.UpdateState(embState);
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
                _embeddedOsFragment = null;
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
            else if (cartridgeUIFragment is EmbeddedOsUiFragment embFrag)
            {
                _embeddedOsFragment = embFrag;
                _osFragment = null;
                if (!_menu.ProgramView.Children.Contains(embFrag))
                {
                    embFrag.Parent?.RemoveChild(embFrag);
                    _menu.ProgramView.RemoveAllChildren();
                    _menu.ProgramView.AddChild(embFrag);
                }

                if (_lastState?.CartridgeState is Content.Shared._Oxyd.Computers.EmbeddedOsUiState embState)
                    embFrag.UpdateState(embState);
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
            else if (cartridgeUIFragment is EmbeddedOsUiFragment)
            {
                cartridgeUIFragment.Parent?.RemoveChild(cartridgeUIFragment);
                _embeddedOsFragment = null;
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
                _embeddedOsFragment = null;
            }
        }

        private PdaBorderColorComponent? GetBorderColorComponent()
        {
            if (EntMan.TryGetComponent<OxydComputerComponent>(Owner, out var comp) && comp.HostMachine is { } hostUid)
            {
                if (EntMan.TryGetComponent<PdaBorderColorComponent>(hostUid, out var hostBorder))
                    return hostBorder;
            }

            if (EntMan.TryGetComponent<TransformComponent>(Owner, out var xform) && xform.ParentUid.IsValid())
            {
                var cur = xform.ParentUid;
                while (cur.IsValid())
                {
                    if (EntMan.TryGetComponent<PdaBorderColorComponent>(cur, out var parentBorder))
                        return parentBorder;
                    if (!EntMan.EntityExists(cur))
                        break;
                    cur = EntMan.GetComponent<TransformComponent>(cur).ParentUid;
                }
            }

            return EntMan.GetComponentOrNull<PdaBorderColorComponent>(Owner);
        }
    }
}
