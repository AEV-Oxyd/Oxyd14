using System.Text;
using Content.Server.CartridgeLoader.Cartridges;
using Content.Server.Cargo.Systems;
using Content.Server.DeviceLinking.Components;
using Content.Server.DeviceLinking.Systems;
using Content.Server.DeviceNetwork.Components;
using Content.Server.Medical.Components;
using Content.Shared._Oxyd.Pda;
using Content.Shared.Atmos.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.DeviceLinking;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Pda;

/// <summary>Server modular PDA — wireless range, signaller/scanner sync, hardware report.</summary>
public sealed partial class OxydModularPdaSystem : SharedOxydModularPdaSystem
{
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private DeviceLinkSystem _deviceLink = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private CartridgeLoaderSystem _cartridges = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydModularPdaComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbs);
        SubscribeLocalEvent<OxydModularPdaComponent, AfterInteractEvent>(OnAfterInteract);
    }

    protected override void AfterRecalculate(Entity<OxydModularPdaComponent> ent)
    {
        if (TryComp<WirelessNetworkComponent>(ent, out var wireless))
            wireless.Range = ent.Comp.EffectiveNetworkRange;

        SyncSignallerHardware(ent);
        SyncScannerHardware(ent);
    }

    private void SyncSignallerHardware(Entity<OxydModularPdaComponent> ent)
    {
        if (ent.Comp.HasSignaller)
        {
            var signaller = EnsureComp<SignallerComponent>(ent);
            EnsureComp<DeviceLinkSourceComponent>(ent);
            _deviceLink.EnsureSourcePorts(ent.Owner, signaller.Port);
        }
        else
        {
            RemComp<SignallerComponent>(ent.Owner);
        }
    }

    private void SyncScannerHardware(Entity<OxydModularPdaComponent> ent)
    {
        var hasMedTek = _cartridges.HasProgram<MedTekCartridgeComponent>(ent.Owner);

        switch (ent.Comp.ActiveScanner)
        {
            case OxydScannerKind.Medical:
                EnsureComp<HealthAnalyzerComponent>(ent);
                RemCompDeferred<GasAnalyzerComponent>(ent.Owner);
                break;
            case OxydScannerKind.Atmos:
                EnsureComp<GasAnalyzerComponent>(ent);
                if (!hasMedTek)
                    RemCompDeferred<HealthAnalyzerComponent>(ent.Owner);
                break;
            default:
                if (!hasMedTek)
                    RemCompDeferred<HealthAnalyzerComponent>(ent.Owner);
                RemCompDeferred<GasAnalyzerComponent>(ent.Owner);
                break;
        }
    }

    private void OnAfterInteract(Entity<OxydModularPdaComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        switch (ent.Comp.ActiveScanner)
        {
            case OxydScannerKind.Price:
            {
                var price = _pricing.GetPrice(target);
                var name = MetaData(target).EntityName;
                ent.Comp.ScanBuffer = $"PRICE SCAN\n{name}: {price:0} cr";
                Dirty(ent);
                _popup.PopupEntity($"{name}: {price:0} cr", ent, args.User);
                args.Handled = true;
                break;
            }
            case OxydScannerKind.Reagent:
            {
                var sb = new StringBuilder("REAGENT SCAN\n");
                sb.AppendLine(MetaData(target).EntityName);
                if (TryComp<SolutionManagerComponent>(target, out var manager))
                {
                    foreach (var (id, sol) in _solutions.EnumerateSolutions((target, manager)))
                    {
                        sb.AppendLine($"[{id ?? "?"}] vol={sol.Comp.Solution.Volume}");
                        foreach (var (reagent, qty) in sol.Comp.Solution.Contents)
                            sb.AppendLine($"  {reagent.Prototype}: {qty}");
                    }
                }
                else
                {
                    sb.AppendLine("(no reagent containers)");
                }

                ent.Comp.ScanBuffer = sb.ToString();
                Dirty(ent);
                _popup.PopupEntity("Reagent scan stored.", ent, args.User);
                args.Handled = true;
                break;
            }
        }
    }

    private void OnVerbs(Entity<OxydModularPdaComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = "Hardware report",
            Act = () => PrintHardwareReport(ent, user),
            Priority = -2,
        });
    }

    private void PrintHardwareReport(Entity<OxydModularPdaComponent> ent, EntityUid user)
    {
        // Rate-limit: printing must not be a paper dispenser.
        var now = _timing.CurTime;
        if (now < ent.Comp.NextPrintAt)
        {
            _popup.PopupEntity("Printer is recharging…", ent, user);
            return;
        }
        ent.Comp.NextPrintAt = now + TimeSpan.FromSeconds(30);

        var paper = Spawn("Paper", Transform(ent).Coordinates);
        _meta.SetEntityName(paper, "PDA hardware report");
        _paper.SetContent(paper, DescribeHardware(ent));
        _popup.PopupEntity("Hardware report printed.", ent, user);
    }
}
