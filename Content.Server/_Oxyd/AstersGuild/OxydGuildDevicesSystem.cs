using Content.Shared._Oxyd.AstersGuild;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Stacks;

namespace Content.Server._Oxyd.AstersGuild;

/// <summary>
/// Interaction handlers for the small Asters money devices — Eris cash_register.dm and
/// EFTPOS.dm both used legacy browse() windows, so per the port rules they stay
/// interaction-driven (no reskin, no BUI):
///  - cash register: empty-hand opens/closes the till; swipe a credits stack to store it.
///  - EFTPOS: activate-in-hand arms a charge; swipe credits on it to pay.
/// </summary>
public sealed partial class OxydGuildDevicesSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private OxydBankSystem _bank = default!;
    [Dependency] private SharedStackSystem _stack = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydCashRegisterComponent, InteractHandEvent>(OnRegisterHand);
        SubscribeLocalEvent<OxydCashRegisterComponent, InteractUsingEvent>(OnRegisterUsing);
        SubscribeLocalEvent<OxydCashRegisterComponent, ExaminedEvent>(OnRegisterExamined);
        SubscribeLocalEvent<OxydEftposComponent, UseInHandEvent>(OnEftposUse);
        SubscribeLocalEvent<OxydEftposComponent, InteractUsingEvent>(OnEftposUsing);
        SubscribeLocalEvent<OxydEftposComponent, ExaminedEvent>(OnEftposExamined);
    }

    /// <summary>Eris attack_hand: open/close the till drawer.</summary>
    private void OnRegisterHand(EntityUid uid, OxydCashRegisterComponent comp, InteractHandEvent args)
    {
        comp.DrawerOpen = !comp.DrawerOpen;
        _popup.PopupEntity(comp.DrawerOpen
                ? $"You open the cash drawer — {comp.Stored}cr inside."
                : "You close the cash drawer.",
            uid, args.User);
        args.Handled = true;
    }

    /// <summary>Eris attackby with cash: drop the stack into the open till.</summary>
    private void OnRegisterUsing(EntityUid uid, OxydCashRegisterComponent comp, InteractUsingEvent args)
    {
        if (!TryComp(args.Used, out StackComponent? stack) || stack.StackTypeId != "OxydCredits")
            return;
        if (!comp.DrawerOpen)
        {
            _popup.PopupEntity("The drawer is closed.", uid, args.User);
            return;
        }
        comp.Stored += stack.Count;
        _popup.PopupEntity($"You drop {stack.Count}cr into the till.", uid, args.User);
        Del(args.Used);
        args.Handled = true;
    }

    private void OnRegisterExamined(EntityUid uid, OxydCashRegisterComponent comp, ExaminedEvent args)
    {
        args.PushMarkup(comp.DrawerOpen
            ? $"The drawer is open — {comp.Stored}cr inside."
            : "The drawer is closed.");
    }

    /// <summary>Eris EFTPOS dial: each activate-in-hand cycles the armed charge.</summary>
    private void OnEftposUse(EntityUid uid, OxydEftposComponent comp, UseInHandEvent args)
    {
        comp.Charge = comp.Charge switch { 0 => 100, 100 => 500, _ => 0 };
        comp.Charging = comp.Charge > 0;
        _popup.PopupEntity(comp.Charging
                ? $"EFTPOS armed — charging {comp.Charge}cr on next swipe."
                : "EFTPOS idle.",
            uid, args.User);
        args.Handled = true;
    }

    /// <summary>Eris swipe: pay the armed charge with a held credits stack into the guild
    /// account (EFTPOS.dm card swipe — cash-based in this port).</summary>
    private void OnEftposUsing(EntityUid uid, OxydEftposComponent comp, InteractUsingEvent args)
    {
        if (!comp.Charging || comp.Charge <= 0)
            return;
        if (!TryComp(args.Used, out StackComponent? stack) || stack.StackTypeId != "OxydCredits")
            return;
        if (stack.Count < comp.Charge)
        {
            _popup.PopupEntity($"Insufficient credits — need {comp.Charge}cr.", uid, args.User);
            return;
        }
        if (stack.Count == comp.Charge)
            Del(args.Used);
        else
            _stack.SetCount((args.Used, stack), stack.Count - comp.Charge);
        _bank.Credit(comp.Charge);
        _popup.PopupEntity($"Charged {comp.Charge}cr to the guild account.", uid, args.User);
        comp.Charge = 0;
        comp.Charging = false;
        args.Handled = true;
    }

    private void OnEftposExamined(EntityUid uid, OxydEftposComponent comp, ExaminedEvent args)
    {
        args.PushMarkup(comp.Charging
            ? $"Charging {comp.Charge}cr — swipe credits to pay."
            : "Idle — activate in hand to set a charge.");
    }
}
