using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.UI;

namespace Content.Server._Oxyd.NeoTheology;

public sealed partial class LitanySystem
{
    private void OnBeginLitanyMessage(Entity<LitanyBookComponent> book, ref BeginLitanyMessage args)
    {
        var actor = args.Actor;
        // Book UI begin: actor must be the UI user; revision is checked inside TryBeginLitany.
        TryBeginLitany(
            actor,
            args.Litany,
            LitanyCastOrigin.Book,
            book: book.Owner,
            expectedRevision: args.StateRevision,
            choiceToken: args.ChoiceToken);
    }

    private void OnCancelLitanyMessage(Entity<LitanyBookComponent> book, ref CancelLitanyMessage args)
    {
        TryCancelLitany(args.Actor, args.RequestId);
    }
}
