using Content.Client.CharacterInfo;
using Content.Shared._Oxyd.Economy;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Oxyd.Economy;

/// <summary>Shows the personal bank account on the character window.</summary>
public sealed class ClientOxydEconomySystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CharacterInfoSystem.GetCharacterInfoControlsEvent>(OnGetCharacterInfoControls);
    }

    private void OnGetCharacterInfoControls(ref CharacterInfoSystem.GetCharacterInfoControlsEvent ev)
    {
        if (!TryComp<OxydPersonalAccountComponent>(ev.Entity, out var account) || account.AccountNumber == 0)
            return;

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
        };
        var owner = string.IsNullOrWhiteSpace(account.OwnerName) ? "Account" : account.OwnerName;
        box.AddChild(new Label { Text = $"{owner}  #{account.AccountNumber}" });
        box.AddChild(new Label { Text = $"{account.Balance} cr" });
        if (account.DepartmentAccountNumber is { } department)
            box.AddChild(new Label { Text = $"Department #{department}" });

        ev.PanelControls["Bank"] = box;
    }
}
