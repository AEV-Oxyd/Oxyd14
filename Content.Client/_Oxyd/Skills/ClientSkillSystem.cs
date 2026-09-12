using System.Reflection.Metadata.Ecma335;
using Content.Client._Oxyd.UI;
using Content.Client.CharacterInfo;
using Content.Client.UserInterface.Systems.Character;
using Content.Shared._Oxyd.Skills;
using Robust.Client.GameStates;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Oxyd.Skills;

/// <summary>
/// This handles...
/// </summary>
public sealed partial class ClientSkillSystem : SharedSkillSystem
{
    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CharacterInfoSystem.GetCharacterInfoControlsEvent>(OnGetCharacterInfoControls);
    }

    private void OnGetCharacterInfoControls(ref CharacterInfoSystem.GetCharacterInfoControlsEvent ev)
    {
        if (!TryComp<MobSkillComponent>(ev.Entity, out var skill))
            return;
        var boxie = new VBox();
        foreach (var (proto, values) in skill.skills)
        {
            if (!ProtoMan.TryIndex<SkillPrototype>(proto, out var instance))
            {
                Log.Error($"Can't find skill {proto}!!!");
                break;
            }
            var tooltip = new Tooltip();
            tooltip.SetMessage(FormattedMessage.FromUnformatted(instance.description));
            var staty = new PanelContainer(){HorizontalExpand = true, VerticalExpand = true, MinWidth = 100};
            var hb = new HBox(){MinWidth = 150};
            staty.PanelOverride = new StyleBoxFlat(backgroundColor: new Color(36, 37, 53));
            staty.HorizontalExpand = true;
            hb.AddChild(new Label() { Text = instance.name, MinWidth = 125 });
            var statString = $"{values[0]}";
            if (values[1] != 0)
            {
                if(values[1] < 0)
                    statString += $"{values[1]}";
                else
                    statString += $"+{values[1]}";
            }
            hb.AddChild(new Label() { Text = statString, MinWidth = 25 });
            foreach (var child in hb.Children)
            {
                child.TooltipDelay = 0;
                child.TooltipSupplier = _ => tooltip;
                child.MouseFilter = Control.MouseFilterMode.Pass;
            }
            staty.AddChild(hb);
            boxie.AddChild(staty);
        }
        ev.PanelControls.Add("Skills", boxie);
    }
}
