using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;

namespace Content.Shared._Oxyd.Computers;

[ByRefEvent]
public readonly record struct EmbeddedComputerHostActivatedEvent(EntityUid User);

public sealed class SharedEmbeddedComputerHostSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EmbeddedComputerHostComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        SubscribeLocalEvent<EmbeddedComputerHostComponent, ActivateInWorldEvent>(OnActivateInWorld, before: new[] { typeof(ActivatableUISystem) });
        SubscribeLocalEvent<EmbeddedComputerHostComponent, GetVerbsEvent<ActivationVerb>>(OnGetVerbs);
    }

    private void OnOpenAttempt(Entity<EmbeddedComputerHostComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnActivateInWorld(Entity<EmbeddedComputerHostComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;

        var ev = new EmbeddedComputerHostActivatedEvent(args.User);
        RaiseLocalEvent(ent, ref ev);
    }

    private void OnGetVerbs(Entity<EmbeddedComputerHostComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("oxyd-open-terminal-verb"),
            Act = () =>
            {
                var ev = new EmbeddedComputerHostActivatedEvent(user);
                RaiseLocalEvent(ent, ref ev);
            }
        });
    }
}
