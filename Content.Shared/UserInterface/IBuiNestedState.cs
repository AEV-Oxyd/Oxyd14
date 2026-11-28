namespace Content.Shared.UserInterface;

/// <summary>
/// Implemented by a <see cref="BoundUserInterfaceState"/> that carries other states inside it
/// (e.g. a PDA state wrapping the state of the active program).
/// The client-side BUI state router flattens these, so nested UI elements receive the inner
/// states directly, without the outer BUI having to forward them by hand.
/// </summary>
public interface IBuiNestedState
{
    /// <summary>
    /// The states carried by this state. May be empty. Nested states may themselves implement
    /// <see cref="IBuiNestedState"/>.
    /// </summary>
    IEnumerable<BoundUserInterfaceState> GetNestedStates();
}
