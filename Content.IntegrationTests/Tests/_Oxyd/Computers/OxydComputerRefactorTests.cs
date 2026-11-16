#nullable enable
using System.Linq;
using Content.Client._Oxyd.Computers.Programs;
using Content.Client._Oxyd.Computers.UI;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._Oxyd.Computers;
using Content.Server._Oxyd.Computers.Network;
using Content.Server._Oxyd.Computers.Programs;
using Content.Server._Oxyd.Computers.ScreenSharing;
using Content.Server._Oxyd.Computers.Tools;
using Content.Server.Atmos.Monitor.Components;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared._Oxyd.Computers.ScreenSharing;
using Content.Shared._Oxyd.Computers.Tools;
using Content.Shared.Access.Components;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Atmos.Monitor.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking.Components;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.PDA;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.Computers;

public sealed class OxydComputerRefactorTests : InteractionTest
{
    private (EntityUid HostUid, EntityUid CompUid) SpawnWorkstation(string protoId = "OxydWorkstationComputer")
    {
        EntityUid host = default;
        EntityUid comp = default;
        Server.WaitPost(() =>
        {
            host = SEntMan.SpawnEntity(protoId, SEntMan.GetCoordinates(TargetCoords));
            if (SEntMan.TryGetComponent<ApcPowerReceiverComponent>(host, out _))
                Server.System<PowerReceiverSystem>().SetNeedsPower(host, false);

            var emb = SEntMan.GetComponent<EmbeddedComputerHostComponent>(host);
            Assert.That(emb.ComputerEntity, Is.Not.Null, $"Host {protoId} did not instantiate encapsulated ComputerEntity.");
            comp = emb.ComputerEntity!.Value;
        }).Wait();

        return (host, comp);
    }

    [Test]
    public async Task TestEncapsulationAndHardwareLimits()
    {
        var compSys = Server.System<OxydComputerSystem>();
        var containerSys = Server.System<SharedContainerSystem>();

        // Req 1, 2, 3: Separate computer entity inside computer_board slot, physical hardware defines RAM/CPU/HDD/NIC.
        var (hostUid, compUid) = SpawnWorkstation("OxydWorkstationComputer");
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<OxydComputerComponent>(hostUid), Is.False,
                "Host device must NOT hold OxydComputerComponent directly (PVS & encapsulation).");
            Assert.That(SEntMan.HasComponent<OxydComputerComponent>(compUid), Is.True);

            Assert.That(containerSys.TryGetContainingContainer((compUid, null), out var parentContainer), Is.True);
            Assert.That(parentContainer!.Owner, Is.EqualTo(hostUid));
            Assert.That(parentContainer.ID, Is.EqualTo("computer_board"));

            var stats = SEntMan.GetComponent<ComputerStatsComponent>(compUid);
            Assert.That(stats.HasCpu, Is.True);
            Assert.That(stats.RamMb, Is.GreaterThanOrEqualTo(1024));
            Assert.That(stats.HasHardDrive, Is.True);
            Assert.That(stats.HasNetworkCard, Is.True);
            Assert.That(stats.PrimaryDriveUid, Is.Not.Null);

            var comp = SEntMan.GetComponent<OxydComputerComponent>(compUid);
            Assert.That(comp.IsBooted, Is.True);
            Assert.That(comp.BootProgram, Is.Not.Null);
        });

        // Remove physical RAM -> computer fails POST on reboot; re-insert RAM -> boots again.
        EntityUid? removedRam = null;
        await Server.WaitAssertion(() =>
        {
            var stats = SEntMan.GetComponent<ComputerStatsComponent>(compUid);
            var itemSlots = Server.System<ItemSlotsSystem>();
            Assert.That(itemSlots.TryEject(compUid, ModularComputerComponent.SlotRam, null, out removedRam), Is.True);
            Assert.That(removedRam, Is.Not.Null);

            compSys.RebootComputer(compUid);
            var comp = SEntMan.GetComponent<OxydComputerComponent>(compUid);
            Assert.That(stats.RamMb, Is.EqualTo(0));
            Assert.That(comp.IsBooted, Is.False);
            Assert.That(comp.BootProgram, Is.Null);
            Assert.That(comp.BootError, Does.Contain("RAM"));
        });

        // Program RAM limit enforcement: reduce physical RAM stick capacity to 36MB (barely enough for 32MB OS),
        // re-insert it, and verify launching Terminal (8MB) is denied by hardware RAM limit!
        await Server.WaitAssertion(() =>
        {
            SEntMan.GetComponent<ComputerRamComponent>(removedRam!.Value).SizeMb = 36;
            var itemSlots = Server.System<ItemSlotsSystem>();
            Assert.That(itemSlots.TryInsert(compUid, ModularComputerComponent.SlotRam, removedRam!.Value, null), Is.True);

            var comp = SEntMan.GetComponent<OxydComputerComponent>(compUid);
            Assert.That(comp.IsBooted, Is.True);
            Assert.That(comp.BootProgram, Is.Not.Null);

            var stats = SEntMan.GetComponent<ComputerStatsComponent>(compUid);
            Assert.That(stats.RamMb, Is.EqualTo(36));
            var ok = compSys.TryLaunchExecutable(compUid, "/bin/terminal.nte", out _, out var err);
            Assert.That(ok, Is.False);
            Assert.That(err, Does.Contain("RAM"));
        });
    }

    [Test]
    public async Task TestFirstNteBootOrderAndProgramBuiParenting()
    {
        var compSys = Server.System<OxydComputerSystem>();
        var fsSys = Server.System<SharedFileSystemSystem>();

        // Req 5, 6, 8, 11: Boot always executes FIRST .nte on hard drive; child programs are real Entities
        // with direct BoundUserInterface (ProgramUiKey.Key) parented inside Boot OS UI.
        await SpawnTarget("OxydWorkstationComputer");
        var hostUid = ToServer(Target)!.Value;
        EntityUid compUid = default;
        EntityUid bootUid = default;

        await Server.WaitPost(() =>
        {
            Server.System<PowerReceiverSystem>().SetNeedsPower(hostUid, false);
            compUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(hostUid).ComputerEntity!.Value;
            bootUid = SEntMan.GetComponent<OxydComputerComponent>(compUid).BootProgram!.Value;
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<OsProgramComponent>(bootUid), Is.True);
            var progComp = SEntMan.GetComponent<ProgramComponent>(bootUid);
            Assert.That(progComp.IsBootProgram, Is.True);
            Assert.That(progComp.ExecutablePath, Is.EqualTo("/bin/os.nte"));
        });

        // Open computer UI as player via Activate() on host
        await Activate();
        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            Assert.That(TryGetBui(ComputerUiKey.Key, out _, FromServer(compUid)), Is.True);
            Assert.That(TryGetBui(ProgramUiKey.Key, out _, FromServer(bootUid)), Is.True);
            var win = GetWindow<ComputerDisplayWindow>();
            Assert.That(win.IsOpen, Is.True);
            var osCtrl = GetControlFromChildren<OsUiControl>(_ => true, win);
            Assert.That(osCtrl, Is.Not.Null);
        });

        // Launch child program (/bin/notekeeper.nte) directly via Boot OS Program BUI
        await SendBui(ProgramUiKey.Key, new OsLaunchProgramMessage("/bin/notekeeper.nte"), FromServer(bootUid));
        await RunTicks(10);

        EntityUid notekeeperUid = default;
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<OxydComputerComponent>(compUid);
            notekeeperUid = comp.ActiveProgram!.Value;
            Assert.That(SEntMan.HasComponent<NotekeeperProgramComponent>(notekeeperUid), Is.True);
            Assert.That(comp.RunningPrograms, Contains.Item(notekeeperUid));
        });

        await Client.WaitAssertion(() =>
        {
            // Direct Entity BUI on child program entity!
            Assert.That(TryGetBui(ProgramUiKey.Key, out _, FromServer(notekeeperUid)), Is.True);

            // And NotekeeperUiControl is physically parented inside OsUiControl inside ComputerDisplayWindow!
            var win = GetWindow<ComputerDisplayWindow>();
            var osCtrl = GetControlFromChildren<OsUiControl>(_ => true, win);
            var noteCtrl = GetControlFromChildren<NotekeeperUiControl>(_ => true, osCtrl);
            Assert.That(noteCtrl, Is.Not.Null);
        });

        // Save persistent .txt file via child Notekeeper BUI -> reboot -> confirm .txt persistence (Req 11)
        await SendBui(ProgramUiKey.Key, new NotekeeperActionMessage(NotekeeperUiAction.SaveFile, "/notes/persisted.txt", "SURVIVES_REBOOT_42"), FromServer(notekeeperUid));
        await RunTicks(5);

        await Server.WaitPost(() => compSys.RebootComputer(compUid));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(fsSys.TryFindFileSystem(compUid, out var driveUid, out var fs), Is.True);
            Assert.That(fsSys.TryGetFile<TextVirtualFile>(driveUid!.Value, "/notes/persisted.txt", out var txt, fs), Is.True);
            Assert.That(txt!.TextContent, Is.EqualTo("SURVIVES_REBOOT_42"));

            // Now prove computers ALWAYS run the FIRST .nte on the hard drive at boot by clearing /bin/*.nte
            // and placing /bin/terminal.nte first!
            foreach (var p in fs!.Files.Keys.ToList())
            {
                if (p.EndsWith(".nte", StringComparison.OrdinalIgnoreCase))
                    fsSys.TryDeleteFile(driveUid.Value, p, fs);
            }

            Assert.That(fsSys.TryWriteFile(driveUid.Value, new ProgramVirtualFile
            {
                Name = "terminal",
                Extension = "nte",
                Directory = "/bin/",
                ProgramId = "OxydTerminalProgram",
                Title = "Terminal",
                RamUsageMb = 12
            }, fs), Is.True);

            compSys.RebootComputer(compUid);
            var reloadedComp = SEntMan.GetComponent<OxydComputerComponent>(compUid);
            Assert.That(reloadedComp.IsBooted, Is.True);
            Assert.That(reloadedComp.BootProgram, Is.Not.Null);
            Assert.That(SEntMan.HasComponent<TerminalProgramComponent>(reloadedComp.BootProgram!.Value), Is.True,
                "Computer must boot the first .nte program on its HDD without any hardcoded OS.");
        });
    }

    [Test]
    public async Task TestHostDeviceLinkIntegrationAcrossDevices()
    {
        var compSys = Server.System<OxydComputerSystem>();
        var fsSys = Server.System<SharedFileSystemSystem>();

        // Req 4: APC, AirAlarm, and Autolathe expose DeviceLink ports to their separate computer entities
        EntityUid apcUid = default;
        EntityUid apcCompUid = default;
        EntityUid alarmUid = default;
        EntityUid alarmCompUid = default;
        EntityUid latheUid = default;
        EntityUid latheCompUid = default;

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(TargetCoords);
            apcUid = SEntMan.SpawnEntity("APCBasic", coords);
            apcCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(apcUid).ComputerEntity!.Value;

            alarmUid = SEntMan.SpawnEntity("AirAlarm", coords);
            Server.System<PowerReceiverSystem>().SetNeedsPower(alarmUid, false);
            alarmCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(alarmUid).ComputerEntity!.Value;

            latheUid = SEntMan.SpawnEntity("Autolathe", coords);
            Server.System<PowerReceiverSystem>().SetNeedsPower(latheUid, false);
            latheCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(latheUid).ComputerEntity!.Value;
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            // None of the host structures own OxydComputerComponent
            Assert.That(SEntMan.HasComponent<OxydComputerComponent>(apcUid), Is.False);
            Assert.That(SEntMan.HasComponent<OxydComputerComponent>(alarmUid), Is.False);
            Assert.That(SEntMan.HasComponent<OxydComputerComponent>(latheUid), Is.False);

            // Computer mirrors host DeviceLink ports!
            var apcPorts = compSys.GetMirroredPorts(apcCompUid);
            Assert.That(apcPorts.Any(p => p.IsHostPort && p.PortId == "ApcCommandSink"), Is.True);
            Assert.That(apcPorts.Any(p => p.IsHostPort && p.PortId == "ApcStatusSource"), Is.True);

            // Launch ApcControlProgram on APC computer & toggle MainBreaker over DeviceLink
            Assert.That(compSys.TryLaunchExecutable(apcCompUid, "/bin/apc_control.nte", out var apcProg, out _), Is.True);
            var apcComp = SEntMan.GetComponent<ApcComponent>(apcUid);
            var initialBreaker = apcComp.MainBreakerEnabled;

            Assert.That(compSys.TryInvokeDeviceLinkPort(apcCompUid, "ApcCommandSink", true, "toggle_breaker", "", out _), Is.True);
            Assert.That(apcComp.MainBreakerEnabled, Is.EqualTo(!initialBreaker));

            // Cut DeviceLink wire -> invocation fails!
            SEntMan.GetComponent<OxydComputerComponent>(apcCompUid).DeviceLinkWireCut = true;
            Assert.That(compSys.TryInvokeDeviceLinkPort(apcCompUid, "ApcCommandSink", true, "toggle_breaker", "", out var dlErr), Is.False);
            Assert.That(dlErr, Does.Contain("DeviceLink"));

            // AirAlarm mode toggle strictly over DeviceLink
            Assert.That(compSys.TryLaunchExecutable(alarmCompUid, "/bin/atmos_control.nte", out _, out _), Is.True);
            Assert.That(compSys.TryInvokeDeviceLinkPort(alarmCompUid, "AirAlarmCommandSink", true, "set_mode", "Panic", out _), Is.True);
            Assert.That(SEntMan.GetComponent<AirAlarmComponent>(alarmUid).CurrentMode, Is.EqualTo(AirAlarmMode.Panic));

            // Autolathe DRM .lathe blueprint decrement via DeviceLink
            Assert.That(fsSys.TryFindFileSystem(latheCompUid, out var latheDrive, out var latheFs), Is.True);
            fsSys.TryWriteFile(latheDrive!.Value, new RecipeBlueprintFile
            {
                Name = "wirecutter",
                Extension = "lathe",
                Directory = "/blueprints/",
                Recipe = "Wirecutter",
                DrmLocked = true,
                RemainingUses = 2,
                Size = 1024
            }, latheFs);

            // Supply steel and plastic (BaseToolRecipe) to Autolathe and trigger print via AutolatheBlueprintSink
            var matSys = Server.System<SharedMaterialStorageSystem>();
            Assert.That(matSys.TryChangeMaterialAmount(latheUid, "Steel", 1000), Is.True);
            Assert.That(matSys.TryChangeMaterialAmount(latheUid, "Plastic", 500), Is.True);

            var pkt = new Content.Shared._Oxyd.Computers.Packets.ComputerPacket("lathe_print");
            pkt.Set("Recipe", "Wirecutter");
            pkt.Set("Amount", 1);
            pkt.SetReference("DriveUid", latheDrive.Value);
            pkt.Set("FilePath", "/blueprints/wirecutter.lathe");
            Assert.That(compSys.TryInvokeDeviceLinkPort(latheCompUid, "AutolatheBlueprintSink", true, "blueprint_print", "Wirecutter", out _, null, pkt), Is.True);

            Assert.That(fsSys.TryGetFile<RecipeBlueprintFile>(latheDrive.Value, "/blueprints/wirecutter.lathe", out var updatedBp, latheFs), Is.True);
            Assert.That(updatedBp!.RemainingUses, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task TestLocalDataPortDebuggerWithoutNetwork()
    {
        var compSys = Server.System<OxydComputerSystem>();
        var fsSys = Server.System<SharedFileSystemSystem>();

        // Req 9: Computers with ComputerDataPort are locally connectable via HardwareDebuggingTerminal even with NO NIC and failed POST!
        await SpawnTarget("OxydWorkstationComputer");
        var hostUid = ToServer(Target)!.Value;
        EntityUid compUid = default;

        await Server.WaitPost(() =>
        {
            Server.System<PowerReceiverSystem>().SetNeedsPower(hostUid, false);
            compUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(hostUid).ComputerEntity!.Value;

            // Remove NIC AND RAM to force offline failed-POST state
            var slots = Server.System<ItemSlotsSystem>();
            slots.TryEject(compUid, ModularComputerComponent.SlotNetwork, null, out _);
            slots.TryEject(compUid, ModularComputerComponent.SlotRam, null, out _);
            compSys.RebootComputer(compUid);
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<ComputerDataPortComponent>(compUid).Enabled, Is.True);
            Assert.That(SEntMan.GetComponent<ComputerStatsComponent>(compUid).HasNetworkCard, Is.False);
            Assert.That(SEntMan.GetComponent<OxydComputerComponent>(compUid).IsBooted, Is.False);
        });

        var dbg = await PlaceInHands("HardwareDebuggingTerminal");
        var dbgUid = ToServer(dbg);
        await Interact();
        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            Assert.That(TryGetBui(HardwareDebuggerUiKey.Key, out _, dbg), Is.True);
        });

        // Write a file via debugger local terminal directly to HDD despite no NIC/OS!
        await SendBui(HardwareDebuggerUiKey.Key, new HardwareDebuggerCommandMessage("write /notes/jtag.txt DATA_PORT_OK"), dbg);
        await SendBui(HardwareDebuggerUiKey.Key, new HardwareDebuggerCommandMessage("ports"), dbg);
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(fsSys.TryFindFileSystem(compUid, out var dUid, out var fs), Is.True);
            Assert.That(fsSys.TryGetFile<TextVirtualFile>(dUid!.Value, "/notes/jtag.txt", out var tf, fs), Is.True);
            Assert.That(tf!.TextContent, Is.EqualTo("DATA_PORT_OK"));

            var hist = SEntMan.GetComponent<HardwareDebuggerComponent>(dbgUid).History;
            Assert.That(hist.Any(l => l.Contains("ComputerCommandSender") || l.Contains("MachineCommandReceiver")), Is.True);
        });
    }

    [Test]
    public async Task TestRouterNetworkRemoteScreenAndNanoTaskPersistence()
    {
        var compSys = Server.System<OxydComputerSystem>();
        var fsSys = Server.System<SharedFileSystemSystem>();
        var routerSys = Server.System<NetworkRouterSystem>();

        // Req 7, 10, 11: Station router connectivity, NIC requirement, data server upload/download,
        // remote SSH + live Screen view authorization, router blacklisting, and .tsk file persistence!
        EntityUid routerHost = default;
        EntityUid serverHost = default;
        EntityUid serverComp = default;
        EntityUid wsTargetHost = default;
        EntityUid wsTargetComp = default;

        await SpawnTarget("OxydWorkstationComputer");
        var wsSourceHost = ToServer(Target)!.Value;
        EntityUid wsSourceComp = default;

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(TargetCoords);
            var power = Server.System<PowerReceiverSystem>();
            power.SetNeedsPower(wsSourceHost, false);
            wsSourceComp = SEntMan.GetComponent<EmbeddedComputerHostComponent>(wsSourceHost).ComputerEntity!.Value;

            routerHost = SEntMan.SpawnEntity("StationNetworkRouter", coords);
            power.SetNeedsPower(routerHost, false);

            serverHost = SEntMan.SpawnEntity("StationManifestServer", coords);
            power.SetNeedsPower(serverHost, false);
            serverComp = SEntMan.GetComponent<EmbeddedComputerHostComponent>(serverHost).ComputerEntity!.Value;

            wsTargetHost = SEntMan.SpawnEntity("OxydWorkstationComputer", coords);
            power.SetNeedsPower(wsTargetHost, false);
            wsTargetComp = SEntMan.GetComponent<EmbeddedComputerHostComponent>(wsTargetHost).ComputerEntity!.Value;
        });
        await RunTicks(5);

        string targetGuid = string.Empty;
        string sourceGuid = string.Empty;
        EntityUid sourceTerm = default;
        EntityUid sourceNanoTask = default;
        EntityUid sourceDownloader = default;

        await Server.WaitAssertion(() =>
        {
            targetGuid = routerSys.EnsureDeviceGuid(wsTargetComp);
            sourceGuid = routerSys.EnsureDeviceGuid(wsSourceComp);
            Assert.That(routerSys.IsNetworkConnected(wsSourceComp), Is.True);
            Assert.That(routerSys.IsNetworkConnected(wsTargetComp), Is.True);

            Assert.That(compSys.TryLaunchExecutable(wsSourceComp, "/bin/terminal.nte", out var tUid, out _), Is.True);
            sourceTerm = tUid!.Value;

            Assert.That(compSys.TryLaunchExecutable(wsSourceComp, "/bin/downloader.nte", out var dUid, out _), Is.True);
            sourceDownloader = dUid!.Value;

            // Install & run NanoTask to verify /tasks/tasks.tsk persistence across reboot!
            Assert.That(fsSys.TryFindFileSystem(wsSourceComp, out var srcDrive, out var srcFs), Is.True);
            fsSys.TryWriteFile(srcDrive!.Value, new ProgramVirtualFile
            {
                Name = "nanotask",
                Extension = "nte",
                Directory = "/bin/",
                ProgramId = "NanoTaskProgram",
                Title = "NanoTask",
                RamUsageMb = 8
            }, srcFs);
            Assert.That(compSys.TryLaunchExecutable(wsSourceComp, "/bin/nanotask.nte", out var ntUid, out _), Is.True);
            sourceNanoTask = ntUid!.Value;
        });

        await Activate();
        await RunTicks(10);

        // Open Terminal + attach remote screen on Target via SSH
        await Client.WaitPost(() =>
        {
            Assert.That(TryGetBui(ProgramUiKey.Key, out var termBui, FromServer(sourceTerm)), Is.True);
            termBui!.SendMessage(new TerminalExecuteCommandMessage($"ssh {targetGuid}"));
            termBui.SendMessage(new TerminalExecuteCommandMessage("screen open"));
        });
        await RunTicks(15);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<ActiveRemoteScreenViewerComponent>(SPlayer, out var viewer), Is.True);
            Assert.That(viewer!.AuthorizedTargets, Contains.Item(SEntMan.GetNetEntity(wsTargetComp)));
            var routerComp = SEntMan.GetComponent<NetworkRouterComponent>(routerHost);
            Assert.That(routerComp.ActiveConnections.Count, Is.GreaterThanOrEqualTo(1));
        });

        // Router Blacklists Source GUID -> active screen share session is terminated immediately!
        await Server.WaitAssertion(() =>
        {
            var routerComp = SEntMan.GetComponent<NetworkRouterComponent>(routerHost);
            routerSys.BlacklistGuid((routerHost, routerComp), sourceGuid);
            Assert.That(SEntMan.TryGetComponent<ActiveRemoteScreenViewerComponent>(SPlayer, out _), Is.False,
                "Router blacklist must immediately drop active remote screen sessions.");
            routerSys.UnblacklistGuid((routerHost, routerComp), sourceGuid);
        });

        // Write a local file, upload it to DataServer via DownloaderProgram -> verify persisted on Server HDD!
        await Server.WaitAssertion(() =>
        {
            Assert.That(fsSys.TryFindFileSystem(wsSourceComp, out var dUid, out var fs), Is.True);
            fsSys.TryWriteFile(dUid!.Value, new TextVirtualFile
            {
                Name = "field_report",
                Extension = "txt",
                Directory = "/notes/",
                TextContent = "NET_UPLOAD_DATA",
                Size = 128
            }, fs);
        });

        await SendBui(ProgramUiKey.Key, new DownloaderActionMessage(DownloaderAction.UploadFile, "/notes/field_report.txt"), FromServer(sourceDownloader));
        await RunTicks(35);

        await Server.WaitAssertion(() =>
        {
            Assert.That(fsSys.TryFindFileSystem(serverComp, out var srvDrive, out var srvFs), Is.True);
            Assert.That(fsSys.TryGetFile<TextVirtualFile>(srvDrive!.Value, "/notes/field_report.txt", out var uploaded, srvFs), Is.True);
            Assert.That(uploaded!.TextContent, Is.EqualTo("NET_UPLOAD_DATA"));
        });

        // NanoTask writes .tsk file on HDD and survives reboot!
        await SendBui(ProgramUiKey.Key, new NanoTaskActionMessage(new NanoTaskAddTask(
            new NanoTaskItem("Fix Router", "Engineer", false, NanoTaskPriority.High))), FromServer(sourceNanoTask));
        await RunTicks(10);

        await Server.WaitPost(() => compSys.RebootComputer(wsSourceComp));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(fsSys.TryFindFileSystem(wsSourceComp, out var dUid, out var fs), Is.True);
            Assert.That(fsSys.TryGetFile<TaskListVirtualFile>(dUid!.Value, NanoTaskProgramComponent.DefaultTasksFilePath, out var tsk, fs), Is.True);
            Assert.That(tsk!.Tasks.Any(t => t.Description == "Fix Router" && t.TaskIsFor == "Engineer"), Is.True);
        });
    }
}
