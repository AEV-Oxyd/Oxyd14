using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Framework.Bundles;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BundleGenericInteractionComponent : Component
{
    [AutoNetworkedField]
    public int throwRandom = 0;
}
