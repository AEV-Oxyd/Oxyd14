using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client._Oxyd.Computers.UI;
using Content.Client.Lathe;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Content.Shared.Stacks;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
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
// Scrollable & Editable Numeric Chip (ContentControl-derived for InputRelay safety)
// =============================================================================

/// <summary>
/// Compact numeric value chip supporting:
/// - Mouse-wheel stepping with debounced server commits and anti-rubber-band hold windows
///   so continuous multi-step scrolling is never overwritten by in-flight server states.
/// - Left-click inline manual numeric editing (Enter or focus-exit commits, Escape cancels).
/// - Right-click quick ON/OFF toggle (and scroll-below-min OFF) for optional bounds.
/// </summary>
public sealed class ScrollNumberChip : ContentControl
{
    private const double ScrollDebounceSeconds = 0.14;
    private const double ServerHoldSeconds = 1.25;

    private readonly IGameTiming _timing;
    private readonly ContentLineEdit _editor;

    public string Label { get; set; }
    public string Unit { get; set; }
    public string Format { get; set; }
    public float Value { get; private set; }
    public float MinValue { get; set; }
    public float MaxValue { get; set; }
    public float Step { get; set; }
    public float EnableDefaultValue { get; set; }
    public bool OptionalEnabled { get; set; }
    public bool BoundEnabled { get; private set; }
    public Color AccentColor { get; set; } = Color.FromHex("#2CA8CC");

    private bool _disabled;
    public bool Disabled
    {
        get => _disabled;
        set
        {
            if (_disabled == value)
                return;

            _disabled = value;
            _editor.Editable = !value;
            if (_disabled && _isEditing)
                CancelEdit();
        }
    }

    public bool IsEditing => _isEditing;
    public ContentLineEdit Editor => _editor;
    public bool HasLocalOverride => _isEditing || _hasPendingSend || _timing.CurTime < _holdUntil;

    /// <summary>
    /// Fires immediately on any local value adjustment (scroll or edit commit) so parent cards
    /// can keep their local state synchronized before the debounced network send fires.
    /// </summary>
    public event Action<bool, float>? OnValueChanged;

    /// <summary>
    /// Fires when the value should be transmitted to the server (debounced for scroll wheel,
    /// immediate for manual typed edits, right-click toggles, or explicit flushes).
    /// </summary>
    public event Action<bool, float>? OnValueCommitted;

    private bool _hovered;
    private bool _isEditing;
    private bool _cancelling;
    private bool _hasPendingSend;
    private TimeSpan _nextSendTime;
    private TimeSpan _holdUntil;

    public ScrollNumberChip(
        string label,
        float value,
        float minValue,
        float maxValue,
        float step,
        string unit = "",
        string format = "0.#",
        bool optionalEnabled = false,
        bool boundEnabled = true,
        float? enableDefaultValue = null)
    {
        _timing = IoCManager.Resolve<IGameTiming>();

        Label = label;
        Value = Math.Clamp(value, minValue, maxValue);
        MinValue = minValue;
        MaxValue = maxValue;
        Step = MathF.Max(0.01f, step);
        Unit = unit;
        Format = format;
        OptionalEnabled = optionalEnabled;
        BoundEnabled = !optionalEnabled || boundEnabled;
        EnableDefaultValue = enableDefaultValue ?? Math.Clamp(value > minValue ? value : minValue + Step, minValue, maxValue);

        MouseFilter = MouseFilterMode.Stop;
        MinHeight = 22f;

        _editor = new ContentLineEdit
        {
            Name = "ScrollNumberInput",
            Visible = false,
            MinHeight = 20f,
            MinWidth = 52f,
            PlaceHolder = OptionalEnabled ? "num / off" : "num"
        };
        _editor.OnTextEntered += _ =>
        {
            CommitEdit(sendNow: true);
            _editor.ReleaseKeyboardFocus();
        };
        _editor.OnCancelRequested += _ => CancelEdit();
        _editor.OnFocusExit += _ =>
        {
            if (_isEditing && !_cancelling)
                CommitEdit(sendNow: true);
        };
        AddChild(_editor);

        UpdateTooltip();
    }

    public string DisplayText =>
        OptionalEnabled && !BoundEnabled
            ? $"{Label}: OFF"
            : $"{Label}: {Value.ToString(Format, CultureInfo.InvariantCulture)}{Unit}";

    /// <summary>
    /// Applies a server-replicated value unless the user is actively typing, has unsent scroll notches,
    /// or is within the anti-rubber-band hold window awaiting server confirmation of their latest edit.
    /// </summary>
    public void ApplyServerValue(bool enabled, float value)
    {
        var normEnabled = !OptionalEnabled || enabled;
        var normVal = Math.Clamp(value, MinValue, MaxValue);

        if (_isEditing || _hasPendingSend)
            return;

        if (_timing.CurTime < _holdUntil)
        {
            var tol = MathF.Max(0.15f, Step * 0.05f);
            var matches = BoundEnabled == normEnabled && (!normEnabled || MathF.Abs(Value - normVal) <= tol);
            if (!matches)
                return;

            _holdUntil = TimeSpan.Zero;
        }

        if (BoundEnabled != normEnabled || MathF.Abs(Value - normVal) > 0.0001f)
        {
            BoundEnabled = normEnabled;
            Value = normVal;
            UpdateTooltip();
            InvalidateMeasure();
        }
    }

    public void BeginEdit()
    {
        if (Disabled || _isEditing)
            return;

        _isEditing = true;
        _cancelling = false;
        _editor.Editable = true;
        _editor.Text = OptionalEnabled && !BoundEnabled
            ? string.Empty
            : Value.ToString(Format, CultureInfo.InvariantCulture);
        _editor.Visible = true;
        _editor.GrabKeyboardFocus();
        InvalidateMeasure();
        InvalidateArrange();
    }

    public void CommitEdit(bool sendNow = true)
    {
        if (!_isEditing)
            return;

        _isEditing = false;
        _editor.Visible = false;

        var raw = (_editor.Text ?? string.Empty).Trim().Replace(',', '.');
        var changed = false;

        if (OptionalEnabled && (string.IsNullOrEmpty(raw) || raw.Equals("off", StringComparison.OrdinalIgnoreCase)))
        {
            if (BoundEnabled)
            {
                BoundEnabled = false;
                changed = true;
            }
        }
        else if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && float.IsFinite(parsed))
        {
            var clamped = Math.Clamp(parsed, MinValue, MaxValue);
            if (!BoundEnabled || MathF.Abs(clamped - Value) > 0.0001f)
            {
                BoundEnabled = true;
                Value = clamped;
                changed = true;
            }
        }

        UpdateTooltip();
        InvalidateMeasure();
        InvalidateArrange();

        if (!changed)
            return;

        if (sendNow)
            CommitImmediate();
        else
            OnValueChanged?.Invoke(BoundEnabled, Value);
    }

    public void CancelEdit()
    {
        if (!_isEditing)
            return;

        _cancelling = true;
        _isEditing = false;
        _editor.Visible = false;
        _cancelling = false;
        InvalidateMeasure();
        InvalidateArrange();
    }

    public void ClearLocalHold()
    {
        if (_isEditing)
            CancelEdit();
        _hasPendingSend = false;
        _holdUntil = TimeSpan.Zero;
    }

    public void FlushPending()
    {
        if (_isEditing)
        {
            CommitEdit(sendNow: true);
            return;
        }

        if (!_hasPendingSend)
            return;

        _hasPendingSend = false;
        _holdUntil = _timing.CurTime + TimeSpan.FromSeconds(ServerHoldSeconds);
        OnValueCommitted?.Invoke(BoundEnabled, Value);
    }

    private void CommitImmediate()
    {
        _hasPendingSend = false;
        _holdUntil = _timing.CurTime + TimeSpan.FromSeconds(ServerHoldSeconds);
        OnValueChanged?.Invoke(BoundEnabled, Value);
        OnValueCommitted?.Invoke(BoundEnabled, Value);
    }

    private void UpdateTooltip()
    {
        var offNote = OptionalEnabled ? "\nRight-click or scroll below min to toggle OFF" : string.Empty;
        ToolTip = $"{DisplayText}\nScroll wheel (±{Step.ToString(Format, CultureInfo.InvariantCulture)}{Unit}) | Click to type a number{offNote}";
    }

    public void AdjustBySteps(int deltaSteps)
    {
        if (Disabled || deltaSteps == 0)
            return;

        if (_isEditing)
            CommitEdit(sendNow: false);

        var changed = false;
        if (OptionalEnabled && !BoundEnabled)
        {
            if (deltaSteps > 0)
            {
                BoundEnabled = true;
                Value = Math.Clamp(EnableDefaultValue, MinValue, MaxValue);
                changed = true;
            }
        }
        else
        {
            var next = Value + deltaSteps * Step;
            if (OptionalEnabled && deltaSteps < 0 && next < MinValue - 0.0001f)
            {
                BoundEnabled = false;
                changed = true;
            }
            else
            {
                next = Math.Clamp(MathF.Round(next / Step) * Step, MinValue, MaxValue);
                if (MathF.Abs(next - Value) > 0.0001f || (OptionalEnabled && !BoundEnabled))
                {
                    BoundEnabled = true;
                    Value = next;
                    changed = true;
                }
            }
        }

        if (!changed)
            return;

        UpdateTooltip();
        InvalidateMeasure();
        OnValueChanged?.Invoke(BoundEnabled, Value);

        // Debounce network commit and hold against stale server echoes while user continues scrolling.
        _hasPendingSend = true;
        _nextSendTime = _timing.CurTime + TimeSpan.FromSeconds(ScrollDebounceSeconds);
        _holdUntil = _timing.CurTime + TimeSpan.FromSeconds(ServerHoldSeconds);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_hasPendingSend && _timing.CurTime >= _nextSendTime)
        {
            _hasPendingSend = false;
            _holdUntil = _timing.CurTime + TimeSpan.FromSeconds(ServerHoldSeconds);
            OnValueCommitted?.Invoke(BoundEnabled, Value);
        }
    }

    protected override void ExitedTree()
    {
        FlushPending();
        base.ExitedTree();
    }

    protected override void MouseEntered()
    {
        base.MouseEntered();
        _hovered = true;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hovered = false;
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        if (Disabled || args.Delta.Y == 0f)
            return;

        AdjustBySteps(args.Delta.Y > 0f ? 1 : -1);
        args.Handle();
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (Disabled)
            return;

        if (args.Function == EngineKeyFunctions.UIClick)
        {
            BeginEdit();
            args.Handle();
        }
        else if (args.Function == EngineKeyFunctions.UIRightClick && OptionalEnabled)
        {
            if (_isEditing)
                CancelEdit();

            if (BoundEnabled)
            {
                BoundEnabled = false;
            }
            else
            {
                BoundEnabled = true;
                Value = Math.Clamp(Value > MinValue ? Value : EnableDefaultValue, MinValue, MaxValue);
            }

            UpdateTooltip();
            InvalidateMeasure();
            CommitImmediate();
            args.Handle();
        }
    }

    private Font ResolveFont()
    {
        return TryGetStyleProperty<Font>("font", out var styleFont) && styleFont != null
            ? styleFont
            : UserInterfaceManager.ThemeDefaults.DefaultFont;
    }

    private float MeasureLogicalTextWidth(Font font, string text)
    {
        var w = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            if (font.TryGetCharMetrics(rune, UIScale, out var m))
                w += m.Advance;
        }
        var scale = MathF.Max(0.1f, UIScale);
        return w > 0f ? w / scale : text.Length * 7f;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var font = ResolveFont();
        var scale = MathF.Max(0.1f, UIScale);
        var baseH = MathF.Max(MinHeight, (font.GetHeight(UIScale) / scale) + 6f);

        if (_isEditing)
        {
            var prefixW = MeasureLogicalTextWidth(font, $"{Label}:");
            _editor.Measure(new Vector2(MathF.Max(56f, availableSize.X - prefixW - 14f), baseH));
            var editW = MathF.Max(58f, _editor.DesiredSize.X);
            return new Vector2(MathF.Max(MinWidth, prefixW + editW + 16f), MathF.Max(baseH, _editor.DesiredSize.Y + 2f));
        }

        var measuredTextW = MeasureLogicalTextWidth(font, DisplayText);
        var logicalW = MathF.Max(MinWidth, measuredTextW + 16f);
        return new Vector2(logicalW, baseH);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        if (_isEditing)
        {
            var font = ResolveFont();
            var prefixW = MeasureLogicalTextWidth(font, $"{Label}:");
            var editLeft = MathF.Min(finalSize.X - 30f, prefixW + 10f);
            var editRight = MathF.Max(editLeft + 24f, finalSize.X - 2f);
            _editor.Arrange(new UIBox2(editLeft, 1f, editRight, MathF.Max(18f, finalSize.Y - 1f)));
        }

        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var rect = PixelSizeBox;
        var active = !Disabled && (_isEditing || !OptionalEnabled || BoundEnabled);
        var bg = Disabled
            ? Color.FromHex("#141920")
            : !active
                ? (_hovered ? Color.FromHex("#18222C") : Color.FromHex("#10161E"))
                : (_isEditing || _hovered ? Color.FromHex("#153447") : Color.FromHex("#0D2130"));
        var border = Disabled
            ? Color.FromHex("#242C36")
            : !active
                ? Color.FromHex("#2B3B48")
                : (_isEditing || _hovered ? Color.FromHex("#33D6FF") : AccentColor.WithAlpha(0.75f));
        var fg = Disabled
            ? Color.FromHex("#5C6B78")
            : !active
                ? Color.FromHex("#7B8E9D")
                : Color.FromHex("#ECF9FF");

        handle.DrawRect(rect, bg, filled: true);
        if (active)
        {
            var barW = MathF.Max(2f, 3f * UIScale);
            handle.DrawRect(new UIBox2(rect.Left + 1f, rect.Top + 1f, rect.Left + 1f + barW, rect.Bottom - 1f), AccentColor, filled: true);
        }
        handle.DrawRect(rect, border, filled: false);

        var font = ResolveFont();
        var text = _isEditing ? $"{Label}:" : DisplayText;
        var dims = handle.GetDimensions(font, text, UIScale);
        var tx = MathF.Floor(rect.Left + 7f * UIScale);
        var ty = MathF.Floor(MathF.Max(1f, (PixelHeight - dims.Y) * 0.5f));
        handle.DrawString(font, new Vector2(tx, ty), text, UIScale, fg);
    }
}

// =============================================================================
// Air Alarm Control UI (Categorized Vents / Scrubbers / Sensors + Single Template)
// =============================================================================

public sealed class AtmosControlUiControl : ProgramUiControl
{
    private enum DeviceCategory : byte
    {
        Vents = 0,
        Scrubbers = 1,
        Sensors = 2
    }

    private readonly ContentLabel _telemetryLabel;
    private readonly ContentLabel _statusLabel;
    private readonly Dictionary<string, ContentButton> _modeButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly ContentButton _autoModeBtn;
    private readonly ContentButton _resyncBtn;

    private readonly ContentLabel _templateSummaryLabel;
    private readonly ContentButton _applyTemplateAllBtn;
    private readonly ContentButton _clearTemplateBtn;

    private readonly ContentButton _categoryVentsBtn;
    private readonly ContentButton _categoryScrubbersBtn;
    private readonly ContentButton _categorySensorsBtn;

    private readonly ContentScrollContainer _devicesScroll;
    private readonly ContentBoxContainer _devicesContainer;
    private readonly ContentLabel _emptyCategoryLabel;

    private readonly List<ContentButton> _staticControlButtons = new();
    private readonly Dictionary<int, VentCardView> _ventCards = new();
    private readonly Dictionary<int, ScrubberCardView> _scrubberCards = new();
    private readonly Dictionary<int, SensorCardView> _sensorCards = new();

    private AtmosControlUiState? _lastState;
    private DeviceCategory _activeCategory = DeviceCategory.Vents;
    private DeviceCategory? _mountedCategory;
    private readonly List<int> _mountedAddresses = new();
    private bool _userPickedCategory;

    public AtmosControlUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(6),
            SeparationOverride = 5
        };

        // ---------------------------------------------------------------------
        // 1. Telemetry & Alarm Mode Strip
        // ---------------------------------------------------------------------
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
                ContentMarginTopOverride = 5,
                ContentMarginBottomOverride = 5
            }
        };

        _telemetryLabel = new ContentLabel
        {
            Text = "Waiting for telemetry...",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#E0F7FA")
        };
        telemetryPanel.AddChild(_telemetryLabel);
        root.AddChild(telemetryPanel);

        var modeRow = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4 };
        void AddModeBtn(string text, string modeKey, AtmosControlAction action)
        {
            var b = new ContentButton { Text = text, Disabled = true, MinWidth = 66 };
            b.OnPressed += _ => SendProgramMessage(new AtmosControlActionMessage(action));
            _modeButtons[modeKey] = b;
            _staticControlButtons.Add(b);
            modeRow.AddChild(b);
        }

        AddModeBtn("Filtering", "Filtering", AtmosControlAction.SetModeFiltering);
        AddModeBtn("WideFilter", "WideFiltering", AtmosControlAction.SetModeWideFiltering);
        AddModeBtn("Fill", "Fill", AtmosControlAction.SetModeFill);
        AddModeBtn("Panic", "Panic", AtmosControlAction.SetModePanic);
        AddModeBtn("Off", "None", AtmosControlAction.SetModeNone);

        _autoModeBtn = new ContentButton { Text = "Auto: ON", Disabled = true, MinWidth = 76 };
        _autoModeBtn.OnPressed += _ => SendProgramMessage(new AtmosControlActionMessage(AtmosControlAction.ToggleAutoMode));
        _staticControlButtons.Add(_autoModeBtn);
        modeRow.AddChild(_autoModeBtn);

        _resyncBtn = new ContentButton { Text = "Resync Devices", Disabled = true, MinWidth = 104 };
        _resyncBtn.OnPressed += _ => SendProgramMessage(new AtmosControlActionMessage(AtmosControlAction.ResyncDevices));
        _staticControlButtons.Add(_resyncBtn);
        modeRow.AddChild(_resyncBtn);
        root.AddChild(modeRow);

        // ---------------------------------------------------------------------
        // 2. Single Active Template Bar (Always Ready to Apply When Saved)
        // ---------------------------------------------------------------------
        var templatePanel = new ContentPanelContainer
        {
            Name = "AtmosTemplatePanel",
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0B1622"),
                BorderColor = Color.FromHex("#1A3F57"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 4,
                ContentMarginBottomOverride = 4
            }
        };

        var tplRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            MinHeight = 26f,
            SeparationOverride = 6
        };

        _templateSummaryLabel = new ContentLabel
        {
            Name = "AtmosTemplateSummaryLabel",
            Text = "Template: None — click 'Save Tpl' on any device.",
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            FontColorOverride = Color.FromHex("#7FA2B5")
        };
        tplRow.AddChild(_templateSummaryLabel);

        _applyTemplateAllBtn = new ContentButton
        {
            Name = "AtmosApplyTemplateAllButton",
            Text = "Apply All",
            MinWidth = 96,
            Visible = false,
            Disabled = true
        };
        _applyTemplateAllBtn.OnPressed += _ =>
        {
            var activeTpl = GetActiveTemplate();
            if (activeTpl != null)
            {
                ClearAllLocalHolds();
                SendProgramMessage(new AtmosApplyTemplateMessage(activeTpl.Name, activeTpl.Kind, null, applyToAll: true));
            }
        };
        tplRow.AddChild(_applyTemplateAllBtn);

        _clearTemplateBtn = new ContentButton
        {
            Name = "AtmosDeleteTemplateButton",
            Text = "Clear",
            MinWidth = 52,
            Visible = false,
            Disabled = true
        };
        _clearTemplateBtn.OnPressed += _ =>
        {
            var activeTpl = GetActiveTemplate();
            if (activeTpl != null)
                SendProgramMessage(new AtmosDeleteTemplateMessage(activeTpl.Name, activeTpl.Kind));
        };
        tplRow.AddChild(_clearTemplateBtn);

        templatePanel.AddChild(tplRow);
        root.AddChild(templatePanel);

        // ---------------------------------------------------------------------
        // 3. Strictly Categorized Device Tabs (Vents / Scrubbers / Sensors)
        // ---------------------------------------------------------------------
        var categoryRow = new ContentWrapContainer
        {
            Name = "AtmosCategoryRow",
            HorizontalSeparation = 4,
            VerticalSeparation = 4
        };
        ContentButton MakeCategoryBtn(string name, string text, DeviceCategory category)
        {
            var btn = new ContentButton { Name = name, Text = text, Disabled = true, MinWidth = 92 };
            btn.OnPressed += _ =>
            {
                _userPickedCategory = true;
                if (_activeCategory != category)
                {
                    FlushAllPendingEdits();
                    _activeCategory = category;
                    SyncDevicesView();
                }
            };
            _staticControlButtons.Add(btn);
            categoryRow.AddChild(btn);
            return btn;
        }

        _categoryVentsBtn = MakeCategoryBtn("AtmosCategoryVentsButton", "Vents (0)", DeviceCategory.Vents);
        _categoryScrubbersBtn = MakeCategoryBtn("AtmosCategoryScrubbersButton", "Scrubbers (0)", DeviceCategory.Scrubbers);
        _categorySensorsBtn = MakeCategoryBtn("AtmosCategorySensorsButton", "Sensors (0)", DeviceCategory.Sensors);
        root.AddChild(categoryRow);

        _statusLabel = new ContentLabel
        {
            Text = "",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#88CCCC"),
            Visible = false
        };
        root.AddChild(_statusLabel);

        // ---------------------------------------------------------------------
        // 4. Scrollable Categorized Devices Panel (In-Place Updated Cards)
        // ---------------------------------------------------------------------
        _devicesScroll = new ContentScrollContainer
        {
            Name = "AtmosDevicesScroll",
            HorizontalExpand = true,
            VerticalExpand = true,
            MinHeight = 200f,
            AlwaysShowBackground = true
        };
        _devicesContainer = new ContentBoxContainer
        {
            Name = "AtmosDevicesContainer",
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 6
        };
        _emptyCategoryLabel = new ContentLabel
        {
            Text = "No linked devices in this category. Link devices with a multitool, then click Resync Devices.",
            FontColorOverride = Color.FromHex("#8899AA"),
            HorizontalExpand = true
        };
        _devicesScroll.AddChild(_devicesContainer);
        root.AddChild(_devicesScroll);

        AddChild(root);
    }

    protected override void ExitedTree()
    {
        FlushAllPendingEdits();
        base.ExitedTree();
    }

    private AtmosTemplateEntryDto? GetActiveTemplate()
    {
        return _lastState != null && _lastState.Templates.Count > 0
            ? _lastState.Templates[0]
            : null;
    }

    private void ClearAllLocalHolds()
    {
        foreach (var v in _ventCards.Values)
            v.ClearLocalHolds();
        foreach (var s in _scrubberCards.Values)
            s.ClearLocalHolds();
        foreach (var sens in _sensorCards.Values)
            sens.ClearLocalHolds();
    }

    private void FlushAllPendingEdits()
    {
        foreach (var v in _ventCards.Values)
            v.FlushPending();
        foreach (var s in _scrubberCards.Values)
            s.FlushPending();
        foreach (var sens in _sensorCards.Values)
            sens.FlushPending();
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not AtmosControlUiState state)
            return;

        _lastState = state;

        _telemetryLabel.Text =
            $"State: {state.AlarmLevel} | Mode: {state.CurrentMode} (Auto: {(state.AutoMode ? "ON" : "OFF")}) | Pressure: {state.PressureKpa:0.0} kPa | Temp: {state.TemperatureCelsius:0.0} C\n" +
            $"Devices: {state.SensorCount} sensors, {state.VentCount} vents, {state.ScrubberCount} scrubbers | {state.GasSummary}";

        foreach (var (modeKey, btn) in _modeButtons)
            btn.Pressed = string.Equals(state.CurrentMode, modeKey, StringComparison.OrdinalIgnoreCase);

        _autoModeBtn.Text = state.AutoMode ? "Auto: ON" : "Auto: OFF";
        _autoModeBtn.Pressed = state.AutoMode;

        _categoryVentsBtn.Text = $"Vents ({state.Vents.Count})";
        _categoryScrubbersBtn.Text = $"Scrubbers ({state.Scrubbers.Count})";
        _categorySensorsBtn.Text = $"Sensors ({state.Sensors.Count})";

        if (!_userPickedCategory && GetCategoryCount(state, _activeCategory) == 0)
        {
            if (state.Vents.Count > 0)
                _activeCategory = DeviceCategory.Vents;
            else if (state.Scrubbers.Count > 0)
                _activeCategory = DeviceCategory.Scrubbers;
            else if (state.Sensors.Count > 0)
                _activeCategory = DeviceCategory.Sensors;
        }

        var showStatus = !state.Locked && !string.IsNullOrWhiteSpace(state.StatusMessage);
        _statusLabel.Text = showStatus ? state.StatusMessage : string.Empty;
        _statusLabel.Visible = showStatus;

        foreach (var btn in _staticControlButtons)
            btn.Disabled = state.Locked;

        UpdateTemplateBar(state);
        SyncDevicesView();
    }

    private static int GetCategoryCount(AtmosControlUiState state, DeviceCategory category) => category switch
    {
        DeviceCategory.Vents => state.Vents.Count,
        DeviceCategory.Scrubbers => state.Scrubbers.Count,
        _ => state.Sensors.Count
    };

    private void UpdateTemplateBar(AtmosControlUiState state)
    {
        var locked = state.Locked;
        _applyTemplateAllBtn.Disabled = locked;
        _clearTemplateBtn.Disabled = locked;

        var tpl = GetActiveTemplate();
        if (tpl == null)
        {
            _templateSummaryLabel.Text = "Template: None — click 'Save Tpl' on any device.";
            _templateSummaryLabel.FontColorOverride = Color.FromHex("#7FA2B5");
            _applyTemplateAllBtn.Visible = false;
            _clearTemplateBtn.Visible = false;
            return;
        }

        var summary = FormatTemplateSummary(tpl);
        _templateSummaryLabel.Text = $"Template: {summary}";
        _templateSummaryLabel.FontColorOverride = Color.FromHex("#88EEFF");
        _applyTemplateAllBtn.Text = tpl.Kind switch
        {
            AtmosTemplateKind.Vent => "Apply All Vents",
            AtmosTemplateKind.Scrubber => "Apply All Scrubbers",
            _ => "Apply All Sensors"
        };
        _applyTemplateAllBtn.Visible = true;
        _clearTemplateBtn.Visible = true;
    }

    private static string FormatTemplateSummary(AtmosTemplateEntryDto tpl)
    {
        switch (tpl.Kind)
        {
            case AtmosTemplateKind.Vent when tpl.Vent is { } v:
                var vState = v.Enabled ? "ON" : "OFF";
                var vDir = v.PumpDirection == VentPumpDirection.Releasing ? "Rel" : "Siph";
                return $"[VENT] {vState} | {vDir} | Ext {v.ExternalPressureBound.ToString("0.#", CultureInfo.InvariantCulture)} kPa | Int {v.InternalPressureBound.ToString("0.#", CultureInfo.InvariantCulture)} kPa";

            case AtmosTemplateKind.Scrubber when tpl.Scrubber is { } s:
                var sState = s.Enabled ? "ON" : "OFF";
                var sDir = s.PumpDirection == ScrubberPumpDirection.Scrubbing ? "Scrub" : "Siph";
                return $"[SCRUBBER] {sState} | {sDir} | {s.VolumeRate.ToString("0", CultureInfo.InvariantCulture)} L/s | {s.FilterGases.Count} gases";

            default:
                return "[SENSOR] Pressure, Temp & Gas Thresholds";
        }
    }

    private void SyncDevicesView()
    {
        if (_lastState == null)
            return;

        _categoryVentsBtn.Pressed = _activeCategory == DeviceCategory.Vents;
        _categoryScrubbersBtn.Pressed = _activeCategory == DeviceCategory.Scrubbers;
        _categorySensorsBtn.Pressed = _activeCategory == DeviceCategory.Sensors;

        var locked = _lastState.Locked;
        var activeTpl = GetActiveTemplate();

        switch (_activeCategory)
        {
            case DeviceCategory.Vents:
                SyncVentCards(_lastState.Vents, locked, activeTpl);
                break;

            case DeviceCategory.Scrubbers:
                SyncScrubberCards(_lastState.Scrubbers, locked, activeTpl);
                break;

            default:
                SyncSensorCards(_lastState.Sensors, locked, activeTpl);
                break;
        }
    }

    private bool NeedsContainerRemount(DeviceCategory category, List<int> desiredAddresses)
    {
        if (_mountedCategory != category || _mountedAddresses.Count != desiredAddresses.Count)
            return true;

        for (var i = 0; i < desiredAddresses.Count; i++)
        {
            if (_mountedAddresses[i] != desiredAddresses[i])
                return true;
        }

        return _devicesContainer.ChildCount == 0;
    }

    private void SyncVentCards(List<AtmosVentEntryDto> vents, bool locked, AtmosTemplateEntryDto? activeTpl)
    {
        var desired = new List<int>(vents.Count);
        for (var i = 0; i < vents.Count; i++)
            desired.Add(vents[i].AddressId);

        var needsRemount = NeedsContainerRemount(DeviceCategory.Vents, desired);

        var liveSet = new HashSet<int>(desired);
        foreach (var staleKey in _ventCards.Keys.Where(k => !liveSet.Contains(k)).ToList())
            _ventCards.Remove(staleKey);

        foreach (var vent in vents)
        {
            if (!_ventCards.TryGetValue(vent.AddressId, out var card))
            {
                card = new VentCardView(vent, SendProgramMessage);
                _ventCards[vent.AddressId] = card;
                needsRemount = true;
            }
            card.UpdateFromServer(vent, locked, activeTpl);
        }

        if (!needsRemount)
            return;

        _devicesContainer.RemoveAllChildren();
        _mountedCategory = DeviceCategory.Vents;
        _mountedAddresses.Clear();
        _mountedAddresses.AddRange(desired);

        if (vents.Count == 0)
        {
            _emptyCategoryLabel.Text = "No linked vents. Link vents with a multitool, then click Resync Devices.";
            _devicesContainer.AddChild(_emptyCategoryLabel);
            return;
        }

        foreach (var addr in desired)
            _devicesContainer.AddChild(_ventCards[addr].Root);
    }

    private void SyncScrubberCards(List<AtmosScrubberEntryDto> scrubbers, bool locked, AtmosTemplateEntryDto? activeTpl)
    {
        var desired = new List<int>(scrubbers.Count);
        for (var i = 0; i < scrubbers.Count; i++)
            desired.Add(scrubbers[i].AddressId);

        var needsRemount = NeedsContainerRemount(DeviceCategory.Scrubbers, desired);

        var liveSet = new HashSet<int>(desired);
        foreach (var staleKey in _scrubberCards.Keys.Where(k => !liveSet.Contains(k)).ToList())
            _scrubberCards.Remove(staleKey);

        foreach (var scrubber in scrubbers)
        {
            if (!_scrubberCards.TryGetValue(scrubber.AddressId, out var card))
            {
                card = new ScrubberCardView(scrubber, SendProgramMessage);
                _scrubberCards[scrubber.AddressId] = card;
                needsRemount = true;
            }
            card.UpdateFromServer(scrubber, locked, activeTpl);
        }

        if (!needsRemount)
            return;

        _devicesContainer.RemoveAllChildren();
        _mountedCategory = DeviceCategory.Scrubbers;
        _mountedAddresses.Clear();
        _mountedAddresses.AddRange(desired);

        if (scrubbers.Count == 0)
        {
            _emptyCategoryLabel.Text = "No linked scrubbers. Link scrubbers with a multitool, then click Resync Devices.";
            _devicesContainer.AddChild(_emptyCategoryLabel);
            return;
        }

        foreach (var addr in desired)
            _devicesContainer.AddChild(_scrubberCards[addr].Root);
    }

    private void SyncSensorCards(List<AtmosSensorEntryDto> sensors, bool locked, AtmosTemplateEntryDto? activeTpl)
    {
        var desired = new List<int>(sensors.Count);
        for (var i = 0; i < sensors.Count; i++)
            desired.Add(sensors[i].AddressId);

        var needsRemount = NeedsContainerRemount(DeviceCategory.Sensors, desired);

        var liveSet = new HashSet<int>(desired);
        foreach (var staleKey in _sensorCards.Keys.Where(k => !liveSet.Contains(k)).ToList())
            _sensorCards.Remove(staleKey);

        foreach (var sensor in sensors)
        {
            if (!_sensorCards.TryGetValue(sensor.AddressId, out var card))
            {
                card = new SensorCardView(sensor, SendProgramMessage);
                _sensorCards[sensor.AddressId] = card;
                needsRemount = true;
            }
            card.UpdateFromServer(sensor, locked, activeTpl);
        }

        if (!needsRemount)
            return;

        _devicesContainer.RemoveAllChildren();
        _mountedCategory = DeviceCategory.Sensors;
        _mountedAddresses.Clear();
        _mountedAddresses.AddRange(desired);

        if (sensors.Count == 0)
        {
            _emptyCategoryLabel.Text = "No linked air sensors. Link sensors with a multitool, then click Resync Devices.";
            _devicesContainer.AddChild(_emptyCategoryLabel);
            return;
        }

        foreach (var addr in desired)
            _devicesContainer.AddChild(_sensorCards[addr].Root);
    }

    private static ContentPanelContainer CreateDeviceCard(string name, Color bg, Color border)
    {
        return new ContentPanelContainer
        {
            Name = name,
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = bg,
                BorderColor = border,
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 4,
                ContentMarginBottomOverride = 4
            }
        };
    }

    private static string FormatGasShort(Gas gas) => gas switch
    {
        Gas.Oxygen => "O2",
        Gas.Nitrogen => "N2",
        Gas.CarbonDioxide => "CO2",
        Gas.Plasma => "Plasma",
        Gas.Tritium => "Trit",
        Gas.WaterVapor => "H2O",
        Gas.Ammonia => "NH3",
        Gas.NitrousOxide => "N2O",
        Gas.Frezon => "Frezon",
        _ => gas.ToString()
    };

    // -------------------------------------------------------------------------
    // Reusable In-Place Device Card Views (preserve focus, typing & scrolling)
    // -------------------------------------------------------------------------

    private sealed class VentCardView
    {
        public ContentPanelContainer Root { get; }

        private readonly int _addressId;
        private readonly Action<BoundUserInterfaceMessage> _send;
        private readonly ContentLabel _titleLabel;
        private readonly ContentButton _powerBtn;
        private readonly ContentButton _dirBtn;
        private readonly ContentButton _saveTplBtn;
        private readonly ContentButton _applyTplBtn;
        private readonly ContentButton _copyAllBtn;
        private readonly ContentButton _extChkBtn;
        private readonly ScrollNumberChip _extChip;
        private readonly ContentButton _intChkBtn;
        private readonly ScrollNumberChip _intChip;

        private string _displayAddress;
        private bool _curEnabled;
        private VentPumpDirection _curDir;
        private VentPressureBound _curChecks;
        private float _curExt;
        private float _curInt;
        private AtmosTemplateEntryDto? _activeTemplate;

        public VentCardView(AtmosVentEntryDto initial, Action<BoundUserInterfaceMessage> send)
        {
            _addressId = initial.AddressId;
            _displayAddress = initial.DisplayAddress;
            _send = send;
            _curEnabled = initial.Enabled;
            _curDir = initial.PumpDirection;
            _curChecks = initial.PressureChecks;
            _curExt = initial.ExternalPressureBound;
            _curInt = initial.InternalPressureBound;

            Root = CreateDeviceCard("AtmosVentCard", Color.FromHex("#0D1C28"), Color.FromHex("#24607A"));
            var box = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 4
            };

            var header = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4 };
            _titleLabel = new ContentLabel
            {
                Text = $"VENT {initial.DisplayAddress}",
                WordWrap = false,
                VerticalAlignment = VAlignment.Center,
                FontColorOverride = Color.FromHex("#66E6FF")
            };
            header.AddChild(_titleLabel);

            _powerBtn = new ContentButton { Name = "AtmosVentPowerButton", MinWidth = 44 };
            _powerBtn.OnPressed += _ => SendVent(enabled: !_curEnabled);
            header.AddChild(_powerBtn);

            _dirBtn = new ContentButton { Name = "AtmosVentDirectionButton", MinWidth = 78 };
            _dirBtn.OnPressed += _ => SendVent(dir: _curDir == VentPumpDirection.Releasing
                ? VentPumpDirection.Siphoning
                : VentPumpDirection.Releasing);
            header.AddChild(_dirBtn);

            _saveTplBtn = new ContentButton { Name = "AtmosSaveVentTemplateButton", Text = "Save Tpl", MinWidth = 64 };
            _saveTplBtn.OnPressed += _ =>
            {
                FlushPending();
                var snap = new AtmosVentEntryDto(_addressId, _displayAddress, _curEnabled, _curDir, _curChecks, _curExt, _curInt);
                _send(new AtmosSaveTemplateMessage("Vent", AtmosTemplateKind.Vent, vent: snap));
            };
            header.AddChild(_saveTplBtn);

            _applyTplBtn = new ContentButton { Name = "AtmosApplyVentTemplateButton", Text = "Apply Tpl", Visible = false };
            _applyTplBtn.OnPressed += _ =>
            {
                if (_activeTemplate is { Kind: AtmosTemplateKind.Vent } tpl)
                {
                    ClearLocalHolds();
                    _send(new AtmosApplyTemplateMessage(tpl.Name, AtmosTemplateKind.Vent, _addressId, applyToAll: false));
                }
            };
            header.AddChild(_applyTplBtn);

            _copyAllBtn = new ContentButton { Name = "AtmosCopyVentAllButton", Text = "Copy All", MinWidth = 64 };
            _copyAllBtn.OnPressed += _ =>
            {
                FlushPending();
                SendVent(applyAll: true);
            };
            header.AddChild(_copyAllBtn);
            box.AddChild(header);

            var settingsRow = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4 };
            _extChkBtn = new ContentButton { Name = "AtmosVentExtCheckButton" };
            _extChkBtn.OnPressed += _ => SendVent(checks: _curChecks ^ VentPressureBound.ExternalBound);
            settingsRow.AddChild(_extChkBtn);

            _extChip = new ScrollNumberChip("Ext", initial.ExternalPressureBound, 0f, Atmospherics.OneAtmosphere * 50f, 5f, " kPa", "0.#")
            {
                Name = "AtmosVentExtBoundChip"
            };
            _extChip.OnValueChanged += (_, v) => _curExt = v;
            _extChip.OnValueCommitted += (_, v) => SendVent(extBound: v);
            settingsRow.AddChild(_extChip);

            _intChkBtn = new ContentButton { Name = "AtmosVentIntCheckButton" };
            _intChkBtn.OnPressed += _ => SendVent(checks: _curChecks ^ VentPressureBound.InternalBound);
            settingsRow.AddChild(_intChkBtn);

            _intChip = new ScrollNumberChip("Int", initial.InternalPressureBound, 0f, Atmospherics.OneAtmosphere * 50f, 5f, " kPa", "0.#")
            {
                Name = "AtmosVentIntBoundChip"
            };
            _intChip.OnValueChanged += (_, v) => _curInt = v;
            _intChip.OnValueCommitted += (_, v) => SendVent(intBound: v);
            settingsRow.AddChild(_intChip);

            box.AddChild(settingsRow);
            Root.AddChild(box);
        }

        public void ClearLocalHolds()
        {
            _extChip.ClearLocalHold();
            _intChip.ClearLocalHold();
        }

        public void FlushPending()
        {
            _extChip.FlushPending();
            _intChip.FlushPending();
        }

        private void SendVent(
            bool? enabled = null,
            VentPumpDirection? dir = null,
            VentPressureBound? checks = null,
            float? extBound = null,
            float? intBound = null,
            bool applyAll = false)
        {
            if (enabled is { } e) _curEnabled = e;
            if (dir is { } d) _curDir = d;
            if (checks is { } c) _curChecks = c;
            if (extBound is { } ext) _curExt = ext;
            if (intBound is { } i) _curInt = i;

            _send(new AtmosUpdateVentMessage(
                _addressId,
                _curEnabled,
                _curDir,
                _curChecks,
                _curExt,
                _curInt,
                applyAll));
        }

        public void UpdateFromServer(AtmosVentEntryDto vent, bool locked, AtmosTemplateEntryDto? activeTpl)
        {
            _displayAddress = vent.DisplayAddress;
            _activeTemplate = activeTpl;
            _curEnabled = vent.Enabled;
            _curDir = vent.PumpDirection;
            _curChecks = vent.PressureChecks;

            _titleLabel.Text = $"VENT {vent.DisplayAddress}";

            _powerBtn.Text = vent.Enabled ? "ON" : "OFF";
            _powerBtn.Pressed = vent.Enabled;
            _powerBtn.Disabled = locked;

            _dirBtn.Text = vent.PumpDirection == VentPumpDirection.Releasing ? "Releasing" : "Siphoning";
            _dirBtn.Pressed = vent.PumpDirection == VentPumpDirection.Siphoning;
            _dirBtn.Disabled = locked;

            _saveTplBtn.Disabled = locked;
            _applyTplBtn.Visible = activeTpl is { Kind: AtmosTemplateKind.Vent };
            _applyTplBtn.Disabled = locked;
            _copyAllBtn.Disabled = locked;

            var hasExt = (vent.PressureChecks & VentPressureBound.ExternalBound) != 0;
            var hasInt = (vent.PressureChecks & VentPressureBound.InternalBound) != 0;

            _extChkBtn.Text = hasExt ? "Ext Check: ON" : "Ext Check: OFF";
            _extChkBtn.Pressed = hasExt;
            _extChkBtn.Disabled = locked;

            _intChkBtn.Text = hasInt ? "Int Check: ON" : "Int Check: OFF";
            _intChkBtn.Pressed = hasInt;
            _intChkBtn.Disabled = locked;

            _extChip.Disabled = locked;
            _extChip.ApplyServerValue(true, vent.ExternalPressureBound);
            _curExt = _extChip.Value;

            _intChip.Disabled = locked;
            _intChip.ApplyServerValue(true, vent.InternalPressureBound);
            _curInt = _intChip.Value;
        }
    }

    private sealed class ScrubberCardView
    {
        public ContentPanelContainer Root { get; }

        private readonly int _addressId;
        private readonly Action<BoundUserInterfaceMessage> _send;
        private readonly ContentLabel _titleLabel;
        private readonly ContentButton _powerBtn;
        private readonly ContentButton _dirBtn;
        private readonly ContentButton _wideBtn;
        private readonly ScrollNumberChip _rateChip;
        private readonly ContentButton _saveTplBtn;
        private readonly ContentButton _applyTplBtn;
        private readonly ContentButton _copyAllBtn;
        private readonly Dictionary<Gas, ContentButton> _gasButtons = new();
        private readonly ContentButton _allGasesBtn;
        private readonly ContentButton _noneGasesBtn;
        private readonly ContentButton _defaultGasesBtn;

        private string _displayAddress;
        private bool _curEnabled;
        private ScrubberPumpDirection _curDir;
        private float _curRate;
        private bool _curWideNet;
        private bool _panicWireCut;
        private List<Gas> _curGases;
        private AtmosTemplateEntryDto? _activeTemplate;

        public ScrubberCardView(AtmosScrubberEntryDto initial, Action<BoundUserInterfaceMessage> send)
        {
            _addressId = initial.AddressId;
            _displayAddress = initial.DisplayAddress;
            _send = send;
            _curEnabled = initial.Enabled;
            _curDir = initial.PumpDirection;
            _curRate = initial.VolumeRate;
            _curWideNet = initial.WideNet;
            _panicWireCut = initial.PanicWireCut;
            _curGases = new List<Gas>(initial.FilterGases);

            Root = CreateDeviceCard("AtmosScrubberCard", Color.FromHex("#121A26"), Color.FromHex("#48628A"));
            var box = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 4
            };

            var header = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4 };
            _titleLabel = new ContentLabel
            {
                Text = $"SCRUBBER {initial.DisplayAddress}",
                WordWrap = false,
                VerticalAlignment = VAlignment.Center,
                FontColorOverride = Color.FromHex("#99CCFF")
            };
            header.AddChild(_titleLabel);

            _powerBtn = new ContentButton { Name = "AtmosScrubberPowerButton", MinWidth = 44 };
            _powerBtn.OnPressed += _ => SendScrubber(enabled: !_curEnabled);
            header.AddChild(_powerBtn);

            _dirBtn = new ContentButton { Name = "AtmosScrubberDirectionButton", MinWidth = 78 };
            _dirBtn.OnPressed += _ => SendScrubber(dir: _curDir == ScrubberPumpDirection.Scrubbing
                ? ScrubberPumpDirection.Siphoning
                : ScrubberPumpDirection.Scrubbing);
            header.AddChild(_dirBtn);

            _wideBtn = new ContentButton { Name = "AtmosScrubberWideNetButton" };
            _wideBtn.OnPressed += _ => SendScrubber(wideNet: !_curWideNet);
            header.AddChild(_wideBtn);

            _rateChip = new ScrollNumberChip("Rate", initial.VolumeRate, 0f, Atmospherics.MaxTransferRate, 10f, " L/s", "0")
            {
                Name = "AtmosScrubberRateChip"
            };
            _rateChip.OnValueChanged += (_, v) => _curRate = v;
            _rateChip.OnValueCommitted += (_, v) => SendScrubber(rate: v);
            header.AddChild(_rateChip);

            _saveTplBtn = new ContentButton { Name = "AtmosSaveScrubberTemplateButton", Text = "Save Tpl", MinWidth = 64 };
            _saveTplBtn.OnPressed += _ =>
            {
                FlushPending();
                var snap = new AtmosScrubberEntryDto(
                    _addressId,
                    _displayAddress,
                    _curEnabled,
                    _curDir,
                    _curRate,
                    _curWideNet,
                    _panicWireCut,
                    new List<Gas>(_curGases));
                _send(new AtmosSaveTemplateMessage("Scrubber", AtmosTemplateKind.Scrubber, scrubber: snap));
            };
            header.AddChild(_saveTplBtn);

            _applyTplBtn = new ContentButton { Name = "AtmosApplyScrubberTemplateButton", Text = "Apply Tpl", Visible = false };
            _applyTplBtn.OnPressed += _ =>
            {
                if (_activeTemplate is { Kind: AtmosTemplateKind.Scrubber } tpl)
                {
                    ClearLocalHolds();
                    _send(new AtmosApplyTemplateMessage(tpl.Name, AtmosTemplateKind.Scrubber, _addressId, applyToAll: false));
                }
            };
            header.AddChild(_applyTplBtn);

            _copyAllBtn = new ContentButton { Name = "AtmosCopyScrubberAllButton", Text = "Copy All", MinWidth = 64 };
            _copyAllBtn.OnPressed += _ =>
            {
                FlushPending();
                SendScrubber(applyAll: true);
            };
            header.AddChild(_copyAllBtn);
            box.AddChild(header);

            var filterRow = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4 };
            var allGases = Enum.GetValues<Gas>();
            foreach (var gas in allGases)
            {
                var capturedGas = gas;
                var gBtn = new ContentButton
                {
                    Name = $"AtmosScrubberGas_{gas}",
                    Text = FormatGasShort(gas),
                    MinWidth = 42,
                    ToolTip = $"Toggle filtering {gas}"
                };
                gBtn.OnPressed += _ =>
                {
                    var updated = new List<Gas>(_curGases);
                    if (updated.Contains(capturedGas))
                        updated.Remove(capturedGas);
                    else
                        updated.Add(capturedGas);
                    SendScrubber(gases: updated);
                };
                _gasButtons[gas] = gBtn;
                filterRow.AddChild(gBtn);
            }

            _allGasesBtn = new ContentButton { Text = "All", MinWidth = 34 };
            _allGasesBtn.OnPressed += _ => SendScrubber(gases: allGases.ToList());
            filterRow.AddChild(_allGasesBtn);

            _noneGasesBtn = new ContentButton { Text = "None", MinWidth = 44 };
            _noneGasesBtn.OnPressed += _ => SendScrubber(gases: new List<Gas>());
            filterRow.AddChild(_noneGasesBtn);

            _defaultGasesBtn = new ContentButton { Text = "Default", MinWidth = 56 };
            _defaultGasesBtn.OnPressed += _ => SendScrubber(gases: GasVentScrubberData.DefaultFilterGases.ToList());
            filterRow.AddChild(_defaultGasesBtn);

            box.AddChild(filterRow);
            Root.AddChild(box);
        }

        public void ClearLocalHolds()
        {
            _rateChip.ClearLocalHold();
        }

        public void FlushPending()
        {
            _rateChip.FlushPending();
        }

        private void SendScrubber(
            bool? enabled = null,
            ScrubberPumpDirection? dir = null,
            float? rate = null,
            bool? wideNet = null,
            List<Gas>? gases = null,
            bool applyAll = false)
        {
            if (enabled is { } e) _curEnabled = e;
            if (dir is { } d) _curDir = d;
            if (rate is { } r) _curRate = r;
            if (wideNet is { } w) _curWideNet = w;
            if (gases != null) _curGases = new List<Gas>(gases);

            _send(new AtmosUpdateScrubberMessage(
                _addressId,
                _curEnabled,
                _curDir,
                _curRate,
                _curWideNet,
                new List<Gas>(_curGases),
                applyAll));
        }

        public void UpdateFromServer(AtmosScrubberEntryDto scrubber, bool locked, AtmosTemplateEntryDto? activeTpl)
        {
            _displayAddress = scrubber.DisplayAddress;
            _activeTemplate = activeTpl;
            _curEnabled = scrubber.Enabled;
            _curDir = scrubber.PumpDirection;
            _curWideNet = scrubber.WideNet;
            _panicWireCut = scrubber.PanicWireCut;
            _curGases = new List<Gas>(scrubber.FilterGases);

            _titleLabel.Text = $"SCRUBBER {scrubber.DisplayAddress}";

            _powerBtn.Text = scrubber.Enabled ? "ON" : "OFF";
            _powerBtn.Pressed = scrubber.Enabled;
            _powerBtn.Disabled = locked;

            _dirBtn.Text = scrubber.PumpDirection == ScrubberPumpDirection.Scrubbing ? "Scrubbing" : "Siphoning";
            _dirBtn.Pressed = scrubber.PumpDirection == ScrubberPumpDirection.Siphoning;
            _dirBtn.Disabled = locked || scrubber.PanicWireCut;

            _wideBtn.Text = scrubber.WideNet ? "WideNet: ON" : "WideNet: OFF";
            _wideBtn.Pressed = scrubber.WideNet;
            _wideBtn.Disabled = locked;

            _rateChip.Disabled = locked;
            _rateChip.ApplyServerValue(true, scrubber.VolumeRate);
            _curRate = _rateChip.Value;

            _saveTplBtn.Disabled = locked;
            _applyTplBtn.Visible = activeTpl is { Kind: AtmosTemplateKind.Scrubber };
            _applyTplBtn.Disabled = locked;
            _copyAllBtn.Disabled = locked;

            foreach (var (gas, btn) in _gasButtons)
            {
                btn.Pressed = scrubber.FilterGases.Contains(gas);
                btn.Disabled = locked;
            }

            _allGasesBtn.Disabled = locked;
            _noneGasesBtn.Disabled = locked;
            _defaultGasesBtn.Disabled = locked;
        }
    }

    private sealed class ThresholdRowView
    {
        public ContentWrapContainer Root { get; }

        private readonly ContentLabel _nameLabel;
        private readonly ContentButton _enableBtn;
        private readonly ScrollNumberChip _loDngChip;
        private readonly ScrollNumberChip _loWrnChip;
        private readonly ScrollNumberChip _hiWrnChip;
        private readonly ScrollNumberChip _hiDngChip;
        private readonly Func<float, float> _toDisplay;
        private readonly Func<float, float> _fromDisplay;
        private readonly Action<AtmosThresholdDto> _onChanged;

        private AtmosThresholdDto _cur;

        public AtmosThresholdDto CurrentThreshold => _cur;

        public ThresholdRowView(
            string label,
            AtmosThresholdDto initial,
            string unit,
            string format,
            float displayMin,
            float displayMax,
            float step,
            Func<float, float> toDisplay,
            Func<float, float> fromDisplay,
            Action<AtmosThresholdDto> onChanged)
        {
            _cur = initial;
            _toDisplay = toDisplay;
            _fromDisplay = fromDisplay;
            _onChanged = onChanged;

            Root = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 3 };

            _nameLabel = new ContentLabel
            {
                Text = label,
                MinWidth = 86,
                WordWrap = false,
                VerticalAlignment = VAlignment.Center
            };
            Root.AddChild(_nameLabel);

            _enableBtn = new ContentButton { MinWidth = 38 };
            _enableBtn.OnPressed += _ =>
            {
                _cur = new AtmosThresholdDto(
                    !_cur.Ignore,
                    _cur.LowerDangerEnabled, _cur.LowerDanger,
                    _cur.LowerWarningEnabled, _cur.LowerWarning,
                    _cur.UpperWarningEnabled, _cur.UpperWarning,
                    _cur.UpperDangerEnabled, _cur.UpperDanger);
                _onChanged(_cur);
            };
            Root.AddChild(_enableBtn);

            ScrollNumberChip MakeBound(
                string bLabel,
                bool bEnabled,
                float rawVal,
                Color accent,
                Func<AtmosThresholdDto, bool, float, AtmosThresholdDto> apply)
            {
                var dispVal = Math.Clamp(toDisplay(rawVal), displayMin, displayMax);
                var chip = new ScrollNumberChip(
                    bLabel,
                    dispVal,
                    displayMin,
                    displayMax,
                    step,
                    unit,
                    format,
                    optionalEnabled: true,
                    boundEnabled: bEnabled,
                    enableDefaultValue: dispVal > displayMin ? dispVal : Math.Clamp(displayMin + step, displayMin, displayMax))
                {
                    AccentColor = accent
                };
                chip.OnValueChanged += (en, dv) => _cur = apply(_cur, en, _fromDisplay(dv));
                chip.OnValueCommitted += (en, dv) =>
                {
                    _cur = apply(_cur, en, _fromDisplay(dv));
                    _onChanged(_cur);
                };
                Root.AddChild(chip);
                return chip;
            }

            _loDngChip = MakeBound("Lo Dng", initial.LowerDangerEnabled, initial.LowerDanger, Color.FromHex("#FF6666"),
                (t, en, v) => new AtmosThresholdDto(t.Ignore, en, v, t.LowerWarningEnabled, t.LowerWarning, t.UpperWarningEnabled, t.UpperWarning, t.UpperDangerEnabled, t.UpperDanger));

            _loWrnChip = MakeBound("Lo Wrn", initial.LowerWarningEnabled, initial.LowerWarning, Color.FromHex("#FFD166"),
                (t, en, v) => new AtmosThresholdDto(t.Ignore, t.LowerDangerEnabled, t.LowerDanger, en, v, t.UpperWarningEnabled, t.UpperWarning, t.UpperDangerEnabled, t.UpperDanger));

            _hiWrnChip = MakeBound("Hi Wrn", initial.UpperWarningEnabled, initial.UpperWarning, Color.FromHex("#FFD166"),
                (t, en, v) => new AtmosThresholdDto(t.Ignore, t.LowerDangerEnabled, t.LowerDanger, t.LowerWarningEnabled, t.LowerWarning, en, v, t.UpperDangerEnabled, t.UpperDanger));

            _hiDngChip = MakeBound("Hi Dng", initial.UpperDangerEnabled, initial.UpperDanger, Color.FromHex("#FF6666"),
                (t, en, v) => new AtmosThresholdDto(t.Ignore, t.LowerDangerEnabled, t.LowerDanger, t.LowerWarningEnabled, t.LowerWarning, t.UpperWarningEnabled, t.UpperWarning, en, v));
        }

        public void ClearLocalHolds()
        {
            _loDngChip.ClearLocalHold();
            _loWrnChip.ClearLocalHold();
            _hiWrnChip.ClearLocalHold();
            _hiDngChip.ClearLocalHold();
        }

        public void FlushPending()
        {
            _loDngChip.FlushPending();
            _loWrnChip.FlushPending();
            _hiWrnChip.FlushPending();
            _hiDngChip.FlushPending();
        }

        public void UpdateFromServer(string label, AtmosThresholdDto thr, bool locked)
        {
            _nameLabel.Text = label;
            _nameLabel.FontColorOverride = thr.Ignore ? Color.FromHex("#6B7C8C") : Color.FromHex("#CFEAF5");

            _enableBtn.Text = thr.Ignore ? "OFF" : "ON";
            _enableBtn.Pressed = !thr.Ignore;
            _enableBtn.Disabled = locked;

            _loDngChip.Disabled = locked;
            _loDngChip.ApplyServerValue(thr.LowerDangerEnabled, _toDisplay(thr.LowerDanger));

            _loWrnChip.Disabled = locked;
            _loWrnChip.ApplyServerValue(thr.LowerWarningEnabled, _toDisplay(thr.LowerWarning));

            _hiWrnChip.Disabled = locked;
            _hiWrnChip.ApplyServerValue(thr.UpperWarningEnabled, _toDisplay(thr.UpperWarning));

            _hiDngChip.Disabled = locked;
            _hiDngChip.ApplyServerValue(thr.UpperDangerEnabled, _toDisplay(thr.UpperDanger));

            _cur = new AtmosThresholdDto(
                thr.Ignore,
                _loDngChip.BoundEnabled, _loDngChip.HasLocalOverride ? _fromDisplay(_loDngChip.Value) : thr.LowerDanger,
                _loWrnChip.BoundEnabled, _loWrnChip.HasLocalOverride ? _fromDisplay(_loWrnChip.Value) : thr.LowerWarning,
                _hiWrnChip.BoundEnabled, _hiWrnChip.HasLocalOverride ? _fromDisplay(_hiWrnChip.Value) : thr.UpperWarning,
                _hiDngChip.BoundEnabled, _hiDngChip.HasLocalOverride ? _fromDisplay(_hiDngChip.Value) : thr.UpperDanger);
        }
    }

    private sealed class SensorCardView
    {
        public ContentPanelContainer Root { get; }

        private readonly int _addressId;
        private readonly Action<BoundUserInterfaceMessage> _send;
        private readonly StyleBoxFlat _cardStyle;
        private readonly ContentLabel _titleLabel;
        private readonly ContentButton _saveTplBtn;
        private readonly ContentButton _applyTplBtn;
        private readonly ContentBoxContainer _rowsBox;
        private readonly ThresholdRowView _pressureRow;
        private readonly ThresholdRowView _tempRow;
        private readonly Dictionary<Gas, ThresholdRowView> _gasRows = new();

        private AtmosTemplateEntryDto? _activeTemplate;

        public SensorCardView(AtmosSensorEntryDto initial, Action<BoundUserInterfaceMessage> send)
        {
            _addressId = initial.AddressId;
            _send = send;

            _cardStyle = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0D1C1F"),
                BorderColor = Color.FromHex("#246E5A"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 4,
                ContentMarginBottomOverride = 4
            };
            Root = new ContentPanelContainer
            {
                Name = "AtmosSensorCard",
                HorizontalExpand = true,
                PanelOverride = _cardStyle
            };

            _rowsBox = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 4
            };

            var header = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4 };
            _titleLabel = new ContentLabel
            {
                WordWrap = false,
                VerticalAlignment = VAlignment.Center
            };
            header.AddChild(_titleLabel);

            _saveTplBtn = new ContentButton { Name = "AtmosSaveSensorTemplateButton", Text = "Save Tpl", MinWidth = 64 };
            header.AddChild(_saveTplBtn);

            _applyTplBtn = new ContentButton { Name = "AtmosApplySensorTemplateButton", Text = "Apply Tpl", Visible = false };
            _applyTplBtn.OnPressed += _ =>
            {
                if (_activeTemplate is { Kind: AtmosTemplateKind.Sensor } tpl)
                {
                    ClearLocalHolds();
                    _send(new AtmosApplyTemplateMessage(tpl.Name, AtmosTemplateKind.Sensor, _addressId, applyToAll: false));
                }
            };
            header.AddChild(_applyTplBtn);
            _rowsBox.AddChild(header);

            _pressureRow = new ThresholdRowView(
                "Pressure",
                initial.PressureThreshold,
                unit: " kPa",
                format: "0",
                displayMin: 0f,
                displayMax: 5000f,
                step: 5f,
                toDisplay: v => v,
                fromDisplay: v => v,
                onChanged: updated => _send(new AtmosUpdateSensorThresholdMessage(
                    _addressId, AtmosMonitorThresholdType.Pressure, updated)));
            _rowsBox.AddChild(_pressureRow.Root);

            _tempRow = new ThresholdRowView(
                "Temp",
                initial.TemperatureThreshold,
                unit: " C",
                format: "0",
                displayMin: -270f,
                displayMax: 1000f,
                step: 5f,
                toDisplay: k => k - 273.15f,
                fromDisplay: c => MathF.Max(0.1f, c + 273.15f),
                onChanged: updated => _send(new AtmosUpdateSensorThresholdMessage(
                    _addressId, AtmosMonitorThresholdType.Temperature, updated)));
            _rowsBox.AddChild(_tempRow.Root);

            _saveTplBtn.OnPressed += _ =>
            {
                FlushPending();
                var gasMap = new Dictionary<Gas, AtmosThresholdDto>();
                foreach (var (g, r) in _gasRows)
                    gasMap[g] = r.CurrentThreshold;

                var tpl = new AtmosSensorTemplateDto(
                    _pressureRow.CurrentThreshold,
                    _tempRow.CurrentThreshold,
                    gasMap);
                _send(new AtmosSaveTemplateMessage("Sensor", AtmosTemplateKind.Sensor, sensor: tpl));
            };

            Root.AddChild(_rowsBox);
        }

        public void ClearLocalHolds()
        {
            _pressureRow.ClearLocalHolds();
            _tempRow.ClearLocalHolds();
            foreach (var r in _gasRows.Values)
                r.ClearLocalHolds();
        }

        public void FlushPending()
        {
            _pressureRow.FlushPending();
            _tempRow.FlushPending();
            foreach (var r in _gasRows.Values)
                r.FlushPending();
        }

        public void UpdateFromServer(AtmosSensorEntryDto sensor, bool locked, AtmosTemplateEntryDto? activeTpl)
        {
            _activeTemplate = activeTpl;

            _cardStyle.BorderColor = sensor.AlarmState switch
            {
                AtmosAlarmType.Danger => Color.FromHex("#B83B3B"),
                AtmosAlarmType.Warning => Color.FromHex("#B88B28"),
                _ => Color.FromHex("#246E5A")
            };

            var tempC = sensor.Temperature - 273.15f;
            _titleLabel.Text = $"SENSOR {sensor.DisplayAddress} [{sensor.AlarmState}] — {sensor.Pressure:0.0} kPa | {tempC:0.0} C";
            _titleLabel.FontColorOverride = sensor.AlarmState switch
            {
                AtmosAlarmType.Danger => Color.FromHex("#FF7777"),
                AtmosAlarmType.Warning => Color.FromHex("#FFD166"),
                _ => Color.FromHex("#77FFBB")
            };

            _saveTplBtn.Disabled = locked;
            _applyTplBtn.Visible = activeTpl is { Kind: AtmosTemplateKind.Sensor };
            _applyTplBtn.Disabled = locked;

            _pressureRow.UpdateFromServer("Pressure", sensor.PressureThreshold, locked);
            _tempRow.UpdateFromServer("Temp", sensor.TemperatureThreshold, locked);

            var totalMoles = sensor.TotalMoles > 0f ? sensor.TotalMoles : 1f;
            foreach (var gas in Enum.GetValues<Gas>())
            {
                if (!sensor.GasThresholds.TryGetValue(gas, out var gThr))
                    continue;

                var moles = sensor.Gases.TryGetValue(gas, out var m) ? m : 0f;
                var pct = sensor.TotalMoles > 0f ? (moles / totalMoles) * 100f : 0f;
                var rowLabel = $"{FormatGasShort(gas)} ({pct:0.#}%)";

                if (!_gasRows.TryGetValue(gas, out var gRow))
                {
                    var capturedGas = gas;
                    gRow = new ThresholdRowView(
                        rowLabel,
                        gThr,
                        unit: "%",
                        format: "0",
                        displayMin: 0f,
                        displayMax: 100f,
                        step: 1f,
                        toDisplay: f => f * 100f,
                        fromDisplay: p => Math.Clamp(p / 100f, 0f, 1f),
                        onChanged: updated => _send(new AtmosUpdateSensorThresholdMessage(
                            _addressId, AtmosMonitorThresholdType.Gas, updated, capturedGas)));
                    _gasRows[gas] = gRow;
                    _rowsBox.AddChild(gRow.Root);
                }

                gRow.UpdateFromServer(rowLabel, gThr, locked);
            }
        }
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
    private const float BaseChromeHeight = 372f;
    private const float MinExpandedWindowHeight = 640f;

    public float DesiredExpandedWindowHeight { get; private set; } = MinExpandedWindowHeight;

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
            MinHeight = 56f,
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
            MinHeight = 28f,
            SeparationOverride = 6
        };

        _searchInput = new ContentLineEdit
        {
            Name = "LatheRecipeSearch",
            HorizontalExpand = true,
            MinWidth = 220f,
            MinHeight = 26f,
            VerticalAlignment = VAlignment.Center,
            PlaceHolder = "Filter blueprints (name, description, material)..."
        };
        _searchInput.OnTextChanged += _ => RebuildRecipeList();

        _blueprintCountLabel = new ContentLabel
        {
            Name = "LatheBlueprintCountLabel",
            Text = "Designs: 0",
            WordWrap = false,
            VerticalAlignment = VAlignment.Center,
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
            MinExpandedWindowHeight,
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
