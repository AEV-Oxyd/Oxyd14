using System.Linq;
using System.Numerics;
using Content.Client._Oxyd.Computers.UI;
using Content.Client.Lathe;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Content.Shared.Stacks;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Oxyd.Computers.Programs;

// =============================================================================
// APC Control UI (via DeviceLink)
// =============================================================================

public sealed class ApcControlUiControl : ProgramUiControl
{
    private readonly ContentLabel _telemetryLabel;
    private readonly ContentLabel _statusLabel;
    private readonly List<ContentButton> _controlButtons = new();

    public ApcControlUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new Thickness(6),
            SeparationOverride = 6
        };

        var telemetryPanel = new ContentPanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0F1A26"),
                BorderColor = Color.FromHex("#1F4B66"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6
            }
        };

        _telemetryLabel = new ContentLabel
        {
            Text = "Waiting for telemetry...",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#E0F7FA")
        };
        telemetryPanel.AddChild(_telemetryLabel);

        _statusLabel = new ContentLabel
        {
            Text = "",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#88CCCC"),
            Visible = false
        };

        var btnRow = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4 };
        void AddBtn(string text, ApcControlAction action)
        {
            var b = new ContentButton { Text = text, Disabled = true };
            b.OnPressed += _ => SendProgramMessage(new ApcControlActionMessage(action));
            _controlButtons.Add(b);
            btnRow.AddChild(b);
        }

        AddBtn("Toggle Breaker", ApcControlAction.ToggleBreaker);

        root.AddChild(telemetryPanel);
        root.AddChild(btnRow);
        root.AddChild(_statusLabel);
        scroll.AddChild(root);
        AddChild(scroll);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not ApcControlUiState state)
            return;

        var trippedTag = state.BreakerTripped ? " [TRIPPED]" : string.Empty;
        _telemetryLabel.Text =
            $"Power: {(state.MainBreakerEnabled ? "ON" : "OFF")}{trippedTag}\n" +
            $"Battery Charge: {state.ChargePercent:0.0}%\n" +
            $"Current Load: {state.LoadWatts} W\n" +
            $"External Supply: {state.ExternalPowerState}";

        var showStatus = !state.Locked && !string.IsNullOrWhiteSpace(state.StatusMessage);
        _statusLabel.Text = showStatus ? state.StatusMessage : string.Empty;
        _statusLabel.Visible = showStatus;

        foreach (var btn in _controlButtons)
            btn.Disabled = state.Locked;
    }
}

// =============================================================================
// Air Alarm Control UI (via DeviceLink)
// =============================================================================

public sealed class AtmosControlUiControl : ProgramUiControl
{
    private readonly ContentLabel _telemetryLabel;
    private readonly ContentLabel _statusLabel;
    private readonly List<ContentButton> _controlButtons = new();

    public AtmosControlUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new Thickness(6),
            SeparationOverride = 6
        };

        var telemetryPanel = new ContentPanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0F1A26"),
                BorderColor = Color.FromHex("#1F4B66"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6
            }
        };

        _telemetryLabel = new ContentLabel
        {
            Text = "Waiting for telemetry...",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#E0F7FA")
        };
        telemetryPanel.AddChild(_telemetryLabel);

        _statusLabel = new ContentLabel
        {
            Text = "",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#88CCCC"),
            Visible = false
        };

        var modeRow = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4 };
        void AddBtn(ContentWrapContainer parent, string text, AtmosControlAction action)
        {
            var b = new ContentButton { Text = text, Disabled = true };
            b.OnPressed += _ => SendProgramMessage(new AtmosControlActionMessage(action));
            _controlButtons.Add(b);
            parent.AddChild(b);
        }

        AddBtn(modeRow, "Filtering", AtmosControlAction.SetModeFiltering);
        AddBtn(modeRow, "WideFilter", AtmosControlAction.SetModeWideFiltering);
        AddBtn(modeRow, "Fill", AtmosControlAction.SetModeFill);
        AddBtn(modeRow, "Panic", AtmosControlAction.SetModePanic);
        AddBtn(modeRow, "Off", AtmosControlAction.SetModeNone);

        var ctrlRow = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4 };
        AddBtn(ctrlRow, "Toggle Auto Mode", AtmosControlAction.ToggleAutoMode);
        AddBtn(ctrlRow, "Resync Devices", AtmosControlAction.ResyncDevices);

        root.AddChild(telemetryPanel);
        root.AddChild(modeRow);
        root.AddChild(ctrlRow);
        root.AddChild(_statusLabel);
        scroll.AddChild(root);
        AddChild(scroll);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not AtmosControlUiState state)
            return;

        _telemetryLabel.Text =
            $"State: {state.AlarmLevel} | Mode: {state.CurrentMode} (Auto: {state.AutoMode})\n" +
            $"Pressure: {state.PressureKpa:0.0} kPa | Temperature: {state.TemperatureCelsius:0.0} C\n" +
            $"Devices: {state.SensorCount} sensors, {state.VentCount} vents, {state.ScrubberCount} scrubbers\n" +
            $"{state.GasSummary}";

        var showStatus = !state.Locked && !string.IsNullOrWhiteSpace(state.StatusMessage);
        _statusLabel.Text = showStatus ? state.StatusMessage : string.Empty;
        _statusLabel.Visible = showStatus;

        foreach (var btn in _controlButtons)
            btn.Disabled = state.Locked;
    }
}

// =============================================================================
// Autolathe Control Helpers (ContentControl-derived for InputRelay safety)
// =============================================================================

/// <summary>
/// Compact progress bar derived from <see cref="ContentControl"/> for fabrication progress.
/// </summary>
public sealed class ContentProgressBar : ContentControl
{
    public float Value { get; set; }
    public string OverlayText { get; set; } = string.Empty;
    public Color BackgroundColor { get; set; } = Color.FromHex("#0A121C");
    public Color BorderColor { get; set; } = Color.FromHex("#24526E");
    public Color FillColor { get; set; } = Color.FromHex("#1992B8");

    public ContentProgressBar()
    {
        MouseFilter = MouseFilterMode.Ignore;
        MinHeight = 18;
        HorizontalExpand = true;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var w = float.IsFinite(availableSize.X) ? MathF.Max(MinWidth, availableSize.X) : MathF.Max(MinWidth, 120f);
        return new Vector2(w, MathF.Max(MinHeight, 18f));
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var rect = PixelSizeBox;
        handle.DrawRect(rect, BackgroundColor, filled: true);

        var clamped = Math.Clamp(Value, 0f, 1f);
        if (clamped > 0f)
        {
            var fillW = MathF.Max(1f, (PixelWidth - 2f) * clamped);
            var fillRect = UIBox2.FromDimensions(new Vector2(1f, 1f), new Vector2(fillW, MathF.Max(1f, PixelHeight - 2f)));
            handle.DrawRect(fillRect, FillColor, filled: true);
        }

        handle.DrawRect(rect, BorderColor, filled: false);

        if (!string.IsNullOrEmpty(OverlayText))
        {
            var font = TryGetStyleProperty<Font>("font", out var styleFont) && styleFont != null
                ? styleFont
                : UserInterfaceManager.ThemeDefaults.DefaultFont;
            var dims = handle.GetDimensions(font, OverlayText, UIScale);
            var pos = new Vector2(
                MathF.Floor((PixelWidth - dims.X) * 0.5f),
                MathF.Floor((PixelHeight - dims.Y) * 0.5f));
            handle.DrawString(font, pos, OverlayText, UIScale, Color.FromHex("#F0FCFF"));
        }
    }
}

/// <summary>
/// Material icon tile showing the stored sheet count as a plain number in the corner of the icon,
/// a right-side vertical white-on-blue bar representing the selected eject amount, and mouse-wheel
/// support for adjusting how many sheets to eject on click.
/// </summary>
public sealed class MaterialIconTile : ContentButton
{
    private readonly Texture? _matIcon;
    private readonly string _materialName;
    private readonly string _sheetCountText;
    private readonly int _volumePerSheet;
    private readonly int _maxEjectable;

    public string MaterialId { get; }
    public int Amount { get; }
    public int FullSheets { get; }
    public int MaxEjectable => _maxEjectable;
    public int SelectedSheets { get; private set; }
    public string CountText => _sheetCountText;

    public event Action<int>? OnSelectedSheetsChanged;

    public MaterialIconTile(
        string materialId,
        string materialName,
        int amount,
        int volumePerSheet,
        int maxStack,
        int initialSelectedSheets,
        Texture? icon)
    {
        MaterialId = materialId;
        _materialName = materialName;
        Amount = amount;
        _volumePerSheet = Math.Max(1, volumePerSheet);
        FullSheets = amount / _volumePerSheet;
        _maxEjectable = Math.Min(Math.Max(1, maxStack), FullSheets);
        SelectedSheets = _maxEjectable > 0
            ? Math.Clamp(initialSelectedSheets, 1, _maxEjectable)
            : 0;
        _matIcon = icon;
        _sheetCountText = FormatSheetCount(amount, _volumePerSheet);

        Text = string.Empty;
        MinSize = new Vector2(54, 48);
        MaxSize = new Vector2(58, 48);
        UpdateTooltip();
    }

    private static string FormatSheetCount(int amount, int volumePerSheet)
    {
        if (amount % volumePerSheet == 0)
            return (amount / volumePerSheet).ToString();

        var sheets = amount / (float) volumePerSheet;
        return $"{sheets:0.#}";
    }

    private void UpdateTooltip()
    {
        ToolTip = _maxEjectable > 0
            ? $"{_materialName}: {_sheetCountText} sheets ({Amount} u)\nEject: {SelectedSheets} / {_maxEjectable} sheet(s) (scroll wheel)\nClick to eject"
            : $"{_materialName}: {_sheetCountText} sheets ({Amount} u)\nLess than 1 full sheet stored";
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        if (_maxEjectable <= 0 || args.Delta.Y == 0f)
            return;

        var step = args.Delta.Y > 0f ? 1 : -1;
        var next = Math.Clamp(SelectedSheets + step, 1, _maxEjectable);
        if (next != SelectedSheets)
        {
            SelectedSheets = next;
            UpdateTooltip();
            OnSelectedSheetsChanged?.Invoke(SelectedSheets);
        }

        args.Handle();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        return new Vector2(54f, 48f);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var rect = PixelSizeBox;
        var pad = 3f * UIScale;

        // Reserve space on the right side for the vertical eject-amount bar
        var barW = MathF.Max(5f, 6f * UIScale);
        var barGap = 3f * UIScale;
        var barRight = PixelWidth - pad;
        var barLeft = MathF.Max(pad + 8f, barRight - barW);
        var barTop = pad;
        var barBottom = MathF.Max(barTop + 4f, PixelHeight - pad);
        var barRect = new UIBox2(barLeft, barTop, barRight, barBottom);

        // Draw material icon centered in the left icon area
        var iconAreaW = MathF.Max(8f, barLeft - barGap - pad);
        var iconAreaH = MathF.Max(8f, PixelHeight - pad * 2f);
        if (_matIcon != null)
        {
            var iconSize = MathF.Max(8f, MathF.Min(iconAreaW, iconAreaH));
            var ix = pad + (iconAreaW - iconSize) * 0.5f;
            var iy = pad + (iconAreaH - iconSize) * 0.5f;
            handle.DrawTextureRect(_matIcon, UIBox2.FromDimensions(new Vector2(ix, iy), new Vector2(iconSize, iconSize)), Color.White);
        }

        // Draw plain white sheet count number in the bottom-left corner of the icon (always visible, not hover-only)
        var font = TryGetStyleProperty<Font>("font", out var styleFont) && styleFont != null
            ? styleFont
            : UserInterfaceManager.ThemeDefaults.DefaultFont;
        var textScale = UIScale * 0.9f;
        var textHeight = font.GetHeight(textScale);
        var tx = MathF.Floor(pad + 1f * UIScale);
        var ty = MathF.Floor(MathF.Max(pad, PixelHeight - textHeight - pad * 0.5f));
        handle.DrawString(font, new Vector2(tx, ty), _sheetCountText, textScale, Color.White, TextOutline.Default);

        // Draw right-side vertical eject-amount bar: blue background with white fill from bottom up
        handle.DrawRect(barRect, Color.FromHex("#1A55B8"), filled: true);
        if (_maxEjectable > 0 && SelectedSheets > 0)
        {
            var fraction = Math.Clamp(SelectedSheets / (float) _maxEjectable, 0f, 1f);
            var fillH = MathF.Max(2f, barRect.Height * fraction);
            var fillRect = new UIBox2(barRect.Left, barRect.Bottom - fillH, barRect.Right, barRect.Bottom);
            handle.DrawRect(fillRect, Color.White, filled: true);
        }
        handle.DrawRect(barRect, Color.FromHex("#3A7CE0"), filled: false);

        handle.DrawRect(rect, new Color(0.16f, 0.36f, 0.48f, 0.7f), filled: false);
    }
}

// =============================================================================
// Autolathe Control UI (via DeviceLink + HDD .lathe blueprints in memory)
// =============================================================================

public sealed class LatheControlUiControl : ProgramUiControl
{
    private const int MaxExpandedRecipes = 8;
    private const float EstimatedRecipeCardHeight = 58f;
    private const float BaseChromeHeight = 336f;

    public float DesiredExpandedWindowHeight { get; private set; } = 480f;

    private readonly IPrototypeManager _proto;
    private readonly IComponentFactory _compFactory;
    private readonly IGameTiming _timing;
    private readonly SpriteSystem _sprite;
    private readonly LatheSystem _lathe;
    private readonly SharedStackSystem _stack;

    private readonly ContentTextureRect _printingIcon;
    private readonly ContentLabel _printingNameLabel;
    private readonly ContentProgressBar _progressBar;
    private readonly ContentLabel _printingUsageLabel;
    private readonly ContentLabel _selectedRecipeLabel;
    private readonly ContentButton _cancelQueueBtn;
    private readonly ContentLabel _queueHeaderLabel;
    private readonly ContentWrapContainer _queueContainer;

    private readonly ContentWrapContainer _materialsContainer;
    private readonly ContentLabel _memoryLabel;
    private readonly ContentPanelContainer _diskBar;
    private readonly ContentLabel _diskLabel;
    private readonly ContentButton _importDiskBtn;
    private readonly ContentButton _ejectDiskBtn;
    private readonly ContentLineEdit _searchInput;
    private readonly ContentLabel _blueprintCountLabel;
    private readonly ContentScrollContainer _recipeScroll;
    private readonly ContentBoxContainer _recipesContainer;

    private readonly Dictionary<string, int> _ejectSelectionByMaterial = new(StringComparer.OrdinalIgnoreCase);

    private LatheControlUiState? _lastState;
    private TimeSpan? _productionStartTime;
    private TimeSpan? _productionLength;
    private bool _isPrinting;

    public LatheControlUiControl()
    {
        _proto = IoCManager.Resolve<IPrototypeManager>();
        _compFactory = IoCManager.Resolve<IComponentFactory>();
        _timing = IoCManager.Resolve<IGameTiming>();
        var entSys = IoCManager.Resolve<IEntitySystemManager>();
        _sprite = entSys.GetEntitySystem<SpriteSystem>();
        _lathe = entSys.GetEntitySystem<LatheSystem>();
        _stack = entSys.GetEntitySystem<SharedStackSystem>();

        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(6),
            SeparationOverride = 6
        };

        // ---------------------------------------------------------------------
        // 1. Currently Printing & Queue Box
        // ---------------------------------------------------------------------
        var printQueueBox = new ContentPanelContainer
        {
            Name = "LathePrintQueueBox",
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0F1A26"),
                BorderColor = Color.FromHex("#1F4B66"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6
            }
        };

        var pqInner = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4
        };

        var printingRow = new ContentBoxContainer
        {
            Name = "LatheCurrentPrintingRow",
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 8
        };

        _printingIcon = new ContentTextureRect
        {
            Name = "LatheCurrentPrintingIcon",
            DisplaySize = new Vector2(36, 36),
            MinSize = new Vector2(36, 36)
        };

        var printingInfo = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 2
        };

        _printingNameLabel = new ContentLabel
        {
            Name = "LatheCurrentPrintingName",
            Text = "Standby — Not Printing",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00D4FF")
        };

        _progressBar = new ContentProgressBar
        {
            Name = "LatheProgressBar",
            Value = 0f,
            OverlayText = "Idle"
        };

        _printingUsageLabel = new ContentLabel
        {
            Name = "LatheCurrentUsageLabel",
            Text = "Resource usage: ---",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#9AB8C8")
        };

        _selectedRecipeLabel = new ContentLabel
        {
            Name = "LatheSelectedRecipeLabel",
            Text = "Selected: None",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#66E6FF")
        };

        printingInfo.AddChild(_printingNameLabel);
        printingInfo.AddChild(_progressBar);
        printingInfo.AddChild(_printingUsageLabel);
        printingInfo.AddChild(_selectedRecipeLabel);

        _cancelQueueBtn = new ContentButton
        {
            Name = "LatheCancelQueueButton",
            Text = "Cancel",
            MinWidth = 64,
            VerticalAlignment = VAlignment.Center,
            Disabled = true
        };
        _cancelQueueBtn.OnPressed += _ => SendProgramMessage(new LatheControlActionMessage(LatheControlAction.CancelAll));

        printingRow.AddChild(_printingIcon);
        printingRow.AddChild(printingInfo);
        printingRow.AddChild(_cancelQueueBtn);

        _queueHeaderLabel = new ContentLabel
        {
            Name = "LatheQueueHeader",
            Text = "Queue: Empty",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#88CCCC"),
            Margin = new Thickness(0, 2, 0, 0)
        };

        _queueContainer = new ContentWrapContainer
        {
            Name = "LatheQueueContainer",
            HorizontalSeparation = 6,
            VerticalSeparation = 4
        };

        pqInner.AddChild(printingRow);
        pqInner.AddChild(_queueHeaderLabel);
        pqInner.AddChild(_queueContainer);
        printQueueBox.AddChild(pqInner);
        root.AddChild(printQueueBox);

        // ---------------------------------------------------------------------
        // 2. Materials Display (Icons with Semi-Transparent Overlaid Count)
        // ---------------------------------------------------------------------
        var matPanel = new ContentPanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0B141E"),
                BorderColor = Color.FromHex("#1A3B50"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 4,
                ContentMarginBottomOverride = 4
            }
        };

        _materialsContainer = new ContentWrapContainer
        {
            Name = "LatheMaterialsContainer",
            HorizontalSeparation = 6,
            VerticalSeparation = 6
        };
        matPanel.AddChild(_materialsContainer);
        root.AddChild(matPanel);

        // ---------------------------------------------------------------------
        // 3. Lathe Memory Bar + Inserted Disk Strip + Recipe Search
        // ---------------------------------------------------------------------
        _memoryLabel = new ContentLabel
        {
            Name = "LatheMemoryLabel",
            Text = "Lathe Memory: ---",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#88CCCC")
        };
        root.AddChild(_memoryLabel);

        _diskBar = new ContentPanelContainer
        {
            Name = "LatheDiskBar",
            HorizontalExpand = true,
            Visible = false,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#112330"),
                BorderColor = Color.FromHex("#2E7A8F"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 4,
                ContentMarginBottomOverride = 4
            }
        };

        var diskRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6
        };

        _diskLabel = new ContentLabel
        {
            Name = "LatheDiskLabel",
            Text = "Disk: None",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#88EEFF"),
            VerticalAlignment = VAlignment.Center
        };

        _importDiskBtn = new ContentButton
        {
            Name = "LatheSaveDiskButton",
            Text = "Save Unencrypted",
            MinWidth = 116,
            Visible = false
        };
        _importDiskBtn.OnPressed += _ => SendProgramMessage(new LatheControlActionMessage(LatheControlAction.ImportDiskBlueprints));

        _ejectDiskBtn = new ContentButton
        {
            Name = "LatheEjectDiskButton",
            Text = "Eject Disk",
            MinWidth = 76
        };
        _ejectDiskBtn.OnPressed += _ => SendProgramMessage(new LatheControlActionMessage(LatheControlAction.EjectDisk));

        diskRow.AddChild(_diskLabel);
        diskRow.AddChild(_importDiskBtn);
        diskRow.AddChild(_ejectDiskBtn);
        _diskBar.AddChild(diskRow);
        root.AddChild(_diskBar);

        var filterRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6
        };

        _searchInput = new ContentLineEdit
        {
            Name = "LatheRecipeSearch",
            HorizontalExpand = true,
            PlaceHolder = "Filter blueprints (disk & memory: name, description, material)..."
        };
        _searchInput.OnTextChanged += _ => RebuildRecipeList();

        _blueprintCountLabel = new ContentLabel
        {
            Name = "LatheBlueprintCountLabel",
            Text = "Designs: 0",
            WordWrap = false,
            FontColorOverride = Color.FromHex("#88CCCC")
        };

        filterRow.AddChild(_searchInput);
        filterRow.AddChild(_blueprintCountLabel);
        root.AddChild(filterRow);

        _recipeScroll = new ContentScrollContainer
        {
            Name = "LatheRecipeScroll",
            HorizontalExpand = true,
            VerticalExpand = true,
            MinHeight = 140f,
            AlwaysShowBackground = true
        };
        _recipesContainer = new ContentBoxContainer
        {
            Name = "LatheRecipesContainer",
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 3
        };
        _recipeScroll.AddChild(_recipesContainer);
        root.AddChild(_recipeScroll);

        AddChild(root);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        ApplyAutoExpandedHeight();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!_isPrinting || _productionStartTime == null || _productionLength == null || _productionLength.Value <= TimeSpan.Zero)
            return;

        var elapsed = _timing.CurTime - _productionStartTime.Value;
        var total = _productionLength.Value;
        var fraction = Math.Clamp((float) (elapsed.TotalSeconds / total.TotalSeconds), 0f, 1f);
        var remaining = Math.Max(0.0, (total - elapsed).TotalSeconds);

        _progressBar.Value = fraction;
        _progressBar.OverlayText = $"{fraction * 100f:0}% ({remaining:0.0}s)";
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not LatheControlUiState state)
            return;

        _lastState = state;
        _productionStartTime = state.ProductionStartTime;
        _productionLength = state.ProductionLength;

        UpdatePrintingAndQueue(state);
        UpdateMaterialsStrip(state);

        if (state.StorageTotalBytes > 0)
        {
            var freeBytes = Math.Max(0, state.StorageTotalBytes - state.StorageUsedBytes);
            _memoryLabel.Text = $"Lathe Memory: {FormatByteSize(state.StorageUsedBytes)} / {FormatByteSize(state.StorageTotalBytes)} ({FormatByteSize(freeBytes)} free)";
            _memoryLabel.FontColorOverride = freeBytes < 4096
                ? Color.FromHex("#FF8866")
                : freeBytes < 12288
                    ? Color.FromHex("#FFD166")
                    : Color.FromHex("#88CCCC");
            _memoryLabel.Visible = true;
        }
        else
        {
            _memoryLabel.Visible = false;
        }

        var hasDisk = !string.IsNullOrEmpty(state.InsertedDiskName);
        _diskBar.Visible = hasDisk;
        if (hasDisk)
        {
            var diskDesignCount = state.AvailableRecipes.Count(r => r.FromDisk);
            var hasCopyableDiskDesign = state.AvailableRecipes.Any(r => r.FromDisk && !IsCopyProtectedBlueprint(r));
            _diskLabel.Text = $"Disk: {state.InsertedDiskName} ({diskDesignCount} loaded)";
            _importDiskBtn.Visible = hasCopyableDiskDesign;
        }
        else
        {
            _importDiskBtn.Visible = false;
        }

        RebuildRecipeList();
        UpdateAutoExpandedHeight(state.AvailableRecipes.Count);
    }

    private void UpdateAutoExpandedHeight(int totalRecipeCount)
    {
        var expandedRows = Math.Clamp(totalRecipeCount, 1, MaxExpandedRecipes);
        DesiredExpandedWindowHeight = Math.Clamp(
            BaseChromeHeight + expandedRows * EstimatedRecipeCardHeight,
            480f,
            BaseChromeHeight + MaxExpandedRecipes * EstimatedRecipeCardHeight);
        ApplyAutoExpandedHeight();
    }

    private void ApplyAutoExpandedHeight()
    {
        var current = Parent;
        while (current != null)
        {
            if (current is ComputerDisplayWindow win)
            {
                win.RequestAutoExpandedHeight(DesiredExpandedWindowHeight);
                break;
            }

            current = current.Parent;
        }
    }

    private void UpdatePrintingAndQueue(LatheControlUiState state)
    {
        _isPrinting = !string.IsNullOrEmpty(state.CurrentPrinting);

        if (_isPrinting && _proto.TryIndex<LatheRecipePrototype>(state.CurrentPrinting, out var currentProto))
        {
            _printingIcon.Texture = ResolveRecipeTexture(currentProto);
            _printingNameLabel.Text = $"Printing: {_lathe.GetRecipeName(currentProto)}";
            _printingNameLabel.FontColorOverride = Color.FromHex("#00FFCC");
            _printingUsageLabel.Text = $"Uses: {FormatRecipeMaterialCosts(currentProto, state.MaterialUseMultiplier)}";

            if (_productionStartTime != null && _productionLength != null && _productionLength.Value > TimeSpan.Zero)
            {
                var elapsed = _timing.CurTime - _productionStartTime.Value;
                var fraction = Math.Clamp((float) (elapsed.TotalSeconds / _productionLength.Value.TotalSeconds), 0f, 1f);
                var remaining = Math.Max(0.0, (_productionLength.Value - elapsed).TotalSeconds);
                _progressBar.Value = fraction;
                _progressBar.OverlayText = $"{fraction * 100f:0}% ({remaining:0.0}s)";
            }
            else
            {
                _progressBar.Value = 0f;
                _progressBar.OverlayText = "Fabricating...";
            }
        }
        else if (_isPrinting)
        {
            _printingIcon.Texture = null;
            _printingNameLabel.Text = $"Printing: {state.CurrentPrinting}";
            _printingNameLabel.FontColorOverride = Color.FromHex("#00FFCC");
            _printingUsageLabel.Text = "Resource usage: ---";
            _progressBar.Value = 0f;
            _progressBar.OverlayText = "Fabricating...";
        }
        else
        {
            _printingIcon.Texture = null;
            _printingNameLabel.Text = state.HasTelemetry ? "Standby — Not Printing" : "Awaiting Autolathe Telemetry...";
            _printingNameLabel.FontColorOverride = Color.FromHex("#88A8B8");
            _printingUsageLabel.Text = "Resource usage: ---";
            _progressBar.Value = 0f;
            _progressBar.OverlayText = "Idle";
        }

        var queueItems = state.QueueEntries.Count > 0
            ? state.QueueEntries
            : state.QueueRecipes.Select(id => new LatheQueueEntryDto(id, 1)).ToList();

        if (!string.IsNullOrEmpty(state.SelectedRecipeId))
        {
            var selName = _proto.TryIndex<LatheRecipePrototype>(state.SelectedRecipeId, out var selProto)
                ? _lathe.GetRecipeName(selProto)
                : state.SelectedRecipeId;
            var selSrc = state.SelectedRecipeFromDisk ? "Disk" : "Mem";
            _selectedRecipeLabel.Text = $"Selected: {selName} ({selSrc})";
            _selectedRecipeLabel.FontColorOverride = Color.FromHex("#66E6FF");
        }
        else
        {
            _selectedRecipeLabel.Text = "Selected: None";
            _selectedRecipeLabel.FontColorOverride = Color.FromHex("#7790A0");
        }

        var totalQueued = queueItems.Sum(q => q.Count);
        _cancelQueueBtn.Disabled = !_isPrinting && totalQueued == 0;
        _queueHeaderLabel.Text = totalQueued > 0 ? $"Queue ({totalQueued} items):" : "Queue: Empty";

        _queueContainer.RemoveAllChildren();
        foreach (var entry in queueItems)
        {
            var pill = new ContentPanelContainer
            {
                Name = "LatheQueueItem",
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = Color.FromHex("#142434"),
                    BorderColor = Color.FromHex("#2A5570"),
                    BorderThickness = new Thickness(1),
                    ContentMarginLeftOverride = 5,
                    ContentMarginRightOverride = 6,
                    ContentMarginTopOverride = 2,
                    ContentMarginBottomOverride = 2
                }
            };

            var row = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                SeparationOverride = 4
            };

            string displayName = entry.RecipeId;
            Texture? iconTex = null;
            if (_proto.TryIndex<LatheRecipePrototype>(entry.RecipeId, out var qProto))
            {
                displayName = _lathe.GetRecipeName(qProto);
                iconTex = ResolveRecipeTexture(qProto);
            }

            if (iconTex != null)
            {
                row.AddChild(new ContentTextureRect
                {
                    Texture = iconTex,
                    DisplaySize = new Vector2(20, 20),
                    MinSize = new Vector2(20, 20)
                });
            }

            row.AddChild(new ContentLabel
            {
                Text = $"{displayName} x{entry.Count}",
                WordWrap = false,
                FontColorOverride = Color.FromHex("#E0F7FA")
            });

            pill.AddChild(row);
            _queueContainer.AddChild(pill);
        }
    }

    private void UpdateMaterialsStrip(LatheControlUiState state)
    {
        _materialsContainer.RemoveAllChildren();

        var nonEmpty = state.Materials
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (nonEmpty.Count == 0)
        {
            _materialsContainer.AddChild(new ContentLabel
            {
                Text = "No materials loaded — insert sheets into the autolathe.",
                FontColorOverride = Color.FromHex("#7790A0")
            });
            return;
        }

        foreach (var (matId, amount) in nonEmpty)
        {
            Texture? icon = null;
            var matName = matId;
            var volumePerSheet = 100;
            var maxStack = 30;
            if (_proto.TryIndex<MaterialPrototype>(matId, out var matProto))
            {
                matName = Loc.GetString(matProto.Name);
                icon = _sprite.Frame0(matProto.Icon);
                if (matProto.StackEntity is { } stackEntId &&
                    _proto.TryIndex(stackEntId, out var stackEntProto))
                {
                    if (stackEntProto.TryComp<PhysicalCompositionComponent>(out var composition, _compFactory) &&
                        composition.MaterialComposition.TryGetValue(matId, out var vol) &&
                        vol > 0)
                    {
                        volumePerSheet = vol;
                    }

                    var rawMax = _stack.GetMaxCount(stackEntProto);
                    maxStack = rawMax == int.MaxValue ? 30 : Math.Max(1, rawMax);
                }
            }

            var initialSelected = _ejectSelectionByMaterial.TryGetValue(matId, out var saved) ? saved : 1;
            var tile = new MaterialIconTile(matId, matName, amount, volumePerSheet, maxStack, initialSelected, icon);
            _ejectSelectionByMaterial[matId] = tile.SelectedSheets;
            tile.OnSelectedSheetsChanged += selected => _ejectSelectionByMaterial[matId] = selected;
            tile.OnPressed += _ =>
            {
                if (tile.SelectedSheets > 0)
                    SendProgramMessage(new LatheControlActionMessage(LatheControlAction.EjectMaterial, matId, amount: tile.SelectedSheets));
            };
            _materialsContainer.AddChild(tile);
        }
    }

    private void RebuildRecipeList()
    {
        _recipesContainer.RemoveAllChildren();

        if (_lastState == null)
            return;

        var query = (_searchInput.Text ?? string.Empty).Trim();
        var filtered = new List<(LatheBlueprintEntryDto Bp, LatheRecipePrototype? Proto, string Name, string Desc, string Usage, bool Affordable)>();

        foreach (var bp in _lastState.AvailableRecipes)
        {
            _proto.TryIndex<LatheRecipePrototype>(bp.RecipeId, out var rProto);
            var displayName = rProto != null ? _lathe.GetRecipeName(rProto) : bp.RecipeName;
            var desc = rProto != null ? _lathe.GetRecipeDescription(rProto) : string.Empty;
            var usage = rProto != null ? FormatRecipeMaterialCosts(rProto, _lastState.MaterialUseMultiplier) : "No materials";
            var affordable = rProto == null || CanAffordRecipe(rProto, _lastState.Materials, _lastState.MaterialUseMultiplier);

            if (!string.IsNullOrEmpty(query) &&
                !displayName.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !desc.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !usage.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !bp.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            filtered.Add((bp, rProto, displayName, desc, usage, affordable));
        }

        var diskTotal = _lastState.AvailableRecipes.Count(r => r.FromDisk);
        var memTotal = _lastState.AvailableRecipes.Count - diskTotal;
        _blueprintCountLabel.Text = diskTotal > 0
            ? $"Designs: {filtered.Count}/{_lastState.AvailableRecipes.Count} (Disk {diskTotal}, Mem {memTotal})"
            : $"Designs: {filtered.Count}/{_lastState.AvailableRecipes.Count}";

        if (filtered.Count == 0)
        {
            _recipesContainer.AddChild(new ContentLabel
            {
                Text = _lastState.AvailableRecipes.Count == 0
                    ? "No .lathe blueprints loaded from disk or computer memory."
                    : "No blueprints match your filter.",
                FontColorOverride = Color.FromHex("#8899AA"),
                HorizontalExpand = true
            });
            return;
        }

        foreach (var (bp, rProto, displayName, desc, usage, affordable) in filtered)
        {
            var isSelected = !string.IsNullOrEmpty(_lastState.SelectedRecipeId) &&
                string.Equals(bp.RecipeId, _lastState.SelectedRecipeId, StringComparison.Ordinal) &&
                bp.FromDisk == _lastState.SelectedRecipeFromDisk &&
                (string.IsNullOrEmpty(_lastState.SelectedRecipeFilePath) ||
                 string.Equals(bp.FilePath, _lastState.SelectedRecipeFilePath, StringComparison.Ordinal));

            var card = new ContentPanelContainer
            {
                Name = "LatheRecipeCard",
                HorizontalExpand = true,
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = isSelected
                        ? Color.FromHex("#102B38")
                        : bp.FromDisk
                            ? (bp.IsEncrypted ? Color.FromHex("#1A1710") : Color.FromHex("#0F1E28"))
                            : Color.FromHex("#0D1824"),
                    BorderColor = isSelected
                        ? Color.FromHex("#00D4FF")
                        : bp.IsEncrypted
                            ? Color.FromHex("#8A6A28")
                            : bp.FromDisk
                                ? Color.FromHex("#2A7A88")
                                : affordable
                                    ? Color.FromHex("#1D4B63")
                                    : Color.FromHex("#24313D"),
                    BorderThickness = new Thickness(1),
                    ContentMarginLeftOverride = 6,
                    ContentMarginRightOverride = 6,
                    ContentMarginTopOverride = 3,
                    ContentMarginBottomOverride = 3
                }
            };

            var row = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 8
            };

            var iconTex = rProto != null ? ResolveRecipeTexture(rProto) : null;
            row.AddChild(new ContentTextureRect
            {
                Texture = iconTex,
                DisplaySize = new Vector2(28, 28),
                MinSize = new Vector2(28, 28)
            });

            var details = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 1
            };

            var fileShort = bp.FilePath.Contains('/') ? bp.FilePath[(bp.FilePath.LastIndexOf('/') + 1)..] : bp.FilePath;
            var metaBadges = new List<string> { bp.FromDisk ? "DISK" : "MEM" };
            if (!string.IsNullOrEmpty(fileShort))
                metaBadges.Add(fileShort);
            if (bp.FileSizeBytes > 0)
                metaBadges.Add(FormatByteSize(bp.FileSizeBytes));
            if (bp.IsEncrypted)
                metaBadges.Add("ENC");
            if (bp.RemainingUses != null)
                metaBadges.Add(bp.PointCost > 1
                    ? $"{bp.RemainingUses} LP ({bp.PointCost}/ea)"
                    : $"{bp.RemainingUses} uses");
            var badgeText = $" [{string.Join(" | ", metaBadges)}]";

            details.AddChild(new ContentLabel
            {
                Text = $"{displayName}{badgeText}",
                HorizontalExpand = true,
                FontColorOverride = bp.IsEncrypted
                    ? Color.FromHex("#FFD166")
                    : bp.FromDisk
                        ? Color.FromHex("#88F0FF")
                        : affordable
                            ? Color.FromHex("#E6FAFF")
                            : Color.FromHex("#99AAB5")
            });

            details.AddChild(new ContentLabel
            {
                Text = $"Materials: {usage}",
                HorizontalExpand = true,
                FontColorOverride = affordable ? Color.FromHex("#88FFAA") : Color.FromHex("#FF8080")
            });

            if (!string.IsNullOrWhiteSpace(desc))
            {
                details.AddChild(new ContentLabel
                {
                    Text = desc,
                    ToolTip = desc,
                    WordWrap = false,
                    ClipText = true,
                    HorizontalExpand = true,
                    FontColorOverride = Color.FromHex("#8FAAB8")
                });
            }

            row.AddChild(details);

            var actionCol = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                VerticalAlignment = VAlignment.Center,
                SeparationOverride = 4
            };

            // Secondary action: save copyable (unencrypted & non-DRM) disk blueprints into lathe HDD memory.
            if (bp.FromDisk && !IsCopyProtectedBlueprint(bp))
            {
                var saveBtn = new ContentButton
                {
                    Name = "LatheSaveBlueprintButton",
                    Text = "Save",
                    MinWidth = 48,
                    VerticalAlignment = VAlignment.Center,
                    ToolTip = bp.FileSizeBytes > 0
                        ? $"Save '{fileShort}' ({FormatByteSize(bp.FileSizeBytes)}) to lathe memory"
                        : $"Save '{fileShort}' to lathe memory"
                };
                var saveRecipeId = bp.RecipeId;
                var saveFilePath = bp.FilePath;
                saveBtn.OnPressed += _ => SendProgramMessage(
                    new LatheControlActionMessage(LatheControlAction.ImportDiskBlueprints, saveRecipeId, saveFilePath));
                actionCol.AddChild(saveBtn);
            }

            var selectBtn = new ContentButton
            {
                Name = "LatheSelectRecipeButton",
                Text = isSelected ? "Selected" : "Select",
                MinWidth = 60,
                Pressed = isSelected,
                VerticalAlignment = VAlignment.Center,
                ToolTip = isSelected
                    ? "Selected for signal-triggered printing (click to clear)"
                    : "Select recipe for signal-triggered printing (persists when UI closes)"
            };
            var selRecipeId = bp.RecipeId;
            var selFilePath = bp.FilePath;
            var selFromDisk = bp.FromDisk;
            var selCurrentlySelected = isSelected;
            selectBtn.OnPressed += _ => SendProgramMessage(
                selCurrentlySelected
                    ? new LatheControlActionMessage(LatheControlAction.SelectRecipe)
                    : new LatheControlActionMessage(LatheControlAction.SelectRecipe, selRecipeId, selFilePath, 1, fromDisk: selFromDisk));
            actionCol.AddChild(selectBtn);

            var requiresUses = bp.IsEncrypted || bp.IsDrmLocked || bp.RemainingUses != null || bp.PointCost > 1;
            var hasEnoughUses = !requiresUses || (bp.RemainingUses != null && bp.RemainingUses.Value >= bp.PointCost);

            var printBtn = new ContentButton
            {
                Name = "LathePrintButton",
                Text = "Print",
                MinWidth = 58,
                VerticalAlignment = VAlignment.Center,
                Disabled = !affordable || !hasEnoughUses
            };
            var printRecipeId = bp.RecipeId;
            var printFilePath = bp.FilePath;
            var printFromDisk = bp.FromDisk;
            printBtn.OnPressed += _ => SendProgramMessage(
                new LatheControlActionMessage(LatheControlAction.PrintRecipe, printRecipeId, printFilePath, 1, fromDisk: printFromDisk));
            actionCol.AddChild(printBtn);

            row.AddChild(actionCol);

            card.AddChild(row);
            _recipesContainer.AddChild(card);
        }
    }

    private static bool IsCopyProtectedBlueprint(LatheBlueprintEntryDto bp)
    {
        return bp.IsEncrypted || bp.IsDrmLocked || bp.RemainingUses != null;
    }

    private static string FormatByteSize(int bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        return $"{bytes / 1024f:0.#} KB";
    }

    private Texture? ResolveRecipeTexture(LatheRecipePrototype recipe)
    {
        if (recipe.Icon != null)
            return _sprite.Frame0(recipe.Icon);

        if (recipe.Result is { } resultId && _proto.TryIndex(resultId, out var entProto))
            return _sprite.GetPrototypeIcon(entProto).Default;

        return null;
    }

    private string FormatRecipeMaterialCosts(LatheRecipePrototype recipe, float multiplier)
    {
        if (recipe.Materials.Count == 0)
            return "None";

        var parts = new List<string>(recipe.Materials.Count);
        foreach (var (matProtoId, rawCost) in recipe.Materials)
        {
            var adjusted = SharedLatheSystem.AdjustMaterial(rawCost, recipe.ApplyMaterialDiscount, multiplier);
            var matName = _proto.TryIndex(matProtoId, out var matProto)
                ? Loc.GetString(matProto.Name)
                : matProtoId.Id;
            parts.Add($"{matName} {adjusted}");
        }

        return string.Join(", ", parts);
    }

    private static bool CanAffordRecipe(LatheRecipePrototype recipe, Dictionary<string, int> storedMaterials, float multiplier)
    {
        foreach (var (matProtoId, rawCost) in recipe.Materials)
        {
            var adjusted = SharedLatheSystem.AdjustMaterial(rawCost, recipe.ApplyMaterialDiscount, multiplier);
            if (!storedMaterials.TryGetValue(matProtoId.Id, out var avail) || avail < adjusted)
                return false;
        }

        return true;
    }
}
