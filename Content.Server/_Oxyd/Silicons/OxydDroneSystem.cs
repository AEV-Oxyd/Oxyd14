using Content.Shared._Oxyd.Silicons;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Silicons;

/// <summary>
/// Eris drone_fabricator + maintenance drone shell (objects/machinery/
/// drone_fabricator.dm, objects/item/silicon/drone_shell.dm): the fabricator
/// deposits folded shells on a cooldown; a shell unfolds into a non-player
/// NPC drone (the fork has no inhabitable silicon drone stack).
/// </summary>
public sealed partial class OxydDroneSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    [SubscribeLocalEvent]
    private void OnDroneFabricate(Entity<OxydDroneFabricatorComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        if (_timing.CurTime < ent.Comp.NextUse)
        {
            _popup.PopupEntity("The fabricator is still assembling a shell.", ent, args.User);
            return;
        }
        ent.Comp.NextUse = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.CooldownSeconds);
        Spawn(ent.Comp.ShellProto, Transform(ent).Coordinates);
        _popup.PopupEntity("The fabricator deposits a folded drone shell.", ent, args.User);
    }

    /// <summary>Eris maint drone folded form; unfolds into a non-player NPC —
    /// inhabited drones need a silicon stack the fork doesn't have.</summary>
    [SubscribeLocalEvent]
    private void OnDroneDeploy(Entity<OxydMaintenanceDroneShellComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        Spawn(ent.Comp.DroneProto, Transform(args.User).Coordinates);
        _popup.PopupEntity("The shell unfolds into a maintenance drone.", args.User, args.User);
        QueueDel(ent);
    }
}
