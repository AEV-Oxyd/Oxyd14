// Eris Moebius research port: R&D console client UI.
// Eris source: nano/templates/rdconsole.tmpl — left sciMenuButton menu,
// destructive-analyzer card, tech level displayBars, lathe design rows,
// disk management block. Stock controls only (ErisSurgeryStyle widgets).
using System.Linq;
using System.Numerics;
using Content.Client._Oxyd.Medical.UI;
using Content.Client.UserInterface.Controls;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Content.Shared._Oxyd.Moebius.Research;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
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
    private string? _selectedNode;
    private readonly Label _points;
    private readonly BoxContainer _menu;
    private readonly PanelContainer _body;

    public OxydRdWindow()
    {
        Title = "Moebius R&D Control Console";
        // Wide enough for the Eris tech map (32px icons on the 0..1 canvas) plus the 200px detail pane.
        SetSize = new Vector2(1240, 900);
        MinSize = new Vector2(980, 700);

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
        _body.RemoveAllChildren();
        // The tech map is a fixed canvas. A scroll view clips the side pane and the tree tabs.
        if (state.Screen == OxydRdScreen.Trees)
        {
            var col = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                SeparationOverride = 8,
                HorizontalExpand = true,
                VerticalExpand = true,
            };
            _body.AddChild(col);
            BuildTrees(col);
            return;
        }

        var scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        var list = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, HorizontalExpand = true };
        scroll.AddChild(list);
        _body.AddChild(scroll);
        switch (state.Screen)
        {
            case OxydRdScreen.Main: BuildMain(list); break;
            case OxydRdScreen.Lathe: BuildLathe(list); break;
            case OxydRdScreen.Disk: BuildDisk(list); break;
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
        else if (!s.Da.Linked)
        {
            dcol.AddChild(new ErisSubLabel("No destructive analyzer linked."));
        }
        else
        {
            dcol.AddChild(new ErisSubLabel("No item loaded inside the destructive analyzer."));
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
            var cell = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8, HorizontalExpand = true };
            var name = new Label { Text = d.Name, HorizontalExpand = true, FontColorOverride = ErisSurgeryColors.Text, ClipText = true };
            row.AddChild(name);
            var print = new ErisLink("Queue");
            if (!d.CanPrint)
                print.SetDisabledLook();
            var rid = d.RecipeId;
            print.OnPressed += _ => Msg(new OxydRdPrintMessage { Recipe = rid, Device = s.SelectedDevice ?? default, Amount = 1 });
            cell.AddChild(row);
            cell.AddChild(print);
            cell.AddChild(new Label
            {
                Text = d.CanPrint ? "ready" : $"needs {d.Missing}",
                FontColorOverride = d.CanPrint ? ErisSurgeryColors.GoodText : ErisSurgeryColors.BadText,
                ClipText = true,
            });
            lcol.AddChild(ErisCandystripe.Wrap(cell, i++));
        }
        col.AddChild(list);
    }

    // Eris rdconsole tech_trees: 32px science.png icons on an absolute canvas,
    // white elbow connectors, and a side pane (name, description, cost, requirements, unlocks).
    private const float MapW = 700f;
    private const float MapH = 520f;
    private const float NodeSize = 40f;
    private const float SideW = 250f;

    private static readonly Color NodeResearched = Color.FromHex("#2f943c");
    private static readonly Color NodeLocked = Color.FromHex("#942f3c");
    private static readonly Color NodeOpen = Color.FromHex("#2c3444");
    private static readonly Color NodeSelected = Color.FromHex("#3333cc");

    private SpriteSystem? _sprites;

    private void BuildTrees(BoxContainer col)
    {
        var s = _state!;
        var current = s.Trees.FirstOrDefault(t => t.Id == s.SelectedTree);
        col.AddChild(new Label
        {
            Text = $"Research points: {s.Points}    {current?.Name ?? "Tech"} level: {current?.Level ?? 0}",
            FontColorOverride = ErisSurgeryColors.Text,
        });

        var picker = new GridContainer { Columns = 3 };
        foreach (var t in s.Trees)
        {
            if (!t.Shown)
                continue;
            var l = new ErisLink($"{t.Name}  {t.Level}");
            if (s.SelectedTree == t.Id)
                l.SetSelectedLook();
            var id = t.Id;
            l.OnPressed += _ => Msg(new OxydRdSelectTreeMessage { Tree = id });
            picker.AddChild(l);
        }
        col.AddChild(picker);

        var split = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 10,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        col.AddChild(split);

        var map = new LayoutContainer { SetSize = new Vector2(MapW, MapH) };
        var canvas = new PanelContainer
        {
            SetSize = new Vector2(MapW, MapH),
            MouseFilter = Control.MouseFilterMode.Ignore,
            PanelOverride = new StyleBoxFlat(Color.FromHex("#14141c"))
            {
                BorderColor = Color.FromHex("#40628a"),
                BorderThickness = new Thickness(1),
            },
        };
        map.AddChild(canvas);
        LayoutContainer.SetPosition(canvas, Vector2.Zero);

        var byId = s.Nodes.ToDictionary(n => n.Id);
        foreach (var node in s.Nodes)
        {
            var ax = node.X * MapW;
            var ay = (1f - node.Y) * MapH;
            foreach (var req in node.Requires)
            {
                if (!byId.TryGetValue(req, out var from))
                    continue;
                AddElbow(map, new Vector2(from.X * MapW, (1f - from.Y) * MapH), new Vector2(ax, ay));
            }
        }

        foreach (var node in s.Nodes)
            AddNode(map, node);

        split.AddChild(map);

        var sideScroll = new ScrollContainer
        {
            SetWidth = SideW,
            VerticalExpand = true,
            MinHeight = MapH,
        };
        var side = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            SetWidth = SideW - 8,
        };
        sideScroll.AddChild(side);
        split.AddChild(sideScroll);

        var selected = s.Nodes.FirstOrDefault(n => n.Id == _selectedNode) ?? s.Nodes.FirstOrDefault();
        if (selected == null)
        {
            side.AddChild(WrapText("Select a technology.", ErisSurgeryColors.Text));
            return;
        }

        _selectedNode = selected.Id;
        side.AddChild(WrapText(selected.Name, ErisSurgeryColors.ItemLabelText));
        side.AddChild(WrapText(string.IsNullOrEmpty(selected.Desc) ? "No description." : selected.Desc, ErisSurgeryColors.Text));
        side.AddChild(WrapText("Cost", ErisSurgeryColors.ItemLabelText));
        side.AddChild(WrapText($"{selected.Cost} Research Points", ErisSurgeryColors.Text));
        side.AddChild(WrapText("Requirements", ErisSurgeryColors.ItemLabelText));
        if (selected.Prereqs.Count == 0)
            side.AddChild(WrapText("None", ErisSurgeryColors.LabelText));
        foreach (var req in selected.Prereqs)
            side.AddChild(WrapText(req.Name, req.Met ? Color.FromHex("#00FF00") : Color.FromHex("#FF4444")));
        side.AddChild(WrapText("Unlocks", ErisSurgeryColors.ItemLabelText));
        if (selected.Unlocks.Count == 0)
            side.AddChild(WrapText("None", ErisSurgeryColors.LabelText));
        foreach (var unlock in selected.Unlocks)
            side.AddChild(WrapText(unlock, ErisSurgeryColors.Text));

        var research = new ErisLink(selected.State == 2 ? "Researched" : "Research");
        if (selected.State != 1)
            research.SetDisabledLook();
        var unlockId = selected.Id;
        research.OnPressed += _ =>
        {
            if (selected.State == 1)
                Msg(new OxydRdUnlockMessage { Node = unlockId });
        };
        side.AddChild(research);
    }

    private void AddNode(LayoutContainer map, OxydRdNodeInfo node)
    {
        var selected = _selectedNode == node.Id;
        var bg = selected
            ? NodeSelected
            : node.State switch
            {
                2 => NodeResearched,
                1 => NodeOpen,
                _ => NodeLocked,
            };
        var button = new ContainerButton
        {
            SetSize = new Vector2(NodeSize, NodeSize),
            ToolTip = node.Name,
            StyleBoxOverride = new StyleBoxFlat(bg)
            {
                BorderColor = selected ? Color.White : Color.FromHex("#111118"),
                BorderThickness = new Thickness(selected ? 2 : 1),
            },
        };
        var icon = new TextureRect
        {
            Texture = NodeIcon(node.Icon),
            SetSize = new Vector2(32, 32),
            Stretch = TextureRect.StretchMode.KeepCentered,
            MouseFilter = Control.MouseFilterMode.Ignore,
            HorizontalAlignment = Control.HAlignment.Center,
            VerticalAlignment = Control.VAlignment.Center,
        };
        button.AddChild(icon);
        var nid = node.Id;
        button.OnPressed += _ =>
        {
            _selectedNode = nid;
            if (_state != null)
                UpdateState(_state);
        };
        map.AddChild(button);
        LayoutContainer.SetPosition(button, new Vector2(node.X * MapW - NodeSize / 2f, (1f - node.Y) * MapH - NodeSize / 2f));
    }

    private Texture? NodeIcon(string state)
    {
        _sprites ??= IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<SpriteSystem>();
        var spec = new SpriteSpecifier.Rsi(new ResPath("/Textures/_Oxyd/Moebius/science.rsi"), state);
        return _sprites.Frame0(spec);
    }

    private static RichTextLabel WrapText(string text, Color color)
    {
        var label = new RichTextLabel { SetWidth = SideW - 16, HorizontalExpand = true };
        var message = new FormattedMessage();
        message.PushColor(color);
        message.AddText(text);
        message.Pop();
        label.SetMessage(message);
        return label;
    }

    private static void AddElbow(LayoutContainer map, Vector2 from, Vector2 to)
    {
        var mid = new Vector2(to.X, from.Y);
        var start = Pull(from, mid, NodeSize / 2f + 2f);
        var end = Pull(to, mid, NodeSize / 2f + 2f);
        AddSegment(map, start, mid);
        AddSegment(map, mid, end);
    }

    private static Vector2 Pull(Vector2 point, Vector2 toward, float dist)
    {
        var delta = toward - point;
        var len = delta.Length();
        if (len < 1f)
            return point;
        return point + delta * (Math.Min(dist, len) / len);
    }

    private static void AddSegment(LayoutContainer map, Vector2 a, Vector2 b)
    {
        var min = Vector2.Min(a, b);
        var size = Vector2.Abs(b - a);
        if (size.X < 1.5f && size.Y < 1.5f)
            return;
        size = Vector2.Max(size, new Vector2(2, 2));
        var line = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat(Color.FromHex("#f2f2f6")),
            MouseFilter = Control.MouseFilterMode.Ignore,
            SetSize = size,
        };
        map.AddChild(line);
        LayoutContainer.SetPosition(line, min);
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
                ? "Research points disk (spent)."
                : $"Research points disk: +{s.DiskPoints} points available."));
            var load = new ErisLink("Load Data");
            if (s.DiskSpent)
                load.SetDisabledLook();
            load.OnPressed += _ => Msg(new OxydRdDiskMessage { Op = OxydRdDiskOp.Upload });
            dcol.AddChild(load);
        }
        else
        {
            var techWord = s.DiskNodeCount == 1 ? "tech" : "techs";
            var designWord = s.DiskDesignCount == 1 ? "design" : "designs";
            dcol.AddChild(new ErisSubLabel($"Data disk: {s.DiskNodeCount} {techWord}, {s.DiskDesignCount} {designWord} stored."));
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
