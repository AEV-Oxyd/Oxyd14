namespace Content.Client.UserInterface.Controls;

/// <summary>
/// A check box control derived from <see cref="ContentButton"/> that toggles state on click.
/// </summary>
public class ContentCheckBox : ContentButton
{
    public ContentCheckBox()
    {
        ToggleMode = true;
    }

    public ContentCheckBox(string text) : base(text)
    {
        ToggleMode = true;
    }
}
