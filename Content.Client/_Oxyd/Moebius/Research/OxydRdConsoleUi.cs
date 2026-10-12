// Eris Moebius research port: R&D console client UI.
// Eris source: nano/templates/rdconsole.tmpl — left sciMenuButton menu,
// destructive-analyzer card, tech level displayBars, lathe design rows,
// disk management block. Stock controls only (ErisSurgeryStyle widgets).
using System.Linq;
using System.Numerics;
using Content.Client._Oxyd.Medical.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Moebius.Research;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Oxyd.Moebius.Research;

/// <summary>BUI for the Moebius R&D console. Name intentionally does not reuse
/// an existing *BoundUserInterface suffix (LooseGetType Endswith collision).</summary>
public sealed class OxydRdConsoleBui : BoundUserInterface
{
    private OxydRdWindow? _window;

    public OxydRdConsoleBui(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        if (_window != null)
            return;
        _window = this.CreateWindow<OxydRdWindow>();
        _window.SendMessageAction = msg => SendMessage(msg);
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is OxydRdState s)
            _window?.UpdateState(s);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _window?.Dispose();
            _window = null;
        }
        base.Dispose(disposing);
    }
}

public sealed class OxydRdWindow : DefaultWindow
{
    public Action<BoundUserInterfaceMessage>? SendMessageAction;
    private OxydRdState? _state;
    private readonly Label _points;
    private readonly BoxContainer _menu;
    private readonly PanelContainer _body;

    public OxydRdWindow()
    {
        Title = "Moebius R&D Control Console";
        SetSize = new Vector2(720, 560);
        MinSize = new Vector2(520, 420);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        Contents.AddChild(root);

        // Eris left column: menu pills + points readout.
        var left = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            MinWidth = 150,
        };
        root.AddChild(left);
        var head = new Label { Text = "Moebius R&D", FontColorOverride = ErisSurgeryColors.ItemLabelText };
        left.AddChild(head);
        _points = new Label { FontColorOverride = ErisSurgeryColors.GoodText };
        left.AddChild(_points);
        _menu = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        left.AddChild(_menu);

        _body = new PanelContainer { HorizontalExpand = true, VerticalExpand = true };
        root.AddChild(_body);
    }

    private void Msg(BoundUserInterfaceMessage m) => SendMessageAction?.Invoke(m);

    public void UpdateState(OxydRdState state)
    {
        _state = state;
        _points.Text = $"Points: {state.Points}";
        RebuildMenu();
        var scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        var col = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, HorizontalExpand = true };
        scroll.AddChild(col);
        _body.RemoveAllChildren();
        _body.AddChild(scroll);
        switch (state.Screen)
        {
            case OxydRdScreen.Main: BuildMain(col); break;
            case OxydRdScreen.Lathe: BuildLathe(col); break;
            case OxydRdScreen.Trees: BuildTrees(col); break;
            case OxydRdScreen.Disk: BuildDisk(col); break;
        }
    }

    private void RebuildMenu()
    {
        _menu.RemoveAllChildren();
        AddMenuLink("Main", OxydRdScreen.Main);
        AddMenuLink("Protolathe / Fab", OxydRdScreen.Lathe);
        AddMenuLink("Tech Trees", OxydRdScreen.Trees);
        AddMenuLink("Disk Management", OxydRdScreen.Disk);
    }

    private void AddMenuLink(string text, OxydRdScreen screen)
    {
        var link = new ErisLink(text);
        if (_state != null && _state.Screen == screen)
            link.SetSelectedLook();
        link.OnPressed += _ => Msg(new OxydRdScreenMessage { Screen = screen });
        _menu.AddChild(link);
    }

    private static Label H(string text) => new() { Text = text, FontColorOverride = ErisSurgeryColors.ItemLabelText };

    // ---- Main screen: DA card + tech bars + tool upload + devices ----
    private void BuildMain(BoxContainer col)
    {
        var s = _state!;
        // Destructive analyzer card (Eris DA block on screen=main).
        var da = new ErisPanel();
        var dcol = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 5 };
        da.AddChild(dcol);
        dcol.AddChild(H("Destructive Analyzer"));
        if (s.Da.HasItem)
        {
            var row = new ErisSpecRow("Loaded item:");
            row.AddValue(new Label { Text = s.Da.ItemName, FontColorOverride = ErisSurgeryColors.Text });
            dcol.AddChild(row);
            foreach (var t in s.Da.TechLines)
                dcol.AddChild(new Label { Text = "  " + t, FontColorOverride = ErisSurgeryColors.GoodText });
            var btns = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
            var deconstruct = new ErisLink("Deconstruct", red: true);
            deconstruct.OnPressed += _ => Msg(new OxydRdDeconstructMessage());
            if (s.Da.Busy)
                deconstruct.SetDisabledLook();
            var eject = new ErisLink("Eject Item");
            eject.OnPressed += _ => Msg(new OxydRdEjectItemMessage());
            if (s.Da.Busy)
                eject.SetDisabledLook();
            btns.AddChild(deconstruct);
            btns.AddChild(eject);
            dcol.AddChild(btns);
            if (s.Da.Busy)
                dcol.AddChild(new Label { Text = "Analysis in progress...", FontColorOverride = ErisSurgeryColors.WoundCountText });
        }
        else
        {
            dcol.AddChild(new ErisSubLabel("Insert an item into the linked analyzer."));
        }
        col.AddChild(da);

        // Science tool upload (Eris: upload datablocks at console).
        var tool = new ErisPanel();
        var tcol = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 5 };
        tool.AddChild(tcol);
        tcol.AddChild(H("Experiment Data"));
        tcol.AddChild(new ErisSubLabel($"Stored data blocks in tool slot: {s.ToolBlocks}"));
        var up = new ErisLink("Upload Data");
        up.OnPressed += _ => Msg(new OxydRdUploadToolMessage());
        if (s.ToolBlocks == 0)
            up.SetDisabledLook();
        tcol.AddChild(up);
        col.AddChild(tool);

        // Tech tree level bars (Eris displayBar per tree).
        var tp = new ErisPanel();
        var pcol = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 5 };
        tp.AddChild(pcol);
        pcol.AddChild(H("Technology Levels"));
        var maxLvl = Math.Max(3, s.Trees.Count > 0 ? s.Trees.Max(t => t.Level) : 0);
        foreach (var t in s.Trees)
        {
            if (!t.Shown)
                continue;
            var row = new ErisSpecRow($"{t.Name}:");
            var bar = new ErisBar();
            bar.SetWidth = 220;
            var frac = t.Level / (float) Math.Max(1, maxLvl);
            bar.SetValue(frac, t.Color, $"{t.Level}");
            row.AddValue(bar);
            pcol.AddChild(row);
        }
        col.AddChild(tp);

        // Linked devices list (Eris linkage block).
        var dev = new ErisPanel();
        var vcol = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        dev.AddChild(vcol);
        vcol.AddChild(H("Linked Devices"));
        if (s.Devices.Count == 0)
            vcol.AddChild(new ErisSubLabel("No devices in range."));
        foreach (var d in s.Devices)
            vcol.AddChild(new ErisSubLabel($"  {d.Kind}: {d.Name}"));
        var rescan = new ErisLink("Rescan Devices");
        rescan.OnPressed += _ => Msg(new OxydRdRescanMessage());
        vcol.AddChild(rescan);
        col.AddChild(dev);
    }

    // ---- Lathe screen: device picker + design rows ----
    private void BuildLathe(BoxContainer col)
    {
        var s = _state!;
        var picker = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        picker.AddChild(H("Device:"));
        foreach (var d in s.Devices)
        {
            if (d.Kind == OxydRdDeviceKind.Analyzer)
                continue;
            var l = new ErisLink(d.Name);
            if (s.SelectedDevice == d.Uid)
                l.SetSelectedLook();
            var dev = d.Uid;
            l.OnPressed += _ => Msg(new OxydRdSelectDeviceMessage { Device = dev });
            picker.AddChild(l);
        }
        col.AddChild(picker);

        var list = new ErisPanel();
        var lcol = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        list.AddChild(lcol);
        lcol.AddChild(H("Known Designs"));
        if (s.Designs.Count == 0)
            lcol.AddChild(new ErisSubLabel("No designs. Link a lathe or research more tech."));
        var i = 0;
        foreach (var d in s.Designs)
        {
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8, HorizontalExpand = true };
            var name = new Label { Text = d.Name, HorizontalExpand = true, FontColorOverride = ErisSurgeryColors.Text, ClipText = true };
            row.AddChild(name);
            var detail = new Label
            {
                Text = d.CanPrint ? "ready" : $"needs {d.Missing}",
                FontColorOverride = d.CanPrint ? ErisSurgeryColors.GoodText : ErisSurgeryColors.BadText,
            };
            row.AddChild(detail);
            var print = new ErisLink("Queue");
            if (!d.CanPrint)
                print.SetDisabledLook();
            var rid = d.RecipeId;
            print.OnPressed += _ => Msg(new OxydRdPrintMessage { Recipe = rid, Device = s.SelectedDevice ?? default, Amount = 1 });
            row.AddChild(print);
            lcol.AddChild(ErisCandystripe.Wrap(row, i++));
        }
        col.AddChild(list);
    }

    // ---- Trees screen: tree picker + node rows ----
    private void BuildTrees(BoxContainer col)
    {
        var s = _state!;
        var picker = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        foreach (var t in s.Trees)
        {
            if (!t.Shown)
                continue;
            var l = new ErisLink($"{t.Name} {t.Level}");
            if (s.SelectedTree == t.Id)
                l.SetSelectedLook();
            var id = t.Id;
            l.OnPressed += _ => Msg(new OxydRdSelectTreeMessage { Tree = id });
            picker.AddChild(l);
        }
        picker.AddChild(new ErisSubLabel($"  Points available: {s.Points}"));
        col.AddChild(picker);

        var list = new ErisPanel();
        var lcol = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        list.AddChild(lcol);
        var i = 0;
        foreach (var n in s.Nodes)
        {
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8, HorizontalExpand = true };
            var name = new Label { Text = $"{n.Name} ({n.Cost})", HorizontalExpand = true, FontColorOverride = ErisSurgeryColors.Text, ClipText = true };
            row.AddChild(name);
            string status;
            if (n.State == 2)
                status = "researched";
            else if (n.Missing.Count > 0)
                status = $"requires {string.Join(", ", n.Missing)}";
            else if (n.Cost > s.Points)
                status = "not enough points";
            else
                status = "available";
            row.AddChild(new Label
            {
                Text = status,
                FontColorOverride = n.State == 2 ? ErisSurgeryColors.GoodText :
                    n.State == 1 ? ErisSurgeryColors.Text : ErisSurgeryColors.BadText,
            });
            if (n.Unlocks.Count > 0)
                row.AddChild(new ErisSubLabel($"unlocks: {string.Join(", ", n.Unlocks)}"));
            var go = new ErisLink("Research");
            if (n.State != 1)
                go.SetDisabledLook();
            var nid = n.Id;
            go.OnPressed += _ => Msg(new OxydRdUnlockMessage { Node = nid });
            row.AddChild(go);
            lcol.AddChild(ErisCandystripe.Wrap(row, i++));
        }
        col.AddChild(list);
    }

    // ---- Disk screen ----
    private void BuildDisk(BoxContainer col)
    {
        var s = _state!;
        var p = new ErisPanel();
        var dcol = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        p.AddChild(dcol);
        dcol.AddChild(H("Disk Management"));
        if (!s.HasDisk)
        {
            dcol.AddChild(new ErisSubLabel("No disk loaded."));
        }
        else if (s.DiskIsPointsDisk)
        {
            dcol.AddChild(new ErisSubLabel(s.DiskSpent
                ? "Research data disk (spent)."
                : $"Research data disk: +{s.DiskPoints} points available."));
            var load = new ErisLink("Load Data");
            if (s.DiskSpent)
                load.SetDisabledLook();
            load.OnPressed += _ => Msg(new OxydRdDiskMessage { Op = OxydRdDiskOp.Upload });
            dcol.AddChild(load);
        }
        else
        {
            dcol.AddChild(new ErisSubLabel($"Data disk: {s.DiskNodeCount} techs, {s.DiskDesignCount} designs stored."));
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
            var load = new ErisLink("Load Data");
            load.OnPressed += _ => Msg(new OxydRdDiskMessage { Op = OxydRdDiskOp.Upload });
            var save = new ErisLink("Save Data");
            save.OnPressed += _ => Msg(new OxydRdDiskMessage { Op = OxydRdDiskOp.Download });
            var clear = new ErisLink("Reset Disk", red: true);
            clear.OnPressed += _ => Msg(new OxydRdDiskMessage { Op = OxydRdDiskOp.Clear });
            row.AddChild(load);
            row.AddChild(save);
            row.AddChild(clear);
            dcol.AddChild(row);
        }
        var eject = new ErisLink("Eject Disk");
        if (!s.HasDisk)
            eject.SetDisabledLook();
        eject.OnPressed += _ => Msg(new OxydRdDiskMessage { Op = OxydRdDiskOp.Eject });
        dcol.AddChild(eject);
        col.AddChild(p);
    }
}
