using Content.Shared._Oxyd.Ntos;
using Content.Shared._Oxyd.Pda;
using Content.Shared.CartridgeLoader;
using Content.Shared.Paper;
using Content.Shared.Popups;

namespace Content.Server._Oxyd.Ntos;

/// <summary>Eris scanner program — shows installed scanner HW + last buffer; print via mini printer.</summary>
public sealed partial class OxydScannerCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridges = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MetaDataSystem _meta = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydScannerCartridgeComponent, CartridgeUiReadyEvent>(OnReady);
        SubscribeLocalEvent<OxydScannerCartridgeComponent, CartridgeMessageEvent>(OnMessage);
    }

    private void OnReady(EntityUid uid, OxydScannerCartridgeComponent component, CartridgeUiReadyEvent args)
        => Push(args.Loader);

    private void OnMessage(EntityUid uid, OxydScannerCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not OxydScannerUiMessageEvent msg)
            return;

        var loader = GetEntity(args.LoaderUid);
        string? error = null;
        string? status = null;

        if (!TryComp<OxydModularPdaComponent>(loader, out var mod))
        {
            Push(loader, "Not a modular PDA");
            return;
        }

        switch (msg.Action)
        {
            case OxydScannerUiAction.Clear:
                mod.ScanBuffer = string.Empty;
                Dirty(loader, mod);
                status = "Buffer cleared";
                break;
            case OxydScannerUiAction.Print:
                if (!mod.HasPrinter)
                    error = "Printer hardware required";
                else if (string.IsNullOrWhiteSpace(mod.ScanBuffer))
                    error = "Nothing to print";
                else
                {
                    var paper = Spawn("Paper", Transform(loader).Coordinates);
                    _meta.SetEntityName(paper, "Scanner report");
                    _paper.SetContent(paper, mod.ScanBuffer);
                    status = "Printed";
                    _popup.PopupEntity("Scan printed.", loader, args.Actor);
                }
                break;
        }

        Push(loader, error, status);
    }

    private void Push(EntityUid loader, string? error = null, string? status = null)
    {
        TryComp<OxydModularPdaComponent>(loader, out var mod);
        var state = new OxydScannerUiState
        {
            ScannerKind = (mod?.ActiveScanner ?? OxydScannerKind.None).ToString().ToLowerInvariant(),
            Buffer = mod?.ScanBuffer ?? string.Empty,
            CanPrint = mod?.HasPrinter == true,
            Error = error,
            Status = status,
        };
        _cartridges.UpdateCartridgeUiState(loader, state);
    }
}
