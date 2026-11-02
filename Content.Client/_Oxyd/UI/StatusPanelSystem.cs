using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Client._Oxyd.UI.Style;
using Content.Client.Gameplay;
using Content.Client.Guidebook.Richtext;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared.CCVar;
using Content.Shared.Mind.Components;
using Robust.Client.Input;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client._Oxyd.UI;

// set a stat panel's content (examine, dynamic stuff , etc). Dont use/raise in INIT!!
public record SetStatPanel(string name, Control content);

//adds to a panel or creates it if it doesn't exist . Dont use/raise in INIT!!
public record AddToPanel(string name, Control content);

// called onEnter in gameplayState ALl systems are initialized and all event subscribed by then.
// register to this on init to add to the panel!!(or init a empty one)
public record CollectStaticPanels(Dictionary<string, Control> content);

// Raised on mindGotAdded. May also be raised at any point during gameplay. Will be targeted at the mindGotAddded entity and in some cases
// also to another entity(with the target being the other entity for both raises) SPCR 2026
public record CollectEntityPanels(EntityUid target, Dictionary<string, Control> panels, Dictionary<string, List<Control>> adding);

public record RemoveStatPanel(string name);

public sealed partial class StatusPanelSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _uiManager = default!;
    [Dependency] private IStateManager _stateManager = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;

    private StatPanel? panel => _uiManager.GetActiveUIWidgetOrNull<StatPanel>();

    public readonly Dictionary<string, Control> panelContent = new();
    public RadioOptions<string> buttons = new(RadioOptionsLayout.Horizontal);

    private readonly Dictionary<string, Button> _categoryButtons = new();
    private readonly List<(string Category, Control Control)> _entityControls = new();

    public string? CurrentCategory { get; private set; }

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        InitButtons();

        SubscribeLocalEvent<SetStatPanel>(ev =>
        {
            setContent(ev.name, ev.content);
        });

        SubscribeLocalEvent<RemoveStatPanel>(ev =>
        {
            removeStatCategory(ev.name);
        });

        SubscribeLocalEvent<AddToPanel>(ev =>
        {
            tryAdd(ev.name, ev.content);
        });

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);

        Subs.CVar(_configurationManager, CCVars.UILayout, _ => resetStatusContent());

        var loadController = _uiManager.GetUIController<GameplayStateLoadController>();
        loadController.OnScreenLoad += OnScreenLoad;
        loadController.OnScreenUnload += OnScreenUnload;

        _stateManager.OnStateChanged += ev =>
        {
            if (ev.NewState is GameplayState state)
            {
                OnStateEntered(state);
            }
            else if (ev.OldState is GameplayState oldState)
            {
                OnStateExited(oldState);
            }
        };
    }

    public void InitButtons()
    {
        buttons = new(RadioOptionsLayout.Horizontal);
        buttons.FirstButtonStyle = "OpenBoth";
        buttons.LastButtonStyle = "OpenBoth";
        buttons.ButtonStyle = "OpenBoth";
    }

    public void SelectCategory(string category)
    {
        if (!panelContent.ContainsKey(category))
            return;

        CurrentCategory = category;

        // Ensure strictly one category button is active at a time
        foreach (var (cat, btn) in _categoryButtons)
        {
            btn.Pressed = (cat == category);
        }

        refreshContent(category);
    }

    public void resetStatusContent()
    {
        if (panel is not StatPanel exist)
            return;

        exist.StatMenus.RemoveAllChildren();
        exist.StatContent.RemoveAllChildren();

        UpdateCategoryButtonStyles();
        foreach (var btn in _categoryButtons.Values)
        {
            btn.Orphan();
            exist.StatMenus.AddChild(btn);
        }

        if (CurrentCategory != null && panelContent.ContainsKey(CurrentCategory))
        {
            SelectCategory(CurrentCategory);
        }
        else if (panelContent.Count > 0)
        {
            var target = panelContent.ContainsKey("IC") ? "IC" : panelContent.Keys.First();
            SelectCategory(target);
        }
    }

    public void refreshContent(string? key = null)
    {
        key ??= CurrentCategory;
        if (key == null || panel is not StatPanel exist)
            return;

        if (!panelContent.TryGetValue(key, out var content))
            return;

        exist.StatContent.RemoveAllChildren();
        content.Orphan();
        exist.StatContent.AddChild(content);
    }

    public void setContent(string key, Control content)
    {
        if (!panelContent.ContainsKey(key))
        {
            addStatCategory(key, content);
        }
        else
        {
            panelContent[key] = content;
            if (CurrentCategory == null || CurrentCategory == key)
            {
                SelectCategory(key);
            }
        }
    }

    public void addStatCategory(string name, BoxContainer content)
    {
        addStatCategory(name, (Control)content);
    }

    public void addStatCategory(string name, Control content)
    {
        if (panelContent.ContainsKey(name))
            return;

        panelContent.Add(name, content);

        var btn = new Button
        {
            Name = name,
            Text = name,
            ToggleMode = true,
        };
        btn.OnPressed += _ => SelectCategory(name);

        _categoryButtons[name] = btn;
        UpdateCategoryButtonStyles();

        if (panel is StatPanel exist)
        {
            btn.Orphan();
            exist.StatMenus.AddChild(btn);
        }

        // Automatically default to "IC" when it is added, or the first category if none yet
        if (CurrentCategory == null || (CurrentCategory != "IC" && name == "IC"))
        {
            SelectCategory("IC");
        }
        else
        {
            btn.Pressed = (name == CurrentCategory);
        }
    }

    public void removeStatCategory(string name)
    {
        if (_categoryButtons.Remove(name, out var btn))
        {
            btn.Orphan();
            UpdateCategoryButtonStyles();
        }

        if (!panelContent.Remove(name, out var container))
            return;

        container.Orphan();

        if (CurrentCategory == name)
        {
            if (panelContent.Count > 0)
            {
                var next = panelContent.ContainsKey("IC") ? "IC" : panelContent.Keys.First();
                SelectCategory(next);
            }
            else
            {
                CurrentCategory = null;
                if (panel is StatPanel exist)
                    exist.StatContent.RemoveAllChildren();
            }
        }
    }

    private void UpdateCategoryButtonStyles()
    {
        var count = _categoryButtons.Count;
        var idx = 0;
        foreach (var btn in _categoryButtons.Values)
        {
            btn.StyleClasses.Remove("OpenBoth");
            btn.StyleClasses.Remove(ErisSheetlet.StyleClassErisHoloButton);
            btn.StyleClasses.Remove(ErisSheetlet.StyleClassErisHoloButtonOpenBoth);
            btn.StyleClasses.Remove(ErisSheetlet.StyleClassErisHoloButtonOpenLeft);
            btn.StyleClasses.Remove(ErisSheetlet.StyleClassErisHoloButtonOpenRight);

            if (count == 1)
            {
                btn.StyleClasses.Add(ErisSheetlet.StyleClassErisHoloButton);
            }
            else if (idx == 0)
            {
                btn.StyleClasses.Add(ErisSheetlet.StyleClassErisHoloButtonOpenRight);
            }
            else if (idx == count - 1)
            {
                btn.StyleClasses.Add(ErisSheetlet.StyleClassErisHoloButtonOpenLeft);
            }
            else
            {
                btn.StyleClasses.Add(ErisSheetlet.StyleClassErisHoloButtonOpenBoth);
            }

            idx++;
        }
    }

    public void removeStatButton(string category, string buttonName)
    {
        if (!panelContent.TryGetValue(category, out var container))
            return;

        Control? targ = null;
        foreach (var ctrl in container.Children)
        {
            if (ctrl is Button button && button.Name == buttonName)
            {
                targ = button;
                break;
            }
        }

        targ?.Orphan();

        if (CurrentCategory == category)
            refreshContent(category);
    }

    public void addStatCatButton(string CategoryName, string buttonText, Action<BaseButton.ButtonEventArgs> action)
    {
        var but = new Button
        {
            Text = buttonText,
        };
        but.StyleClasses.Add(ErisSheetlet.StyleClassErisHoloButton);
        but.OnPressed += action;
        tryAdd(CategoryName, but);
    }

    public void addStatCatButton(string CategoryName, string buttonText, Action<BaseButton.ButtonToggledEventArgs> action)
    {
        var but = new Button
        {
            Text = buttonText,
            ToggleMode = true,
        };
        but.OnToggled += action;
        but.StyleClasses.Add(ErisSheetlet.StyleClassErisHoloButton);
        tryAdd(CategoryName, but);
    }

    public void tryAdd(string name, Control content)
    {
        if (!panelContent.TryGetValue(name, out var container))
        {
            container = new WrapContainer
            {
                HorizontalExpand = true,
                SeparationOverride = 4,
                CrossSeparationOverride = 4,
            };
            addStatCategory(name, container);
        }

        if (!string.IsNullOrEmpty(content.Name))
        {
            foreach (var child in container.Children)
            {
                if (child.Name == content.Name)
                    return;
            }
        }

        content.Orphan();
        container.AddChild(content);

        if (CurrentCategory == name)
            refreshContent(name);
    }

    public void ClearEntityPanels()
    {
        foreach (var (_, ctrl) in _entityControls)
        {
            ctrl.Orphan();
        }
        _entityControls.Clear();
    }

    private void OnPlayerAttached(LocalPlayerAttachedEvent ev)
    {
        TriggerPanelCollection(ev.Entity, null);
    }

    public void onPlayerAttach(LocalPlayerAttachedEvent ev) => OnPlayerAttached(ev);

    private void OnPlayerDetached(LocalPlayerDetachedEvent ev)
    {
        ClearEntityPanels();
        if (CurrentCategory != null)
            refreshContent(CurrentCategory);
    }

    public void TriggerPanelCollection(EntityUid owner, EntityUid? target)
    {
        ClearEntityPanels();

        Log.Debug("Collecting entity panels");
        var myEv = new CollectEntityPanels(owner, new(), new());
        RaiseLocalEvent(owner, myEv);
        if (target is EntityUid exist)
        {
            RaiseLocalEvent(exist, myEv);
        }

        foreach (var (category, control) in myEv.panels)
        {
            setContent(category, control);
            _entityControls.Add((category, control));
        }

        foreach (var (category, controls) in myEv.adding)
        {
            foreach (var elem in controls)
            {
                tryAdd(category, elem);
                _entityControls.Add((category, elem));
            }
        }

        if (CurrentCategory != null)
            refreshContent(CurrentCategory);
    }

    private void OnScreenLoad()
    {
        resetStatusContent();
    }

    private void OnScreenUnload()
    {
        foreach (var btn in _categoryButtons.Values)
        {
            btn.Orphan();
        }
        foreach (var content in panelContent.Values)
        {
            content.Orphan();
        }
    }

    public void OnStateEntered(GameplayState state)
    {
        var ev = new CollectStaticPanels(panelContent);
        RaiseLocalEvent(ev);

        foreach (var (key, value) in ev.content)
        {
            if (!panelContent.ContainsKey(key))
            {
                addStatCategory(key, value);
            }
        }

        resetStatusContent();
    }

    public void OnStateExited(GameplayState state)
    {
        ClearEntityPanels();
        foreach (var btn in _categoryButtons.Values)
        {
            btn.Orphan();
        }
        _categoryButtons.Clear();
        foreach (var content in panelContent.Values)
        {
            content.Orphan();
        }
        panelContent.Clear();
        CurrentCategory = null;
    }
}
