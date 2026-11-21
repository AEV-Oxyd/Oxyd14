using Content.Client._Oxyd.Computers.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._Oxyd.Computers.Programs;

// =============================================================================
// APC Control UI (via DeviceLink)
// =============================================================================

public sealed class ApcControlUiControl : ProgramUiControl
{
    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _lockStatusLabel;
    private readonly ContentLabel _telemetryLabel;
    private readonly ContentLabel _statusLabel;
    private readonly List<ContentButton> _controlButtons = new();

    public ApcControlUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        var root = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, Margin = new Thickness(6) };
        _headerLabel = new ContentLabel { Text = "AREA POWER CONTROLLER [LOCKED]", HorizontalExpand = true, FontColorOverride = Color.FromHex("#FFA726") };
        _lockStatusLabel = new ContentLabel { Text = "Status: LOCKED — Swipe authorized ID card on APC to unlock", HorizontalExpand = true, FontColorOverride = Color.FromHex("#FF5555"), Margin = new Thickness(0, 4, 0, 2) };
        _telemetryLabel = new ContentLabel { Text = "Waiting for DeviceLink telemetry...", HorizontalExpand = true, Margin = new Thickness(0, 4) };
        _statusLabel = new ContentLabel { Text = "", HorizontalExpand = true, FontColorOverride = Color.FromHex("#88CCCC"), Margin = new Thickness(0, 4) };

        var btnRow = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4, Margin = new Thickness(0, 6) };
        void AddBtn(string text, ApcControlAction action)
        {
            var b = new ContentButton { Text = text, Disabled = true };
            b.OnPressed += _ => SendProgramMessage(new ApcControlActionMessage(action));
            _controlButtons.Add(b);
            btnRow.AddChild(b);
        }

        AddBtn("Toggle Breaker", ApcControlAction.ToggleBreaker);
        AddBtn("Breaker ON", ApcControlAction.SetBreakerOn);
        AddBtn("Breaker OFF", ApcControlAction.SetBreakerOff);
        AddBtn("Refresh", ApcControlAction.Refresh);
        AddBtn("Save Log (.txt)", ApcControlAction.SaveLog);

        root.AddChild(_headerLabel);
        root.AddChild(_lockStatusLabel);
        root.AddChild(_telemetryLabel);
        root.AddChild(btnRow);
        root.AddChild(_statusLabel);
        scroll.AddChild(root);
        AddChild(scroll);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not ApcControlUiState state)
            return;

        _headerLabel.Text = $"APC: {state.AreaName} | {(state.Locked ? "[LOCKED]" : "[UNLOCKED]")} | DeviceLink: {(state.IsDeviceLinkEnabled ? "CONNECTED" : "SEVERED")}";
        _lockStatusLabel.Text = state.Locked
            ? "Status: LOCKED — Swipe authorized ID card on APC to unlock"
            : "Status: UNLOCKED — Swipe authorized ID card on APC to lock";
        _lockStatusLabel.FontColorOverride = state.Locked ? Color.FromHex("#FF5555") : Color.FromHex("#55FF88");
        _telemetryLabel.Text =
            $"Main Breaker: {(state.MainBreakerEnabled ? "CLOSED (POWER ON)" : "OPEN (OFF)")} {(state.BreakerTripped ? "[TRIPPED]" : "")}\n" +
            $"Battery Charge: {state.ChargePercent:0.0}%\n" +
            $"Current Load: {state.LoadWatts} W\n" +
            $"External Supply: {state.ExternalPowerState}";
        _statusLabel.Text = state.StatusMessage;

        foreach (var btn in _controlButtons)
            btn.Disabled = state.Locked;
    }
}

// =============================================================================
// Air Alarm Control UI (via DeviceLink)
// =============================================================================

public sealed class AtmosControlUiControl : ProgramUiControl
{
    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _lockStatusLabel;
    private readonly ContentLabel _telemetryLabel;
    private readonly ContentLabel _statusLabel;
    private readonly List<ContentButton> _controlButtons = new();

    public AtmosControlUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        var root = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, Margin = new Thickness(6) };
        _headerLabel = new ContentLabel { Text = "AIR ALARM CONTROLLER [LOCKED]", HorizontalExpand = true, FontColorOverride = Color.FromHex("#33B5E5") };
        _lockStatusLabel = new ContentLabel { Text = "Status: LOCKED — Swipe authorized ID card on Air Alarm to unlock", HorizontalExpand = true, FontColorOverride = Color.FromHex("#FF5555"), Margin = new Thickness(0, 4, 0, 2) };
        _telemetryLabel = new ContentLabel { Text = "Waiting for DeviceLink telemetry...", HorizontalExpand = true, Margin = new Thickness(0, 4) };
        _statusLabel = new ContentLabel { Text = "", HorizontalExpand = true, FontColorOverride = Color.FromHex("#88CCCC"), Margin = new Thickness(0, 4) };

        var modeRow = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4, Margin = new Thickness(0, 4) };
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

        var ctrlRow = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4, Margin = new Thickness(0, 4) };
        AddBtn(ctrlRow, "Toggle Auto Mode", AtmosControlAction.ToggleAutoMode);
        AddBtn(ctrlRow, "Resync Devices", AtmosControlAction.ResyncDevices);
        AddBtn(ctrlRow, "Refresh", AtmosControlAction.Refresh);
        AddBtn(ctrlRow, "Save Log (.txt)", AtmosControlAction.SaveLog);

        root.AddChild(_headerLabel);
        root.AddChild(_lockStatusLabel);
        root.AddChild(_telemetryLabel);
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

        _headerLabel.Text = $"AIR ALARM | {(state.Locked ? "[LOCKED]" : "[UNLOCKED]")} | State: {state.AlarmLevel} | Mode: {state.CurrentMode} (Auto: {state.AutoMode}) | Link: {(state.IsDeviceLinkEnabled ? "OK" : "CUT")}";
        _lockStatusLabel.Text = state.Locked
            ? "Status: LOCKED — Swipe authorized ID card on Air Alarm to unlock"
            : "Status: UNLOCKED — Swipe authorized ID card on Air Alarm to lock";
        _lockStatusLabel.FontColorOverride = state.Locked ? Color.FromHex("#FF5555") : Color.FromHex("#55FF88");
        _telemetryLabel.Text =
            $"Pressure: {state.PressureKpa:0.0} kPa | Temperature: {state.TemperatureCelsius:0.0} C\n" +
            $"Devices: {state.SensorCount} sensors, {state.VentCount} vents, {state.ScrubberCount} scrubbers\n" +
            $"{state.GasSummary}";
        _statusLabel.Text = state.StatusMessage;

        foreach (var btn in _controlButtons)
            btn.Disabled = state.Locked;
    }
}

// =============================================================================
// Autolathe Control UI (via DeviceLink + HDD .lathe blueprints)
// =============================================================================

public sealed class LatheControlUiControl : ProgramUiControl
{
    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _materialsLabel;
    private readonly ContentLabel _statusLabel;
    private readonly ContentBoxContainer _recipesContainer;

    public LatheControlUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(6) };
        _headerLabel = new ContentLabel { Text = "AUTOLATHE CONTROLLER", HorizontalExpand = true, FontColorOverride = Color.FromHex("#90A4AE") };
        _materialsLabel = new ContentLabel { Text = "Materials: ---", HorizontalExpand = true, Margin = new Thickness(0, 4) };
        _statusLabel = new ContentLabel { Text = "", HorizontalExpand = true, FontColorOverride = Color.FromHex("#88CCCC"), Margin = new Thickness(0, 2, 0, 4) };

        var actRow = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4, Margin = new Thickness(0, 0, 0, 4) };
        var refBtn = new ContentButton { Text = "Refresh" };
        refBtn.OnPressed += _ => SendProgramMessage(new LatheControlActionMessage(LatheControlAction.Refresh));
        var cancelBtn = new ContentButton { Text = "Cancel Queue" };
        cancelBtn.OnPressed += _ => SendProgramMessage(new LatheControlActionMessage(LatheControlAction.CancelAll));
        actRow.AddChild(refBtn);
        actRow.AddChild(cancelBtn);

        root.AddChild(_headerLabel);
        root.AddChild(_materialsLabel);
        root.AddChild(actRow);
        root.AddChild(_statusLabel);

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _recipesContainer = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        scroll.AddChild(_recipesContainer);
        root.AddChild(scroll);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not LatheControlUiState state)
            return;

        _headerLabel.Text = $"AUTOLATHE | {state.LatheStatus} | Printing: {(string.IsNullOrEmpty(state.CurrentPrinting) ? "Idle" : state.CurrentPrinting)} | Queue: {state.QueueRecipes.Count} | Link: {(state.IsDeviceLinkEnabled ? "OK" : "CUT")}";
        _materialsLabel.Text = state.Materials.Count > 0
            ? "Materials: " + string.Join(" | ", System.Linq.Enumerable.Select(state.Materials, kv => $"{kv.Key}: {kv.Value}"))
            : "Materials: Empty";
        _statusLabel.Text = state.StatusMessage;

        _recipesContainer.RemoveAllChildren();
        foreach (var bp in state.AvailableRecipes)
        {
            var row = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 2) };
            var tag = string.IsNullOrEmpty(bp.FilePath) ? "[Static]" : $"[{bp.FilePath}{(bp.RemainingUses != null ? $" ({bp.RemainingUses}x)" : "")}{(bp.IsEncrypted ? " ENC" : "")}]";
            row.AddChild(new ContentLabel { Text = $"{bp.RecipeName} {tag}", HorizontalExpand = true });

            if (bp.IsEncrypted)
            {
                var decBtn = new ContentButton { Text = "Decrypt", MinWidth = 65 };
                decBtn.OnPressed += _ => SendProgramMessage(new LatheControlActionMessage(LatheControlAction.DecryptBlueprint, bp.RecipeId, bp.FilePath));
                row.AddChild(decBtn);
            }
            else
            {
                var printBtn = new ContentButton { Text = "Print", MinWidth = 55 };
                printBtn.OnPressed += _ => SendProgramMessage(new LatheControlActionMessage(LatheControlAction.PrintRecipe, bp.RecipeId, bp.FilePath, 1));
                row.AddChild(printBtn);
            }
            _recipesContainer.AddChild(row);
        }
    }
}
