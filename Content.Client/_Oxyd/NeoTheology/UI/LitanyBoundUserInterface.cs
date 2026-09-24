using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.UI;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._Oxyd.NeoTheology.UI;

/// <summary>
/// Presentation-only book window. Clicks never authorize a cast; the server owns
/// that decision when the speech/cast milestone lands.
/// </summary>
public sealed class LitanyBoundUserInterface : BoundUserInterface
{
    private LitanyWindow? _window;

    public LitanyBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<LitanyWindow>();

        _window.BeginLitany += OnBeginLitany;
        _window.SubmitChoices += OnSubmitChoices;
        _window.CancelLitany += OnCancelLitany;
        _window.OnClose += Close;
    }

    private void OnBeginLitany(ProtoId<LitanyPrototype> litany, uint revision, string? choiceToken)
    {
        // The server derives the actor from the BUI context and revalidates the
        // revision, entitlement, target and cost. The client only forwards the
        // selected prototype and opaque choice token.
        SendMessage(new BeginLitanyMessage(litany, revision, choiceToken));
    }

    private void OnSubmitChoices(
        string requestId,
        List<string> selectedTokens,
        string? recipeId,
        string? plainText)
    {
        SendMessage(new SubmitLitanyChoicesMessage(
            requestId,
            selectedTokens,
            recipeId,
            plainText));
    }

    private void OnCancelLitany(string requestId)
    {
        SendMessage(new CancelLitanyMessage(requestId));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is LitanyViewerSnapshot snapshot)
            _window?.UpdateState(snapshot);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (_window is null)
            return;

        switch (message)
        {
            case LitanyViewerSnapshotMessage snapshot:
                _window.UpdateSnapshot(snapshot);
                break;
            case LitanyChoiceSnapshotMessage choices:
                _window.UpdateChoiceSnapshot(choices);
                break;
            case LitanyProgressMessage progress:
                _window.UpdateProgress(progress);
                break;
            case LitanyResultMessage result:
                _window.UpdateResult(result);
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _window is not null)
        {
            _window.BeginLitany -= OnBeginLitany;
            _window.SubmitChoices -= OnSubmitChoices;
            _window.CancelLitany -= OnCancelLitany;
            _window.OnClose -= Close;
            _window.Dispose();
            _window = null;
        }

        base.Dispose(disposing);
    }
}
