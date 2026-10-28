using Content.Client.UserInterface.Controls;
using Content.Shared.Hands.Components;

namespace Content.Client.UserInterface.Systems.Hands.Controls;

public sealed class HandButton : SlotControl
{
    public HandLocation HandLocation { get; }

    public HandButton(string handName, HandLocation handLocation)
    {
        HandLocation = handLocation;
        Name = "hand_" + handName;
        SlotName = handName;
        SetBackground(handLocation);
    }

    private void SetBackground(HandLocation handLoc)
    {
        var path = handLoc switch
        {
            HandLocation.Left => "hand_l",
            HandLocation.Middle => "hand_m",
            HandLocation.Right => "hand_r",
            _ => ButtonTexturePath
        };
        ButtonTexturePath = $"Slots/{path}";
        HighlightTexturePath = $"Slots/{path}_highlight";
    }
    
    
}
