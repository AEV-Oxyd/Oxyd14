using Content.Shared.CartridgeLoader;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Tag;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Pda;

/// <summary>
/// Shared half of Eris modular PDA hardware — installs CPU/HDD/net/peripheral and drives DiskSpace.
/// </summary>
public abstract partial class SharedOxydModularPdaSystem : EntitySystem
{
    private static readonly ProtoId<TagPrototype> TagCpu = "OxydHwCpu";
    private static readonly ProtoId<TagPrototype> TagHdd = "OxydHwHdd";
    private static readonly ProtoId<TagPrototype> TagNet = "OxydHwNet";
    private static readonly ProtoId<TagPrototype> TagPeripheral = "OxydHwPeripheral";

    [Dependency] protected readonly ItemSlotsSystem Slots = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydModularPdaComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<OxydModularPdaComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<OxydModularPdaComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<OxydModularPdaComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<OxydModularPdaComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<OxydModularPdaComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<OxydComputerHardwareComponent, ExaminedEvent>(OnHwExamined);
    }

    private void OnInit(Entity<OxydModularPdaComponent> ent, ref ComponentInit args)
    {
        ConfigureSlot(ent.Comp.CpuSlot, TagCpu, ent.Comp.StartingCpu, "CPU");
        ConfigureSlot(ent.Comp.HddSlot, TagHdd, ent.Comp.StartingHdd, "HDD");
        ConfigureSlot(ent.Comp.NetSlot, TagNet, ent.Comp.StartingNetwork, "Network");
        ConfigureSlot(ent.Comp.PeripheralSlot, TagPeripheral, ent.Comp.StartingPeripheral, "Peripheral");

        Slots.AddItemSlot(ent.Owner, OxydModularPdaComponent.CpuSlotId, ent.Comp.CpuSlot);
        Slots.AddItemSlot(ent.Owner, OxydModularPdaComponent.HddSlotId, ent.Comp.HddSlot);
        Slots.AddItemSlot(ent.Owner, OxydModularPdaComponent.NetSlotId, ent.Comp.NetSlot);
        Slots.AddItemSlot(ent.Owner, OxydModularPdaComponent.PeripheralSlotId, ent.Comp.PeripheralSlot);
    }

    private void OnMapInit(Entity<OxydModularPdaComponent> ent, ref MapInitEvent args)
    {
        Recalculate(ent);
    }

    private void OnRemove(Entity<OxydModularPdaComponent> ent, ref ComponentRemove args)
    {
        Slots.RemoveItemSlot(ent.Owner, ent.Comp.CpuSlot);
        Slots.RemoveItemSlot(ent.Owner, ent.Comp.HddSlot);
        Slots.RemoveItemSlot(ent.Owner, ent.Comp.NetSlot);
        Slots.RemoveItemSlot(ent.Owner, ent.Comp.PeripheralSlot);
    }

    private void OnInserted(Entity<OxydModularPdaComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (!IsHwSlot(args.Container.ID))
            return;

        if (TryComp(args.Entity, out OxydComputerHardwareComponent? hw) &&
            !SlotMatches(args.Container.ID, hw.Slot))
        {
            Slots.TryEject(ent.Owner, args.Container.ID, user: null, out _);
            return;
        }

        Recalculate(ent);
    }

    private void OnRemoved(Entity<OxydModularPdaComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (!IsHwSlot(args.Container.ID))
            return;

        Recalculate(ent);
    }

    private void OnExamined(Entity<OxydModularPdaComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(
            $"[color=cyan]Modular bay[/color]: disk {ent.Comp.EffectiveDiskSpace}, CPU threads {ent.Comp.EffectiveMaxPrograms}, net {ent.Comp.EffectiveNetworkRange}m"
            + (ent.Comp.HasTeslaLink ? ", tesla-link" : string.Empty)
            + (ent.Comp.HasPrinter ? ", printer" : string.Empty)
            + $", draw {ent.Comp.TotalPowerUsage}W");
    }

    private void OnHwExamined(Entity<OxydComputerHardwareComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var hw = ent.Comp;
        args.PushMarkup(
            $"[color=yellow]Slot[/color]: {hw.Slot}"
            + (hw.DiskCapacity > 0 ? $", capacity {hw.DiskCapacity}" : string.Empty)
            + (hw.DiskBonus > 0 ? $", disk +{hw.DiskBonus}" : string.Empty)
            + (hw.MaxPrograms > 0 ? $", programs {hw.MaxPrograms}" : string.Empty)
            + (hw.NetworkRange > 0 ? $", range {hw.NetworkRange}m" : string.Empty)
            + (hw.PowerUsage > 0 ? $", {hw.PowerUsage}W" : string.Empty));
    }

    public virtual void Recalculate(Entity<OxydModularPdaComponent> ent)
    {
        var cpu = GetHw(ent, OxydModularPdaComponent.CpuSlotId);
        var hdd = GetHw(ent, OxydModularPdaComponent.HddSlotId);
        var net = GetHw(ent, OxydModularPdaComponent.NetSlotId);
        var peri = GetHw(ent, OxydModularPdaComponent.PeripheralSlotId);

        var disk = hdd?.DiskCapacity > 0 ? hdd.DiskCapacity : ent.Comp.BaseDiskSpace;
        disk += cpu?.DiskBonus ?? 0;

        ent.Comp.EffectiveDiskSpace = Math.Max(1, disk);
        ent.Comp.EffectiveMaxPrograms = Math.Max(1, cpu?.MaxPrograms ?? 1);
        ent.Comp.EffectiveNetworkRange = net is { NetworkRange: > 0 }
            ? net.NetworkRange
            : ent.Comp.DefaultNetworkRange;
        if (net is { LongRange: true })
            ent.Comp.EffectiveNetworkRange = Math.Max(ent.Comp.EffectiveNetworkRange, 2000);

        ent.Comp.HasTeslaLink = peri?.TeslaLink == true;
        ent.Comp.HasPrinter = peri?.Printer == true;
        ent.Comp.HasSignaller = peri?.Signaller == true;
        ent.Comp.ActiveScanner = peri?.Scanner ?? OxydScannerKind.None;
        ent.Comp.TotalPowerUsage =
            (cpu?.PowerUsage ?? 0) + (hdd?.PowerUsage ?? 0) + (net?.PowerUsage ?? 0) + (peri?.PowerUsage ?? 0);

        if (TryComp<CartridgeLoaderComponent>(ent, out var loader))
        {
            loader.DiskSpace = ent.Comp.EffectiveDiskSpace;
            Dirty(ent.Owner, loader);
        }

        Dirty(ent);
        AfterRecalculate(ent);
    }

    /// <summary>Server applies wireless range etc.</summary>
    protected virtual void AfterRecalculate(Entity<OxydModularPdaComponent> ent)
    {
    }

    public string DescribeHardware(Entity<OxydModularPdaComponent> ent)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Modular PDA hardware — disk {ent.Comp.EffectiveDiskSpace}, threads {ent.Comp.EffectiveMaxPrograms}");
        AppendSlot(sb, "CPU", ent, OxydModularPdaComponent.CpuSlotId);
        AppendSlot(sb, "HDD", ent, OxydModularPdaComponent.HddSlotId);
        AppendSlot(sb, "NET", ent, OxydModularPdaComponent.NetSlotId);
        AppendSlot(sb, "PER", ent, OxydModularPdaComponent.PeripheralSlotId);
        sb.AppendLine($"Net range {ent.Comp.EffectiveNetworkRange}m | draw {ent.Comp.TotalPowerUsage}W"
                      + (ent.Comp.HasTeslaLink ? " | tesla-link" : string.Empty)
                      + (ent.Comp.HasPrinter ? " | printer" : string.Empty)
                      + (ent.Comp.HasSignaller ? " | signaller" : string.Empty)
                      + (ent.Comp.ActiveScanner != OxydScannerKind.None
                          ? $" | scanner={ent.Comp.ActiveScanner}"
                          : string.Empty));
        return sb.ToString();
    }

    private void AppendSlot(System.Text.StringBuilder sb, string label, Entity<OxydModularPdaComponent> ent, string slotId)
    {
        var item = Slots.GetItemOrNull(ent.Owner, slotId);
        if (item == null)
        {
            sb.AppendLine($"{label}: — empty —");
            return;
        }

        sb.AppendLine($"{label}: {MetaData(item.Value).EntityName}");
    }

    protected OxydComputerHardwareComponent? GetHw(EntityUid pda, string slotId)
    {
        var item = SoftsGetItem(pda, slotId);
        return item != null && TryComp(item.Value, out OxydComputerHardwareComponent? hw) ? hw : null;
    }

    private EntityUid? SoftsGetItem(EntityUid pda, string slotId) => Slots.GetItemOrNull(pda, slotId);

    private static void ConfigureSlot(ItemSlot slot, ProtoId<TagPrototype> tag, EntProtoId? starting, string name)
    {
        slot.Name = name;
        slot.StartingItem = starting;
        slot.Whitelist = new EntityWhitelist
        {
            Tags = new List<ProtoId<TagPrototype>> { tag },
        };
    }

    private static bool IsHwSlot(string id) =>
        id is OxydModularPdaComponent.CpuSlotId
            or OxydModularPdaComponent.HddSlotId
            or OxydModularPdaComponent.NetSlotId
            or OxydModularPdaComponent.PeripheralSlotId;

    private static bool SlotMatches(string containerId, OxydHardwareSlot kind) =>
        (containerId, kind) switch
        {
            (OxydModularPdaComponent.CpuSlotId, OxydHardwareSlot.Cpu) => true,
            (OxydModularPdaComponent.HddSlotId, OxydHardwareSlot.Hdd) => true,
            (OxydModularPdaComponent.NetSlotId, OxydHardwareSlot.Network) => true,
            (OxydModularPdaComponent.PeripheralSlotId, OxydHardwareSlot.Peripheral) => true,
            _ => false,
        };
}
