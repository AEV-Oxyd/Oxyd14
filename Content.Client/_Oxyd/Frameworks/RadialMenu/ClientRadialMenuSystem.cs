using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Framework.RadialMenu;
using Robust.Client.UserInterface;
using Robust.Client.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._Oxyd.Frameworks.RadialMenu;

public sealed partial  class ClientRadialMenuSystem : SharedRadialMenuSystem
{
    [Dependency] private  IUserInterfaceManager _ui = default!;
    [Dependency] private  IPlayerManager _playerManager = default!;
    [Dependency] private  IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RadialMenuOpenEvent>(OnOpenRadial);
    }

    private void OnOpenRadial(RadialMenuOpenEvent ev)
    {
        OpenMenu(ev.RequestId, ev.Options, GetEntity(ev.Target));
    }

    public override void ShowRadial(ICommonSession player,
        List<RadialMenuOption> options,
        Action<RadialBaseSelection> callback,
        EntityUid? target = null,
        bool server = true,
        bool client = true)
    {
        if (!client)
            return;
        if (player == _playerManager.LocalSession)
        {
            OpenMenu(Guid.NewGuid(), options, target);
        }
    }

    protected override void OpenMenu(Guid requestId, List<RadialMenuOption> options, EntityUid? target = null)
    {
        if (!_timing.IsFirstTimePredicted)
            return;
        var menu = _ui.CreateWindow<SimpleRadialMenu>();

        if (target != null)
            menu.Track(target.Value);

        var menuOptions = new List<RadialMenuOptionBase>();
        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            var opt = options[i];
            var option = new RadialMenuActionOption<int>(
                selectedIndex =>
                {
                    RaiseNetworkEvent(new RadialMenuSelectionEvent
                    {
                        RequestId = requestId,
                        SelectedIndex = selectedIndex,
                    });
                    menu.Close();
                },
                index
            )
            {
                ToolTip = opt.Tooltip
            };

            if (opt is EntityRadialMenuOption entityOpt)
            {
                option.IconSpecifier = RadialMenuIconSpecifier.With(GetEntity(entityOpt.Entity));
            }
            else if (opt is SpriteRadialMenuOption spriteOpt)
            {
                option.IconSpecifier = RadialMenuIconSpecifier.With(spriteOpt.Sprite);
            }
            else if (opt is PrototypeRadialMenuOption protoOpt)
            {
                option.IconSpecifier = RadialMenuIconSpecifier.With(new EntProtoId(protoOpt.Prototype));
            }

            menuOptions.Add(option);
        }

        menu.SetButtons(menuOptions);

        if (target != null)
        {
            menu.UpdatePosition();
        }
        else
        {
            menu.OpenOverMouseScreenPosition();
        }
    }
}
