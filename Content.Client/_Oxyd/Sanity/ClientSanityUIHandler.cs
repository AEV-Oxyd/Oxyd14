using System.Linq;
using Content.Client._Oxyd.Framework;
using Content.Client._Oxyd.UI;
using Content.Client.CharacterInfo;
using Content.Server._Oxyd.SanityInsightAndResting;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Oxyd.Sanity;

/// <summary>
/// This handles...
/// </summary>
public sealed class ClientSanityUIHandler : EntitySystem
{
    [Dependency] private StatusPanelSystem panels = default!;
    public const string focusInsightButtonId = "fins";
    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<CharacterInfoSystem.GetCharacterInfoControlsEvent>(OnGetCharacterInfoControls);
    }

    private void OnGetCharacterInfoControls(ref CharacterInfoSystem.GetCharacterInfoControlsEvent ev)
    {
        if (!TryComp<SanityComponent>(ev.Entity, out var sanityComponent))
            return;
        var cont = new SanityMenu(sanityComponent);
        ev.PanelControls["Sanity"] = cont;
    }
    [SubscribeLocalEvent]
    private void OnPanelRequest(EntityUid id, SanityComponent component, CollectEntityPanels ev)
    {
        if(ClientOxydHelpers.FindControl<Button>(panels.panelContent["IC"], focusInsightButtonId, out _))
        {
            return;
        }
        var butt = new Button()
        {
            Name = focusInsightButtonId,
            Text = "Focus Insight",
            ToolTip = "0.75x gain modifier",
            TooltipDelay = 0f,
        };
        butt.OnButtonDown += (args) =>
        {
            RaiseNetworkEvent(new RequestInternalFocus());
        };
        if (!ev.adding.TryGetValue("IC", out var panel))
            ev.adding["IC"] = new List<Control>();
        ev.adding["IC"].Add(butt);
    }
}
