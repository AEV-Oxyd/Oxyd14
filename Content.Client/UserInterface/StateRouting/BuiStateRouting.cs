using System.Linq;
using Content.Shared.UserInterface;
using Robust.Client.UserInterface;

namespace Content.Client.UserInterface.StateRouting;

/// <summary>
/// What a <see cref="IBuiStateReceiver"/> did with a state. Decided by the implementer.
/// </summary>
public enum BuiStateReceiveResult : byte
{
    /// <summary>The receiver is not interested in this state.</summary>
    Ignored,

    /// <summary>The receiver used the state; other receivers still get it (default for "just listening").</summary>
    Handled,

    /// <summary>The receiver used the state and nobody else (remaining receivers, the owning BUI) should react to it.</summary>
    Consumed,
}

/// <summary>
/// Implement on any <see cref="Control"/> to receive states of the BUI it is nested under,
/// no matter how deep it sits in the control tree and without the parent forwarding anything.
/// The control only has to live below the control registered via
/// <see cref="RoutedBoundUserInterface.SetStateScopeRoot"/>.
/// </summary>
/// <remarks>
/// Receiving never affects the parent BUI's own handling of the state, unless a receiver (or scope)
/// explicitly returns <see cref="BuiStateReceiveResult.Consumed"/>.
/// </remarks>
public interface IBuiStateReceiver
{
    BuiStateReceiveResult ReceiveBuiState(BoundUserInterfaceState state);
}

/// <summary>
/// Optionally implement on a <see cref="Control"/> to configure how states flow through its subtree.
/// A scope can be a receiver too (it is offered the state before its children).
/// </summary>
public interface IBuiStateScope
{
    /// <summary>
    /// Return false to hide this state from the whole subtree (isolation / filtering).
    /// </summary>
    bool AcceptsBuiState(BoundUserInterfaceState state) => true;

    /// <summary>
    /// Return true to stop a state from travelling any further once something in this subtree handled it,
    /// i.e. it will not reach later siblings, ancestors' receivers, or the owning BUI's own logic
    /// (see <see cref="RoutedBoundUserInterface.LastStateConsumed"/>).
    /// </summary>
    bool ConsumesBuiState(BoundUserInterfaceState state) => false;
}

/// <summary>
/// Small helper for controls: register typed handlers and forward <see cref="IBuiStateReceiver"/> to it.
/// </summary>
/// <example>
/// <code>
/// private readonly BuiStateHandlers _handlers = new();
/// public BuiStateReceiveResult ReceiveBuiState(BoundUserInterfaceState s) => _handlers.Receive(s);
/// // in ctor:
/// _handlers.On&lt;TerminalUiState&gt;(UpdateState);
/// </code>
/// </example>
public sealed class BuiStateHandlers
{
    private readonly List<Func<BoundUserInterfaceState, BuiStateReceiveResult>> _handlers = new();

    /// <summary>Handle states of type <typeparamref name="T"/>. Set <paramref name="consume"/> to stop further propagation.</summary>
    public BuiStateHandlers On<T>(Action<T> handler, bool consume = false) where T : BoundUserInterfaceState
    {
        _handlers.Add(s =>
        {
            if (s is not T typed)
                return BuiStateReceiveResult.Ignored;

            handler(typed);
            return consume ? BuiStateReceiveResult.Consumed : BuiStateReceiveResult.Handled;
        });
        return this;
    }

    public BuiStateReceiveResult Receive(BoundUserInterfaceState state)
    {
        var result = BuiStateReceiveResult.Ignored;
        foreach (var handler in _handlers)
        {
            var r = handler(state);
            if (r > result)
                result = r;
        }

        return result;
    }
}

/// <summary>
/// Pushes BUI states into the control tree under a root control. Owned by <see cref="RoutedBoundUserInterface"/>.
/// </summary>
public sealed class BuiStateRouter
{
    private Control? _root;

    /// <summary>
    /// Whether <see cref="IBuiNestedState"/> states are flattened and delivered individually. Default true.
    /// </summary>
    public bool ExpandNestedStates = true;

    /// <summary>
    /// Master switch; when false nothing is routed (the BUI behaves exactly as before).
    /// </summary>
    public bool Enabled = true;

    public void SetRoot(Control? root) => _root = root;

    /// <summary>
    /// Delivers <paramref name="state"/> (and any nested states) to all receivers under the root.
    /// </summary>
    /// <returns>True if the top-level state was consumed.</returns>
    public bool Dispatch(BoundUserInterfaceState state)
    {
        if (!Enabled || _root == null)
            return false;

        var consumed = DispatchOne(state);

        if (ExpandNestedStates && state is IBuiNestedState nested)
        {
            foreach (var inner in nested.GetNestedStates())
            {
                DispatchOne(inner);
                if (inner is IBuiNestedState)
                    DispatchNestedRecursive(inner);
            }
        }

        return consumed;
    }

    private void DispatchNestedRecursive(BoundUserInterfaceState state)
    {
        if (state is not IBuiNestedState nested)
            return;

        foreach (var inner in nested.GetNestedStates())
        {
            DispatchOne(inner);
            DispatchNestedRecursive(inner);
        }
    }

    private bool DispatchOne(BoundUserInterfaceState state)
    {
        return _root != null && Visit(_root, state, out _) == BuiStateReceiveResult.Consumed;
    }

    private static BuiStateReceiveResult Visit(Control control, BoundUserInterfaceState state, out bool any)
    {
        any = false;
        var scope = control as IBuiStateScope;

        if (scope != null && !scope.AcceptsBuiState(state))
            return BuiStateReceiveResult.Ignored;

        var result = BuiStateReceiveResult.Ignored;

        if (control is IBuiStateReceiver receiver)
        {
            result = receiver.ReceiveBuiState(state);
            if (result != BuiStateReceiveResult.Ignored)
                any = true;
            if (result == BuiStateReceiveResult.Consumed)
                return result;
        }

        if (control.ChildCount > 0)
        {
            // Snapshot: receivers are allowed to add/remove controls while handling.
            foreach (var child in control.Children.ToArray())
            {
                var childResult = Visit(child, state, out var childAny);
                any |= childAny;

                if (childResult == BuiStateReceiveResult.Consumed)
                    return childResult;
            }
        }

        if (any && scope != null && scope.ConsumesBuiState(state))
            return BuiStateReceiveResult.Consumed;

        return any ? BuiStateReceiveResult.Handled : BuiStateReceiveResult.Ignored;
    }
}

/// <summary>
/// A BUI that routes every state it receives into its control tree. Nested controls opt in by implementing
/// <see cref="IBuiStateReceiver"/>; no forwarding code is needed in this BUI or in any intermediate control.
/// </summary>
/// <remarks>
/// Subclasses must call <c>base.UpdateState</c> (all existing BUIs already do) and call
/// <see cref="SetStateScopeRoot"/> once their window/root control exists.
/// Configure via <see cref="Router"/> (<see cref="BuiStateRouter.Enabled"/>, <see cref="BuiStateRouter.ExpandNestedStates"/>),
/// per-subtree via <see cref="IBuiStateScope"/>, and per-receiver via <see cref="BuiStateReceiveResult"/>.
/// </remarks>
public abstract class RoutedBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    public BuiStateRouter Router { get; } = new();

    /// <summary>
    /// True if the most recent state was consumed by a receiver/scope. Implementers who want consumption
    /// to also suppress their own handling can check this after calling <c>base.UpdateState</c>.
    /// </summary>
    [ViewVariables]
    protected bool LastStateConsumed { get; private set; }

    protected RoutedBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    /// <summary>
    /// Sets the control whose subtree receives routed states (usually the BUI's window).
    /// </summary>
    protected void SetStateScopeRoot(Control? root) => Router.SetRoot(root);

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        LastStateConsumed = Router.Dispatch(state);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            Router.SetRoot(null);
    }
}
