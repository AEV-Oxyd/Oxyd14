#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
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
using Content.Shared.Examine;
using Content.Shared.Lathe;
using Content.Shared.Lock;
using Content.Shared.Materials;
using Content.Shared.PDA;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
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

            // Launch ApcControlProgram on APC computer & verify lock gating over DeviceLink
            Assert.That(compSys.TryLaunchExecutable(apcCompUid, "/bin/apc_control.nte", out var apcProg, out _), Is.True);
            var apcComp = SEntMan.GetComponent<ApcComponent>(apcUid);
            var lockSys = Server.System<LockSystem>();
            var initialBreaker = apcComp.MainBreakerEnabled;

            // Starts locked: breaker toggle over DeviceLink must be rejected while locked
            Assert.That(lockSys.IsLocked(apcUid), Is.True);
            Assert.That(compSys.TryInvokeDeviceLinkPort(apcCompUid, "ApcCommandSink", true, "toggle_breaker", "", out _), Is.True);
            Assert.That(apcComp.MainBreakerEnabled, Is.EqualTo(initialBreaker), "Locked APC must not toggle breaker.");

            // Unlock APC -> breaker toggle succeeds
            lockSys.Unlock(apcUid, null);
            Assert.That(compSys.TryInvokeDeviceLinkPort(apcCompUid, "ApcCommandSink", true, "toggle_breaker", "", out _), Is.True);
            Assert.That(apcComp.MainBreakerEnabled, Is.EqualTo(!initialBreaker));

            // Cut DeviceLink wire -> invocation fails!
            SEntMan.GetComponent<OxydComputerComponent>(apcCompUid).DeviceLinkWireCut = true;
            Assert.That(compSys.TryInvokeDeviceLinkPort(apcCompUid, "ApcCommandSink", true, "toggle_breaker", "", out var dlErr), Is.False);
            Assert.That(dlErr, Does.Contain("DeviceLink"));

            // AirAlarm mode toggle strictly over DeviceLink (blocked while locked, allowed once unlocked)
            Assert.That(compSys.TryLaunchExecutable(alarmCompUid, "/bin/atmos_control.nte", out _, out _), Is.True);
            Assert.That(lockSys.IsLocked(alarmUid), Is.True);
            var initialMode = SEntMan.GetComponent<AirAlarmComponent>(alarmUid).CurrentMode;
            Assert.That(compSys.TryInvokeDeviceLinkPort(alarmCompUid, "AirAlarmCommandSink", true, "set_mode", "Panic", out _), Is.True);
            Assert.That(SEntMan.GetComponent<AirAlarmComponent>(alarmUid).CurrentMode, Is.EqualTo(initialMode), "Locked AirAlarm must not change mode.");

            lockSys.Unlock(alarmUid, null);
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

    [Test]
    public async Task TestPdaCloseReopenLifecycleAndWindowDrag()
    {
        var compSys = Server.System<OxydComputerSystem>();

        // Open a real PDA with child program, drag its window header, close it, and reopen cleanly.
        await SpawnTarget("DetectivePDA");
        var pdaUid = ToServer(Target)!.Value;
        EntityUid compUid = default;

        await Server.WaitAssertion(() =>
        {
            compUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(pdaUid).ComputerEntity!.Value;
            Assert.That(compSys.TryLaunchExecutable(compUid, "/bin/notekeeper.nte", out _, out _), Is.True);
        });

        await Activate();
        await RunTicks(10);

        ComputerDisplayWindow? win = null;
        var startPos = Vector2.Zero;
        ScreenCoordinates downScreen = default;
        var relPos = new Vector2(120, 18);
        var uiScale = 1f;
        WindowId winId = default;

        await Client.WaitAssertion(() =>
        {
            Assert.That(TryFindWindow(typeof(ComputerDisplayWindow), out var baseWin), Is.True);
            win = (ComputerDisplayWindow) baseWin!;
            Assert.That(win.MouseFilter, Is.EqualTo(Control.MouseFilterMode.Stop));

            uiScale = win.UIScale;
            winId = win.Window?.Id ?? default;
            startPos = win.Position;
            var headerGlobalPos = win.GlobalPosition + relPos;
            downScreen = new ScreenCoordinates(headerGlobalPos * uiScale, winId);
            var hit = UiMan.MouseGetControl(downScreen);
            Assert.That(hit, Is.EqualTo(win), "Header drag area must hit ComputerDisplayWindow directly.");
        });

        await Client.DoGuiEvent(win!, new GUIBoundKeyEventArgs(
            EngineKeyFunctions.UIClick,
            BoundKeyState.Down,
            downScreen,
            default,
            relPos,
            relPos * uiScale));
        await RunTicks(1);

        var dragDelta = new Vector2(65, 45);
        var movedGlobal = downScreen.Position / uiScale + dragDelta;
        var movedScreen = new ScreenCoordinates(movedGlobal * uiScale, winId);
        var moveArgs = new GUIMouseMoveEventArgs(
            dragDelta,
            win!,
            movedGlobal,
            movedScreen,
            relPos + dragDelta,
            (relPos + dragDelta) * uiScale);
        var mouseMoveMethod = typeof(Control).GetMethod("MouseMove", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await Client.WaitPost(() => mouseMoveMethod.Invoke(win!, new object[] { moveArgs }));
        await RunTicks(1);

        await Client.DoGuiEvent(win!, new GUIBoundKeyEventArgs(
            EngineKeyFunctions.UIClick,
            BoundKeyState.Up,
            movedScreen,
            default,
            relPos,
            relPos * uiScale));
        await RunTicks(2);

        await Client.WaitAssertion(() =>
        {
            Assert.That(win!.Position.X, Is.GreaterThan(startPos.X + 40f));
            Assert.That(win.Position.Y, Is.GreaterThan(startPos.Y + 25f));
        });

        // Close PDA display on client and verify ComputerDisplayWindow disposal never disposes mounted ProgramUiControls.
        await Client.WaitPost(() => win!.Close());
        await RunTicks(1);

        await Client.WaitAssertion(() =>
        {
            var cCompUid = CEntMan.GetEntity(FromServer(compUid));
            var uiSys = Client.System<ClientComputerUiSystem>();
            var bootCtrl = uiSys.GetBootControl(cCompUid);
            if (bootCtrl != null)
                Assert.That(bootCtrl.Disposed, Is.False);
        });

        await RunTicks(15);

        await Client.WaitAssertion(() =>
        {
            Assert.That(TryFindWindow(typeof(ComputerDisplayWindow), out _), Is.False);
        });

        // Re-open PDA via normal Activate and verify display + boot program mount cleanly.
        await Activate();
        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            Assert.That(TryFindWindow(typeof(ComputerDisplayWindow), out var reopenedBase), Is.True);
            var reopened = (ComputerDisplayWindow) reopenedBase!;
            Assert.That(reopened.Disposed, Is.False);
            Assert.That(reopened.IsOpen, Is.True);
            var cCompUid = CEntMan.GetEntity(FromServer(compUid));
            var bootCtrl = Client.System<ClientComputerUiSystem>().GetBootControl(cCompUid);
            Assert.That(bootCtrl, Is.Not.Null);
            Assert.That(bootCtrl!.Disposed, Is.False);
            reopened.Close();
        });
        await RunTicks(15);
    }

    private static void CollectDescendants<T>(Robust.Client.UserInterface.Control root, System.Collections.Generic.List<T> results)
        where T : Robust.Client.UserInterface.Control
    {
        if (root is T match)
            results.Add(match);
        foreach (var child in root.Children)
            CollectDescendants(child, results);
    }

    private static bool HasAncestor(Robust.Client.UserInterface.Control child, Robust.Client.UserInterface.Control ancestor)
    {
        for (var cur = child; cur != null; cur = cur.Parent)
        {
            if (cur == ancestor)
                return true;
        }
        return false;
    }

    private async Task ClickAtScreenCoords(ScreenCoordinates screenCoords)
    {
        var uiType = UiMan.GetType();
        var mFocusDown = uiType.GetMethod("HandleCanFocusDown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var mFocusUp = uiType.GetMethod("HandleCanFocusUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var mKeyDown = uiType.GetMethod("KeyBindDown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Robust.Shared.Input.BoundKeyEventArgs) }, null)!;
        var mKeyUp = uiType.GetMethod("KeyBindUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Robust.Shared.Input.BoundKeyEventArgs) }, null)!;

        await Client.WaitPost(() =>
        {
            mFocusDown.Invoke(UiMan, new object?[] { screenCoords, null });
            mKeyDown.Invoke(UiMan, new object[]
            {
                new Robust.Shared.Input.BoundKeyEventArgs(
                    EngineKeyFunctions.UIClick,
                    BoundKeyState.Down,
                    screenCoords,
                    true,
                    false)
            });
        });
        await RunTicks(1);
        await Client.WaitPost(() =>
        {
            mKeyUp.Invoke(UiMan, new object[]
            {
                new Robust.Shared.Input.BoundKeyEventArgs(
                    EngineKeyFunctions.UIClick,
                    BoundKeyState.Up,
                    screenCoords,
                    true,
                    false)
            });
            mFocusUp.Invoke(UiMan, null);
        });
        await RunTicks(6);
    }

    private async Task ClickViaHitTest(Robust.Client.UserInterface.Control btn)
    {
        ScreenCoordinates screenCoords = default;
        await Client.WaitAssertion(() =>
        {
            Assert.That(btn.VisibleInTree, Is.True, $"Button {btn.GetType().Name} is not VisibleInTree");
            Assert.That(btn.Size.X, Is.GreaterThan(1f));
            Assert.That(btn.Size.Y, Is.GreaterThan(1f));
            var center = btn.GlobalPixelPosition + btn.PixelSize / 2;
            screenCoords = new ScreenCoordinates(center, btn.Window?.Id ?? default);
            var hit = UiMan.MouseGetControl(screenCoords);
            Assert.That(hit, Is.Not.Null);
            Assert.That(HasAncestor(hit!, btn) || HasAncestor(btn, hit!), Is.True,
                $"Mouse click hit {hit!.GetType().Name} instead of target button {btn.GetType().Name}");
        });

        await ClickAtScreenCoords(screenCoords);
    }

    private async Task TypeIntoLineEdit(Content.Client.UserInterface.Controls.ContentLineEdit edit, string text, bool submit = false)
    {
        await Client.WaitAssertion(() =>
        {
            Assert.That(edit.VisibleInTree, Is.True, "Target ContentLineEdit must be VisibleInTree");
            Assert.That(edit.RectClipContent, Is.True, "ContentLineEdit must clip horizontal overflow");
            Assert.That(edit.ActiveFont, Is.Not.TypeOf<Robust.Client.Graphics.DummyFont>(),
                "ContentLineEdit must resolve a renderable stylesheet font instead of DummyFont");
            Assert.That(edit.ActiveFont.GetHeight(edit.UIScale), Is.GreaterThan(0),
                "ContentLineEdit resolved font must have positive glyph height");
            Assert.That(edit.PixelSize.X, Is.GreaterThan(10));
            Assert.That(edit.PixelSize.Y, Is.GreaterThan(10));
        });

        await Client.WaitPost(() =>
        {
            var center = edit.GlobalPixelPosition + edit.PixelSize / 2;
            var sc = new ScreenCoordinates(center, edit.Window?.Id ?? default);
            var hit = UiMan.MouseGetControl(sc);
            Assert.That(hit, Is.Not.Null, "MouseGetControl returned null over ContentLineEdit");
            Assert.That(hit == edit || HasAncestor(hit!, edit), Is.True,
                $"Mouse click hit {hit!.GetType().Name} instead of ContentLineEdit");

            var uiType = UiMan.GetType();
            var mFocusDown = uiType.GetMethod("HandleCanFocusDown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            var mFocusUp = uiType.GetMethod("HandleCanFocusUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            var mKeyDown = uiType.GetMethod("KeyBindDown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Robust.Shared.Input.BoundKeyEventArgs) }, null)!;
            var mKeyUp = uiType.GetMethod("KeyBindUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Robust.Shared.Input.BoundKeyEventArgs) }, null)!;
            var mText = uiType.GetMethod("TextEntered", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

            mFocusDown.Invoke(UiMan, new object?[] { sc, null });
            mKeyDown.Invoke(UiMan, new object[] { new Robust.Shared.Input.BoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, sc, true, false) });
            mKeyUp.Invoke(UiMan, new object[] { new Robust.Shared.Input.BoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, sc, true, false) });
            mFocusUp.Invoke(UiMan, null);
            Assert.That(edit.HasKeyboardFocus(), Is.True, "ContentLineEdit did not gain keyboard focus on click");
            foreach (var ch in text)
                mText.Invoke(UiMan, new object[] { new Robust.Client.Input.TextEnteredEventArgs(ch.ToString()) });
            if (text.Length > 1)
            {
                mKeyDown.Invoke(UiMan, new object[] { new Robust.Shared.Input.BoundKeyEventArgs(EngineKeyFunctions.TextBackspace, BoundKeyState.Down, sc, false, false) });
                mKeyUp.Invoke(UiMan, new object[] { new Robust.Shared.Input.BoundKeyEventArgs(EngineKeyFunctions.TextBackspace, BoundKeyState.Up, sc, false, false) });
                mText.Invoke(UiMan, new object[] { new Robust.Client.Input.TextEnteredEventArgs(text[^1].ToString()) });
            }
            Assert.That(edit.Text, Does.EndWith(text), "ContentLineEdit did not accept entered text");
            if (submit)
            {
                mKeyDown.Invoke(UiMan, new object[] { new Robust.Shared.Input.BoundKeyEventArgs(EngineKeyFunctions.TextSubmit, BoundKeyState.Down, sc, false, false) });
                mKeyUp.Invoke(UiMan, new object[] { new Robust.Shared.Input.BoundKeyEventArgs(EngineKeyFunctions.TextSubmit, BoundKeyState.Up, sc, false, false) });
            }
        });
        await RunTicks(6);
    }

    private static Content.Client.UserInterface.Controls.ContentButton? FindRowRunButton(OsUiControl osCtrl, string appTitle)
    {
        var rows = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentBoxContainer>();
        CollectDescendants(osCtrl, rows);
        foreach (var row in rows)
        {
            if (!row.VisibleInTree)
                continue;
            if (!row.Children.OfType<Content.Client.UserInterface.Controls.ContentLabel>()
                .Any(l => l.Text != null && l.Text.Contains(appTitle, StringComparison.OrdinalIgnoreCase)))
                continue;
            var btn = row.Children.OfType<Content.Client.UserInterface.Controls.ContentButton>()
                .FirstOrDefault(b => b.VisibleInTree && b.Text != null && b.Text.Contains("Run", StringComparison.OrdinalIgnoreCase));
            if (btn != null)
                return btn;
        }
        return null;
    }

    [Test]
    public async Task TestRealAppUiSingleInstanceAndInteractiveControls()
    {

        // Held PDA (`PlaceInHands` + `UseInHand`) -> click Apps -> launch Notekeeper
        var pdaNet = await PlaceInHands("DetectivePDA");
        var pdaUid = ToServer(pdaNet);
        EntityUid compUid = default;

        await Server.WaitAssertion(() =>
        {
            compUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(pdaUid).ComputerEntity!.Value;
        });

        await UseInHand();
        await RunTicks(12);

        ComputerDisplayWindow? win = null;
        OsUiControl? osCtrl = null;
        Content.Client.UserInterface.Controls.ContentButton? appsBtn = null;
        await Client.WaitAssertion(() =>
        {
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen).ToList();
            Assert.That(openWins.Count, Is.EqualTo(1));
            win = openWins[0];
            var osList = new System.Collections.Generic.List<OsUiControl>();
            CollectDescendants(win, osList);
            Assert.That(osList.Count, Is.EqualTo(1));
            osCtrl = osList[0];
            var btns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(osCtrl, btns);
            appsBtn = btns.FirstOrDefault(b => b.Text != null && b.Text.Contains("Apps", StringComparison.OrdinalIgnoreCase));
            Assert.That(appsBtn, Is.Not.Null);
        });

        await ClickViaHitTest(appsBtn!);

        Content.Client.UserInterface.Controls.ContentButton? noteRunBtn = null;
        await Client.WaitAssertion(() =>
        {
            noteRunBtn = FindRowRunButton(osCtrl!, "Notekeeper");
            Assert.That(noteRunBtn, Is.Not.Null);
        });
        await ClickViaHitTest(noteRunBtn!);

        // Verify ONLY 1 NotekeeperUiControl is mounted (no duplicate instance!) and type/click real controls
        NotekeeperUiControl? noteCtrl = null;
        Content.Client.UserInterface.Controls.ContentLineEdit? newNameInput = null;
        Content.Client.UserInterface.Controls.ContentLineEdit? editorLine = null;
        Content.Client.UserInterface.Controls.ContentButton? createBtn = null;
        Content.Client.UserInterface.Controls.ContentButton? saveBtn = null;
        await Client.WaitAssertion(() =>
        {
            var noteList = new System.Collections.Generic.List<NotekeeperUiControl>();
            CollectDescendants(win!, noteList);
            Assert.That(noteList.Count, Is.EqualTo(1), $"Expected 1 NotekeeperUiControl, found {noteList.Count}");
            noteCtrl = noteList[0];

            var inputs = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentLineEdit>();
            CollectDescendants(noteCtrl, inputs);
            Assert.That(inputs.Count, Is.EqualTo(2));
            newNameInput = inputs[0];
            editorLine = inputs[1];

            var noteBtns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(noteCtrl, noteBtns);
            createBtn = noteBtns.First(b => b.Text != null && b.Text.Contains("Create", StringComparison.OrdinalIgnoreCase));
            saveBtn = noteBtns.First(b => b.Text == "Save");
        });

        await TypeIntoLineEdit(newNameInput!, "enter_note", submit: true);
        const string longLine = "enter line with long overflow verification 12345678901234567890";
        await TypeIntoLineEdit(editorLine!, longLine, submit: true);
        await Client.WaitAssertion(() =>
        {
            Assert.That(newNameInput!.Text, Is.Empty);
            Assert.That(editorLine!.Text, Is.Empty);
            Assert.That(editorLine.HasKeyboardFocus(), Is.True, "Notekeeper editor should keep focus across Enter submit + BUI update");
            var saveCenter = new ScreenCoordinates(saveBtn!.GlobalPixelPosition + saveBtn.PixelSize / 2, saveBtn.Window?.Id ?? default);
            Assert.That(UiMan.MouseGetControl(saveCenter), Is.Not.Null, "Save button pushed out of hit-test bounds by long text");
        });
        await Server.WaitAssertion(() =>
        {
            var fsSysLocal = Server.System<SharedFileSystemSystem>();
            Assert.That(fsSysLocal.TryFindFileSystem(compUid, out var driveUid, out var fs), Is.True);
            Assert.That(fsSysLocal.TryGetFile<TextVirtualFile>(driveUid!.Value, "/notes/enter_note.txt", out var enterFile, fs), Is.True);
            Assert.That(enterFile!.TextContent, Does.Contain(longLine));
        });

        await TypeIntoLineEdit(newNameInput!, "field_logx");
        await Client.WaitPost(() =>
        {
            var uiType = UiMan.GetType();
            var mKeyDown = uiType.GetMethod("KeyBindDown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Robust.Shared.Input.BoundKeyEventArgs) }, null)!;
            var mKeyUp = uiType.GetMethod("KeyBindUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Robust.Shared.Input.BoundKeyEventArgs) }, null)!;
            var sc = new ScreenCoordinates(newNameInput!.GlobalPixelPosition + newNameInput.PixelSize / 2, newNameInput.Window?.Id ?? default);
            Assert.That(newNameInput.HasKeyboardFocus(), Is.True);
            mKeyDown.Invoke(UiMan, new object[] { new Robust.Shared.Input.BoundKeyEventArgs(EngineKeyFunctions.TextBackspace, BoundKeyState.Down, sc, false, false) });
            mKeyUp.Invoke(UiMan, new object[] { new Robust.Shared.Input.BoundKeyEventArgs(EngineKeyFunctions.TextBackspace, BoundKeyState.Up, sc, false, false) });
            Assert.That(newNameInput.Text, Is.EqualTo("field_log"));
            mKeyDown.Invoke(UiMan, new object[] { new Robust.Shared.Input.BoundKeyEventArgs(EngineKeyFunctions.TextReleaseFocus, BoundKeyState.Down, sc, false, false) });
            mKeyUp.Invoke(UiMan, new object[] { new Robust.Shared.Input.BoundKeyEventArgs(EngineKeyFunctions.TextReleaseFocus, BoundKeyState.Up, sc, false, false) });
            Assert.That(newNameInput.HasKeyboardFocus(), Is.False);
        });
        await ClickViaHitTest(createBtn!);
        await TypeIntoLineEdit(editorLine!, "SINGLE_UI_WORKS");
        await ClickViaHitTest(saveBtn!);

        await Server.WaitAssertion(() =>
        {
            var fsSys = SEntMan.System<Content.Shared._Oxyd.Computers.FileSystem.SharedFileSystemSystem>();
            Assert.That(fsSys.TryFindFileSystem(compUid, out var dUid, out var fs), Is.True);
            Assert.That(fsSys.TryGetFile<Content.Shared._Oxyd.Computers.FileSystem.TextVirtualFile>(dUid!.Value, "/notes/field_log.txt", out var tf, fs), Is.True);
            Assert.That(tf!.TextContent, Is.EqualTo("SINGLE_UI_WORKS"));
        });

        await Client.WaitAssertion(() =>
        {
            var labels = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentLabel>();
            CollectDescendants(noteCtrl!, labels);
            Assert.That(labels.Any(l => l.Text != null && l.Text.Contains("SINGLE_UI_WORKS")), Is.True);
        });

        // Switch to Apps tab, launch Terminal, verify ONLY 1 TerminalUiControl, type `ls` and submit
        await ClickViaHitTest(appsBtn!);
        Content.Client.UserInterface.Controls.ContentButton? termRunBtn = null;
        await Client.WaitAssertion(() =>
        {
            termRunBtn = FindRowRunButton(osCtrl!, "Terminal");
            Assert.That(termRunBtn, Is.Not.Null);
        });
        await ClickViaHitTest(termRunBtn!);

        Content.Client.UserInterface.Controls.ContentLineEdit? termInput = null;
        await Client.WaitAssertion(() =>
        {
            var termList = new System.Collections.Generic.List<TerminalUiControl>();
            CollectDescendants(win!, termList);
            Assert.That(termList.Count, Is.EqualTo(1), $"Expected 1 TerminalUiControl, found {termList.Count}");
            var inputs = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentLineEdit>();
            CollectDescendants(termList[0], inputs);
            Assert.That(inputs.Count, Is.EqualTo(1));
            termInput = inputs[0];
        });

        await TypeIntoLineEdit(termInput!, "ls", submit: true);
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var active = SEntMan.GetComponent<OxydComputerComponent>(compUid).ActiveProgram!.Value;
            var termComp = SEntMan.GetComponent<Content.Shared._Oxyd.Computers.Programs.TerminalProgramComponent>(active);
            Assert.That(termComp.History.Any(h => h.Contains("system.log")), Is.True);
        });

        // Close and reopen PDA with running child programs and verify single instances remain intact
        await Client.WaitPost(() => win!.Close());
        await RunTicks(10);
        await UseInHand();
        await RunTicks(12);

        await Client.WaitAssertion(() =>
        {
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen).ToList();
            Assert.That(openWins.Count, Is.EqualTo(1));
            var termList = new System.Collections.Generic.List<TerminalUiControl>();
            CollectDescendants(openWins[0], termList);
            Assert.That(termList.Count, Is.EqualTo(1));
            openWins[0].Close();
        });
        await RunTicks(10);

        // APCBasic EmbeddedOS -> single ApcControlUiControl + real mouse breaker click
        await SpawnTarget("APCBasic");
        var apcUid = ToServer(Target)!.Value;
        bool initialBreaker = false;
        await Server.WaitPost(() =>
        {
            SEntMan.RemoveComponent<Content.Shared.Access.Components.AccessReaderComponent>(apcUid);
            Server.System<LockSystem>().Unlock(apcUid, null);
            initialBreaker = SEntMan.GetComponent<Content.Server.Power.Components.ApcComponent>(apcUid).MainBreakerEnabled;
        });

        await Activate();
        await RunTicks(12);

        Content.Client.UserInterface.Controls.ContentButton? breakerBtn = null;
        await Client.WaitAssertion(() =>
        {
            Assert.That(TryFindWindow(typeof(ComputerDisplayWindow), out var apcWinBase), Is.True);
            var apcWin = (ComputerDisplayWindow) apcWinBase!;
            var apcCtrls = new System.Collections.Generic.List<Content.Client._Oxyd.Computers.Programs.ApcControlUiControl>();
            CollectDescendants(apcWin, apcCtrls);
            Assert.That(apcCtrls.Count, Is.EqualTo(1));
            var apcBtns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(apcCtrls[0], apcBtns);
            breakerBtn = apcBtns.FirstOrDefault(b => b.VisibleInTree && !b.Disabled && b.Text != null &&
                (b.Text.Contains("Breaker", StringComparison.OrdinalIgnoreCase) || b.Text.Contains("Toggle", StringComparison.OrdinalIgnoreCase)));
            Assert.That(breakerBtn, Is.Not.Null);
        });

        await ClickViaHitTest(breakerBtn!);

        await Server.WaitAssertion(() =>
        {
            var afterBreaker = SEntMan.GetComponent<Content.Server.Power.Components.ApcComponent>(apcUid).MainBreakerEnabled;
            Assert.That(afterBreaker, Is.EqualTo(!initialBreaker));
        });
    }

    private async Task ClickThroughRemoteScreenView(
        Content.Client._Oxyd.Computers.ScreenSharing.RemoteScreenView remoteView,
        Robust.Client.UserInterface.Control remoteTargetRoot,
        Robust.Client.UserInterface.Control remoteTargetCtrl)
    {
        ScreenCoordinates clickCoords = default;
        await Client.WaitAssertion(() =>
        {
            Assert.That(remoteView.VisibleInTree, Is.True);
            Assert.That(remoteView.PixelSize.X, Is.GreaterThan(20));
            Assert.That(remoteView.PixelSize.Y, Is.GreaterThan(20));
            Assert.That(remoteTargetRoot.Disposed, Is.False);
            Assert.That(remoteTargetRoot.PixelSize.X, Is.GreaterThan(20));
            Assert.That(remoteTargetRoot.PixelSize.Y, Is.GreaterThan(20));
            Assert.That(remoteTargetCtrl.VisibleInTree, Is.True);
            Assert.That(remoteTargetCtrl.PixelSize.X, Is.GreaterThan(2));
            Assert.That(remoteTargetCtrl.PixelSize.Y, Is.GreaterThan(2));

            var hostPixelSize = remoteTargetRoot.PixelSize;
            var scale = MathF.Min(
                (float) remoteView.PixelWidth / hostPixelSize.X,
                (float) remoteView.PixelHeight / hostPixelSize.Y);
            var fittedW = MathF.Round(hostPixelSize.X * scale);
            var fittedH = MathF.Round(hostPixelSize.Y * scale);
            var offsetX = MathF.Round((remoteView.PixelWidth - fittedW) / 2f);
            var offsetY = MathF.Round((remoteView.PixelHeight - fittedH) / 2f);

            var targetCenterInRoot = (remoteTargetCtrl.GlobalPixelPosition + remoteTargetCtrl.PixelSize / 2)
                - remoteTargetRoot.GlobalPixelPosition;
            var normX = (float) targetCenterInRoot.X / hostPixelSize.X;
            var normY = (float) targetCenterInRoot.Y / hostPixelSize.Y;

            var localInView = new Robust.Shared.Maths.Vector2i(
                (int) MathF.Round(offsetX + normX * fittedW),
                (int) MathF.Round(offsetY + normY * fittedH));
            clickCoords = new ScreenCoordinates(remoteView.GlobalPixelPosition + localInView, remoteView.Window?.Id ?? default);

            var hit = UiMan.MouseGetControl(clickCoords);
            Assert.That(hit, Is.Not.Null, "MouseGetControl returned null over RemoteScreenView");
            Assert.That(HasAncestor(hit!, remoteView), Is.True,
                $"Expected click on RemoteScreenView, hit {hit!.GetType().Name}");
        });

        await ClickAtScreenCoords(clickCoords);
    }

    [Test]
    public async Task TestRemoteScreenClickingAndControlRelay()
    {
        var routerSys = Server.System<NetworkRouterSystem>();
        var power = Server.System<PowerReceiverSystem>();

        var pdaNet = await PlaceInHands("DetectivePDA");
        var pdaUid = ToServer(pdaNet);

        EntityUid apcUid = default;
        EntityUid wsUid = default;
        EntityUid wsCompUid = default;
        string apcGuid = string.Empty;
        string wsGuid = string.Empty;
        bool initialBreaker = false;

        EntityUid apcCompUid = default;
        EntityUid pdaCompUid = default;

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(TargetCoords);
            var routerHost = SEntMan.SpawnEntity("StationNetworkRouter", coords);
            power.SetNeedsPower(routerHost, false);

            apcUid = SEntMan.SpawnEntity("APCBasic", coords);
            SEntMan.RemoveComponent<Content.Shared.Access.Components.AccessReaderComponent>(apcUid);
            Server.System<LockSystem>().Unlock(apcUid, null);
            initialBreaker = SEntMan.GetComponent<Content.Server.Power.Components.ApcComponent>(apcUid).MainBreakerEnabled;
            apcCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(apcUid).ComputerEntity!.Value;

            wsUid = SEntMan.SpawnEntity("OxydWorkstationComputer", coords);
            power.SetNeedsPower(wsUid, false);
            wsCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(wsUid).ComputerEntity!.Value;

            pdaCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(pdaUid).ComputerEntity!.Value;
        });
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            apcGuid = routerSys.EnsureDeviceGuid(apcCompUid);
            wsGuid = routerSys.EnsureDeviceGuid(wsCompUid);
            Assert.That(routerSys.IsNetworkConnected(pdaCompUid), Is.True);
            Assert.That(routerSys.IsNetworkConnected(apcCompUid), Is.True);
            Assert.That(routerSys.IsNetworkConnected(wsCompUid), Is.True);
        });

        // Open held PDA and launch Terminal
        await UseInHand();
        await RunTicks(12);

        ComputerDisplayWindow? pdaWin = null;
        Content.Client.UserInterface.Controls.ContentButton? pdaAppsBtn = null;
        await Client.WaitAssertion(() =>
        {
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen).ToList();
            Assert.That(openWins.Count, Is.EqualTo(1));
            pdaWin = openWins[0];
            var btns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(pdaWin, btns);
            pdaAppsBtn = btns.First(b => b.Text != null && b.Text.Contains("Apps", StringComparison.OrdinalIgnoreCase));
        });
        await ClickViaHitTest(pdaAppsBtn!);

        Content.Client.UserInterface.Controls.ContentButton? termRunBtn = null;
        await Client.WaitAssertion(() =>
        {
            var osList = new System.Collections.Generic.List<OsUiControl>();
            CollectDescendants(pdaWin!, osList);
            Assert.That(osList.Count, Is.EqualTo(1));
            termRunBtn = FindRowRunButton(osList[0], "Terminal");
            Assert.That(termRunBtn, Is.Not.Null);
        });
        await ClickViaHitTest(termRunBtn!);

        Content.Client.UserInterface.Controls.ContentLineEdit? termInput = null;
        TerminalUiControl? termCtrl = null;
        await Client.WaitAssertion(() =>
        {
            var termList = new System.Collections.Generic.List<TerminalUiControl>();
            CollectDescendants(pdaWin!, termList);
            Assert.That(termList.Count, Is.EqualTo(1));
            termCtrl = termList[0];
            var inputs = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentLineEdit>();
            CollectDescendants(termCtrl, inputs);
            Assert.That(inputs.Count, Is.EqualTo(1));
            termInput = inputs[0];
        });

        // 1) SSH + `screen open` on remote APC -> verify stashed offscreen + IInputRelay click toggles physical breaker
        await TypeIntoLineEdit(termInput!, $"ssh {apcGuid}", submit: true);
        await TypeIntoLineEdit(termInput!, "screen open", submit: true);
        await RunTicks(15);

        await Server.WaitAssertion(() =>
        {
            var pdaComp = SEntMan.GetComponent<OxydComputerComponent>(pdaCompUid);
            var termProgUid = pdaComp.ActiveProgram!.Value;
            var termComp = SEntMan.GetComponent<Content.Shared._Oxyd.Computers.Programs.TerminalProgramComponent>(termProgUid);
            Assert.That(termComp.IsScreenAttached, Is.True,
                $"Expected Terminal IsScreenAttached == true, history:\n{string.Join("\n", termComp.History)}");
            Assert.That(SEntMan.TryGetComponent<ActiveRemoteScreenViewerComponent>(SPlayer, out var viewer), Is.True,
                "Expected SPlayer to have ActiveRemoteScreenViewerComponent");
            Assert.That(viewer!.AuthorizedTargets, Contains.Item(SEntMan.GetNetEntity(apcCompUid)));
            Assert.That(Server.System<SharedUserInterfaceSystem>().IsUiOpen(apcCompUid, ComputerUiKey.Key, SPlayer), Is.True,
                "Expected apcCompUid ComputerUiKey to remain open for SPlayer on Server");
        });

        Content.Client._Oxyd.Computers.ScreenSharing.RemoteScreenView? remoteView = null;
        ComputerDisplayWindow? stashedApcWin = null;
        Content.Client.UserInterface.Controls.ContentButton? remoteBreakerBtn = null;
        await Client.WaitAssertion(() =>
        {
            var cApcComp = CEntMan.GetEntity(FromServer(apcCompUid));
            Assert.That(CEntMan.TryGetComponent<Robust.Shared.GameObjects.UserInterfaceComponent>(cApcComp, out var apcUiComp), Is.True,
                $"Client missing UserInterfaceComponent on remote apcCompUid ({cApcComp})");
            Assert.That(apcUiComp!.ClientOpenInterfaces.TryGetValue(ComputerUiKey.Key, out var openApcBui), Is.True,
                "ClientOpenInterfaces missing ComputerUiKey on remote apcCompUid");
            Assert.That(openApcBui, Is.AssignableTo<Content.Client.UserInterface.StateRouting.RoutedBoundUserInterface>());

            var directWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen).ToList();
            Assert.That(directWins.Count, Is.EqualTo(1), "Remote screen should be stashed offscreen rather than popped out on WindowRoot");

            var stash = UiMan.WindowRoot.Children.FirstOrDefault(c => c.Name == "RemoteOffscreenStash");
            Assert.That(stash, Is.Not.Null);
            stashedApcWin = stash!.Children.OfType<ComputerDisplayWindow>().FirstOrDefault(w => !w.Disposed);
            Assert.That(stashedApcWin, Is.Not.Null, "Remote APC ComputerDisplayWindow should be stashed in RemoteOffscreenStash");
            Assert.That(stashedApcWin!.ScreenInsetControl, Is.TypeOf<Content.Client.UserInterface.Controls.ContentPanelContainer>());

            var views = new System.Collections.Generic.List<Content.Client._Oxyd.Computers.ScreenSharing.RemoteScreenView>();
            CollectDescendants(termCtrl!, views);
            Assert.That(views.Count, Is.EqualTo(1));
            remoteView = views[0];

            var btns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(stashedApcWin.ScreenInsetControl, btns);
            remoteBreakerBtn = btns.FirstOrDefault(b => b.VisibleInTree && !b.Disabled && b.Text != null &&
                (b.Text.Contains("Breaker", StringComparison.OrdinalIgnoreCase) || b.Text.Contains("Toggle", StringComparison.OrdinalIgnoreCase)));
            Assert.That(remoteBreakerBtn, Is.Not.Null, "Remote APC screen must expose a ContentButton for Toggle Breaker");
            Assert.That(remoteBreakerBtn, Is.AssignableTo<Content.Client.UserInterface.Controls.IInputRelayTarget>());
        });

        await ClickThroughRemoteScreenView(remoteView!, stashedApcWin!.ScreenInsetControl, remoteBreakerBtn!);
        await RunTicks(8);

        await Server.WaitAssertion(() =>
        {
            var afterBreaker = SEntMan.GetComponent<Content.Server.Power.Components.ApcComponent>(apcUid).MainBreakerEnabled;
            Assert.That(afterBreaker, Is.EqualTo(!initialBreaker), "Clicking Toggle Breaker inside RemoteScreenView must relay click and toggle server APC state.");
        });

        // 2) Detach remote APC stream via UI "Detach Screen" button, exit SSH, then stream OxydWorkstationComputer -> click `Apps` + `Run` Notekeeper via RemoteScreenView
        Content.Client.UserInterface.Controls.ContentButton? detachBtn = null;
        await Client.WaitAssertion(() =>
        {
            var termBtns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(termCtrl!, termBtns);
            detachBtn = termBtns.FirstOrDefault(b => b.VisibleInTree && b.Text == "Detach Screen");
            Assert.That(detachBtn, Is.Not.Null, "Terminal remote screen panel must expose visible 'Detach Screen' ContentButton");
        });
        await ClickViaHitTest(detachBtn!);
        await RunTicks(8);

        await TypeIntoLineEdit(termInput!, "exit", submit: true);
        await TypeIntoLineEdit(termInput!, $"ssh {wsGuid}", submit: true);
        await TypeIntoLineEdit(termInput!, "screen open", submit: true);
        await RunTicks(18);

        ComputerDisplayWindow? stashedWsWin = null;
        OsUiControl? wsOsCtrl = null;
        Content.Client.UserInterface.Controls.ContentButton? remoteWsAppsBtn = null;
        await Client.WaitAssertion(() =>
        {
            var stash = UiMan.WindowRoot.Children.FirstOrDefault(c => c.Name == "RemoteOffscreenStash");
            Assert.That(stash, Is.Not.Null);
            stashedWsWin = stash!.Children.OfType<ComputerDisplayWindow>().FirstOrDefault(w => !w.Disposed);
            Assert.That(stashedWsWin, Is.Not.Null);

            var osList = new System.Collections.Generic.List<OsUiControl>();
            CollectDescendants(stashedWsWin!.ScreenInsetControl, osList);
            Assert.That(osList.Count, Is.EqualTo(1));
            wsOsCtrl = osList[0];

            var btns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(wsOsCtrl, btns);
            remoteWsAppsBtn = btns.FirstOrDefault(b => b.VisibleInTree && b.Text != null && b.Text.Contains("Apps", StringComparison.OrdinalIgnoreCase));
            Assert.That(remoteWsAppsBtn, Is.Not.Null);
        });

        await ClickThroughRemoteScreenView(remoteView!, stashedWsWin!.ScreenInsetControl, remoteWsAppsBtn!);
        await RunTicks(8);

        Content.Client.UserInterface.Controls.ContentButton? remoteNoteRunBtn = null;
        await Client.WaitAssertion(() =>
        {
            remoteNoteRunBtn = FindRowRunButton(wsOsCtrl!, "Notekeeper");
            Assert.That(remoteNoteRunBtn, Is.Not.Null, "Clicking Apps inside RemoteScreenView must reveal Notekeeper Run ContentButton");
        });

        await ClickThroughRemoteScreenView(remoteView!, stashedWsWin.ScreenInsetControl, remoteNoteRunBtn!);
        await RunTicks(12);

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<OxydComputerComponent>(wsCompUid);
            Assert.That(comp.ActiveProgram, Is.Not.Null);
            var prog = SEntMan.GetComponent<Content.Shared._Oxyd.Computers.Programs.ProgramComponent>(comp.ActiveProgram!.Value);
            Assert.That(prog.Prototype?.Id, Is.EqualTo("NotekeeperProgram"),
                "Clicking Run Notekeeper inside RemoteScreenView must launch Notekeeper on remote server computer.");
        });

        await Client.WaitPost(() => pdaWin!.Close());
        await RunTicks(8);
    }

    [Test]
    public async Task TestRemoteScreenStaysWorkingWhenAlreadyOpenTargetClosed()
    {
        var routerSys = Server.System<NetworkRouterSystem>();
        var power = Server.System<PowerReceiverSystem>();

        await PlaceInHands("DetectivePDA");

        EntityUid apcUid = default;
        EntityUid wsUid = default;
        EntityUid wsCompUid = default;
        string apcGuid = string.Empty;
        string wsGuid = string.Empty;
        bool initialBreaker = false;

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(TargetCoords);
            var routerHost = SEntMan.SpawnEntity("StationNetworkRouter", coords);
            power.SetNeedsPower(routerHost, false);

            apcUid = SEntMan.SpawnEntity("APCBasic", coords);
            SEntMan.RemoveComponent<Content.Shared.Access.Components.AccessReaderComponent>(apcUid);
            Server.System<LockSystem>().Unlock(apcUid, null);
            initialBreaker = SEntMan.GetComponent<Content.Server.Power.Components.ApcComponent>(apcUid).MainBreakerEnabled;
            var apcCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(apcUid).ComputerEntity!.Value;
            apcGuid = routerSys.EnsureDeviceGuid(apcCompUid);

            wsUid = SEntMan.SpawnEntity("OxydWorkstationComputer", coords);
            power.SetNeedsPower(wsUid, false);
            wsCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(wsUid).ComputerEntity!.Value;
            wsGuid = routerSys.EnsureDeviceGuid(wsCompUid);
        });
        await RunTicks(6);

        // 1) Open Workstation UI FIRST (already open BEFORE remote screen attaches).
        Target = FromServer(wsUid);
        await Activate();
        await RunTicks(12);

        ComputerDisplayWindow? wsWin = null;
        await Client.WaitAssertion(() =>
        {
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen).ToList();
            Assert.That(openWins.Count, Is.EqualTo(1));
            wsWin = openWins[0];
            Assert.That(wsWin.Parent, Is.EqualTo(UiMan.WindowRoot));
        });

        // Open held PDA -> Apps -> Terminal
        await UseInHand();
        await RunTicks(12);

        ComputerDisplayWindow? pdaWin = null;
        Content.Client.UserInterface.Controls.ContentButton? pdaAppsBtn = null;
        await Client.WaitAssertion(() =>
        {
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen).ToList();
            Assert.That(openWins.Count, Is.EqualTo(2));
            pdaWin = openWins.First(w => w != wsWin);
            var btns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(pdaWin, btns);
            pdaAppsBtn = btns.First(b => b.Text != null && b.Text.Contains("Apps", StringComparison.OrdinalIgnoreCase));
            pdaWin.MoveToFront();
        });
        await ClickViaHitTest(pdaAppsBtn!);

        Content.Client.UserInterface.Controls.ContentButton? termRunBtn = null;
        await Client.WaitAssertion(() =>
        {
            var osList = new System.Collections.Generic.List<OsUiControl>();
            CollectDescendants(pdaWin!, osList);
            termRunBtn = FindRowRunButton(osList[0], "Terminal");
            Assert.That(termRunBtn, Is.Not.Null);
        });
        await ClickViaHitTest(termRunBtn!);

        Content.Client.UserInterface.Controls.ContentLineEdit? termInput = null;
        TerminalUiControl? termCtrl = null;
        await Client.WaitAssertion(() =>
        {
            var termList = new System.Collections.Generic.List<TerminalUiControl>();
            CollectDescendants(pdaWin!, termList);
            Assert.That(termList.Count, Is.EqualTo(1));
            termCtrl = termList[0];
            var inputs = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentLineEdit>();
            CollectDescendants(termCtrl, inputs);
            termInput = inputs[0];
        });

        // Connect to already-open Workstation via SSH + screen open
        await TypeIntoLineEdit(termInput!, $"ssh {wsGuid}", submit: true);
        await TypeIntoLineEdit(termInput!, "screen open", submit: true);
        await RunTicks(15);

        Content.Client._Oxyd.Computers.ScreenSharing.RemoteScreenView? remoteView = null;
        Content.Client.UserInterface.Controls.ContentButton? wsCloseBtn = null;
        await Client.WaitAssertion(() =>
        {
            // Already-open target UI MUST NOT be forced into RemoteScreenView or off-screen!
            Assert.That(wsWin!.Disposed, Is.False);
            Assert.That(wsWin.Parent, Is.EqualTo(UiMan.WindowRoot));

            var views = new System.Collections.Generic.List<Content.Client._Oxyd.Computers.ScreenSharing.RemoteScreenView>();
            CollectDescendants(termCtrl!, views);
            Assert.That(views.Count, Is.EqualTo(1));
            remoteView = views[0];
            Assert.That(HasAncestor(wsWin, remoteView), Is.False, "Target UI must never be parented inside RemoteScreenView.");

            var allBtns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(wsWin, allBtns);
            wsCloseBtn = allBtns.FirstOrDefault(b => b.Name == "CloseButton");
            Assert.That(wsCloseBtn, Is.Not.Null);
            wsWin.MoveToFront();
        });

        // User closes already-open Workstation by clicking its CloseButton ('X'):
        // Must move it to RemoteOffscreenStash instead of closing the BUI, and remote clicking must keep working!
        await ClickViaHitTest(wsCloseBtn!);
        await RunTicks(8);

        Content.Client.UserInterface.Controls.ContentButton? remoteWsAppsBtn = null;
        await Client.WaitAssertion(() =>
        {
            Assert.That(wsWin!.Disposed, Is.False);
            Assert.That(wsWin.Parent?.Name, Is.EqualTo("RemoteOffscreenStash"),
                "Closing an already-open target window via X while remote-viewed must move it offscreen instead of closing BUI.");
            pdaWin!.MoveToFront();

            var osList = new System.Collections.Generic.List<OsUiControl>();
            CollectDescendants(wsWin.ScreenInsetControl, osList);
            Assert.That(osList.Count, Is.EqualTo(1));
            var btns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(osList[0], btns);
            remoteWsAppsBtn = btns.First(b => b.VisibleInTree && b.Text != null && b.Text.Contains("Apps", StringComparison.OrdinalIgnoreCase));
        });

        await ClickThroughRemoteScreenView(remoteView!, wsWin!.ScreenInsetControl, remoteWsAppsBtn!);
        await RunTicks(8);

        Content.Client.UserInterface.Controls.ContentButton? remoteNoteRunBtn = null;
        await Client.WaitAssertion(() =>
        {
            var osList = new System.Collections.Generic.List<OsUiControl>();
            CollectDescendants(wsWin.ScreenInsetControl, osList);
            remoteNoteRunBtn = FindRowRunButton(osList[0], "Notekeeper");
            Assert.That(remoteNoteRunBtn, Is.Not.Null);
        });

        await ClickThroughRemoteScreenView(remoteView!, wsWin.ScreenInsetControl, remoteNoteRunBtn!);
        await RunTicks(12);

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<OxydComputerComponent>(wsCompUid);
            Assert.That(comp.ActiveProgram, Is.Not.Null);
            var prog = SEntMan.GetComponent<Content.Shared._Oxyd.Computers.Programs.ProgramComponent>(comp.ActiveProgram!.Value);
            Assert.That(prog.Prototype?.Id, Is.EqualTo("NotekeeperProgram"));
        });

        // Reopen the previously closed Workstation via in-world Activate while still remote-viewing it:
        // Workstation window MUST reappear on WindowRoot (not closed/hidden) AND remote screen must stay interactive!
        Target = FromServer(wsUid);
        await Activate();
        await RunTicks(12);

        await Client.WaitAssertion(() =>
        {
            Assert.That(wsWin!.Disposed, Is.False);
            Assert.That(wsWin.Parent, Is.EqualTo(UiMan.WindowRoot),
                "Reopening a previously closed remote-viewed Workstation must bring its UI back onto WindowRoot.");
            Assert.That(wsWin.VisibleInTree, Is.True);
            var noteCtrls = new System.Collections.Generic.List<NotekeeperUiControl>();
            CollectDescendants(wsWin.ScreenInsetControl, noteCtrls);
            Assert.That(noteCtrls.Count, Is.EqualTo(1), "Reopened Workstation must keep mounted child program UIs intact.");
            wsWin.MoveToFront();
        });

        // Close Workstation a second time via X -> still stashes offscreen cleanly, remote view still works!
        await ClickViaHitTest(wsCloseBtn!);
        await RunTicks(8);

        await Client.WaitAssertion(() =>
        {
            Assert.That(wsWin!.Disposed, Is.False);
            Assert.That(wsWin.Parent?.Name, Is.EqualTo("RemoteOffscreenStash"));
            pdaWin!.MoveToFront();
        });

        // 2) Detach, exit SSH, open APC UI FIRST, stream it via screen open, then close apcWin via Close() (Escape / shortcut simulation)
        Content.Client.UserInterface.Controls.ContentButton? detachBtn = null;
        await Client.WaitAssertion(() =>
        {
            var termBtns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(termCtrl!, termBtns);
            detachBtn = termBtns.First(b => b.VisibleInTree && b.Text == "Detach Screen");
        });
        await ClickViaHitTest(detachBtn!);
        await RunTicks(8);

        await TypeIntoLineEdit(termInput!, "exit", submit: true);

        Target = FromServer(apcUid);
        await Activate();
        await RunTicks(10);

        ComputerDisplayWindow? apcWin = null;
        await Client.WaitAssertion(() =>
        {
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen && w != pdaWin).ToList();
            Assert.That(openWins.Count, Is.EqualTo(1));
            apcWin = openWins[0];
            pdaWin!.MoveToFront();
        });

        await TypeIntoLineEdit(termInput!, $"ssh {apcGuid}", submit: true);
        await TypeIntoLineEdit(termInput!, "screen open", submit: true);
        await RunTicks(15);

        await Client.WaitAssertion(() =>
        {
            Assert.That(apcWin!.Disposed, Is.False);
            Assert.That(apcWin.Parent, Is.EqualTo(UiMan.WindowRoot));
            Assert.That(HasAncestor(apcWin, remoteView!), Is.False);
        });

        await Client.WaitPost(() => apcWin!.Close());
        await RunTicks(8);

        Content.Client.UserInterface.Controls.ContentButton? remoteBreakerBtn = null;
        await Client.WaitAssertion(() =>
        {
            Assert.That(apcWin!.Disposed, Is.False);
            Assert.That(apcWin.Parent?.Name, Is.EqualTo("RemoteOffscreenStash"),
                "Calling Close() on an already-open target window while remote-viewed must move it offscreen instead of closing BUI.");
            var btns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(apcWin.ScreenInsetControl, btns);
            remoteBreakerBtn = btns.First(b => b.VisibleInTree && !b.Disabled && b.Text != null &&
                (b.Text.Contains("Breaker", StringComparison.OrdinalIgnoreCase) || b.Text.Contains("Toggle", StringComparison.OrdinalIgnoreCase)));
        });

        await ClickThroughRemoteScreenView(remoteView!, apcWin!.ScreenInsetControl, remoteBreakerBtn!);
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            var afterBreaker = SEntMan.GetComponent<Content.Server.Power.Components.ApcComponent>(apcUid).MainBreakerEnabled;
            Assert.That(afterBreaker, Is.EqualTo(!initialBreaker),
                "Remote screen clicking must continue toggling server APC state after already-open target window is closed.");
        });

        // Reopen the previously closed APC via Activate while still remote-viewing it, then verify BOTH
        // the reopened APC window is visible on WindowRoot AND clicking Toggle Breaker inside RemoteScreenView still works!
        Target = FromServer(apcUid);
        await Activate();
        await RunTicks(12);

        await Client.WaitAssertion(() =>
        {
            Assert.That(apcWin!.Disposed, Is.False);
            Assert.That(apcWin.Parent, Is.EqualTo(UiMan.WindowRoot),
                "Reopening a previously closed remote-viewed APC must bring its UI back onto WindowRoot.");
            Assert.That(apcWin.VisibleInTree, Is.True);
            pdaWin!.MoveToFront();
        });

        await ClickThroughRemoteScreenView(remoteView!, apcWin!.ScreenInsetControl, remoteBreakerBtn!);
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            var afterReopenBreaker = SEntMan.GetComponent<Content.Server.Power.Components.ApcComponent>(apcUid).MainBreakerEnabled;
            Assert.That(afterReopenBreaker, Is.EqualTo(initialBreaker),
                "Remote screen clicking must still work after reopening a previously closed target UI.");
        });

        await Client.WaitPost(() => pdaWin!.Close());
        await RunTicks(8);
    }

    [Test]
    public async Task TestCloseAndReopenRemoteViewPreservesOpenAndPhysicallyOpenedTargetUi()
    {
        var routerSys = Server.System<NetworkRouterSystem>();
        var power = Server.System<PowerReceiverSystem>();

        await PlaceInHands("DetectivePDA");

        EntityUid apcUid = default;
        EntityUid wsUid = default;
        EntityUid wsCompUid = default;
        string apcGuid = string.Empty;
        string wsGuid = string.Empty;
        bool initialBreaker = false;

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(TargetCoords);
            var routerHost = SEntMan.SpawnEntity("StationNetworkRouter", coords);
            power.SetNeedsPower(routerHost, false);

            apcUid = SEntMan.SpawnEntity("APCBasic", coords);
            SEntMan.RemoveComponent<Content.Shared.Access.Components.AccessReaderComponent>(apcUid);
            Server.System<LockSystem>().Unlock(apcUid, null);
            initialBreaker = SEntMan.GetComponent<Content.Server.Power.Components.ApcComponent>(apcUid).MainBreakerEnabled;
            var apcCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(apcUid).ComputerEntity!.Value;
            apcGuid = routerSys.EnsureDeviceGuid(apcCompUid);

            wsUid = SEntMan.SpawnEntity("OxydWorkstationComputer", coords);
            power.SetNeedsPower(wsUid, false);
            wsCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(wsUid).ComputerEntity!.Value;
            wsGuid = routerSys.EnsureDeviceGuid(wsCompUid);
        });
        await RunTicks(6);

        // 1) Workstation is ALREADY OPEN at start of remote view -> Detach remote view ->
        //    Workstation MUST stay open on WindowRoot, click inside it works, then re-opening remote view keeps Workstation open!
        Target = FromServer(wsUid);
        await Activate();
        await RunTicks(12);

        ComputerDisplayWindow? wsWin = null;
        await Client.WaitAssertion(() =>
        {
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen).ToList();
            Assert.That(openWins.Count, Is.EqualTo(1));
            wsWin = openWins[0];
        });

        await UseInHand();
        await RunTicks(12);

        ComputerDisplayWindow? pdaWin = null;
        Content.Client.UserInterface.Controls.ContentButton? pdaAppsBtn = null;
        await Client.WaitAssertion(() =>
        {
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen).ToList();
            Assert.That(openWins.Count, Is.EqualTo(2));
            pdaWin = openWins.First(w => w != wsWin);
            var btns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(pdaWin, btns);
            pdaAppsBtn = btns.First(b => b.Text != null && b.Text.Contains("Apps", StringComparison.OrdinalIgnoreCase));
            pdaWin.MoveToFront();
        });
        await ClickViaHitTest(pdaAppsBtn!);

        Content.Client.UserInterface.Controls.ContentButton? termRunBtn = null;
        await Client.WaitAssertion(() =>
        {
            var osList = new System.Collections.Generic.List<OsUiControl>();
            CollectDescendants(pdaWin!, osList);
            termRunBtn = FindRowRunButton(osList[0], "Terminal");
            Assert.That(termRunBtn, Is.Not.Null);
        });
        await ClickViaHitTest(termRunBtn!);

        Content.Client.UserInterface.Controls.ContentLineEdit? termInput = null;
        TerminalUiControl? termCtrl = null;
        await Client.WaitAssertion(() =>
        {
            var termList = new System.Collections.Generic.List<TerminalUiControl>();
            CollectDescendants(pdaWin!, termList);
            termCtrl = termList[0];
            var inputs = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentLineEdit>();
            CollectDescendants(termCtrl, inputs);
            termInput = inputs[0];
        });

        await TypeIntoLineEdit(termInput!, $"ssh {wsGuid}", submit: true);
        await TypeIntoLineEdit(termInput!, "screen open", submit: true);
        await RunTicks(12);

        // Detach remote view while Workstation is open -> Workstation must stay open on WindowRoot
        Content.Client.UserInterface.Controls.ContentButton? detachBtn = null;
        await Client.WaitAssertion(() =>
        {
            Assert.That(wsWin!.Disposed, Is.False);
            Assert.That(wsWin.Parent, Is.EqualTo(UiMan.WindowRoot));
            var termBtns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(termCtrl!, termBtns);
            detachBtn = termBtns.First(b => b.VisibleInTree && b.Text == "Detach Screen");
            pdaWin!.MoveToFront();
        });
        await ClickViaHitTest(detachBtn!);
        await RunTicks(8);

        Content.Client.UserInterface.Controls.ContentButton? wsAppsBtn = null;
        await Client.WaitAssertion(() =>
        {
            Assert.That(wsWin!.Disposed, Is.False);
            Assert.That(wsWin.Parent, Is.EqualTo(UiMan.WindowRoot),
                "Detaching remote view must keep already-open target window visible on WindowRoot.");
            wsWin.MoveToFront();
            var btns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(wsWin.ScreenInsetControl, btns);
            wsAppsBtn = btns.First(b => b.VisibleInTree && b.Text != null && b.Text.Contains("Apps", StringComparison.OrdinalIgnoreCase));
        });

        // First click directly inside Workstation window after closing remote view must NOT close/hide it
        await ClickViaHitTest(wsAppsBtn!);
        await RunTicks(8);

        Content.Client.UserInterface.Controls.ContentButton? wsNoteRunBtn = null;
        await Client.WaitAssertion(() =>
        {
            Assert.That(wsWin!.Disposed, Is.False);
            Assert.That(wsWin.Parent, Is.EqualTo(UiMan.WindowRoot));
            var osList = new System.Collections.Generic.List<OsUiControl>();
            CollectDescendants(wsWin.ScreenInsetControl, osList);
            wsNoteRunBtn = FindRowRunButton(osList[0], "Notekeeper");
            Assert.That(wsNoteRunBtn, Is.Not.Null, "First click on already-open target window after closing remote view must keep window open and interactive.");
            pdaWin!.MoveToFront();
        });

        // Reopen remote view (`screen open` again) while Workstation is open on WindowRoot ->
        // Workstation stays open on WindowRoot AND remote screen click launches Notekeeper!
        await TypeIntoLineEdit(termInput!, "screen open", submit: true);
        await RunTicks(12);

        Content.Client._Oxyd.Computers.ScreenSharing.RemoteScreenView? remoteView = null;
        await Client.WaitAssertion(() =>
        {
            Assert.That(wsWin!.Disposed, Is.False);
            Assert.That(wsWin.Parent, Is.EqualTo(UiMan.WindowRoot),
                "Reopening remote view must NOT hide an already-open target window off-screen.");
            var views = new System.Collections.Generic.List<Content.Client._Oxyd.Computers.ScreenSharing.RemoteScreenView>();
            CollectDescendants(termCtrl!, views);
            remoteView = views[0];
            pdaWin!.MoveToFront();
        });

        await ClickThroughRemoteScreenView(remoteView!, wsWin!.ScreenInsetControl, wsNoteRunBtn!);
        await RunTicks(12);

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<OxydComputerComponent>(wsCompUid);
            Assert.That(comp.ActiveProgram, Is.Not.Null);
            var prog = SEntMan.GetComponent<Content.Shared._Oxyd.Computers.Programs.ProgramComponent>(comp.ActiveProgram!.Value);
            Assert.That(prog.Prototype?.Id, Is.EqualTo("NotekeeperProgram"));
        });

        // 2) Physically open APC DURING remote view -> click Breaker on APC -> close remote view ->
        //    APC stays open, reopen remote view -> APC STILL stays open and remote click works!
        await Client.WaitAssertion(() =>
        {
            var termBtns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(termCtrl!, termBtns);
            detachBtn = termBtns.First(b => b.VisibleInTree && b.Text == "Detach Screen");
            pdaWin!.MoveToFront();
        });
        await ClickViaHitTest(detachBtn!);
        await RunTicks(8);
        await TypeIntoLineEdit(termInput!, "exit", submit: true);

        // Close Workstation manually now that remote view is detached (normal X close must work!)
        Content.Client.UserInterface.Controls.ContentButton? wsCloseBtn = null;
        await Client.WaitAssertion(() =>
        {
            var btns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(wsWin!, btns);
            wsCloseBtn = btns.First(b => b.Name == "CloseButton");
            wsWin!.MoveToFront();
        });
        await ClickViaHitTest(wsCloseBtn!);
        await RunTicks(8);
        await Client.WaitAssertion(() =>
        {
            Assert.That(wsWin!.IsOpen, Is.False, "After remote view is detached, closing target window must close normally.");
            pdaWin!.MoveToFront();
        });

        // Start remote view on CLOSED APC -> physically activate APC during remote view!
        await TypeIntoLineEdit(termInput!, $"ssh {apcGuid}", submit: true);
        await TypeIntoLineEdit(termInput!, "screen open", submit: true);
        await RunTicks(12);

        Target = FromServer(apcUid);
        await Activate();
        await RunTicks(10);

        ComputerDisplayWindow? apcWin = null;
        Content.Client.UserInterface.Controls.ContentButton? apcBreakerBtn = null;
        await Client.WaitAssertion(() =>
        {
            var stash = UiMan.WindowRoot.Children.FirstOrDefault(c => c.Name == "RemoteOffscreenStash");
            Assert.That(stash == null || !stash.Children.OfType<ComputerDisplayWindow>().Any(), Is.True,
                "Physically opening APC during remote view must pull it out of RemoteOffscreenStash.");
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen && w != pdaWin).ToList();
            Assert.That(openWins.Count, Is.EqualTo(1));
            apcWin = openWins[0];
            var btns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(apcWin.ScreenInsetControl, btns);
            apcBreakerBtn = btns.First(b => b.VisibleInTree && !b.Disabled && b.Text != null &&
                (b.Text.Contains("Breaker", StringComparison.OrdinalIgnoreCase) || b.Text.Contains("Toggle", StringComparison.OrdinalIgnoreCase)));
            apcWin.MoveToFront();
        });

        // First click directly inside physically-opened APC window must NOT make window disappear!
        await ClickViaHitTest(apcBreakerBtn!);
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            var afterBreaker = SEntMan.GetComponent<Content.Server.Power.Components.ApcComponent>(apcUid).MainBreakerEnabled;
            Assert.That(afterBreaker, Is.EqualTo(!initialBreaker));
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(apcWin!.Disposed, Is.False);
            Assert.That(apcWin.Parent, Is.EqualTo(UiMan.WindowRoot),
                "First click inside physically-opened APC during remote view must keep window visible on WindowRoot.");
            var termBtns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(termCtrl!, termBtns);
            detachBtn = termBtns.First(b => b.VisibleInTree && b.Text == "Detach Screen");
            pdaWin!.MoveToFront();
        });

        // Close remote view (Detach Screen) -> Reopen remote view (`screen open`) -> APC STILL visible & remote click toggles breaker!
        await ClickViaHitTest(detachBtn!);
        await RunTicks(8);

        await Client.WaitAssertion(() =>
        {
            Assert.That(apcWin!.Disposed, Is.False);
            Assert.That(apcWin.Parent, Is.EqualTo(UiMan.WindowRoot),
                "Detaching remote view must keep physically-opened APC window open on WindowRoot.");
        });

        await TypeIntoLineEdit(termInput!, "screen open", submit: true);
        await RunTicks(12);

        await Client.WaitAssertion(() =>
        {
            Assert.That(apcWin!.Disposed, Is.False);
            Assert.That(apcWin.Parent, Is.EqualTo(UiMan.WindowRoot),
                "Reopening remote view must keep physically-opened APC window open on WindowRoot.");
            pdaWin!.MoveToFront();
        });

        await ClickThroughRemoteScreenView(remoteView!, apcWin!.ScreenInsetControl, apcBreakerBtn!);
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            var afterSecondToggle = SEntMan.GetComponent<Content.Server.Power.Components.ApcComponent>(apcUid).MainBreakerEnabled;
            Assert.That(afterSecondToggle, Is.EqualTo(initialBreaker));
        });

        await Client.WaitPost(() => pdaWin!.Close());
        await RunTicks(8);
    }

    [Test]
    public async Task TestApcWalkAwayRemoteReopenAndRewalkInRangeDoesNotCrashUiFrameUpdate()
    {
        var routerSys = Server.System<NetworkRouterSystem>();
        var power = Server.System<PowerReceiverSystem>();
        var xformSys = SEntMan.System<Robust.Shared.GameObjects.SharedTransformSystem>();

        await PlaceInHands("DetectivePDA");

        EntityUid apcUid = default;
        EntityUid apcCompUid = default;
        string apcGuid = string.Empty;
        bool initialBreaker = false;

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(TargetCoords);
            var routerHost = SEntMan.SpawnEntity("StationNetworkRouter", coords);
            power.SetNeedsPower(routerHost, false);

            apcUid = SEntMan.SpawnEntity("APCBasic", coords);
            SEntMan.RemoveComponent<Content.Shared.Access.Components.AccessReaderComponent>(apcUid);
            Server.System<LockSystem>().Unlock(apcUid, null);
            initialBreaker = SEntMan.GetComponent<Content.Server.Power.Components.ApcComponent>(apcUid).MainBreakerEnabled;
            apcCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(apcUid).ComputerEntity!.Value;
            apcGuid = routerSys.EnsureDeviceGuid(apcCompUid);
        });
        await RunTicks(6);

        // 1) Open APC physically
        Target = FromServer(apcUid);
        await Activate();
        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen).ToList();
            Assert.That(openWins.Count, Is.EqualTo(1), "Physical APC activation should open 1 ComputerDisplayWindow.");
        });

        // 2) Walk away out of physical UI range -> APC window closes cleanly
        await Server.WaitPost(() =>
        {
            var farCoords = SEntMan.GetCoordinates(TargetCoords).Offset(new Vector2(25f, 0f));
            xformSys.SetCoordinates(SPlayer, farCoords);
        });
        await RunTicks(12);

        await Client.WaitAssertion(() =>
        {
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen).ToList();
            Assert.That(openWins.Count, Is.EqualTo(0), "Walking out of APC range must close the physical APC window.");
        });

        // 3) Reopen APC remotely via handheld PDA Terminal while still out of physical range
        await UseInHand();
        await RunTicks(10);

        ComputerDisplayWindow? pdaWin = null;
        Content.Client.UserInterface.Controls.ContentButton? pdaAppsBtn = null;
        await Client.WaitAssertion(() =>
        {
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen).ToList();
            Assert.That(openWins.Count, Is.EqualTo(1));
            pdaWin = openWins[0];
            var btns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(pdaWin, btns);
            pdaAppsBtn = btns.First(b => b.Text != null && b.Text.Contains("Apps", StringComparison.OrdinalIgnoreCase));
        });
        await ClickViaHitTest(pdaAppsBtn!);

        Content.Client.UserInterface.Controls.ContentButton? termRunBtn = null;
        await Client.WaitAssertion(() =>
        {
            var osList = new System.Collections.Generic.List<OsUiControl>();
            CollectDescendants(pdaWin!, osList);
            termRunBtn = FindRowRunButton(osList[0], "Terminal");
            Assert.That(termRunBtn, Is.Not.Null);
        });
        await ClickViaHitTest(termRunBtn!);

        Content.Client.UserInterface.Controls.ContentLineEdit? termInput = null;
        TerminalUiControl? termCtrl = null;
        await Client.WaitAssertion(() =>
        {
            var termList = new System.Collections.Generic.List<TerminalUiControl>();
            CollectDescendants(pdaWin!, termList);
            termCtrl = termList[0];
            var inputs = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentLineEdit>();
            CollectDescendants(termCtrl, inputs);
            termInput = inputs[0];
        });

        // Add sibling window after pdaWin on WindowRoot so DoFrameUpdateRecursive enumerates multiple WindowRoot children
        var siblingWin = default(Robust.Client.UserInterface.CustomControls.DefaultWindow);
        await Client.WaitPost(() =>
        {
            siblingWin = new Robust.Client.UserInterface.CustomControls.DefaultWindow { SetSize = new Vector2(100, 100) };
            siblingWin.Open();
        });

        await TypeIntoLineEdit(termInput!, $"ssh {apcGuid}", submit: true);
        await TypeIntoLineEdit(termInput!, "screen open", submit: true);
        await RunTicks(2);

        var frameUpdateMethod = UiMan.GetType().GetMethod("FrameUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(frameUpdateMethod, Is.Not.Null);
        var frameArgs = new Robust.Shared.Timing.FrameEventArgs(0.016f);

        // Explicitly drive UI FrameUpdate directly (as Clyde render loop does) with RemoteScreenView active
        await Client.WaitPost(() =>
        {
            frameUpdateMethod!.Invoke(UiMan, [frameArgs]);
        });
        await RunTicks(12);

        // 4) Walk back into physical range of APC while remote terminal view is open,
        //    exercise FrameUpdate, and activate APC physically -> no InvalidOperationException in DoFrameUpdateRecursive
        await Server.WaitPost(() =>
        {
            var nearCoords = SEntMan.GetCoordinates(TargetCoords);
            xformSys.SetCoordinates(SPlayer, nearCoords);
        });
        await RunTicks(10);

        await Client.WaitPost(() =>
        {
            frameUpdateMethod!.Invoke(UiMan, [frameArgs]);
        });

        Target = FromServer(apcUid);
        await Activate();
        await RunTicks(10);

        await Client.WaitPost(() =>
        {
            frameUpdateMethod!.Invoke(UiMan, [frameArgs]);
        });

        ComputerDisplayWindow? apcWin = null;
        Content.Client.UserInterface.Controls.ContentButton? apcBreakerBtn = null;
        await Client.WaitAssertion(() =>
        {
            var stash = UiMan.WindowRoot.Children.FirstOrDefault(c => c.Name == "RemoteOffscreenStash");
            Assert.That(stash == null || !stash.Children.OfType<ComputerDisplayWindow>().Any(), Is.True,
                "Activating APC after walking back into range should bring APC window onto WindowRoot cleanly.");
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen && w != pdaWin).ToList();
            Assert.That(openWins.Count, Is.EqualTo(1));
            apcWin = openWins[0];
            var btns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(apcWin.ScreenInsetControl, btns);
            apcBreakerBtn = btns.First(b => b.VisibleInTree && !b.Disabled && b.Text != null &&
                (b.Text.Contains("Breaker", StringComparison.OrdinalIgnoreCase) || b.Text.Contains("Toggle", StringComparison.OrdinalIgnoreCase)));
            pdaWin!.MoveToFront();
        });

        Content.Client._Oxyd.Computers.ScreenSharing.RemoteScreenView? remoteView = null;
        await Client.WaitAssertion(() =>
        {
            var views = new System.Collections.Generic.List<Content.Client._Oxyd.Computers.ScreenSharing.RemoteScreenView>();
            CollectDescendants(termCtrl!, views);
            remoteView = views[0];
        });

        await ClickThroughRemoteScreenView(remoteView!, apcWin!.ScreenInsetControl, apcBreakerBtn!);
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            var afterBreaker = SEntMan.GetComponent<Content.Server.Power.Components.ApcComponent>(apcUid).MainBreakerEnabled;
            Assert.That(afterBreaker, Is.EqualTo(!initialBreaker));
        });

        await Client.WaitPost(() =>
        {
            siblingWin?.Close();
            pdaWin!.Close();
        });
        await RunTicks(8);
    }

#if DEBUG
    [Test]
    public async Task TestDebugModeDownloadServerAlwaysAvailable()
    {
        var compSys = Server.System<OxydComputerSystem>();
        var fsSys = Server.System<SharedFileSystemSystem>();
        var routerSys = Server.System<NetworkRouterSystem>();
        var dataServerSys = Server.System<Content.Server._Oxyd.Computers.Servers.DataServerSystem>();

        // Spawn held PDA, APC, Workstation, and another Workstation on a separate MapId with NO StationNetworkRouter and NO BaseDataServer.
        var pdaNet = await PlaceInHands("DetectivePDA");
        var pdaUid = ToServer(pdaNet);
        EntityUid compUid = default;
        EntityUid apcCompUid = default;
        EntityUid wsCompUid = default;
        EntityUid remoteMapWsCompUid = default;
        EntityUid downloaderUid = default;
        EntityUid terminalUid = default;
        EntityUid bootUid = default;

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(TargetCoords);
            var power = Server.System<PowerReceiverSystem>();
            compUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(pdaUid).ComputerEntity!.Value;
            bootUid = SEntMan.GetComponent<OxydComputerComponent>(compUid).BootProgram!.Value;

            var apcUid = SEntMan.SpawnEntity("APCBasic", coords);
            apcCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(apcUid).ComputerEntity!.Value;

            var wsUid = SEntMan.SpawnEntity("OxydWorkstationComputer", coords);
            power.SetNeedsPower(wsUid, false);
            wsCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(wsUid).ComputerEntity!.Value;

            SEntMan.System<SharedMapSystem>().CreateMap(out var secondMapId);
            var remoteWsUid = SEntMan.SpawnEntity("OxydWorkstationComputer", new MapCoordinates(Vector2.Zero, secondMapId));
            power.SetNeedsPower(remoteWsUid, false);
            remoteMapWsCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(remoteWsUid).ComputerEntity!.Value;
        });
        await RunTicks(5);

        string apcGuid = string.Empty;
        string remoteWsGuid = string.Empty;
        await Server.WaitAssertion(() =>
        {
            Assert.That(dataServerSys.TryGetFirstOnlineRepositoryServer(compUid, out var srvUid, out var srvCompUid, out _, out _, out var err),
                Is.True, $"Expected debug download server to be available without map router/server, got error: {err}");
            Assert.That(dataServerSys.IsDebugFallbackServer(srvUid), Is.True);
            Assert.That(dataServerSys.IsDebugFallbackServer(srvCompUid), Is.True);
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(srvUid).EntityName, Is.EqualTo("DEV DEBUG SERVER"));
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(srvCompUid).EntityName, Is.EqualTo("DEV DEBUG SERVER"));

            // ALL devices (even across separate maps) connect to the global debug network!
            Assert.That(routerSys.IsNetworkConnected(compUid), Is.True);
            Assert.That(routerSys.IsNetworkConnected(apcCompUid), Is.True);
            Assert.That(routerSys.IsNetworkConnected(wsCompUid), Is.True);
            Assert.That(routerSys.IsNetworkConnected(remoteMapWsCompUid), Is.True);

            Assert.That(routerSys.GetConnectedNetworkLabel(compUid), Is.EqualTo("DEV DEBUG SERVER"));
            Assert.That(routerSys.GetConnectedNetworkLabel(apcCompUid), Is.EqualTo("DEV DEBUG SERVER"));
            Assert.That(routerSys.GetConnectedNetworkLabel(remoteMapWsCompUid), Is.EqualTo("DEV DEBUG SERVER"));

            apcGuid = routerSys.EnsureDeviceGuid(apcCompUid);
            remoteWsGuid = routerSys.EnsureDeviceGuid(remoteMapWsCompUid);

            Assert.That(compSys.TryLaunchExecutable(compUid, "/bin/downloader.nte", out var dlUid, out _), Is.True);
            downloaderUid = dlUid!.Value;

            Assert.That(compSys.TryLaunchExecutable(compUid, "/bin/terminal.nte", out var tUid, out _), Is.True);
            terminalUid = tUid!.Value;
        });

        await UseInHand();
        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            Assert.That(TryGetBui(ProgramUiKey.Key, out var bootBui, FromServer(bootUid)), Is.True);
            var bootCtrl = ((ProgramBoundUserInterface) bootBui!).Control;
            Assert.That(bootCtrl, Is.Not.Null);
            Assert.That(GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Text != null && l.Text.Contains("DEV DEBUG SERVER"), bootCtrl!), Is.Not.Null,
                "Boot OS status bar must show DEV DEBUG SERVER on debug global network.");

            Assert.That(TryGetBui(ProgramUiKey.Key, out var dlBui, FromServer(downloaderUid)), Is.True);
            var dlCtrl = ((ProgramBoundUserInterface) dlBui!).Control;
            Assert.That(dlCtrl, Is.Not.Null);
            Assert.That(GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Text != null && l.Text.Contains("Server: DEV DEBUG SERVER") && l.Text.Contains("ONLINE"), dlCtrl!), Is.Not.Null,
                "Downloader UI must show DEV DEBUG SERVER | ONLINE.");

            Assert.That(TryGetBui(ProgramUiKey.Key, out var termBui, FromServer(terminalUid)), Is.True);
            var termCtrl = ((ProgramBoundUserInterface) termBui!).Control;
            Assert.That(termCtrl, Is.Not.Null);
            Assert.That(GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Text != null && l.Text.Contains("NET: ONLINE (DEV DEBUG SERVER)"), termCtrl!), Is.Not.Null,
                "Terminal UI header must show NET: ONLINE (DEV DEBUG SERVER).");
        });

        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage("ifconfig"), FromServer(terminalUid));
        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage("scan"), FromServer(terminalUid));
        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage("ping DEV DEBUG SERVER"), FromServer(terminalUid));
        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage($"ping {apcGuid}"), FromServer(terminalUid));
        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage($"ssh {remoteWsGuid}"), FromServer(terminalUid));
        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage("screen open"), FromServer(terminalUid));
        await RunTicks(12);

        await Server.WaitAssertion(() =>
        {
            var hist = SEntMan.GetComponent<TerminalProgramComponent>(terminalUid).History;
            Assert.That(hist.Any(l => l.Contains("ROUTER=ONLINE (DEV DEBUG SERVER)")), Is.True);
            Assert.That(hist.Any(l => l.Contains("DEV DEBUG SERVER") && l.Contains(apcGuid) == false && l.Contains("64 bytes")), Is.True,
                $"Expected ping DEV DEBUG SERVER to succeed, got:\n{string.Join("\n", hist)}");
            Assert.That(hist.Any(l => l.Contains($"64 bytes from {apcGuid}")), Is.True);
            Assert.That(hist.Any(l => l.Contains(remoteWsGuid) && l.Contains("64 bytes") == false), Is.True);
            Assert.That(SEntMan.TryGetComponent<ActiveRemoteScreenViewerComponent>(SPlayer, out var viewer), Is.True);
            Assert.That(viewer!.AuthorizedTargets, Contains.Item(SEntMan.GetNetEntity(remoteMapWsCompUid)));
        });

        await SendBui(ProgramUiKey.Key, new DownloaderActionMessage(DownloaderAction.DownloadItem, "/bin/nanotask.nte"), FromServer(downloaderUid));
        await RunTicks(35);

        await Server.WaitAssertion(() =>
        {
            Assert.That(fsSys.TryFindFileSystem(compUid, out var driveUid, out var fs), Is.True);
            Assert.That(fsSys.TryGetFile<ProgramVirtualFile>(driveUid!.Value, "/bin/nanotask.nte", out var dlProg, fs), Is.True);
            Assert.That(dlProg!.ProgramId, Is.EqualTo("NanoTaskProgram"));
        });
    }

    [Test]
    public async Task TestRemoteScreenTerminalKeepsTextInputFocus()
    {
        var routerSys = Server.System<NetworkRouterSystem>();
        var power = Server.System<PowerReceiverSystem>();
        var fsSys = Server.System<Content.Shared._Oxyd.Computers.FileSystem.SharedFileSystemSystem>();

        var pdaNet = await PlaceInHands("DetectivePDA");
        var pdaUid = ToServer(pdaNet);

        EntityUid wsUid = default;
        EntityUid wsCompUid = default;
        EntityUid pdaCompUid = default;
        string wsGuid = string.Empty;

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(TargetCoords);
            var routerHost = SEntMan.SpawnEntity("StationNetworkRouter", coords);
            power.SetNeedsPower(routerHost, false);

            wsUid = SEntMan.SpawnEntity("OxydWorkstationComputer", coords);
            power.SetNeedsPower(wsUid, false);
            wsCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(wsUid).ComputerEntity!.Value;
            pdaCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(pdaUid).ComputerEntity!.Value;
        });
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            wsGuid = routerSys.EnsureDeviceGuid(wsCompUid);
            Assert.That(routerSys.IsNetworkConnected(pdaCompUid), Is.True);
            Assert.That(routerSys.IsNetworkConnected(wsCompUid), Is.True);
        });

        await UseInHand();
        await RunTicks(12);

        ComputerDisplayWindow? pdaWin = null;
        Content.Client.UserInterface.Controls.ContentButton? pdaAppsBtn = null;
        await Client.WaitAssertion(() =>
        {
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen).ToList();
            Assert.That(openWins.Count, Is.EqualTo(1));
            pdaWin = openWins[0];
            var btns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(pdaWin, btns);
            pdaAppsBtn = btns.First(b => b.Text != null && b.Text.Contains("Apps", StringComparison.OrdinalIgnoreCase));
        });
        await ClickViaHitTest(pdaAppsBtn!);

        Content.Client.UserInterface.Controls.ContentButton? termRunBtn = null;
        await Client.WaitAssertion(() =>
        {
            var osList = new System.Collections.Generic.List<OsUiControl>();
            CollectDescendants(pdaWin!, osList);
            Assert.That(osList.Count, Is.EqualTo(1));
            termRunBtn = FindRowRunButton(osList[0], "Terminal");
            Assert.That(termRunBtn, Is.Not.Null);
        });
        await ClickViaHitTest(termRunBtn!);

        Content.Client.UserInterface.Controls.ContentLineEdit? pdaTermInput = null;
        TerminalUiControl? pdaTermCtrl = null;
        await Client.WaitAssertion(() =>
        {
            var termList = new System.Collections.Generic.List<TerminalUiControl>();
            CollectDescendants(pdaWin!, termList);
            Assert.That(termList.Count, Is.EqualTo(1));
            pdaTermCtrl = termList[0];
            var inputs = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentLineEdit>();
            CollectDescendants(pdaTermCtrl, inputs);
            Assert.That(inputs.Count, Is.EqualTo(1));
            pdaTermInput = inputs[0];
        });

        await TypeIntoLineEdit(pdaTermInput!, $"ssh {wsGuid}", submit: true);
        await TypeIntoLineEdit(pdaTermInput!, "screen open", submit: true);
        await RunTicks(18);

        Content.Client._Oxyd.Computers.ScreenSharing.RemoteScreenView? remoteView = null;
        ComputerDisplayWindow? stashedWsWin = null;
        OsUiControl? wsOsCtrl = null;
        Content.Client.UserInterface.Controls.ContentButton? remoteWsAppsBtn = null;
        await Client.WaitAssertion(() =>
        {
            var views = new System.Collections.Generic.List<Content.Client._Oxyd.Computers.ScreenSharing.RemoteScreenView>();
            CollectDescendants(pdaTermCtrl!, views);
            Assert.That(views.Count, Is.EqualTo(1));
            remoteView = views[0];

            var stash = UiMan.WindowRoot.Children.FirstOrDefault(c => c.Name == "RemoteOffscreenStash");
            Assert.That(stash, Is.Not.Null);
            stashedWsWin = stash!.Children.OfType<ComputerDisplayWindow>().FirstOrDefault(w => !w.Disposed);
            Assert.That(stashedWsWin, Is.Not.Null);

            var osList = new System.Collections.Generic.List<OsUiControl>();
            CollectDescendants(stashedWsWin!.ScreenInsetControl, osList);
            Assert.That(osList.Count, Is.EqualTo(1));
            wsOsCtrl = osList[0];

            var btns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(wsOsCtrl, btns);
            remoteWsAppsBtn = btns.FirstOrDefault(b => b.VisibleInTree && b.Text != null && b.Text.Contains("Apps", StringComparison.OrdinalIgnoreCase));
            Assert.That(remoteWsAppsBtn, Is.Not.Null);
        });

        await ClickThroughRemoteScreenView(remoteView!, stashedWsWin!.ScreenInsetControl, remoteWsAppsBtn!);
        await RunTicks(8);

        Content.Client.UserInterface.Controls.ContentButton? remoteTermRunBtn = null;
        await Client.WaitAssertion(() =>
        {
            remoteTermRunBtn = FindRowRunButton(wsOsCtrl!, "Terminal");
            Assert.That(remoteTermRunBtn, Is.Not.Null, "Clicking Apps inside RemoteScreenView must reveal Terminal Run ContentButton");
        });

        await ClickThroughRemoteScreenView(remoteView!, stashedWsWin.ScreenInsetControl, remoteTermRunBtn!);
        await RunTicks(12);

        Content.Client.UserInterface.Controls.ContentLineEdit? remoteTermInput = null;
        await Client.WaitAssertion(() =>
        {
            var termList = new System.Collections.Generic.List<TerminalUiControl>();
            CollectDescendants(stashedWsWin.ScreenInsetControl, termList);
            Assert.That(termList.Count, Is.EqualTo(1));
            var inputs = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentLineEdit>();
            CollectDescendants(termList[0], inputs);
            Assert.That(inputs.Count, Is.EqualTo(1));
            remoteTermInput = inputs[0];
            Assert.That(remoteTermInput.VisibleInTree, Is.True);
        });

        // Clicking remote Terminal input through RemoteScreenView must keep KeyboardFocused on remoteTermInput AFTER MouseUp.
        await ClickThroughRemoteScreenView(remoteView!, stashedWsWin.ScreenInsetControl, remoteTermInput!);

        await Client.WaitAssertion(() =>
        {
            Assert.That(UiMan.KeyboardFocused, Is.EqualTo(remoteTermInput),
                $"Expected KeyboardFocused to remain on remote Terminal ContentLineEdit after UIClick release, got {UiMan.KeyboardFocused?.GetType().Name ?? "null"}");
            Assert.That(remoteTermInput!.HasKeyboardFocus(), Is.True);
        });

        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            Assert.That(UiMan.KeyboardFocused, Is.EqualTo(remoteTermInput),
                "Remote Terminal ContentLineEdit lost focus across subsequent frames/ticks.");
        });

        // Now type and edit without re-clicking, exercising TextEntered, TextBackspace, and TextSubmit directly on UiMan.
        await Client.WaitPost(() =>
        {
            var uiType = UiMan.GetType();
            var mKeyDown = uiType.GetMethod("KeyBindDown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Robust.Shared.Input.BoundKeyEventArgs) }, null)!;
            var mKeyUp = uiType.GetMethod("KeyBindUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Robust.Shared.Input.BoundKeyEventArgs) }, null)!;
            var mText = uiType.GetMethod("TextEntered", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

            foreach (var ch in "write /notes/remote_term.txt hx")
                mText.Invoke(UiMan, new object[] { new Robust.Client.Input.TextEnteredEventArgs(ch.ToString()) });

            var sc = new ScreenCoordinates(remoteView!.GlobalPixelPosition + remoteView.PixelSize / 2, remoteView.Window?.Id ?? default);
            mKeyDown.Invoke(UiMan, new object[] { new Robust.Shared.Input.BoundKeyEventArgs(EngineKeyFunctions.TextBackspace, BoundKeyState.Down, sc, false, false) });
            mKeyUp.Invoke(UiMan, new object[] { new Robust.Shared.Input.BoundKeyEventArgs(EngineKeyFunctions.TextBackspace, BoundKeyState.Up, sc, false, false) });
            mText.Invoke(UiMan, new object[] { new Robust.Client.Input.TextEnteredEventArgs("i") });

            Assert.That(remoteTermInput!.Text, Is.EqualTo("write /notes/remote_term.txt hi"));
            Assert.That(UiMan.KeyboardFocused, Is.EqualTo(remoteTermInput));
        });

        await RunTicks(4);

        await Client.WaitPost(() =>
        {
            Assert.That(UiMan.KeyboardFocused, Is.EqualTo(remoteTermInput));
            var uiType = UiMan.GetType();
            var mKeyDown = uiType.GetMethod("KeyBindDown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Robust.Shared.Input.BoundKeyEventArgs) }, null)!;
            var mKeyUp = uiType.GetMethod("KeyBindUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Robust.Shared.Input.BoundKeyEventArgs) }, null)!;
            var sc = new ScreenCoordinates(remoteView!.GlobalPixelPosition + remoteView.PixelSize / 2, remoteView.Window?.Id ?? default);
            mKeyDown.Invoke(UiMan, new object[] { new Robust.Shared.Input.BoundKeyEventArgs(EngineKeyFunctions.TextSubmit, BoundKeyState.Down, sc, false, false) });
            mKeyUp.Invoke(UiMan, new object[] { new Robust.Shared.Input.BoundKeyEventArgs(EngineKeyFunctions.TextSubmit, BoundKeyState.Up, sc, false, false) });
        });

        await RunTicks(12);

        // Even after command submission and server TerminalUiState refresh, focus must remain in the remote command box.
        await Client.WaitAssertion(() =>
        {
            Assert.That(UiMan.KeyboardFocused, Is.EqualTo(remoteTermInput),
                "Remote Terminal ContentLineEdit lost focus after submitting command and receiving updated TerminalUiState.");
        });

        await Client.WaitPost(() =>
        {
            var uiType = UiMan.GetType();
            var mKeyDown = uiType.GetMethod("KeyBindDown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Robust.Shared.Input.BoundKeyEventArgs) }, null)!;
            var mKeyUp = uiType.GetMethod("KeyBindUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Robust.Shared.Input.BoundKeyEventArgs) }, null)!;
            var mText = uiType.GetMethod("TextEntered", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            foreach (var ch in "ls")
                mText.Invoke(UiMan, new object[] { new Robust.Client.Input.TextEnteredEventArgs(ch.ToString()) });
            var sc = new ScreenCoordinates(remoteView!.GlobalPixelPosition + remoteView.PixelSize / 2, remoteView.Window?.Id ?? default);
            mKeyDown.Invoke(UiMan, new object[] { new Robust.Shared.Input.BoundKeyEventArgs(EngineKeyFunctions.TextSubmit, BoundKeyState.Down, sc, false, false) });
            mKeyUp.Invoke(UiMan, new object[] { new Robust.Shared.Input.BoundKeyEventArgs(EngineKeyFunctions.TextSubmit, BoundKeyState.Up, sc, false, false) });
        });

        await RunTicks(12);

        await Server.WaitAssertion(() =>
        {
            Assert.That(fsSys.TryFindFileSystem(wsCompUid, out var driveUid, out var fs), Is.True);
            Assert.That(fsSys.TryGetFile<Content.Shared._Oxyd.Computers.FileSystem.TextVirtualFile>(driveUid!.Value, "/notes/remote_term.txt", out var noteFile, fs), Is.True);
            Assert.That(noteFile!.TextContent, Is.EqualTo("hi"));

            var wsComp = SEntMan.GetComponent<OxydComputerComponent>(wsCompUid);
            Assert.That(wsComp.ActiveProgram, Is.Not.Null);
            var termComp = SEntMan.GetComponent<Content.Shared._Oxyd.Computers.Programs.TerminalProgramComponent>(wsComp.ActiveProgram!.Value);
            Assert.That(termComp.History.Any(l => l.Contains("system.log")), Is.True,
                $"Expected `ls` executed via remote Terminal to list system.log, got:\n{string.Join("\n", termComp.History)}");
        });

        await Client.WaitPost(() => pdaWin!.Close());
        await RunTicks(8);
    }
#endif

    [Test]
    public async Task TestDeviceGuidOnlyOnEmbeddedComputerAndHiddenOnExamine()
    {
        var guidSys = Server.System<SharedDeviceGuidSystem>();
        var examineSys = Server.System<ExamineSystemShared>();
        var power = Server.System<PowerReceiverSystem>();

        var protos = new[]
        {
            "OxydWorkstationComputer",
            "DetectivePDA",
            "APCBasic",
            "AirAlarm",
            "StationNetworkRouter",
            "StationManifestServer"
        };

        var spawned = new List<(string Proto, EntityUid HostUid, EntityUid CompUid, string CompGuid)>();

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(TargetCoords);
            foreach (var proto in protos)
            {
                var hostUid = SEntMan.SpawnEntity(proto, coords);
                if (SEntMan.HasComponent<ApcPowerReceiverComponent>(hostUid))
                    power.SetNeedsPower(hostUid, false);

                var emb = SEntMan.GetComponent<EmbeddedComputerHostComponent>(hostUid);
                Assert.That(emb.ComputerEntity, Is.Not.Null, $"{proto} must have an embedded computer entity.");
                var compUid = emb.ComputerEntity!.Value;
                var compGuid = guidSys.GetGuid(compUid);
                spawned.Add((proto, hostUid, compUid, compGuid));
            }
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var playerUid = ToServer(Player);
            foreach (var (proto, hostUid, compUid, compGuid) in spawned)
            {
                Assert.That(SEntMan.HasComponent<DeviceGuidComponent>(hostUid), Is.False,
                    $"Host machine {proto} must NOT carry DeviceGuidComponent directly.");
                Assert.That(SEntMan.HasComponent<DeviceGuidComponent>(compUid), Is.True,
                    $"Embedded computer of {proto} must carry DeviceGuidComponent.");
                Assert.That(compGuid, Is.Not.Null.And.Length.EqualTo(8),
                    $"Embedded computer of {proto} must have a valid 8-char GUID.");
                Assert.That(guidSys.GetGuid(hostUid), Is.EqualTo(compGuid),
                    $"SharedDeviceGuidSystem.GetGuid({proto} host) must resolve to the embedded computer's GUID.");

                var hostExamine = examineSys.GetExamineText(hostUid, playerUid).ToMarkup();
                Assert.That(hostExamine, Does.Not.Contain(compGuid),
                    $"Examining {proto} host must NOT reveal Device GUID.");
                Assert.That(hostExamine, Does.Not.Contain("Device Network GUID"),
                    $"Examining {proto} host must NOT show Device Network GUID text.");

                var compExamine = examineSys.GetExamineText(compUid, playerUid).ToMarkup();
                Assert.That(compExamine, Does.Not.Contain(compGuid),
                    $"Examining embedded computer of {proto} must NOT reveal Device GUID.");
            }
        });

        // Verify the embedded computer's GUID IS shown in the device UI (Workstation + APC).
        var wsEntry = spawned.First(s => s.Proto == "OxydWorkstationComputer");
        Target = FromServer(wsEntry.HostUid);
        await Activate();
        await RunTicks(12);

        await Client.WaitAssertion(() =>
        {
            Assert.That(TryFindWindow(typeof(ComputerDisplayWindow), out var winBase), Is.True);
            var win = (ComputerDisplayWindow) winBase!;
            Assert.That(win.ScreenSurface.DeviceGuid, Is.EqualTo(wsEntry.CompGuid));
            var guidLabel = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Text != null && l.Text.Contains(wsEntry.CompGuid), win);
            Assert.That(guidLabel, Is.Not.Null, "Workstation UI must display the embedded computer's GUID.");
            win.Close();
        });
        await RunTicks(8);

        var apcEntry = spawned.First(s => s.Proto == "APCBasic");
        Target = FromServer(apcEntry.HostUid);
        await Activate();
        await RunTicks(12);

        await Client.WaitAssertion(() =>
        {
            Assert.That(TryFindWindow(typeof(ComputerDisplayWindow), out var winBase), Is.True);
            var win = (ComputerDisplayWindow) winBase!;
            Assert.That(win.ScreenSurface.DeviceGuid, Is.EqualTo(apcEntry.CompGuid));
            var guidLabel = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Text != null && l.Text.Contains(apcEntry.CompGuid), win);
            Assert.That(guidLabel, Is.Not.Null, "APC UI must display the embedded computer's GUID.");
            win.Close();
        });
        await RunTicks(8);
    }

    [Test]
    public async Task TestApcAndAirAlarmAlwaysOpenUiAndRequireIdSwipeUnlock()
    {
        var lockSys = Server.System<LockSystem>();
        var power = Server.System<PowerReceiverSystem>();

        // -----------------------------------------------------------------
        // 1) APC: Opens UI without access, locked by default, swipe ID to unlock/lock
        // -----------------------------------------------------------------
        await SpawnTarget("APCBasic");
        var apcUid = ToServer(Target)!.Value;
        EntityUid apcProgUid = default;
        bool initialBreaker = false;

        await Server.WaitAssertion(() =>
        {
            Assert.That(lockSys.IsLocked(apcUid), Is.True, "APC must start locked.");
            initialBreaker = SEntMan.GetComponent<ApcComponent>(apcUid).MainBreakerEnabled;
            var apcCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(apcUid).ComputerEntity!.Value;
            apcProgUid = SEntMan.GetComponent<OxydComputerComponent>(apcCompUid).ActiveProgram!.Value;
        });

        // Player has NO ID/access -> activating APC must STILL open the UI!
        await Activate();
        await RunTicks(12);

        await Client.WaitAssertion(() =>
        {
            Assert.That(TryFindWindow(typeof(ComputerDisplayWindow), out var apcWinBase), Is.True,
                "APC UI must open even when player has no access.");
            var apcWin = (ComputerDisplayWindow) apcWinBase!;
            var apcCtrls = new List<ApcControlUiControl>();
            CollectDescendants(apcWin, apcCtrls);
            Assert.That(apcCtrls.Count, Is.EqualTo(1));

            var lockedLabel = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Text != null && l.Text.Contains("LOCKED", StringComparison.OrdinalIgnoreCase) &&
                     !l.Text.Contains("UNLOCKED", StringComparison.OrdinalIgnoreCase), apcCtrls[0]);
            Assert.That(lockedLabel, Is.Not.Null, "Locked APC UI must clearly display LOCKED status.");

            var apcBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(apcCtrls[0], apcBtns);
            Assert.That(apcBtns.Count, Is.GreaterThan(0));
            Assert.That(apcBtns.All(b => b.Disabled), Is.True, "All APC control buttons must be disabled while locked.");
        });

        // Sending BUI action while locked must be blocked server-side!
        await SendBui(ProgramUiKey.Key, new ApcControlActionMessage(ApcControlAction.ToggleBreaker), FromServer(apcProgUid));
        await RunTicks(6);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<ApcComponent>(apcUid).MainBreakerEnabled, Is.EqualTo(initialBreaker),
                "Breaker must not toggle while APC is locked.");
        });

        // Swiping an unauthorized ID card (PassengerIDCard) must keep APC locked!
        await InteractUsing("PassengerIDCard");
        await RunTicks(8);
        await Server.WaitAssertion(() =>
        {
            Assert.That(lockSys.IsLocked(apcUid), Is.True, "Unauthorized Passenger ID swipe must NOT unlock APC.");
        });

        // Swiping an authorized ID card (EngineeringIDCard) must UNLOCK APC and live-update the open UI!
        await InteractUsing("EngineeringIDCard");
        await RunTicks(10);

        Content.Client.UserInterface.Controls.ContentButton? apcToggleBtn = null;
        await Server.WaitAssertion(() =>
        {
            Assert.That(lockSys.IsLocked(apcUid), Is.False, "Authorized Engineering ID swipe must unlock APC.");
        });
        await Client.WaitAssertion(() =>
        {
            Assert.That(TryFindWindow(typeof(ComputerDisplayWindow), out var apcWinBase), Is.True);
            var apcWin = (ComputerDisplayWindow) apcWinBase!;
            var apcCtrls = new List<ApcControlUiControl>();
            CollectDescendants(apcWin, apcCtrls);
            Assert.That(apcCtrls.Count, Is.EqualTo(1));

            var unlockedLabel = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Text != null && l.Text.Contains("UNLOCKED", StringComparison.OrdinalIgnoreCase), apcCtrls[0]);
            Assert.That(unlockedLabel, Is.Not.Null, "Unlocked APC UI must update to display UNLOCKED status.");

            var apcBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(apcCtrls[0], apcBtns);
            Assert.That(apcBtns.All(b => !b.Disabled), Is.True, "APC control buttons must be enabled once unlocked.");
            apcToggleBtn = apcBtns.FirstOrDefault(b => b.Text == "Toggle Breaker");
            Assert.That(apcToggleBtn, Is.Not.Null);
        });

        // Clicking Toggle Breaker now works!
        await ClickViaHitTest(apcToggleBtn!);
        await RunTicks(8);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<ApcComponent>(apcUid).MainBreakerEnabled, Is.EqualTo(!initialBreaker),
                "Unlocked APC breaker must toggle on button click.");
        });

        // Swiping EngineeringIDCard again re-locks APC and disables UI buttons again!
        await Interact();
        await RunTicks(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(lockSys.IsLocked(apcUid), Is.True, "Second authorized ID swipe must re-lock APC.");
        });
        await Client.WaitAssertion(() =>
        {
            Assert.That(TryFindWindow(typeof(ComputerDisplayWindow), out var apcWinBase), Is.True);
            var apcWin = (ComputerDisplayWindow) apcWinBase!;
            var apcCtrls = new List<ApcControlUiControl>();
            CollectDescendants(apcWin, apcCtrls);
            var apcBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(apcCtrls[0], apcBtns);
            Assert.That(apcBtns.All(b => b.Disabled), Is.True, "Re-locked APC buttons must be disabled again.");
            apcWin.Close();
        });
        await RunTicks(8);

        // -----------------------------------------------------------------
        // 2) AirAlarm: Opens UI without access, locked by default, swipe ID to unlock/lock
        // -----------------------------------------------------------------
        await SpawnTarget("AirAlarm");
        var alarmUid = ToServer(Target)!.Value;
        EntityUid alarmProgUid = default;
        AirAlarmMode initialMode = default;

        await Server.WaitPost(() =>
        {
            power.SetNeedsPower(alarmUid, false);
        });
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            Assert.That(lockSys.IsLocked(alarmUid), Is.True, "AirAlarm must start locked.");
            initialMode = SEntMan.GetComponent<AirAlarmComponent>(alarmUid).CurrentMode;
            var alarmCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(alarmUid).ComputerEntity!.Value;
            alarmProgUid = SEntMan.GetComponent<OxydComputerComponent>(alarmCompUid).ActiveProgram!.Value;
        });

        // Drop EngineeringIDCard so player has NO access -> activating AirAlarm must STILL open UI!
        await Drop();
        await Activate();
        await RunTicks(12);

        await Client.WaitAssertion(() =>
        {
            Assert.That(TryFindWindow(typeof(ComputerDisplayWindow), out var alarmWinBase), Is.True,
                "AirAlarm UI must open even when player has no access.");
            var alarmWin = (ComputerDisplayWindow) alarmWinBase!;
            var atmosCtrls = new List<AtmosControlUiControl>();
            CollectDescendants(alarmWin, atmosCtrls);
            Assert.That(atmosCtrls.Count, Is.EqualTo(1));

            var lockedLabel = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Text != null && l.Text.Contains("LOCKED", StringComparison.OrdinalIgnoreCase) &&
                     !l.Text.Contains("UNLOCKED", StringComparison.OrdinalIgnoreCase), atmosCtrls[0]);
            Assert.That(lockedLabel, Is.Not.Null, "Locked AirAlarm UI must clearly display LOCKED status.");

            var alarmBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(atmosCtrls[0], alarmBtns);
            Assert.That(alarmBtns.Count, Is.GreaterThan(0));
            Assert.That(alarmBtns.All(b => b.Disabled), Is.True, "All AirAlarm control buttons must be disabled while locked.");
        });

        // BUI mode change while locked must be rejected server-side!
        await SendBui(ProgramUiKey.Key, new AtmosControlActionMessage(AtmosControlAction.SetModePanic), FromServer(alarmProgUid));
        await RunTicks(6);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<AirAlarmComponent>(alarmUid).CurrentMode, Is.EqualTo(initialMode),
                "AirAlarm mode must not change while locked.");
        });

        // Unauthorized Passenger ID swipe must NOT unlock AirAlarm
        await InteractUsing("PassengerIDCard");
        await RunTicks(8);
        await Server.WaitAssertion(() =>
        {
            Assert.That(lockSys.IsLocked(alarmUid), Is.True, "Unauthorized Passenger ID swipe must NOT unlock AirAlarm.");
        });

        // Authorized AtmosIDCard swipe must UNLOCK AirAlarm and live-update open UI!
        await InteractUsing("AtmosIDCard");
        await RunTicks(10);

        Content.Client.UserInterface.Controls.ContentButton? panicBtn = null;
        await Server.WaitAssertion(() =>
        {
            Assert.That(lockSys.IsLocked(alarmUid), Is.False, "Authorized Atmos ID swipe must unlock AirAlarm.");
        });
        await Client.WaitAssertion(() =>
        {
            Assert.That(TryFindWindow(typeof(ComputerDisplayWindow), out var alarmWinBase), Is.True);
            var alarmWin = (ComputerDisplayWindow) alarmWinBase!;
            var atmosCtrls = new List<AtmosControlUiControl>();
            CollectDescendants(alarmWin, atmosCtrls);
            Assert.That(atmosCtrls.Count, Is.EqualTo(1));

            var unlockedLabel = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Text != null && l.Text.Contains("UNLOCKED", StringComparison.OrdinalIgnoreCase), atmosCtrls[0]);
            Assert.That(unlockedLabel, Is.Not.Null, "Unlocked AirAlarm UI must update to display UNLOCKED status.");

            var alarmBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(atmosCtrls[0], alarmBtns);
            Assert.That(alarmBtns.All(b => !b.Disabled), Is.True, "AirAlarm control buttons must be enabled once unlocked.");
            panicBtn = alarmBtns.FirstOrDefault(b => b.Text == "Panic");
            Assert.That(panicBtn, Is.Not.Null);
        });

        // Clicking Panic button now changes AirAlarm mode!
        await ClickViaHitTest(panicBtn!);
        await RunTicks(8);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<AirAlarmComponent>(alarmUid).CurrentMode, Is.EqualTo(AirAlarmMode.Panic),
                "Unlocked AirAlarm must switch to Panic mode on button click.");
        });

        // Swiping AtmosIDCard again re-locks AirAlarm!
        await Interact();
        await RunTicks(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(lockSys.IsLocked(alarmUid), Is.True, "Second authorized ID swipe must re-lock AirAlarm.");
        });
        await Client.WaitAssertion(() =>
        {
            Assert.That(TryFindWindow(typeof(ComputerDisplayWindow), out var alarmWinBase), Is.True);
            var alarmWin = (ComputerDisplayWindow) alarmWinBase!;
            var atmosCtrls = new List<AtmosControlUiControl>();
            CollectDescendants(alarmWin, atmosCtrls);
            var alarmBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(atmosCtrls[0], alarmBtns);
            Assert.That(alarmBtns.All(b => b.Disabled), Is.True, "Re-locked AirAlarm buttons must be disabled again.");
            alarmWin.Close();
        });
        await RunTicks(8);
    }
}
