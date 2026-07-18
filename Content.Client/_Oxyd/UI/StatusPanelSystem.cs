using Content.Client.Gameplay;
using Content.Client.Guidebook.Richtext;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Oxyd.UI;

// set a stat panel's content (examine, dynamic stuff , etc). Dont use/raise in INIT!!
public record SetStatPanel(string name, BoxContainer content);

//adds to a panel or creates it if it doesn't exist . Dont use/raise in INIT!!
public record AddToPanel(string name, Control content);

// called onEnter in gameplayState. ALl systems are initialized and all event subscribed by then.
// register to this on init to add to the panel!!(or init a empty one)
public record CollectStaticPanels(Dictionary<string, BoxContainer> content);
public record RemoveStatPanel(string name);

public sealed class StatusPanelSystem : EntitySystem, IOnStateChanged<GameplayState>
{
    [Dependency] private IUserInterfaceManager _uiManager = default!;
    private StatPanel panel = default!;
    public Dictionary<string, BoxContainer> panelContent = new();
    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<SetStatPanel>(ev =>
        {
            panelContent[ev.name] = ev.content;
        });
        SubscribeLocalEvent<RemoveStatPanel>(ev =>
        {
            panelContent.Remove(ev.name);
        });
        SubscribeLocalEvent<AddToPanel>(ev =>
        {
            if(!panelContent.ContainsKey(ev.name))
                panelContent[ev.name] = new BoxContainer();
            panelContent[ev.name].AddChild(ev.content);
        });

        SubscribeLocalEvent<CollectStaticPanels>(initializeBaseStatPanels);
    }

    public void initializeBaseStatPanels(CollectStaticPanels statPanel)
    {
        statPanel.content["OOC"] =  new BoxContainer();
        statPanel.content["IC"] = new BoxContainer();
        statPanel.content["Interact"] = new BoxContainer();
        statPanel.content["Info"] = new BoxContainer();
    }

    public void OnStateEntered(GameplayState state)
    {
        panel = _uiManager.GetActiveUIWidget<StatPanel>();
        var ev = new CollectStaticPanels(panelContent);
        RaiseLocalEvent(ev);
    }

    public void OnStateExited(GameplayState state)
    {
        panel = null!;
    }
}
