using System.Linq;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Computers.UI;

/// <summary>
/// Implemented by ComputerDisplayWindow so the Boot program UI control can take total ownership
/// of the display viewport.
/// </summary>
public interface IComputerDisplayHost
{
    void MountBootProgram(ProgramUiControl control);
    void UnmountBootProgram(ProgramUiControl control);
}

/// <summary>
/// Implemented by OS boot programs (such as OsUiControl and EmbeddedOsUiControl) so they can
/// parent target program UI controls inside their own UI viewport while retaining full UI control.
/// </summary>
public interface IOsProgramUiHost
{
    void AttachChildProgram(EntityUid programUid, ProgramUiControl control);
    void DetachChildProgram(EntityUid programUid, ProgramUiControl control);
    void DetachAllChildPrograms();
}

/// <summary>
/// Coordinates client-side parenting between Computer Display BUIs, Boot Program BUIs, and Child Program BUIs,
/// ensuring entity-based BUIs attach cleanly in any network packet arrival order.
/// </summary>
public sealed class ClientComputerUiSystem : EntitySystem
{
    private readonly Dictionary<EntityUid, IComputerDisplayHost> _displays = new();
    private readonly Dictionary<EntityUid, (EntityUid ProgUid, ProgramUiControl Control)> _bootUis = new();
    private readonly Dictionary<EntityUid, Dictionary<EntityUid, ProgramUiControl>> _childUis = new();

    public void RegisterDisplayHost(EntityUid compUid, IComputerDisplayHost host)
    {
        _displays[compUid] = host;
        if (_bootUis.TryGetValue(compUid, out var boot))
        {
            if (boot.Control.Disposed)
                _bootUis.Remove(compUid);
            else
                host.MountBootProgram(boot.Control);
        }
    }

    public void UnregisterDisplayHost(EntityUid compUid, IComputerDisplayHost host)
    {
        if (_bootUis.TryGetValue(compUid, out var boot) && !boot.Control.Disposed)
            host.UnmountBootProgram(boot.Control);

        if (_displays.TryGetValue(compUid, out var existing) && existing == host)
            _displays.Remove(compUid);
    }

    public void RegisterProgramUi(EntityUid compUid, EntityUid progUid, bool isBootProgram, ProgramUiControl control)
    {
        if (control.Disposed)
            return;

        if (isBootProgram)
        {
            if (_bootUis.TryGetValue(compUid, out var oldBoot) && oldBoot.Control != control)
            {
                if (_displays.TryGetValue(compUid, out var oldDisp) && !oldBoot.Control.Disposed)
                    oldDisp.UnmountBootProgram(oldBoot.Control);
                if (oldBoot.Control is IOsProgramUiHost oldOs && !oldBoot.Control.Disposed)
                    oldOs.DetachAllChildPrograms();
            }

            _bootUis[compUid] = (progUid, control);
            if (_displays.TryGetValue(compUid, out var display))
                display.MountBootProgram(control);

            // Attach any child program UIs that opened slightly before the boot OS BUI finished opening.
            if (control is IOsProgramUiHost osHost && _childUis.TryGetValue(compUid, out var children))
            {
                foreach (var (cProgUid, cCtrl) in children.ToList())
                {
                    if (cCtrl.Disposed)
                    {
                        children.Remove(cProgUid);
                        continue;
                    }

                    osHost.AttachChildProgram(cProgUid, cCtrl);
                }

                if (children.Count == 0)
                    _childUis.Remove(compUid);
            }
            return;
        }

        if (!_childUis.TryGetValue(compUid, out var dict))
        {
            dict = new Dictionary<EntityUid, ProgramUiControl>();
            _childUis[compUid] = dict;
        }

        if (dict.TryGetValue(progUid, out var oldChild) && oldChild != control)
        {
            if (_bootUis.TryGetValue(compUid, out var existingBoot) && !existingBoot.Control.Disposed && existingBoot.Control is IOsProgramUiHost existingHost)
                existingHost.DetachChildProgram(progUid, oldChild);
        }

        dict[progUid] = control;

        if (_bootUis.TryGetValue(compUid, out var bootInfo))
        {
            if (bootInfo.Control.Disposed)
                _bootUis.Remove(compUid);
            else if (bootInfo.Control is IOsProgramUiHost host)
                host.AttachChildProgram(progUid, control);
        }
    }

    public void UnregisterProgramUi(EntityUid compUid, EntityUid progUid, ProgramUiControl control)
    {
        if (_bootUis.TryGetValue(compUid, out var bootInfo) && (bootInfo.Control == control || bootInfo.Control.Disposed))
        {
            if (bootInfo.Control is IOsProgramUiHost osHost && !bootInfo.Control.Disposed)
                osHost.DetachAllChildPrograms();

            if (_displays.TryGetValue(compUid, out var display))
                display.UnmountBootProgram(bootInfo.Control);

            _bootUis.Remove(compUid);
        }

        if (_childUis.TryGetValue(compUid, out var dict))
        {
            dict.Remove(progUid);
            if (_bootUis.TryGetValue(compUid, out var b) && !b.Control.Disposed && b.Control is IOsProgramUiHost osHost)
                osHost.DetachChildProgram(progUid, control);

            if (dict.Count == 0)
                _childUis.Remove(compUid);
        }
    }

    public ProgramUiControl? GetBootControl(EntityUid compUid)
    {
        if (!_bootUis.TryGetValue(compUid, out var info))
            return null;

        if (info.Control.Disposed)
        {
            _bootUis.Remove(compUid);
            return null;
        }

        return info.Control;
    }

    public ProgramUiControl? GetChildControl(EntityUid compUid, EntityUid progUid)
    {
        if (!_childUis.TryGetValue(compUid, out var dict) || !dict.TryGetValue(progUid, out var ctrl))
            return null;

        if (ctrl.Disposed)
        {
            dict.Remove(progUid);
            if (dict.Count == 0)
                _childUis.Remove(compUid);
            return null;
        }

        return ctrl;
    }
}
