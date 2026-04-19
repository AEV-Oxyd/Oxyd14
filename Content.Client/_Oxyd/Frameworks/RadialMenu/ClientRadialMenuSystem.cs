using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Framework.RadialMenu;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Frameworks.RadialMenu;

public sealed class ClientRadialMenuSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        SubscribeNetworkEvent<RadialMenuOpenEvent>(OnOpenRadial);
    }

    private void OnOpenRadial(RadialMenuOpenEvent ev)
    {
        var menu = _ui.CreateWindow<SimpleRadialMenu>();

        var options = new List<RadialMenuOptionBase>();
        for (var i = 0; i < ev.Options.Count; i++)
        {
            var index = i;
            var entity = ev.Options[i];
            options.Add(new RadialMenuActionOption<int>(
                selectedIndex =>
                {
                    RaiseNetworkEvent(new RadialMenuSelectionEvent
                    {
                        RequestId = ev.RequestId,
                        SelectedIndex = selectedIndex,
                    });
                    menu.Close();
                },
                index
            )
            {
                IconSpecifier = RadialMenuIconSpecifier.With(GetEntity(entity)),
            });
        }

        menu.SetButtons(options);
        menu.OpenOverMouseScreenPosition();
    }
}
