using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.PDA.Ringer;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Content.Shared.Wires;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.Computers;

/// <summary>
/// Intercepts host machine / PDA activations and relays them into the encapsulated
/// computer entity, keeping machine UI and hardware data isolated inside the child computer.
/// </summary>
public sealed partial class SharedEmbeddedComputerHostSystem : EntitySystem
{
    private EntityQuery<OxydComputerComponent> _computerQuery;

    public override void Initialize()
    {
        base.Initialize();

        _computerQuery = GetEntityQuery<OxydComputerComponent>();

        SubscribeLocalEvent<EmbeddedComputerHostComponent, ActivatableUIOpenAttemptEvent>(OnActivatableOpenAttempt);
        SubscribeLocalEvent<EmbeddedComputerHostComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<EmbeddedComputerHostComponent, ActivateInWorldEvent>(OnActivateInWorld);
        SubscribeLocalEvent<EmbeddedComputerHostComponent, GetVerbsEvent<ActivationVerb>>(AddActivationVerbs);
        SubscribeLocalEvent<EmbeddedComputerHostComponent, InteractUsingEvent>(OnHostInteractUsing);
        SubscribeLocalEvent<EmbeddedComputerHostComponent, AfterInteractEvent>(OnHostAfterInteract);
    }

    private void OnActivatableOpenAttempt(Entity<EmbeddedComputerHostComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        // Only intercept primary machine UI openings; let Wires, Storage, Ringer, Store, ResearchClient pass through.
        if (TryComp<ActivatableUIComponent>(ent.Owner, out var act) &&
            act.Key is Enum key &&
            (key.Equals(WiresUiKey.Key) ||
             key.Equals(RingerUiKey.Key) ||
             key.ToString() == "Store" ||
             key.ToString() == "Storage"))
        {
            return;
        }

        args.Cancel();
        if (!args.Silent)
        {
            var ev = new EmbeddedComputerHostActivatedEvent(ent.Owner, args.User);
            RaiseLocalEvent(ent.Owner, ref ev);
        }
    }

    private void OnUseInHand(Entity<EmbeddedComputerHostComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var ev = new EmbeddedComputerHostActivatedEvent(ent.Owner, args.User);
        RaiseLocalEvent(ent.Owner, ref ev);
    }

    private void OnActivateInWorld(Entity<EmbeddedComputerHostComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        var ev = new EmbeddedComputerHostActivatedEvent(ent.Owner, args.User);
        RaiseLocalEvent(ent.Owner, ref ev);
    }

    private void AddActivationVerbs(Entity<EmbeddedComputerHostComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("verb-common-open-ui"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/open.svg.192dpi.png")),
            Act = () =>
            {
                var ev = new EmbeddedComputerHostActivatedEvent(ent.Owner, user);
                RaiseLocalEvent(ent.Owner, ref ev);
            }
        });
    }

    private void OnHostInteractUsing(Entity<EmbeddedComputerHostComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.ComputerEntity is not { } compUid || !_computerQuery.TryComp(compUid, out var comp))
            return;

        if (!comp.IsBooted || comp.ActiveProgram is not { } activeProg)
            return;

        var relayed = new ProgramRelayedEvent<InteractUsingEvent>(compUid, args);
        RaiseLocalEvent(activeProg, ref relayed);
        args = relayed.Args;
    }

    private void OnHostAfterInteract(Entity<EmbeddedComputerHostComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || ent.Comp.ComputerEntity is not { } compUid || !_computerQuery.TryComp(compUid, out var comp))
            return;

        if (!comp.IsBooted)
            return;

        foreach (var prog in comp.RunningPrograms)
        {
            var relayed = new ProgramRelayedEvent<AfterInteractEvent>(compUid, args);
            RaiseLocalEvent(prog, ref relayed);
            args = relayed.Args;
            if (args.Handled)
                break;
        }
    }
}

[ByRefEvent]
public readonly record struct EmbeddedComputerHostActivatedEvent(EntityUid HostUid, EntityUid User);
