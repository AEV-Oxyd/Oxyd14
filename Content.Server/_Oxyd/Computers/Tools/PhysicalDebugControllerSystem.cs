using System.Linq;
using Content.Server._Oxyd.Computers.Programs;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared._Oxyd.Computers.Tools;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Oxyd.Computers.Tools;

/// <summary>
/// Handles physical JTAG/data-port linking for the handheld <see cref="PhysicalDebugControllerComponent"/>.
/// Clicking any computer or <see cref="EmbeddedComputerHostComponent"/> attaches the controller's
/// local Terminal directly to the target entity and opens the terminal UI.
/// </summary>
public sealed partial class PhysicalDebugControllerSystem : EntitySystem
{
    [Dependency] private OxydComputerSystem _computer = default!;
    [Dependency] private TerminalSystem _terminal = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    [Dependency] private EntityQuery<EmbeddedComputerHostComponent> _hostQuery = default!;
    [Dependency] private EntityQuery<OxydComputerComponent> _computerQuery = default!;
    [Dependency] private EntityQuery<TerminalProgramComponent> _terminalQuery = default!;

    private static readonly SoundPathSpecifier LinkSound = new("/Audio/Machines/terminal_insert_disc.ogg");

    [SubscribeLocalEvent]
    private void OnAfterInteract(Entity<PhysicalDebugControllerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { Valid: true } target)
            return;

        if (target == ent.Owner)
            return;

        EntityUid targetCompUid;
        if (_hostQuery.TryComp(target, out var targetHost))
        {
            if (!_computer.EnsureComputerBoundToHost((target, targetHost), out targetCompUid))
                return;
        }
        else if (_computerQuery.HasComp(target))
        {
            targetCompUid = target;
        }
        else
        {
            return;
        }

        if (!_hostQuery.TryComp(ent.Owner, out var controllerHost) ||
            !_computer.EnsureComputerBoundToHost((ent.Owner, controllerHost), out var localCompUid) ||
            !_computerQuery.TryComp(localCompUid, out var localComp))
        {
            return;
        }

        if (!localComp.IsBooted)
            _computer.RefreshComputerBootState(localCompUid, localComp);

        var termProgUid = ResolveTerminalProgram(localCompUid, localComp);
        if (termProgUid == null)
        {
            _popup.PopupEntity("Debug controller terminal is not running.", ent.Owner, args.User);
            args.Handled = true;
            return;
        }

        if (!_terminal.ConnectPhysicalTarget(
                termProgUid.Value,
                localCompUid,
                targetCompUid,
                args.User,
                ent.Comp.MaxPhysicalRange,
                out var message))
        {
            _popup.PopupEntity(message, target, args.User);
            args.Handled = true;
            return;
        }

        ent.Comp.LinkedTargetComputer = targetCompUid;
        Dirty(ent);

        _audio.PlayPvs(LinkSound, target);
        _popup.PopupEntity(message, target, args.User);
        _computer.OpenComputerForActor(localCompUid, args.User, localComp);
        args.Handled = true;
    }

    private EntityUid? ResolveTerminalProgram(EntityUid compUid, OxydComputerComponent comp)
    {
        if (comp.BootProgram is { Valid: true } bootUid && Exists(bootUid) && _terminalQuery.HasComp(bootUid))
            return bootUid;

        foreach (var progUid in comp.RunningPrograms)
        {
            if (Exists(progUid) && _terminalQuery.HasComp(progUid))
                return progUid;
        }

        if (_computer.TryLaunchExecutable(compUid, "/bin/terminal.nte", out var launched, out _, comp: comp) &&
            launched is { Valid: true } launchedUid &&
            _terminalQuery.HasComp(launchedUid))
        {
            return launchedUid;
        }

        return null;
    }
}
