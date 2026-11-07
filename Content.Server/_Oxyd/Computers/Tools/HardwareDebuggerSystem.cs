using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Tools;
using Content.Shared.Interaction;
using Content.Shared.Popups;

namespace Content.Server._Oxyd.Computers.Tools;

public sealed partial class HardwareDebuggerSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HardwareDebuggerComponent, AfterInteractEvent>(OnAfterInteract);
    }

    private void OnAfterInteract(EntityUid uid, HardwareDebuggerComponent comp, AfterInteractEvent args)
    {
        if (args.Target is not { } target || !args.CanReach)
            return;

        if (TryComp<ComputerStatsComponent>(target, out var stats))
        {
            var msg = $"[DIAGNOSTICS] Target: {ToPrettyString(target)}\n" +
                      $"CPU: {stats.CpuFrequencyMhz} MHz | RAM: {stats.RamMb} MB | Storage: {stats.StorageBytes / 1024} KB | DirectAccessOnly: {stats.DirectHardwareAccessOnly}";
            _popup.PopupEntity(msg, target, args.User);
            args.Handled = true;
        }
    }
}
