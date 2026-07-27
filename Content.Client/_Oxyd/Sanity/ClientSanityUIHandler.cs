using Content.Client.CharacterInfo;
using Content.Server._Oxyd.SanityInsightAndResting;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Oxyd.Sanity;

/// <summary>
/// This handles...
/// </summary>
public sealed class ClientSanityUIHandler : EntitySystem
{
    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<CharacterInfoSystem.GetCharacterInfoControlsEvent>(OnGetCharacterInfoControls);
    }

    private void OnGetCharacterInfoControls(CharacterInfoSystem.GetCharacterInfoControlsEvent ev)
    {
        if (!TryComp<SanityComponent>(ev.Entity, out var sanityComponent))
            return;
        var cont = new BoxContainer();
        ev.PanelControls["Sanity"] = cont;
    }
}
