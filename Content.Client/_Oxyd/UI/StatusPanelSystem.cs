using System.Linq;
using Content.Client.Gameplay;
using Content.Client.Guidebook.Richtext;
using Content.Shared.CCVar;
using Robust.Client.Input;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Client._Oxyd.UI;

// set a stat panel's content (examine, dynamic stuff , etc). Dont use/raise in INIT!!
public record SetStatPanel(string name, BoxContainer content);

//adds to a panel or creates it if it doesn't exist . Dont use/raise in INIT!!
public record AddToPanel(string name, Control content);

// called onEnter in gameplayState. ALl systems are initialized and all event subscribed by then.
// register to this on init to add to the panel!!(or init a empty one)
public record CollectStaticPanels(Dictionary<string, BoxContainer> content);
public record RemoveStatPanel(string name);

public sealed class StatusPanelSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _uiManager = default!;
    [Dependency] private IStateManager _stateManager = default!;
    [Dependency] private IConfigurationManager configurationManager = default!;
    private StatPanel? panel => _uiManager.GetActiveUIWidgetOrNull<StatPanel>();
    public Dictionary<string, BoxContainer> panelContent = new();
    public RadioOptions<string> buttons = new RadioOptions<string>(RadioOptionsLayout.Horizontal);


    public void resetStatusContent()
    {
        if (panel is null)
            return;
        panel.StatContent.RemoveAllChildren();
        panel.StatMenus.RemoveAllChildren();
        panel.StatMenus.AddChild(buttons);
    }

    public void setContent(string key)
    {
        if (panel is null)
            return;
        panel.StatContent.RemoveAllChildren();
        panel.StatContent.AddChild(panelContent[key]);
    }
    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<SetStatPanel>(ev =>
        {
            panelContent[ev.name] = ev.content;
            setContent(ev.name);
        });
        SubscribeLocalEvent<RemoveStatPanel>(ev =>
        {
            panelContent.Remove(ev.name);
            setContent(panelContent.Keys.First());
        });
        SubscribeLocalEvent<AddToPanel>(ev =>
        {
            tryAdd(ev.name, ev.content);
        });
        Subs.CVar(configurationManager, CCVars.UILayout, _ => resetStatusContent());
        buttons.FirstButtonStyle = "OpenBoth";
        buttons.LastButtonStyle = "OpenBoth";
        buttons.ButtonStyle = "OpenBoth";
        buttons.OnItemSelected += (ev) =>
        {
            buttons.Select(ev.Id);
            setContent(ev.Button.SelectedValue);
        };
        _stateManager.OnStateChanged += (ev =>
        {
            if (ev.NewState is GameplayState state)
            {
                OnStateEntered(state);
            }
            else if (ev.OldState is GameplayState oldState)
            {
                OnStateExited(oldState);
            }
        });
    }

    public void addStatCategory(string name, BoxContainer content)
    {
        panelContent.Add(name, content);
        buttons.AddItem(name, name);
    }

    public void removeStatCategory(string name)
    {
        panelContent.Remove(name);
        // THEY LOOOOOVE LOCKING DATA BEHIND PRIVATE FIELDS IN ENGINE!!! SPCR 2026
        int target = -1;
        for (int i = 0; i < buttons.ItemCount; i++)
        {
            var butt = (RadioOptionButtonData<string>)buttons.GetItemMetadata(i)!;
            if (butt.Value == name)
            {
                target = butt.Id;
                break;
            }
        }

        if (target != -1)
            buttons.RemoveItem(target);
    }

    public void removeStatButton(string category, string buttonName)
    {
        Control? targ = null;
        foreach (var ctrl in panelContent[category].Children)
        {
            if (ctrl is Button button && button.Name == buttonName)
            {
                targ = button;
                break;
            }
        }
        if(targ is not null)
            panelContent[category].Children.Remove(targ);
    }

    public void addStatCatButton(string CategoryName, string buttonText, Action<BaseButton.ButtonEventArgs> action)
    {
        var but = new Button()
        {
            Text = buttonText,

        };
        but.StyleClasses.Add("OpenBoth");
        but.OnPressed += action;
        tryAdd(CategoryName, but);
    }


    public void addStatCatButton(string CategoryName, string buttonText, Action<BaseButton.ButtonToggledEventArgs> action)
    {
        var but = new Button()
        {
            Text = buttonText,
            ToggleMode = true
        };
        but.OnToggled += action;
        but.StyleClasses.Add("OpenBoth");
        tryAdd(CategoryName, but);
    }

    public void tryAdd(string name, Control content)
    {
        if(!panelContent.ContainsKey(name))
            addStatCategory(name, new BoxContainer());
        panelContent[name].AddChild(content);
    }
    public void OnStateEntered(GameplayState state)
    {
        var ev = new CollectStaticPanels(panelContent);
        RaiseLocalEvent(ev);
        resetStatusContent();
    }

    public void OnStateExited(GameplayState state)
    {

    }
}
