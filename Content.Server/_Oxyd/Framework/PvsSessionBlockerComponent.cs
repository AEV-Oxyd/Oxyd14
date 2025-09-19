namespace Content.Server._Oxyd.Framework;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class PvsSessionBlockerComponent : Component
{
    public HashSet<string> blacklistedFor = new();
}
