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
            host.MountBootProgram(boot.Control);
    }

    public void UnregisterDisplayHost(EntityUid compUid, IComputerDisplayHost host)
    {
        if (_displays.TryGetValue(compUid, out var existing) && existing == host)
            _displays.Remove(compUid);
    }

    public void RegisterProgramUi(EntityUid compUid, EntityUid progUid, bool isBootProgram, ProgramUiControl control)
    {
        if (isBootProgram)
        {
            _bootUis[compUid] = (progUid, control);
            if (_displays.TryGetValue(compUid, out var display))
                display.MountBootProgram(control);

            // Attach any child program UIs that opened slightly before the boot OS BUI finished opening.
            if (control is IOsProgramUiHost osHost && _childUis.TryGetValue(compUid, out var children))
            {
                foreach (var (cProgUid, cCtrl) in children.ToList())
                {
                    if (!cCtrl.Disposed)
                        osHost.AttachChildProgram(cProgUid, cCtrl);
                }
            }
            return;
        }

        if (!_childUis.TryGetValue(compUid, out var dict))
        {
            dict = new Dictionary<EntityUid, ProgramUiControl>();
            _childUis[compUid] = dict;
        }

        dict[progUid] = control;

        if (_bootUis.TryGetValue(compUid, out var bootInfo) && bootInfo.Control is IOsProgramUiHost host)
            host.AttachChildProgram(progUid, control);
    }

    public void UnregisterProgramUi(EntityUid compUid, EntityUid progUid, ProgramUiControl control)
    {
        if (_bootUis.TryGetValue(compUid, out var bootInfo) && bootInfo.Control == control)
        {
            if (_displays.TryGetValue(compUid, out var display))
                display.UnmountBootProgram(control);
            _bootUis.Remove(compUid);
        }

        if (_childUis.TryGetValue(compUid, out var dict))
        {
            dict.Remove(progUid);
            if (_bootUis.TryGetValue(compUid, out var b) && b.Control is IOsProgramUiHost osHost)
                osHost.DetachChildProgram(progUid, control);

            if (dict.Count == 0)
                _childUis.Remove(compUid);
        }
    }

    public ProgramUiControl? GetBootControl(EntityUid compUid) =>
        _bootUis.TryGetValue(compUid, out var info) ? info.Control : null;

    public ProgramUiControl? GetChildControl(EntityUid compUid, EntityUid progUid) =>
        _childUis.TryGetValue(compUid, out var dict) && dict.TryGetValue(progUid, out var ctrl) ? ctrl : null;
}
