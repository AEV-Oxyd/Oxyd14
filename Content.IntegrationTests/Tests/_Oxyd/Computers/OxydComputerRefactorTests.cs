#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._Oxyd.Computers.Programs;
using Content.Client._Oxyd.Computers.UI;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._Oxyd.Computers;
using Content.Server._Oxyd.Computers.Cameras;
using Content.Server._Oxyd.Computers.Network;
using Content.Server._Oxyd.Computers.Programs;
using Content.Server._Oxyd.Computers.ScreenSharing;
using Content.Server._Oxyd.Computers.Tools;
using Content.Server.Atmos.Monitor.Components;
using Content.Server.Materials;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.Cameras;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared._Oxyd.Computers.ScreenSharing;
using Content.Shared._Oxyd.Computers.Tools;
using Content.Shared.Access.Components;
using Content.Shared.Interaction;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Atmos.Monitor.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking.Components;
using Content.Shared.Examine;
using Content.Shared.Lathe;
using Content.Shared.Lock;
using Content.Shared.Materials;
using Content.Shared.PDA;
using Content.Shared.Research.Prototypes;
using Content.Shared.Security;
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
            Assert.That(hit == btn || HasAncestor(hit!, btn), Is.True,
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

    private static Content.Client.UserInterface.Controls.ContentButton? FindRowRunButton(Control osCtrl, string appTitle)
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
                .FirstOrDefault(b => b.VisibleInTree && b.Text != null &&
                    (b.Text.Contains("Run", StringComparison.OrdinalIgnoreCase) ||
                     b.Text.Contains("Launch", StringComparison.OrdinalIgnoreCase)));
            if (btn != null)
                return btn;
        }

        // Also support icon-tile launcher cards (Workstation DesktopAppsGrid AppTile_*)
        var panels = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentPanelContainer>();
        CollectDescendants(osCtrl, panels);
        foreach (var panel in panels)
        {
            if (!panel.VisibleInTree || panel.Name == null || !panel.Name.StartsWith("AppTile_", StringComparison.Ordinal))
                continue;

            var labels = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentLabel>();
            CollectDescendants(panel, labels);
            if (!labels.Any(l => l.Text != null && l.Text.Contains(appTitle, StringComparison.OrdinalIgnoreCase)))
                continue;

            var btns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(panel, btns);
            var btn = btns.FirstOrDefault(b => b.VisibleInTree && b.Text != null &&
                (b.Text.Contains("Launch", StringComparison.OrdinalIgnoreCase) ||
                 b.Text.Contains("Run", StringComparison.OrdinalIgnoreCase)));
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
        PdaOsUiControl? osCtrl = null;
        Content.Client.UserInterface.Controls.ContentButton? homeBtn = null;
        await Client.WaitAssertion(() =>
        {
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen).ToList();
            Assert.That(openWins.Count, Is.EqualTo(1));
            win = openWins[0];
            var osList = new System.Collections.Generic.List<PdaOsUiControl>();
            CollectDescendants(win, osList);
            Assert.That(osList.Count, Is.EqualTo(1));
            osCtrl = osList[0];
            var btns = new System.Collections.Generic.List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(osCtrl, btns);
            homeBtn = btns.FirstOrDefault(b => b.Text != null && b.Text.Equals("Home", StringComparison.OrdinalIgnoreCase));
            Assert.That(homeBtn, Is.Not.Null);
        });

        // Home and Apps are merged on PDA OS, so Notekeeper is immediately launchable from Home
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

        // Switch back to merged Home tab, launch Terminal, verify ONLY 1 TerminalUiControl, type `ls` and submit
        await ClickViaHitTest(homeBtn!);
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

        // Open held PDA and launch Terminal directly from merged Home view
        await UseInHand();
        await RunTicks(12);

        ComputerDisplayWindow? pdaWin = null;
        Content.Client.UserInterface.Controls.ContentButton? termRunBtn = null;
        await Client.WaitAssertion(() =>
        {
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen).ToList();
            Assert.That(openWins.Count, Is.EqualTo(1));
            pdaWin = openWins[0];
            var osList = new System.Collections.Generic.List<PdaOsUiControl>();
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
            remoteWsAppsBtn = btns.FirstOrDefault(b => b.VisibleInTree && b.Text != null &&
                (b.Text.Contains("Home", StringComparison.OrdinalIgnoreCase) ||
                 b.Text.Contains("Apps", StringComparison.OrdinalIgnoreCase)));
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

        // Open held PDA -> merged Home -> Terminal
        await UseInHand();
        await RunTicks(12);

        ComputerDisplayWindow? pdaWin = null;
        Content.Client.UserInterface.Controls.ContentButton? termRunBtn = null;
        await Client.WaitAssertion(() =>
        {
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen).ToList();
            Assert.That(openWins.Count, Is.EqualTo(2));
            pdaWin = openWins.First(w => w != wsWin);
            pdaWin.MoveToFront();
            var osList = new System.Collections.Generic.List<PdaOsUiControl>();
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
            remoteWsAppsBtn = btns.First(b => b.VisibleInTree && b.Text != null &&
                (b.Text.Contains("Home", StringComparison.OrdinalIgnoreCase) ||
                 b.Text.Contains("Apps", StringComparison.OrdinalIgnoreCase)));
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
        Content.Client.UserInterface.Controls.ContentButton? termRunBtn = null;
        await Client.WaitAssertion(() =>
        {
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen).ToList();
            Assert.That(openWins.Count, Is.EqualTo(2));
            pdaWin = openWins.First(w => w != wsWin);
            pdaWin.MoveToFront();
            var osList = new System.Collections.Generic.List<PdaOsUiControl>();
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
            wsAppsBtn = btns.First(b => b.VisibleInTree && b.Text != null &&
                (b.Text.Contains("Home", StringComparison.OrdinalIgnoreCase) ||
                 b.Text.Contains("Apps", StringComparison.OrdinalIgnoreCase)));
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
        Content.Client.UserInterface.Controls.ContentButton? termRunBtn = null;
        await Client.WaitAssertion(() =>
        {
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen).ToList();
            Assert.That(openWins.Count, Is.EqualTo(1));
            pdaWin = openWins[0];
            var osList = new System.Collections.Generic.List<PdaOsUiControl>();
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
        Content.Client.UserInterface.Controls.ContentButton? termRunBtn = null;
        await Client.WaitAssertion(() =>
        {
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen).ToList();
            Assert.That(openWins.Count, Is.EqualTo(1));
            pdaWin = openWins[0];
            var osList = new System.Collections.Generic.List<PdaOsUiControl>();
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
            remoteWsAppsBtn = btns.FirstOrDefault(b => b.VisibleInTree && b.Text != null &&
                (b.Text.Contains("Home", StringComparison.OrdinalIgnoreCase) ||
                 b.Text.Contains("Apps", StringComparison.OrdinalIgnoreCase)));
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
            var embList = new List<EmbeddedOsUiControl>();
            CollectDescendants(apcWin, embList);
            Assert.That(embList.Count, Is.EqualTo(1));

            // Simplified APC layout: strictly 2 tabs (Controller & Terminal) and NO hardware stats on Controller view
            var allEmbBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(embList[0], allEmbBtns);
            var tabBar = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentWrapContainer>(
                w => w.Name == "EmbeddedTabBar", embList[0]);
            Assert.That(tabBar, Is.Not.Null);
            var visibleTabBtns = tabBar!.Children.OfType<Content.Client.UserInterface.Controls.ContentButton>()
                .Where(b => b.VisibleInTree).Select(b => b.Text).ToList();
            Assert.That(visibleTabBtns, Is.EquivalentTo(new[] { "Controller", "Terminal" }),
                "Simplified APC UI must expose strictly 2 tabs: Controller and Terminal.");

            var allVisibleLabels = new List<Content.Client.UserInterface.Controls.ContentLabel>();
            CollectDescendants(embList[0], allVisibleLabels);
            var visibleTexts = allVisibleLabels.Where(l => l.VisibleInTree && !string.IsNullOrEmpty(l.Text)).Select(l => l.Text!).ToList();
            Assert.That(visibleTexts.Any(t => t.Contains("CPU:", StringComparison.Ordinal) || t.Contains("RAM:", StringComparison.Ordinal)),
                Is.False, "APC Controller tab must NOT display computer hardware stats.");

            // Hardware lock must render as a full-UI transparent overlay with a lock icon
            var lockOverlay = GetControlFromChildren<HardwareLockOverlayControl>(_ => true, embList[0]);
            Assert.That(lockOverlay, Is.Not.Null, "Locked APC must have HardwareLockOverlayControl.");
            Assert.That(lockOverlay!.VisibleInTree, Is.True, "Hardware lock overlay must be visible while APC is locked.");
            var lockIcon = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentTextureRect>(
                t => t.Name == "HardwareLockIcon", lockOverlay);
            Assert.That(lockIcon, Is.Not.Null, "Hardware lock overlay must include the locked keyhole icon.");

            var apcCtrls = new List<ApcControlUiControl>();
            CollectDescendants(apcWin, apcCtrls);
            Assert.That(apcCtrls.Count, Is.EqualTo(1));

            // No redundant [LOCKED]/[UNLOCKED] text or Area Power Controller header inside ApcControlUiControl
            var apcLabels = new List<Content.Client.UserInterface.Controls.ContentLabel>();
            CollectDescendants(apcCtrls[0], apcLabels);
            Assert.That(apcLabels.Any(l => l.VisibleInTree && l.Text != null &&
                (l.Text.Contains("[LOCKED]", StringComparison.OrdinalIgnoreCase) ||
                 l.Text.Contains("[UNLOCKED]", StringComparison.OrdinalIgnoreCase) ||
                 l.Text.Contains("AREA POWER CONTROLLER", StringComparison.OrdinalIgnoreCase))),
                Is.False, "APC controller UI must not duplicate outer box title or [LOCKED] text.");

            var apcBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(apcCtrls[0], apcBtns);
            Assert.That(apcBtns.Count, Is.GreaterThan(0));
            Assert.That(apcBtns.Any(b => b.Text != null &&
                (b.Text.Equals("Refresh", StringComparison.OrdinalIgnoreCase) ||
                 b.Text.Contains("Save Log", StringComparison.OrdinalIgnoreCase))),
                Is.False, "APC controller UI must not show Refresh or Save Log buttons.");
            Assert.That(apcBtns.All(b => b.Disabled), Is.True, "All APC control buttons must be disabled while locked.");

            // Clicking over an APC button while locked must hit the full-UI lock overlay
            var btnCenter = apcBtns[0].GlobalPixelPosition + apcBtns[0].PixelSize / 2;
            var hit = UiMan.MouseGetControl(new ScreenCoordinates(btnCenter, apcWin.Window?.Id ?? default));
            Assert.That(hit == lockOverlay || (hit != null && HasAncestor(hit, lockOverlay)), Is.True,
                "Hardware lock overlay must cover and intercept clicks over the whole APC UI while locked.");

            AssertAllVisibleChildrenWithinViewport(apcWin, embList[0], "APC Locked UI");
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
        Content.Client.UserInterface.Controls.ContentButton? apcTerminalTabBtn = null;
        Content.Client.UserInterface.Controls.ContentButton? apcControllerTabBtn = null;
        await Server.WaitAssertion(() =>
        {
            Assert.That(lockSys.IsLocked(apcUid), Is.False, "Authorized Engineering ID swipe must unlock APC.");
        });
        await Client.WaitAssertion(() =>
        {
            Assert.That(TryFindWindow(typeof(ComputerDisplayWindow), out var apcWinBase), Is.True);
            var apcWin = (ComputerDisplayWindow) apcWinBase!;
            var lockOverlay = GetControlFromChildren<HardwareLockOverlayControl>(_ => true, apcWin);
            Assert.That(lockOverlay, Is.Not.Null);
            Assert.That(lockOverlay!.VisibleInTree, Is.False, "Hardware lock overlay must hide once APC is unlocked.");

            apcControllerTabBtn = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentButton>(
                b => b.Name == "ApcTabControllerButton", apcWin);
            apcTerminalTabBtn = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentButton>(
                b => b.Name == "ApcTabTerminalButton", apcWin);
            Assert.That(apcControllerTabBtn, Is.Not.Null);
            Assert.That(apcTerminalTabBtn, Is.Not.Null);

            var apcCtrls = new List<ApcControlUiControl>();
            CollectDescendants(apcWin, apcCtrls);
            Assert.That(apcCtrls.Count, Is.EqualTo(1));

            var apcBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(apcCtrls[0], apcBtns);
            Assert.That(apcBtns.Count, Is.EqualTo(1), "APC menu must expose strictly a single Toggle Breaker button.");
            Assert.That(apcBtns.All(b => !b.Disabled), Is.True, "APC control buttons must be enabled once unlocked.");
            apcToggleBtn = apcBtns.FirstOrDefault(b => b.Text == "Toggle Breaker");
            Assert.That(apcToggleBtn, Is.Not.Null);
        });

        // Clicking Toggle Breaker now works and live-updates the open UI without needing Refresh!
        await ClickViaHitTest(apcToggleBtn!);
        await RunTicks(8);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<ApcComponent>(apcUid).MainBreakerEnabled, Is.EqualTo(!initialBreaker),
                "Unlocked APC breaker must toggle on button click.");
        });
        await Client.WaitAssertion(() =>
        {
            Assert.That(TryFindWindow(typeof(ComputerDisplayWindow), out var apcWinBase), Is.True);
            var apcWin = (ComputerDisplayWindow) apcWinBase!;
            var apcCtrls = new List<ApcControlUiControl>();
            CollectDescendants(apcWin, apcCtrls);
            var expectedBreakerText = !initialBreaker ? "Power: ON" : "Power: OFF";
            var telemetryLabel = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.VisibleInTree && l.Text != null && l.Text.Contains(expectedBreakerText, StringComparison.Ordinal),
                apcCtrls[0]);
            Assert.That(telemetryLabel, Is.Not.Null, "Open APC UI must automatically update Power: ON/OFF state without Refresh.");
            Assert.That(telemetryLabel!.Text, Does.Not.Contain("(POWER ON)").And.Not.Contain("(OFF)"));
        });

        // Clicking Terminal tab launches/focuses Terminal inside APC, and Controller switches back!
        await ClickViaHitTest(apcTerminalTabBtn!);
        await RunTicks(10);
        await Client.WaitAssertion(() =>
        {
            Assert.That(TryFindWindow(typeof(ComputerDisplayWindow), out var apcWinBase), Is.True);
            var apcWin = (ComputerDisplayWindow) apcWinBase!;
            var termList = new List<TerminalUiControl>();
            CollectDescendants(apcWin, termList);
            Assert.That(termList.Count, Is.EqualTo(1), "Clicking APC Terminal tab must open TerminalUiControl.");
            Assert.That(termList[0].VisibleInTree, Is.True);
        });
        await ClickViaHitTest(apcControllerTabBtn!);
        await RunTicks(8);
        await Client.WaitAssertion(() =>
        {
            Assert.That(TryFindWindow(typeof(ComputerDisplayWindow), out var apcWinBase), Is.True);
            var apcWin = (ComputerDisplayWindow) apcWinBase!;
            var apcCtrls = new List<ApcControlUiControl>();
            CollectDescendants(apcWin, apcCtrls);
            Assert.That(apcCtrls.Count, Is.EqualTo(1));
            Assert.That(apcCtrls[0].VisibleInTree, Is.True, "Clicking APC Controller tab must return to ApcControlUiControl.");
        });

        // Swiping EngineeringIDCard again re-locks APC, restores lock overlay, and disables UI buttons again!
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
            var lockOverlay = GetControlFromChildren<HardwareLockOverlayControl>(_ => true, apcWin);
            Assert.That(lockOverlay, Is.Not.Null);
            Assert.That(lockOverlay!.VisibleInTree, Is.True, "Re-locking APC must show the hardware lock overlay again.");
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
            var lockOverlay = GetControlFromChildren<HardwareLockOverlayControl>(_ => true, alarmWin);
            Assert.That(lockOverlay, Is.Not.Null, "Locked AirAlarm must have HardwareLockOverlayControl.");
            Assert.That(lockOverlay!.VisibleInTree, Is.True, "Hardware lock overlay must be visible while AirAlarm is locked.");

            // Strictly 2 tabs: Controller & Terminal (no Programs (.nte), no DeviceLinks, no hardware stats bar)
            var alarmTabBar = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentWrapContainer>(
                c => c.Name == "EmbeddedTabBar", alarmWin);
            Assert.That(alarmTabBar, Is.Not.Null);
            var alarmTabBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(alarmTabBar!, alarmTabBtns);
            alarmTabBtns = alarmTabBtns.Where(b => b.Visible).ToList();
            Assert.That(alarmTabBtns.Select(b => b.Text).ToList(), Is.EqualTo(new[] { "Controller", "Terminal" }),
                "AirAlarm embedded UI must expose strictly Controller and Terminal tabs.");
            var alarmOsLabels = new List<Content.Client.UserInterface.Controls.ContentLabel>();
            CollectDescendants(alarmWin, alarmOsLabels);
            Assert.That(alarmOsLabels.Any(l => l.VisibleInTree && (l.Text ?? string.Empty).Contains("CPU:")), Is.False,
                "Simplified AirAlarm UI must hide the embedded OS hardware stats bar.");

            var atmosCtrls = new List<AtmosControlUiControl>();
            CollectDescendants(alarmWin, atmosCtrls);
            Assert.That(atmosCtrls.Count, Is.EqualTo(1));

            var atmosLabels = new List<Content.Client.UserInterface.Controls.ContentLabel>();
            CollectDescendants(atmosCtrls[0], atmosLabels);
            Assert.That(atmosLabels.Any(l => l.VisibleInTree && l.Text != null &&
                (l.Text.Contains("[LOCKED]", StringComparison.OrdinalIgnoreCase) ||
                 l.Text.Contains("[UNLOCKED]", StringComparison.OrdinalIgnoreCase) ||
                 l.Text.Contains("AIR ALARM CONTROLLER", StringComparison.OrdinalIgnoreCase))),
                Is.False, "AirAlarm controller UI must not duplicate outer box title or [LOCKED] text.");
            Assert.That(atmosLabels.Any(l => l.VisibleInTree && l.Text != null &&
                (l.Text.Contains("Devices:", StringComparison.OrdinalIgnoreCase) ||
                 l.Text.Contains("(Auto:", StringComparison.OrdinalIgnoreCase) ||
                 l.Text.Contains("0.0 mol", StringComparison.OrdinalIgnoreCase) ||
                 l.Text.Contains("-273.", StringComparison.OrdinalIgnoreCase))),
                Is.False, "AirAlarm status header must omit redundant device counts, Auto duplicate, 0.0 mol dumps, and -273 C placeholder.");

            var alarmBadge = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Name == "AtmosAlarmBadgeLabel", atmosCtrls[0]);
            var modeStatus = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Name == "AtmosModeStatusLabel", atmosCtrls[0]);
            var pressureStatus = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Name == "AtmosPressureStatusLabel", atmosCtrls[0]);
            var tempStatus = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Name == "AtmosTempStatusLabel", atmosCtrls[0]);
            Assert.That(alarmBadge?.VisibleInTree, Is.True);
            Assert.That(modeStatus?.VisibleInTree, Is.True);
            Assert.That(pressureStatus?.VisibleInTree, Is.True);
            Assert.That(tempStatus?.VisibleInTree, Is.True);

            var alarmBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(atmosCtrls[0], alarmBtns);
            Assert.That(alarmBtns.Count, Is.GreaterThan(0));
            Assert.That(alarmBtns.Any(b => b.Text != null &&
                (b.Text.Equals("Refresh", StringComparison.OrdinalIgnoreCase) ||
                 b.Text.Contains("Save Log", StringComparison.OrdinalIgnoreCase))),
                Is.False, "AirAlarm controller UI must not show Refresh or Save Log buttons.");
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
            var lockOverlay = GetControlFromChildren<HardwareLockOverlayControl>(_ => true, alarmWin);
            Assert.That(lockOverlay, Is.Not.Null);
            Assert.That(lockOverlay!.VisibleInTree, Is.False, "Hardware lock overlay must hide once AirAlarm is unlocked.");

            var atmosCtrls = new List<AtmosControlUiControl>();
            CollectDescendants(alarmWin, atmosCtrls);
            Assert.That(atmosCtrls.Count, Is.EqualTo(1));

            var alarmBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(atmosCtrls[0], alarmBtns);
            Assert.That(alarmBtns.All(b => !b.Disabled), Is.True, "AirAlarm control buttons must be enabled once unlocked.");
            panicBtn = alarmBtns.FirstOrDefault(b => b.Text == "Panic");
            Assert.That(panicBtn, Is.Not.Null);
        });

        // Clicking Panic button now changes AirAlarm mode and automatically updates open UI!
        await ClickViaHitTest(panicBtn!);
        await RunTicks(8);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<AirAlarmComponent>(alarmUid).CurrentMode, Is.EqualTo(AirAlarmMode.Panic),
                "Unlocked AirAlarm must switch to Panic mode on button click.");
        });
        await Client.WaitAssertion(() =>
        {
            Assert.That(TryFindWindow(typeof(ComputerDisplayWindow), out var alarmWinBase), Is.True);
            var alarmWin = (ComputerDisplayWindow) alarmWinBase!;
            var atmosCtrls = new List<AtmosControlUiControl>();
            CollectDescendants(alarmWin, atmosCtrls);
            var modeLabel = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.VisibleInTree && l.Text != null && l.Text.Contains("Mode: Panic", StringComparison.Ordinal),
                atmosCtrls[0]);
            Assert.That(modeLabel, Is.Not.Null, "Open AirAlarm UI must automatically update mode without Refresh.");
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

    [Test]
    public async Task TestRemoteIdAuthCommandAndAccessEncoderApp()
    {
        var compSys = Server.System<OxydComputerSystem>();
        var fsSys = Server.System<SharedFileSystemSystem>();
        var routerSys = Server.System<NetworkRouterSystem>();
        var lockSys = Server.System<LockSystem>();
        var power = Server.System<PowerReceiverSystem>();
        var slots = Server.System<ItemSlotsSystem>();

        var pdaNet = await PlaceInHands("DetectivePDA");
        var pdaUid = ToServer(pdaNet);

        EntityUid apcUid = default;
        EntityUid apcCompUid = default;
        EntityUid alarmUid = default;
        EntityUid alarmCompUid = default;
        EntityUid wsUid = default;
        EntityUid wsCompUid = default;
        EntityUid pdaCompUid = default;
        EntityUid pdaTermUid = default;
        EntityUid engCardUid = default;
        string apcGuid = string.Empty;
        string alarmGuid = string.Empty;
        bool initialBreaker = false;

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(TargetCoords);
            var routerHost = SEntMan.SpawnEntity("StationNetworkRouter", coords);
            power.SetNeedsPower(routerHost, false);

            apcUid = SEntMan.SpawnEntity("APCBasic", coords);
            apcCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(apcUid).ComputerEntity!.Value;
            initialBreaker = SEntMan.GetComponent<ApcComponent>(apcUid).MainBreakerEnabled;

            alarmUid = SEntMan.SpawnEntity("AirAlarm", coords);
            power.SetNeedsPower(alarmUid, false);
            alarmCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(alarmUid).ComputerEntity!.Value;

            wsUid = SEntMan.SpawnEntity("OxydWorkstationComputer", coords);
            power.SetNeedsPower(wsUid, false);
            wsCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(wsUid).ComputerEntity!.Value;

            pdaCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(pdaUid).ComputerEntity!.Value;

            engCardUid = SEntMan.SpawnEntity("EngineeringIDCard", coords);
        });
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            apcGuid = routerSys.EnsureDeviceGuid(apcCompUid);
            alarmGuid = routerSys.EnsureDeviceGuid(alarmCompUid);
            Assert.That(lockSys.IsLocked(apcUid), Is.True);
            Assert.That(lockSys.IsLocked(alarmUid), Is.True);

            Assert.That(compSys.TryLaunchExecutable(pdaCompUid, "/bin/terminal.nte", out var tUid, out _), Is.True);
            pdaTermUid = tUid!.Value;
        });

        // Open PDA UI so live RemoteScreenView updates can be observed too.
        await UseInHand();
        await RunTicks(10);

        // 1) SSH into locked APC + open live screen; DetectiveIDCard (inserted by default) lacks Engineering -> DENIED.
        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage($"ssh {apcGuid}"), FromServer(pdaTermUid));
        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage("screen open"), FromServer(pdaTermUid));
        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage("auth"), FromServer(pdaTermUid));
        await RunTicks(8);

        await Server.WaitAssertion(() =>
        {
            Assert.That(lockSys.IsLocked(apcUid), Is.True, "Unauthorized Detective ID in PDA must not unlock Engineering APC.");
            var term = SEntMan.GetComponent<TerminalProgramComponent>(pdaTermUid);
            Assert.That(term.History.Any(l => l.Contains("ACCESS DENIED", StringComparison.OrdinalIgnoreCase)), Is.True);
        });

        // 2) Replace PDA's ID with EngineeringIDCard -> `auth` in SSH unlocks remote APC automatically!
        await Server.WaitPost(() =>
        {
            slots.TryEject(pdaUid, PdaComponent.PdaIdSlotId, null, out _);
            Assert.That(slots.TryInsert(pdaUid, PdaComponent.PdaIdSlotId, engCardUid, null), Is.True);
        });
        await RunTicks(4);

        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage("auth"), FromServer(pdaTermUid));
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(lockSys.IsLocked(apcUid), Is.False, "Authorized Engineering ID inserted in PDA must unlock APC via 'auth'.");
        });

        // Toggle breaker over SSH now that APC is unlocked!
        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage("apc toggle_breaker"), FromServer(pdaTermUid));
        await RunTicks(6);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<ApcComponent>(apcUid).MainBreakerEnabled, Is.EqualTo(!initialBreaker));
        });

        // Re-lock over SSH (`auth lock`) and exit SSH, then verify local `auth <guid>` also works with inserted ID.
        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage("auth lock"), FromServer(pdaTermUid));
        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage("exit"), FromServer(pdaTermUid));
        await RunTicks(6);
        await Server.WaitAssertion(() =>
        {
            Assert.That(lockSys.IsLocked(apcUid), Is.True);
        });

        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage($"auth {apcGuid} unlock"), FromServer(pdaTermUid));
        await RunTicks(6);
        await Server.WaitAssertion(() =>
        {
            Assert.That(lockSys.IsLocked(apcUid), Is.False, "Local shell 'auth <guid> unlock' with inserted PDA ID must unlock APC.");
        });
        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage($"auth {apcGuid} lock"), FromServer(pdaTermUid));
        await RunTicks(6);
        await Server.WaitAssertion(() =>
        {
            Assert.That(lockSys.IsLocked(apcUid), Is.True);
        });

        // 3) Launch ID Access Encoder on PDA -> build /keys/eng_key.acc from inserted EngineeringIDCard -> persists in FS!
        EntityUid pdaEncUid = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(compSys.TryLaunchExecutable(pdaCompUid, "/bin/access_encoder.nte", out var eUid, out var err), Is.True, err);
            pdaEncUid = eUid!.Value;
        });
        await RunTicks(8);

        await Client.WaitAssertion(() =>
        {
            Assert.That(TryGetBui(ProgramUiKey.Key, out var encBui, FromServer(pdaEncUid)), Is.True);
            var encCtrl = ((ProgramBoundUserInterface) encBui!).Control;
            Assert.That(encCtrl, Is.TypeOf<AccessEncoderUiControl>());
            var srcLabel = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Text != null && l.Text.Contains("Engineering"), encCtrl!);
            Assert.That(srcLabel, Is.Not.Null, "ID Access Encoder UI must display Engineering access from inserted PDA ID.");
        });

        await SendBui(ProgramUiKey.Key, new AccessEncoderActionMessage(AccessEncoderAction.BuildSaveFile, "eng_key"), FromServer(pdaEncUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            Assert.That(fsSys.TryFindFileSystem(pdaCompUid, out var driveUid, out var fs), Is.True);
            Assert.That(fsSys.TryGetFile<AccessCodesVirtualFile>(driveUid!.Value, "/keys/eng_key.acc", out var accFile, fs), Is.True,
                "ID Access Encoder must save .acc file into /keys/ on the filesystem.");
            Assert.That(accFile!.AccessTags.Any(t => t.Id == "Engineering"), Is.True);

            // Reboot PDA and verify the saved .acc file persists in filesystem!
            compSys.RebootComputer(pdaCompUid);
            Assert.That(fsSys.TryFindFileSystem(pdaCompUid, out var driveAfter, out var fsAfter), Is.True);
            Assert.That(fsSys.TryGetFile<AccessCodesVirtualFile>(driveAfter!.Value, "/keys/eng_key.acc", out _, fsAfter), Is.True,
                "Saved .acc file must persist in the filesystem across computer reboots.");
        });

        // 4) Eject ID from PDA (NO ID inserted!) -> `auth <apcGuid>` without file fails; `auth <apcGuid> /keys/eng_key.acc` SUCCEEDS!
        await Server.WaitPost(() =>
        {
            slots.TryEject(pdaUid, PdaComponent.PdaIdSlotId, null, out _);
            Assert.That(compSys.TryGetInsertedIdCard(pdaCompUid, out _), Is.False);
            Assert.That(compSys.TryLaunchExecutable(pdaCompUid, "/bin/terminal.nte", out var tUid2, out _), Is.True);
            pdaTermUid = tUid2!.Value;
        });
        await RunTicks(6);

        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage($"auth {apcGuid}"), FromServer(pdaTermUid));
        await RunTicks(6);
        await Server.WaitAssertion(() =>
        {
            Assert.That(lockSys.IsLocked(apcUid), Is.True, "Without inserted ID or .acc argument, auth must not unlock APC.");
            var term = SEntMan.GetComponent<TerminalProgramComponent>(pdaTermUid);
            Assert.That(term.History.Any(l => l.Contains("No ID card inserted", StringComparison.OrdinalIgnoreCase)), Is.True);
        });

        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage("cat /keys/eng_key.acc"), FromServer(pdaTermUid));
        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage($"auth {apcGuid} /keys/eng_key.acc"), FromServer(pdaTermUid));
        await RunTicks(6);
        await Server.WaitAssertion(() =>
        {
            Assert.That(lockSys.IsLocked(apcUid), Is.False, "Remote auth using /keys/eng_key.acc must unlock APC even with no ID inserted.");
            var term = SEntMan.GetComponent<TerminalProgramComponent>(pdaTermUid);
            Assert.That(term.History.Any(l => l.Contains("[ACC]")), Is.True);
            Assert.That(term.History.Any(l => l.Contains("Codes (") && l.Contains("Engineering")), Is.True);
            var reader = SEntMan.GetComponent<AccessReaderComponent>(apcUid);
            Assert.That(reader.AccessLog.Count, Is.GreaterThan(0), "Remote .acc authorization must log access on target AccessReader.");
        });

        // Short filename resolution (`eng_key`) also works to re-lock!
        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage($"auth {apcGuid} lock eng_key"), FromServer(pdaTermUid));
        await RunTicks(6);
        await Server.WaitAssertion(() =>
        {
            Assert.That(lockSys.IsLocked(apcUid), Is.True, "Short filename 'eng_key' must resolve to /keys/eng_key.acc and re-lock APC.");
        });

        // Close PDA window before workstation test.
        await Client.WaitPost(() =>
        {
            foreach (var w in UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().ToList())
                w.Close();
        });
        await RunTicks(6);

        // 5) Workstation (no PDA) -> open ID Access Encoder -> swipe AtmosIDCard on Workstation -> save /keys/atmos_key.acc -> SSH + auth AirAlarm!
        EntityUid wsEncUid = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(compSys.TryLaunchExecutable(wsCompUid, "/bin/access_encoder.nte", out var wEnc, out var err), Is.True, err);
            wsEncUid = wEnc!.Value;
        });

        Target = FromServer(wsUid);
        await Activate();
        await RunTicks(8);
        await InteractUsing("AtmosIDCard");
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var encComp = SEntMan.GetComponent<AccessEncoderProgramComponent>(wsEncUid);
            Assert.That(encComp.HasScannedCard, Is.True, "Swiping AtmosIDCard on Workstation running ID Access Encoder must scan the card.");
            Assert.That(encComp.ScannedAccessTags.Any(t => t.Id == "Atmospherics"), Is.True);
        });

        await SendBui(ProgramUiKey.Key, new AccessEncoderActionMessage(AccessEncoderAction.BuildSaveFile, "atmos_key"), FromServer(wsEncUid));
        await RunTicks(6);

        EntityUid wsTermUid = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(fsSys.TryFindFileSystem(wsCompUid, out var wsDrive, out var wsFs), Is.True);
            Assert.That(fsSys.TryGetFile<AccessCodesVirtualFile>(wsDrive!.Value, "/keys/atmos_key.acc", out var atmosAcc, wsFs), Is.True);
            Assert.That(atmosAcc!.AccessTags.Any(t => t.Id == "Atmospherics"), Is.True);

            Assert.That(compSys.TryLaunchExecutable(wsCompUid, "/bin/terminal.nte", out var wt, out _), Is.True);
            wsTermUid = wt!.Value;
        });
        await RunTicks(6);

        // SSH into locked AirAlarm from Workstation and unlock using /keys/atmos_key.acc!
        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage($"ssh {alarmGuid}"), FromServer(wsTermUid));
        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage("auth /keys/atmos_key.acc"), FromServer(wsTermUid));
        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage("atmos set_mode Panic"), FromServer(wsTermUid));
        await RunTicks(8);

        await Server.WaitAssertion(() =>
        {
            Assert.That(lockSys.IsLocked(alarmUid), Is.False, "SSH 'auth /keys/atmos_key.acc' must unlock AirAlarm.");
            Assert.That(SEntMan.GetComponent<AirAlarmComponent>(alarmUid).CurrentMode, Is.EqualTo(AirAlarmMode.Panic),
                "Unlocked AirAlarm must accept remote 'atmos set_mode Panic'.");
        });

        await SendBui(ProgramUiKey.Key, new TerminalExecuteCommandMessage("auth lock /keys/atmos_key.acc"), FromServer(wsTermUid));
        await RunTicks(6);
        await Server.WaitAssertion(() =>
        {
            Assert.That(lockSys.IsLocked(alarmUid), Is.True, "SSH 'auth lock /keys/atmos_key.acc' must re-lock AirAlarm.");
        });

        await Client.WaitPost(() =>
        {
            foreach (var w in UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().ToList())
                w.Close();
        });
        await RunTicks(6);
    }

    [Test]
    public async Task TestPdaOsProgramAndControlWrapping()
    {
        var compSys = Server.System<OxydComputerSystem>();
        var power = Server.System<PowerReceiverSystem>();
        var slots = Server.System<ItemSlotsSystem>();

        // 1) Verify PDA boots dedicated /bin/pda_os.nte (PdaOsProgramComponent + PdaOsUiControl)
        //    while Workstation continues booting /bin/os.nte (OsProgramComponent + OsUiControl).
        var pdaNet = await PlaceInHands("DetectivePDA");
        var pdaUid = ToServer(pdaNet);
        EntityUid pdaCompUid = default;
        EntityUid pdaBootUid = default;
        EntityUid wsUid = default;
        EntityUid wsBootUid = default;
        EntityUid capIdUid = default;

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(TargetCoords);
            wsUid = SEntMan.SpawnEntity("OxydWorkstationComputer", coords);
            power.SetNeedsPower(wsUid, false);
            var wsCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(wsUid).ComputerEntity!.Value;
            wsBootUid = SEntMan.GetComponent<OxydComputerComponent>(wsCompUid).BootProgram!.Value;

            pdaCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(pdaUid).ComputerEntity!.Value;
            pdaBootUid = SEntMan.GetComponent<OxydComputerComponent>(pdaCompUid).BootProgram!.Value;

            capIdUid = SEntMan.SpawnEntity("CaptainIDCard", coords);
        });
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<PdaOsProgramComponent>(pdaBootUid), Is.True, "PDA must boot PdaOsProgramComponent.");
            Assert.That(SEntMan.GetComponent<ProgramComponent>(pdaBootUid).ExecutablePath, Is.EqualTo("/bin/pda_os.nte"));

            Assert.That(SEntMan.HasComponent<OsProgramComponent>(wsBootUid), Is.True, "Workstation must still boot OsProgramComponent.");
            Assert.That(SEntMan.GetComponent<ProgramComponent>(wsBootUid).ExecutablePath, Is.EqualTo("/bin/os.nte"));
        });

        // Open PDA UI and verify reworked PdaOsUiControl:
        // - Single unified tab bar (Home, Device, + inline running app tabs; NO Apps or Active App tab)
        // - Merged Home displays Round Time, Name, Inserted ID & VFS apps list
        // - Top-bar clickable utility icons (Flashlight toggle works)
        // - Hardware stats hidden on Home and shown ONLY inside Device tab
        await UseInHand();
        await RunTicks(10);

        ComputerDisplayWindow? pdaWin = null;
        PdaOsUiControl? pdaOsCtrl = null;
        Content.Client.UserInterface.Controls.ContentButton? flashlightBtn = null;
        Content.Client.UserInterface.Controls.ContentButton? deviceTabBtn = null;
        Content.Client.UserInterface.Controls.ContentButton? homeTabBtn = null;
        await Client.WaitAssertion(() =>
        {
            var openWins = UiMan.WindowRoot.Children.OfType<ComputerDisplayWindow>().Where(w => w.IsOpen).ToList();
            Assert.That(openWins.Count, Is.EqualTo(1));
            pdaWin = openWins[0];

            var pdaOsList = new List<PdaOsUiControl>();
            CollectDescendants(pdaWin, pdaOsList);
            Assert.That(pdaOsList.Count, Is.EqualTo(1), "PDA window must mount PdaOsUiControl.");
            pdaOsCtrl = pdaOsList[0];

            var btns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(pdaOsCtrl, btns);

            // Single unified tab bar: Home & Device present, NO separate Apps or Active App tab
            homeTabBtn = btns.FirstOrDefault(b => b.VisibleInTree && b.Name == "TabHomeButton");
            deviceTabBtn = btns.FirstOrDefault(b => b.VisibleInTree && b.Name == "TabDeviceButton");
            Assert.That(homeTabBtn, Is.Not.Null, "Unified tab bar must contain Home tab.");
            Assert.That(deviceTabBtn, Is.Not.Null, "Unified tab bar must contain Device tab.");
            Assert.That(btns.Any(b => b.VisibleInTree && b.Text != null &&
                (b.Text.Equals("Apps", StringComparison.OrdinalIgnoreCase) ||
                 b.Text.Equals("Active App", StringComparison.OrdinalIgnoreCase))),
                Is.False, "PDA OS must merge Home+Apps and omit redundant Active App tab.");

            // Top-bar clickable utility icon for Flashlight
            flashlightBtn = btns.FirstOrDefault(b => b.VisibleInTree && b.Name == "UtilityFlashlightButton");
            Assert.That(flashlightBtn, Is.Not.Null, "PDA OS top bar must expose clickable Flashlight utility icon button.");

            // Merged Home shows Round Time, Name, Inserted ID & VFS apps, while hiding hardware stats (CPU/RAM/HDD)
            var visibleLabels = new List<Content.Client.UserInterface.Controls.ContentLabel>();
            CollectDescendants(pdaOsCtrl, visibleLabels);
            var visibleTexts = visibleLabels.Where(l => l.VisibleInTree && !string.IsNullOrEmpty(l.Text)).Select(l => l.Text!).ToList();

            Assert.That(visibleTexts.Any(t => t.StartsWith("Round Time:", StringComparison.Ordinal)), Is.True,
                "Home view must display Round Time.");
            Assert.That(visibleTexts.Any(t => t.StartsWith("Name:", StringComparison.Ordinal)), Is.True,
                "Home view must display Owner Name.");
            Assert.That(visibleTexts.Any(t => t.StartsWith("Inserted ID:", StringComparison.Ordinal)), Is.True,
                "Home view must display Inserted ID.");
            Assert.That(FindRowRunButton(pdaOsCtrl, "Terminal"), Is.Not.Null,
                "Merged Home view must list VFS apps (Terminal) directly.");
            Assert.That(visibleTexts.Any(t => t.Contains("CPU:", StringComparison.Ordinal) || t.Contains("RAM:", StringComparison.Ordinal)),
                Is.False, "Hardware stats (CPU/RAM) must NOT be shown on Home or always-visible PDA chrome.");

            AssertAllVisibleChildrenWithinViewport(pdaWin, pdaOsCtrl, "PdaOsUiControl Home");
        });

        await ClickViaHitTest(flashlightBtn!);
        await RunTicks(6);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<PdaComponent>(pdaUid).FlashlightOn, Is.True,
                "Clicking Flashlight icon in PDA OS must toggle PDA flashlight ON.");
        });

        // Switch to Device tab -> hardware stats ARE visible here, and still within bounds!
        await ClickViaHitTest(deviceTabBtn!);
        await RunTicks(6);
        await Client.WaitAssertion(() =>
        {
            var visibleLabels = new List<Content.Client.UserInterface.Controls.ContentLabel>();
            CollectDescendants(pdaOsCtrl!, visibleLabels);
            var visibleTexts = visibleLabels.Where(l => l.VisibleInTree && !string.IsNullOrEmpty(l.Text)).Select(l => l.Text!).ToList();

            Assert.That(visibleTexts.Any(t => t.Contains("CPU:", StringComparison.Ordinal) && t.Contains("RAM:", StringComparison.Ordinal)),
                Is.True, "Device tab must display hardware diagnostics.");
            AssertAllVisibleChildrenWithinViewport(pdaWin!, pdaOsCtrl!, "PdaOsUiControl Device");
        });
        await ClickViaHitTest(homeTabBtn!);
        await RunTicks(4);

        // 2) Insert CaptainIDCard (AllAccess — 20+ access codes!) into PDA, open ID Access Encoder,
        //    save /keys/captain.acc, and verify all controls + access code chips wrap cleanly inside bounds!
        EntityUid encUid = default;
        await Server.WaitPost(() =>
        {
            slots.TryEject(pdaUid, PdaComponent.PdaIdSlotId, null, out _);
            Assert.That(slots.TryInsert(pdaUid, PdaComponent.PdaIdSlotId, capIdUid, null), Is.True);
            Assert.That(compSys.TryLaunchExecutable(pdaCompUid, "/bin/access_encoder.nte", out var eUid, out var err), Is.True, err);
            encUid = eUid!.Value;
        });
        await RunTicks(10);

        await SendBui(ProgramUiKey.Key, new AccessEncoderActionMessage(AccessEncoderAction.BuildSaveFile, "captain"), FromServer(encUid));
        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            var encList = new List<AccessEncoderUiControl>();
            CollectDescendants(pdaWin!, encList);
            Assert.That(encList.Count, Is.EqualTo(1));

            // Access code chip wrap container must wrap Captain's 20+ access tags onto multiple rows!
            var wraps = new List<Content.Client.UserInterface.Controls.ContentWrapContainer>();
            CollectDescendants(encList[0], wraps);
            var tagWrap = wraps.FirstOrDefault(w => w.ChildCount >= 10);
            Assert.That(tagWrap, Is.Not.Null, "ID Access Encoder must display Captain's access codes inside a ContentWrapContainer.");
            Assert.That(tagWrap!.Size.Y, Is.GreaterThan(40f),
                $"Expected Captain's {tagWrap.ChildCount} access codes to wrap onto multiple rows, but height was {tagWrap.Size.Y}.");

            AssertAllVisibleChildrenWithinViewport(pdaWin!, encList[0], "AccessEncoderUiControl (Captain AllAccess)");
        });

        // 3) Launch File Browser (9 directory buttons in dirBar!) on PDA and verify directory bar wraps within bounds!
        await Server.WaitPost(() =>
        {
            Assert.That(compSys.TryLaunchExecutable(pdaCompUid, "/bin/file_browser.nte", out _, out var err), Is.True, err);
        });
        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            var fbList = new List<FileBrowserUiControl>();
            CollectDescendants(pdaWin!, fbList);
            Assert.That(fbList.Count, Is.EqualTo(1));
            AssertAllVisibleChildrenWithinViewport(pdaWin!, fbList[0], "FileBrowserUiControl");

            pdaWin!.Close();
        });
        await RunTicks(8);
    }

    [Test]
    public async Task TestNetworkConfiguratorLinkPdaToButtonDoesNotAssertOnServer()
    {
        // Regression test: using NetworkConfigurator to link a SignalButton to a PDA (including in-hand)
        // previously asserted on server with "Why are you raising predictive events on the server?"
        // because OpenDeviceLinkUi passed predicted: true to SharedUserInterfaceSystem.OpenUi.
        await SpawnTarget("SignalButton");
        var buttonUid = ToServer(Target)!.Value;

        EntityUid pdaUid = default;
        await Server.WaitPost(() =>
        {
            pdaUid = SEntMan.SpawnEntity("DetectivePDA", SEntMan.GetCoordinates(TargetCoords));
        });
        await RunTicks(4);

        var cfgNet = await PlaceInHands("NetworkConfigurator");
        var cfgUid = ToServer(cfgNet);

        // 1) Click SignalButton with NetworkConfigurator -> automatically switches to LinkMode and starts link on button
        await Interact();
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var cfg = SEntMan.GetComponent<Content.Shared.DeviceConfigurator.Components.NetworkConfiguratorComponent>(cfgUid);
            Assert.That(cfg.ActiveDeviceLink, Is.EqualTo(buttonUid),
                "Clicking SignalButton with NetworkConfigurator must start linking from SignalButton.");
        });

        // 2) Interact active NetworkConfigurator with the PDA -> opens Link UI cleanly without server assert!
        await Server.WaitPost(() =>
        {
            var interactionSys = Server.System<Content.Shared.Interaction.SharedInteractionSystem>();
            interactionSys.InteractUsing(SPlayer, cfgUid, pdaUid, SEntMan.GetComponent<Robust.Shared.GameObjects.TransformComponent>(pdaUid).Coordinates);
        });
        await RunTicks(8);

        await Server.WaitAssertion(() =>
        {
            var cfg = SEntMan.GetComponent<Content.Shared.DeviceConfigurator.Components.NetworkConfiguratorComponent>(cfgUid);
            Assert.That(cfg.DeviceLinkTarget, Is.EqualTo(pdaUid), "DeviceLinkTarget must be set to the PDA.");
            Assert.That(Server.System<SharedUserInterfaceSystem>().IsUiOpen(cfgUid, Content.Shared.DeviceConfigurator.NetworkConfiguratorUiKey.Link, SPlayer),
                Is.True, "NetworkConfigurator Link UI must open cleanly on server without predictive assert.");
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(TryFindWindow(typeof(Content.Client.NetworkConfigurator.NetworkConfiguratorLinkMenu), out var linkWin),
                Is.True, "NetworkConfiguratorLinkMenu must open cleanly on client.");
            ((Robust.Client.UserInterface.CustomControls.BaseWindow) linkWin!).Close();
        });
        await RunTicks(6);
    }

    [Test]
    public async Task TestAutolatheTwoTabUiInMemoryBlueprintsAndPrinting()
    {
        var compSys = Server.System<OxydComputerSystem>();
        var fsSys = Server.System<SharedFileSystemSystem>();
        var matSys = Server.System<SharedMaterialStorageSystem>();

        await SpawnTarget("Autolathe");
        var latheUid = ToServer(Target)!.Value;
        EntityUid latheCompUid = default;
        EntityUid latheBootUid = default;
        EntityUid latheProgUid = default;

        await Server.WaitPost(() =>
        {
            Server.System<PowerReceiverSystem>().SetNeedsPower(latheUid, false);
            latheCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(latheUid).ComputerEntity!.Value;

            Assert.That(matSys.TryChangeMaterialAmount(latheUid, "Steel", 8000), Is.True);
            Assert.That(matSys.TryChangeMaterialAmount(latheUid, "Plastic", 4000), Is.True);
        });
        await RunTicks(8);

        // 1) All default autolathe blueprints must live in computer VFS memory (/blueprints/*.lathe) with timestamps!
        string wirecutterFilePath = string.Empty;
        string wrenchFilePath = string.Empty;
        await Server.WaitAssertion(() =>
        {
            var cComp = SEntMan.GetComponent<OxydComputerComponent>(latheCompUid);
            Assert.That(cComp.IsBooted, Is.True, "Powered Autolathe embedded computer must boot.");
            latheBootUid = cComp.BootProgram!.Value;
            latheProgUid = cComp.ActiveProgram!.Value;

            Assert.That(fsSys.TryFindFileSystem(latheCompUid, out _, out var fs), Is.True);
            var bpFiles = fs!.Files.Values.OfType<RecipeBlueprintFile>().ToList();
            Assert.That(bpFiles.Count, Is.GreaterThan(0), "Autolathe static blueprints must be materialized into computer memory.");

            foreach (var bp in bpFiles)
            {
                if (bp.GetAllRecipes().Any(r => r.Id == "Wirecutter"))
                    wirecutterFilePath = bp.FullPath;
                if (bp.GetAllRecipes().Any(r => r.Id == "Wrench"))
                    wrenchFilePath = bp.FullPath;
            }

            Assert.That(wirecutterFilePath, Is.Not.Empty, "Wirecutter blueprint must exist in computer memory.");
            Assert.That(wrenchFilePath, Is.Not.Empty, "Wrench blueprint must exist in computer memory.");
        });

        // 2) Open Autolathe UI and verify simplified 2-tab mode (Controller & Terminal only) + rich LatheControlUiControl!
        await Activate();
        await RunTicks(10);

        ComputerDisplayWindow? win = null;
        await Client.WaitAssertion(() =>
        {
            win = GetWindow<ComputerDisplayWindow>();
            Assert.That(win.IsOpen, Is.True);

            // Strictly 2 tabs: Controller & Terminal (no Programs (.nte), no DeviceLinks, no hardware stats bar)
            var tabBar = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentWrapContainer>(
                c => c.Name == "EmbeddedTabBar", win);
            Assert.That(tabBar, Is.Not.Null);
            var visibleTabButtons = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(tabBar!, visibleTabButtons);
            visibleTabButtons = visibleTabButtons.Where(b => b.Visible).ToList();
            Assert.That(visibleTabButtons.Select(b => b.Text).ToList(), Is.EqualTo(new[] { "Controller", "Terminal" }),
                "Autolathe embedded UI must expose strictly Controller and Terminal tabs.");

            // Rich LatheControlUiControl: materials as MaterialIconTile icons with overlaid counts, NOT text split by '|'
            var latheUi = GetControlFromChildren<LatheControlUiControl>(_ => true, win);
            Assert.That(latheUi, Is.Not.Null);

            var matTiles = new List<MaterialIconTile>();
            CollectDescendants(latheUi!, matTiles);
            Assert.That(matTiles.Count, Is.GreaterThanOrEqualTo(2), "Materials must render as MaterialIconTile icon controls.");
            Assert.That(matTiles.Any(t => t.MaterialId == "Steel" && t.CountText == "80"), Is.True);
            Assert.That(matTiles.Any(t => t.MaterialId == "Plastic" && t.CountText == "40"), Is.True);

            // Recipe cards with descriptions & costs
            var cards = new List<Content.Client.UserInterface.Controls.ContentPanelContainer>();
            CollectDescendants(latheUi!, cards);
            var recipeCards = cards.Where(c => c.Name == "LatheRecipeCard").ToList();
            Assert.That(recipeCards.Count, Is.GreaterThan(5), "In-memory blueprints must populate recipe browser cards.");
        });

        // 3) Queue Wirecutter x2 + Wrench x1 from in-memory blueprints and verify live printing + queue UI + actual item output!
        await SendBui(ProgramUiKey.Key, new LatheControlActionMessage(LatheControlAction.PrintRecipe, "Wirecutter", wirecutterFilePath, 2), FromServer(latheProgUid));
        await SendBui(ProgramUiKey.Key, new LatheControlActionMessage(LatheControlAction.PrintRecipe, "Wrench", wrenchFilePath, 1), FromServer(latheProgUid));
        await RunTicks(4);

        await Server.WaitAssertion(() =>
        {
            var latheComp = SEntMan.GetComponent<LatheComponent>(latheUid);
            Assert.That(latheComp.CurrentRecipe?.Id, Is.EqualTo("Wirecutter"), "Printing an in-memory blueprint must start fabricating.");
            Assert.That(latheComp.Queue.Count, Is.GreaterThanOrEqualTo(2), "Remaining items must be queued.");
        });

        await Client.WaitAssertion(() =>
        {
            var latheUi = GetControlFromChildren<LatheControlUiControl>(_ => true, win!);
            Assert.That(latheUi, Is.Not.Null);

            var nameLbl = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Name == "LatheCurrentPrintingName", latheUi!);
            var usageLbl = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Name == "LatheCurrentUsageLabel", latheUi!);
            var progBar = GetControlFromChildren<ContentProgressBar>(
                p => p.Name == "LatheProgressBar", latheUi!);
            var queueContainer = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentWrapContainer>(
                w => w.Name == "LatheQueueContainer", latheUi!);

            Assert.That(nameLbl!.Text, Does.Contain("wirecutter").IgnoreCase);
            Assert.That(usageLbl!.Text, Does.Contain("Steel").IgnoreCase);
            Assert.That(progBar!.OverlayText, Does.Contain("%"));

            var queueLabels = new List<Content.Client.UserInterface.Controls.ContentLabel>();
            CollectDescendants(queueContainer!, queueLabels);
            Assert.That(queueLabels.Any(l => (l.Text ?? string.Empty).Contains("x1")), Is.True,
                "Queue must display queued item icon + name + count.");
        });

        // Regression test: rapidly queuing >10 items (including a same-tick burst!) must NOT trigger DeviceLink
        // InvokeLimit overload or freeze the Autolathe UI on a single stuck state!
        for (var i = 0; i < 12; i++)
        {
            await SendBui(ProgramUiKey.Key, new LatheControlActionMessage(
                LatheControlAction.PrintRecipe,
                i % 2 == 0 ? "Wrench" : "Wirecutter",
                i % 2 == 0 ? wrenchFilePath : wirecutterFilePath,
                1), FromServer(latheProgUid));
        }
        await RunTicks(6);

        var expectedQueued = 0;
        await Server.WaitAssertion(() =>
        {
            var latheComp = SEntMan.GetComponent<LatheComponent>(latheUid);
            expectedQueued = latheComp.Queue.Sum(b => Math.Max(0, b.ItemsRequested - b.ItemsPrinted));
            Assert.That(expectedQueued, Is.GreaterThanOrEqualTo(8),
                "Autolathe queue must keep accepting items beyond the default DeviceLink InvokeLimit.");
        });

        await Client.WaitAssertion(() =>
        {
            var latheUi = GetControlFromChildren<LatheControlUiControl>(_ => true, win!);
            var queueHeader = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Name == "LatheQueueHeader", latheUi!);
            Assert.That(queueHeader!.Text, Is.EqualTo($"Queue ({expectedQueued} items):"),
                "Autolathe UI must stay synchronized with server queue after rapidly queuing >10 items.");
        });

        // Cancel queue and verify UI transitions cleanly back to Standby / Queue: Empty (not stuck on Printing!)
        await SendBui(ProgramUiKey.Key, new LatheControlActionMessage(LatheControlAction.CancelAll), FromServer(latheProgUid));
        await RunTicks(6);

        await Client.WaitAssertion(() =>
        {
            var latheUi = GetControlFromChildren<LatheControlUiControl>(_ => true, win!);
            var nameLbl = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Name == "LatheCurrentPrintingName", latheUi!);
            var queueHeader = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Name == "LatheQueueHeader", latheUi!);
            Assert.That(nameLbl!.Text, Does.Contain("Standby"),
                "Autolathe UI must return cleanly to Standby after cancelling queue.");
            Assert.That(queueHeader!.Text, Is.EqualTo("Queue: Empty"));
        });

        // Re-queue 1 Wirecutter after rapid burst + cancel and advance past CompleteTime (2s) to prove printing still works!
        await SendBui(ProgramUiKey.Key, new LatheControlActionMessage(LatheControlAction.PrintRecipe, "Wirecutter", wirecutterFilePath, 1), FromServer(latheProgUid));
        await RunSeconds(3f);

        await Server.WaitAssertion(() =>
        {
            var spawnedWirecutters = 0;
            var metaQuery = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
            while (metaQuery.MoveNext(out _, out var meta))
            {
                if (meta.EntityPrototype?.ID == "Wirecutter")
                    spawnedWirecutters++;
            }

            Assert.That(spawnedWirecutters, Is.GreaterThanOrEqualTo(1),
                "Autolathe must actually spawn the fabricated Wirecutter item from its in-memory blueprint.");
        });

        await Client.WaitAssertion(() =>
        {
            var latheUi = GetControlFromChildren<LatheControlUiControl>(_ => true, win!);
            var nameLbl = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Name == "LatheCurrentPrintingName", latheUi!);
            Assert.That(nameLbl!.Text, Does.Contain("Standby"),
                "Autolathe UI must update back to Standby when final queued item finishes printing.");
        });

        // 4) Switch to Terminal tab inside the 2-tab embedded OS
        await SendBui(ProgramUiKey.Key, new EmbeddedOsLaunchProgramMessage("/bin/terminal.nte"), FromServer(latheBootUid));
        await RunTicks(8);

        await Client.WaitAssertion(() =>
        {
            var termCtrl = GetControlFromChildren<TerminalUiControl>(_ => true, win!);
            Assert.That(termCtrl, Is.Not.Null);
            Assert.That(termCtrl!.VisibleInTree, Is.True);
            win!.Close();
        });
        await RunTicks(6);
    }

    [Test]
    public async Task TestPdaEjectPlacesItemInUserHandWhileHoldingPda()
    {
        var compSys = Server.System<OxydComputerSystem>();
        var handsSys = Server.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>();

        // Give InteractionTestMob a second hand (it only starts with hand_right) so it can hold the PDA AND receive ejected items.
        await Server.WaitPost(() =>
        {
            handsSys.AddHand(SPlayer, "hand_left", Content.Shared.Hands.Components.HandLocation.Left);
        });

        // Hold CaptainPDA (which starts with an ID card & fountain pen inserted) in player's hand.
        var pdaNet = await PlaceInHands("CaptainPDA");
        var pdaUid = ToServer(pdaNet);
        EntityUid pdaCompUid = default;
        EntityUid pdaBootUid = default;
        EntityUid idUid = default;
        EntityUid penUid = default;

        await Server.WaitAssertion(() =>
        {
            pdaCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(pdaUid).ComputerEntity!.Value;
            pdaBootUid = SEntMan.GetComponent<OxydComputerComponent>(pdaCompUid).BootProgram!.Value;
            var pda = SEntMan.GetComponent<PdaComponent>(pdaUid);
            Assert.That(pda.IdSlot.Item, Is.Not.Null, "CaptainPDA should start with an ID card.");
            Assert.That(pda.PenSlot.Item, Is.Not.Null, "CaptainPDA should start with a pen.");
            idUid = pda.IdSlot.Item!.Value;
            penUid = pda.PenSlot.Item!.Value;

            compSys.OpenComputerForActor(pdaCompUid, SPlayer);
        });
        await RunTicks(8);

        // Click Eject ID via PDA OS while holding the PDA -> ID card must land in the player's free hand!
        await SendBui(ProgramUiKey.Key, new PdaOsHardwareActionMessage("eject_id"), FromServer(pdaBootUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var held = handsSys.EnumerateHeld(SPlayer).ToList();
            Assert.That(held, Contains.Item(pdaUid), "Player should still be holding the PDA.");
            Assert.That(held, Contains.Item(idUid), "Ejecting ID while holding the PDA must put the ID card in the user's hand.");

            // Drop the ID card to free the other hand, then test Eject Pen too.
            handsSys.TryDrop(SPlayer, idUid);
        });
        await RunTicks(4);

        await SendBui(ProgramUiKey.Key, new PdaOsHardwareActionMessage("eject_pen"), FromServer(pdaBootUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var held = handsSys.EnumerateHeld(SPlayer).ToList();
            Assert.That(held, Contains.Item(penUid), "Ejecting Pen while holding the PDA must put the pen in the user's hand.");
        });

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            win.Close();
        });
        await RunTicks(4);
    }

    [Test]
    public async Task TestRecordFileDownloadTimestampsComputerRoutersAndEditablePacketSniffer()
    {
        var compSys = Server.System<OxydComputerSystem>();
        var fsSys = Server.System<SharedFileSystemSystem>();
        var routerSys = Server.System<NetworkRouterSystem>();
        var guidSys = Server.System<SharedDeviceGuidSystem>();
        var powerSys = Server.System<PowerReceiverSystem>();
        var itemSlots = Server.System<ItemSlotsSystem>();

        EntityUid routerHost = default;
        EntityUid routerCompUid = default;
        EntityUid serverHost = default;
        EntityUid serverCompUid = default;
        EntityUid pda1Host = default;
        EntityUid pda1CompUid = default;
        EntityUid pda2Host = default;
        EntityUid pda2CompUid = default;
        EntityUid wsHost = default;
        EntityUid wsCompUid = default;

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(TargetCoords);

            routerHost = SEntMan.SpawnEntity("StationNetworkRouter", coords);
            powerSys.SetNeedsPower(routerHost, false);
            routerCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(routerHost).ComputerEntity!.Value;

            serverHost = SEntMan.SpawnEntity("BaseDataServer", coords);
            powerSys.SetNeedsPower(serverHost, false);
            serverCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(serverHost).ComputerEntity!.Value;

            pda1Host = SEntMan.SpawnEntity("DetectivePDA", coords);
            pda1CompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(pda1Host).ComputerEntity!.Value;

            pda2Host = SEntMan.SpawnEntity("DetectivePDA", coords);
            pda2CompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(pda2Host).ComputerEntity!.Value;

            wsHost = SEntMan.SpawnEntity("OxydWorkstationComputer", coords);
            powerSys.SetNeedsPower(wsHost, false);
            wsCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(wsHost).ComputerEntity!.Value;
        });
        await RunTicks(8);

        // 1) Verify both Router and DataServer are real booted computers with HDDs, and place a local Crew Manifest copy on DataServer's HDD!
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<OxydComputerComponent>(routerCompUid).IsBooted, Is.True, "StationNetworkRouter must be a booted computer.");
            Assert.That(SEntMan.GetComponent<OxydComputerComponent>(serverCompUid).IsBooted, Is.True, "BaseDataServer must be a booted computer.");
            Assert.That(fsSys.TryFindFileSystem(routerCompUid, out _, out _), Is.True, "Router computer must have its own HDD.");
            Assert.That(fsSys.TryFindFileSystem(serverCompUid, out var srvDrive, out var srvFs), Is.True, "DataServer computer must have its own HDD.");

            Assert.That(fsSys.TryWriteFile(srvDrive!.Value, new DatabaseVirtualFile
            {
                Name = "crew_manifest",
                Extension = "db",
                Directory = "/records/",
                TableName = "crew_manifest",
                Data = new Dictionary<string, string>
                {
                    ["__station"] = "NSS Oxyd",
                    ["row_0"] = "Alice\tCaptain\tCaptain\tJobIconCaptain",
                    ["row_1"] = "Bob\tStation Engineer\tStationEngineer\tJobIconStationEngineer"
                },
                Size = 256
            }, srvFs), Is.True);
        });

        // 2) Launch CrewManifest & FileBrowser on PDA #1 -> must download /records/crew_manifest.db from DataServer's local copy into PDA #1's memory with a timestamp!
        EntityUid pda1ManifestProg = default;
        EntityUid pda1FileBrowserProg = default;
        TimeSpan firstDownloadTs = default;
        await Server.WaitPost(() =>
        {
            Assert.That(compSys.TryLaunchExecutable(pda1CompUid, "/bin/crew_manifest.nte", out var mProg, out var mErr), Is.True, mErr);
            pda1ManifestProg = mProg!.Value;
            Assert.That(compSys.TryLaunchExecutable(pda1CompUid, "/bin/file_browser.nte", out var fbProg, out var fbErr), Is.True, fbErr);
            pda1FileBrowserProg = fbProg!.Value;
        });
        await RunTicks(8);

        await Server.WaitAssertion(() =>
        {
            Assert.That(fsSys.TryFindFileSystem(pda1CompUid, out var pda1Drive, out var pda1Fs), Is.True);
            Assert.That(fsSys.TryGetFile<DatabaseVirtualFile>(pda1Drive!.Value, CrewManifestProgramComponent.CacheFilePath, out var localManifest, pda1Fs), Is.True,
                "CrewManifest program must download /records/crew_manifest.db into PDA memory.");
            Assert.That(localManifest!.Data.Values.Any(v => v.StartsWith("Alice\tCaptain\t")), Is.True);
            Assert.That(localManifest.Data.Values.Any(v => v.StartsWith("Bob\tStation Engineer\t")), Is.True);
            Assert.That(localManifest.LastModified, Is.GreaterThan(TimeSpan.Zero), "Files in memory must record a LastModified timestamp.");
            firstDownloadTs = localManifest.LastModified;
        });

        // Open PDA #1 UI and verify timestamps render in FileBrowser & CrewManifest UI!
        await Server.WaitPost(() =>
        {
            compSys.SwitchActiveProgram(pda1CompUid, pda1FileBrowserProg, actor: SPlayer);
            compSys.OpenComputerForActor(pda1CompUid, SPlayer);
        });
        await RunTicks(8);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var fb = GetControlFromChildren<FileBrowserUiControl>(_ => true, win);
            Assert.That(fb, Is.Not.Null);
            var labels = new List<Content.Client.UserInterface.Controls.ContentLabel>();
            CollectDescendants(fb!, labels);
            Assert.That(labels.Any(l => (l.Text ?? string.Empty).Contains("crew_manifest.db") && (l.Text ?? string.Empty).Contains("[")), Is.True,
                "FileBrowser must show timestamp on files in memory.");
            win.Close();
        });
        await RunTicks(4);

        // 3) Open PacketSniffer on Workstation -> verify player-relevant editable TCP/MANIFEST packet was captured!
        EntityUid snifferProg = default;
        string capturedPacketId = string.Empty;
        await Server.WaitPost(() =>
        {
            Assert.That(compSys.TryLaunchExecutable(wsCompUid, "/bin/packet_sniffer.nte", out var sProg, out var sErr), Is.True, sErr);
            snifferProg = sProg!.Value;
            compSys.OpenComputerForActor(wsCompUid, SPlayer);
        });
        await RunTicks(8);

        await Server.WaitAssertion(() =>
        {
            var routerComp = SEntMan.GetComponent<NetworkRouterComponent>(routerHost);
            var manifestPkt = routerComp.RecordedPackets.LastOrDefault(p =>
                p.Protocol == "TCP/MANIFEST" && p.IsPlayerRelevant && p.IsEditable);
            Assert.That(manifestPkt, Is.Not.Null, "Router & PacketSniffer must capture player-relevant editable TCP/MANIFEST packet.");
            Assert.That(manifestPkt!.EditableEntries["Alice"], Is.EqualTo("Captain"));
            Assert.That(manifestPkt.EditableEntries["Bob"], Is.EqualTo("Station Engineer"));
            capturedPacketId = manifestPkt.PacketId;
        });

        ComputerDisplayWindow? wsWin = null;
        await Client.WaitAssertion(() =>
        {
            wsWin = GetWindow<ComputerDisplayWindow>();
            var snifferUi = GetControlFromChildren<PacketSnifferUiControl>(_ => true, wsWin);
            Assert.That(snifferUi, Is.Not.Null);
            var editBtn = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentButton>(
                b => b.Name == "SnifferEditPacketButton", snifferUi!);
            Assert.That(editBtn, Is.Not.Null, "PacketSniffer UI must provide an Edit button for captured player-relevant editable packets.");
        });

        // 4) Modify captured Crew Manifest packet (Alice -> Clown) and transmit directly to PDA #1!
        await RunTicks(5);
        await SendBui(ProgramUiKey.Key, new PacketSnifferActionMessage(
            PacketSnifferAction.ModifyAndTransmitPacket,
            targetGuid: guidSys.GetGuid(pda1CompUid),
            packetId: capturedPacketId,
            modifiedEntries: new Dictionary<string, string>
            {
                ["Alice"] = "Clown",
                ["Bob"] = "Station Engineer"
            },
            applyToSource: false), FromServer(snifferProg));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            Assert.That(fsSys.TryFindFileSystem(pda1CompUid, out var pda1Drive, out var pda1Fs), Is.True);
            Assert.That(fsSys.TryGetFile<DatabaseVirtualFile>(pda1Drive!.Value, CrewManifestProgramComponent.CacheFilePath, out var updatedManifest, pda1Fs), Is.True);
            Assert.That(updatedManifest!.Data.Values.Any(v => v.StartsWith("Alice\tClown\t")), Is.True,
                "Transmitting modified Crew Manifest packet must update target device's local /records/crew_manifest.db.");
            Assert.That(updatedManifest.LastModified, Is.GreaterThanOrEqualTo(firstDownloadTs));
        });

        // 5) Poison DataServer's local copy via PacketSniffer (applyToSource: true -> Bob = Syndicate Agent) and confirm PDA #2 downloads the poisoned copy!
        await SendBui(ProgramUiKey.Key, new PacketSnifferActionMessage(
            PacketSnifferAction.ModifyAndTransmitPacket,
            sourceGuid: guidSys.GetGuid(serverCompUid),
            packetId: capturedPacketId,
            modifiedEntries: new Dictionary<string, string>
            {
                ["Alice"] = "Captain",
                ["Bob"] = "Syndicate Agent"
            },
            applyToSource: true), FromServer(snifferProg));
        await RunTicks(6);

        await Client.WaitAssertion(() =>
        {
            wsWin!.Close();
        });
        await RunTicks(4);

        await Server.WaitPost(() =>
        {
            Assert.That(compSys.TryLaunchExecutable(pda2CompUid, "/bin/crew_manifest.nte", out _, out var err), Is.True, err);
        });
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            Assert.That(fsSys.TryFindFileSystem(pda2CompUid, out var pda2Drive, out var pda2Fs), Is.True);
            Assert.That(fsSys.TryGetFile<DatabaseVirtualFile>(pda2Drive!.Value, CrewManifestProgramComponent.CacheFilePath, out var pda2Manifest, pda2Fs), Is.True);
            Assert.That(pda2Manifest!.Data.Values.Any(v => v.StartsWith("Bob\tSyndicate Agent\t")), Is.True,
                "New clients downloading Crew Manifest must receive the server computer's modified local copy.");
        });

        // 6) Verify Router computer also serves its own local copy when DataServer is offline!
        await Server.WaitPost(() =>
        {
            // Cut DataServer's network wire so only the Router computer handles network record downloads
            SEntMan.GetComponent<OxydComputerComponent>(serverCompUid).NetworkWireCut = true;
            compSys.RecalculateHardwareStats(serverCompUid);

            Assert.That(fsSys.TryFindFileSystem(routerCompUid, out var rDrive, out var rFs), Is.True);
            Assert.That(fsSys.TryWriteFile(rDrive!.Value, new DatabaseVirtualFile
            {
                Name = "crew_manifest",
                Extension = "db",
                Directory = "/records/",
                TableName = "crew_manifest",
                Data = new Dictionary<string, string>
                {
                    ["__station"] = "Router Local Copy",
                    ["row_0"] = "Charlie\tRouter Admin\tAssistant\tJobIconAssistant"
                },
                Size = 256
            }, rFs), Is.True);

            // Force PDA #2 to refresh CrewManifest from the Router computer's local copy
            var pda2Comp = SEntMan.GetComponent<OxydComputerComponent>(pda2CompUid);
            var pda2ManifestProg = pda2Comp.RunningPrograms.First(p => SEntMan.HasComponent<CrewManifestProgramComponent>(p));
            compSys.SwitchActiveProgram(pda2CompUid, pda2ManifestProg);
        });
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            Assert.That(fsSys.TryFindFileSystem(pda2CompUid, out var pda2Drive, out var pda2Fs), Is.True);
            Assert.That(fsSys.TryGetFile<DatabaseVirtualFile>(pda2Drive!.Value, CrewManifestProgramComponent.CacheFilePath, out var routerCopy, pda2Fs), Is.True);
            Assert.That(routerCopy!.Data["__station"], Is.EqualTo("Router Local Copy"));
            Assert.That(routerCopy.Data.Values.Any(v => v.StartsWith("Charlie\tRouter Admin\t")), Is.True,
                "StationNetworkRouter computer must serve its own local /records/crew_manifest.db copy.");
        });

        // 7) Verify offline reading from local cached file when PDA #1 loses its NIC!
        await Server.WaitPost(() =>
        {
            itemSlots.TryEject(pda1CompUid, ModularComputerComponent.SlotNetwork, null, out _);
            compSys.RecalculateHardwareStats(pda1CompUid);
            compSys.SwitchActiveProgram(pda1CompUid, pda1ManifestProg, actor: SPlayer);
            compSys.OpenComputerForActor(pda1CompUid, SPlayer);
        });
        await RunTicks(8);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var manifestUi = GetControlFromChildren<CrewManifestUiControl>(_ => true, win);
            Assert.That(manifestUi, Is.Not.Null);
            var labels = new List<Content.Client.UserInterface.Controls.ContentLabel>();
            CollectDescendants(manifestUi!, labels);
            Assert.That(labels.Any(l => (l.Text ?? string.Empty).Contains("Alice — Clown")), Is.True,
                "Offline CrewManifest must render its local cached /records/crew_manifest.db file from memory.");
            Assert.That(labels.Any(l => (l.Text ?? string.Empty).Contains("[File:")), Is.True,
                "CrewManifest UI must display the cached file timestamp.");
            win.Close();
        });
        await RunTicks(4);
    }

    [Test]
    public async Task TestEncryptedRecipeDiskUsesCopyProtectionInstantLoadAndLimitedLatheMemory()
    {
        await SpawnTarget("Autolathe");
        var latheUid = ToServer(Target)!.Value;
        EntityUid latheCompUid = default;
        EntityUid latheProgUid = default;
        EntityUid latheDriveUid = default;
        EntityUid secDiskUid = default;
        EntityUid hudDiskUid = default;
        EntityUid engDiskUid = default;
        EntityUid bigPackDiskUid = default;
        EntityUid pdaUid = default;
        EntityUid pdaCompUid = default;
        EntityUid pdaBrowserProg = default;

        await Server.WaitAssertion(() =>
        {
            var coords = SEntMan.GetComponent<TransformComponent>(latheUid).Coordinates;
            Server.System<PowerReceiverSystem>().SetNeedsPower(latheUid, false);
            latheCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(latheUid).ComputerEntity!.Value;

            var matSys = Server.System<MaterialStorageSystem>();
            Assert.That(matSys.TryChangeMaterialAmount(latheUid, "Steel", 20000), Is.True);
            Assert.That(matSys.TryChangeMaterialAmount(latheUid, "Glass", 20000), Is.True);
            Assert.That(matSys.TryChangeMaterialAmount(latheUid, "Plastic", 20000), Is.True);

            secDiskUid = SEntMan.SpawnAtPosition("BaseRecipeDisk", coords);
            hudDiskUid = SEntMan.SpawnAtPosition("OxydRecipeDiskEncrypted", coords);
            engDiskUid = SEntMan.SpawnAtPosition("OxydDataDiskEngineering", coords);
            bigPackDiskUid = SEntMan.SpawnAtPosition("OxydDataDiskBlank", coords);
            pdaUid = SEntMan.SpawnAtPosition("PassengerPDA", coords);
            pdaCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(pdaUid).ComputerEntity!.Value;

            // Populate an unencrypted 10-recipe multi-blueprint file on bigPackDiskUid (> 20 KB)
            var fsSys = Server.System<SharedFileSystemSystem>();
            var bigPackFs = SEntMan.GetComponent<FileSystemComponent>(bigPackDiskUid);
            var wroteBig = fsSys.TryWriteFile(bigPackDiskUid, new RecipeBlueprintFile
            {
                Name = "mega_engineering_pack",
                Extension = "lathe",
                Directory = "/blueprints/",
                Recipes = new List<ProtoId<LatheRecipePrototype>>
                {
                    "DoorElectronics",
                    "FirelockElectronics",
                    "SolarTrackerElectronics",
                    "APCElectronics",
                    "AirAlarmElectronics",
                    "FireAlarmElectronics",
                    "PowerCellSmall",
                    "PowerCellMedium",
                    "Multitool",
                    "Wirecutter"
                },
                IsEncrypted = false,
                DrmLocked = false,
                RemainingUses = null
            }, bigPackFs);
            Assert.That(wroteBig, Is.True, "Writing multi-blueprint file to blank disk must succeed.");
        });

        await RunTicks(12);

        // 1) Verify dynamic file sizing (multi-blueprint > single-blueprint) & tight Autolathe HDD free space!
        await Server.WaitAssertion(() =>
        {
            var fsSys = Server.System<SharedFileSystemSystem>();
            var cComp = SEntMan.GetComponent<OxydComputerComponent>(latheCompUid);
            Assert.That(cComp.IsBooted, Is.True);
            latheProgUid = cComp.ActiveProgram!.Value;

            Assert.That(fsSys.TryFindFileSystem(latheCompUid, out var drive, out var latheFs), Is.True);
            latheDriveUid = drive!.Value;

            var secFs = SEntMan.GetComponent<FileSystemComponent>(secDiskUid);
            Assert.That(fsSys.TryGetFile<RecipeBlueprintFile>(secDiskUid, "/blueprints/security_gear.lathe", out var secBp, secFs), Is.True);

            var engFs = SEntMan.GetComponent<FileSystemComponent>(engDiskUid);
            Assert.That(fsSys.TryGetFile<RecipeBlueprintFile>(engDiskUid, "/blueprints/door_electronics.lathe", out var doorBp, engFs), Is.True);

            var bigFs = SEntMan.GetComponent<FileSystemComponent>(bigPackDiskUid);
            Assert.That(fsSys.TryGetFile<RecipeBlueprintFile>(bigPackDiskUid, "/blueprints/mega_engineering_pack.lathe", out var bigBp, bigFs), Is.True);

            Assert.That(secBp!.IsEncrypted, Is.True, "BaseRecipeDisk security_gear.lathe must be encrypted.");
            Assert.That(secBp.Size, Is.EqualTo(16896), "4-recipe DRM pack size must include per-recipe + pointUsage scaling.");
            Assert.That(doorBp!.Size, Is.EqualTo(2560), "Single-recipe blueprint must be 2.5 KB.");
            Assert.That(bigBp!.Size, Is.EqualTo(20992), "10-recipe multi-blueprint file must be 20.5 KB.");
            Assert.That(bigBp.Size, Is.GreaterThan(secBp.Size));
            Assert.That(secBp.Size, Is.GreaterThan(doorBp.Size));

            var freeBytes = latheFs!.MaxStorageBytes - latheFs.UsedStorageBytes;
            Assert.That(latheFs.MaxStorageBytes, Is.EqualTo(241664), "Autolathe drive must have compact 236 KB capacity.");
            Assert.That(freeBytes, Is.GreaterThanOrEqualTo(doorBp.Size), "Autolathe must still have enough free space to save a few small blueprints.");
            Assert.That(freeBytes, Is.LessThan(bigBp.Size), "Autolathe limited memory must not have enough free space for a large 10-recipe pack.");

            // Encrypted recipe files can NEVER be written directly to any ComputerHardDriveComponent!
            var encryptedClone = (RecipeBlueprintFile) secBp.Clone();
            Assert.That(fsSys.TryWriteFile(latheDriveUid, encryptedClone, latheFs), Is.False,
                "SharedFileSystemSystem must block storing encrypted recipe files on computer hard drives.");
        });

        // 2) Open Autolathe UI, insert BaseRecipeDisk (encrypted, 30 LP), verify INSTANT load + NO Save/Decrypt buttons!
        await Server.WaitPost(() =>
        {
            var itemSlots = Server.System<ItemSlotsSystem>();
            Assert.That(itemSlots.TryInsert(latheUid, SharedLatheSystem.diskSlot, secDiskUid, null), Is.True);
            Server.System<OxydComputerSystem>().OpenComputerForActor(latheCompUid, SPlayer);
        });
        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var latheUi = GetControlFromChildren<LatheControlUiControl>(_ => true, win);
            Assert.That(latheUi, Is.Not.Null);

            var labels = new List<Content.Client.UserInterface.Controls.ContentLabel>();
            CollectDescendants(latheUi!, labels);
            Assert.That(labels.Any(l => l.Name == "LatheMemoryLabel" && (l.Text ?? string.Empty).Contains("free")), Is.True,
                "Autolathe UI must display remaining lathe memory.");
            Assert.That(labels.Any(l => (l.Text ?? string.Empty).Contains("DISK") && (l.Text ?? string.Empty).Contains("security_gear.lathe") && (l.Text ?? string.Empty).Contains("30 LP")), Is.True,
                "Inserting an encrypted recipe disk must instantly load its designs into the Autolathe UI.");

            var buttons = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(latheUi!, buttons);
            Assert.That(buttons.Any(b => b.Text == "Decrypt"), Is.False,
                "Encrypted recipe disks must not offer a free Decrypt button.");
            Assert.That(buttons.Any(b => b.Name == "LatheSaveBlueprintButton" && b.VisibleInTree), Is.False,
                "Encrypted disk recipes must NOT show a Save button.");
            Assert.That(buttons.Any(b => b.Name == "LatheSaveDiskButton" && b.VisibleInTree), Is.False,
                "Save Unencrypted button must remain hidden when disk only holds encrypted blueprints.");
        });

        // 3) Print DRMHud (4 LP) and DRMCuffs (2 LP) directly off BaseRecipeDisk and verify exact use subtraction (30 -> 26 -> 24)!
        await SendBui(ProgramUiKey.Key,
            new LatheControlActionMessage(LatheControlAction.PrintRecipe, "DRMHud", "/blueprints/security_gear.lathe", 1, fromDisk: true),
            FromServer(latheProgUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var fsSys = Server.System<SharedFileSystemSystem>();
            var secFs = SEntMan.GetComponent<FileSystemComponent>(secDiskUid);
            Assert.That(fsSys.TryGetFile<RecipeBlueprintFile>(secDiskUid, "/blueprints/security_gear.lathe", out var secBp, secFs), Is.True);
            Assert.That(secBp!.RemainingUses, Is.EqualTo(26), "Printing DRMHud (4 LP) from encrypted disk must subtract 4 uses (30 -> 26).");
        });

        await SendBui(ProgramUiKey.Key,
            new LatheControlActionMessage(LatheControlAction.PrintRecipe, "DRMCuffs", "/blueprints/security_gear.lathe", 1, fromDisk: true),
            FromServer(latheProgUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var fsSys = Server.System<SharedFileSystemSystem>();
            var secFs = SEntMan.GetComponent<FileSystemComponent>(secDiskUid);
            Assert.That(fsSys.TryGetFile<RecipeBlueprintFile>(secDiskUid, "/blueprints/security_gear.lathe", out var secBp, secFs), Is.True);
            Assert.That(secBp!.RemainingUses, Is.EqualTo(24), "Printing DRMCuffs (2 LP) from encrypted disk must subtract 2 uses (26 -> 24).");
        });

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var latheUi = GetControlFromChildren<LatheControlUiControl>(_ => true, win);
            var labels = new List<Content.Client.UserInterface.Controls.ContentLabel>();
            CollectDescendants(latheUi!, labels);
            Assert.That(labels.Any(l => (l.Text ?? string.Empty).Contains("security_gear.lathe") && (l.Text ?? string.Empty).Contains("24 LP")), Is.True,
                "Autolathe UI must immediately update remaining uses after printing from encrypted disk.");
        });

        // 4) Eject BaseRecipeDisk via UI action, insert OxydRecipeDiskEncrypted (5 uses), exhaust uses 5 -> 0 & verify block at 0!
        await SendBui(ProgramUiKey.Key, new LatheControlActionMessage(LatheControlAction.EjectDisk), FromServer(latheProgUid));
        await RunTicks(4);

        await Server.WaitAssertion(() =>
        {
            var latheComp = SEntMan.GetComponent<LatheComponent>(latheUid);
            Assert.That(latheComp.diskSlot?.Item, Is.Null, "EjectDisk UI action must eject the disk from lathe slot.");

            var itemSlots = Server.System<ItemSlotsSystem>();
            Assert.That(itemSlots.TryInsert(latheUid, SharedLatheSystem.diskSlot, hudDiskUid, null), Is.True);
        });
        await RunTicks(8);

        // Attempt to copy/import encrypted disk into lathe memory & decrypt — BOTH MUST BE BLOCKED!
        await SendBui(ProgramUiKey.Key,
            new LatheControlActionMessage(LatheControlAction.ImportDiskBlueprints, string.Empty, "/blueprints/security_hud.lathe"),
            FromServer(latheProgUid));
        await SendBui(ProgramUiKey.Key,
            new LatheControlActionMessage(LatheControlAction.ImportDiskBlueprints),
            FromServer(latheProgUid));
        await SendBui(ProgramUiKey.Key,
            new LatheControlActionMessage(LatheControlAction.DecryptBlueprint, "ClothingEyesHudSecurity", "/blueprints/security_hud.lathe"),
            FromServer(latheProgUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var fsSys = Server.System<SharedFileSystemSystem>();
            var latheFs = SEntMan.GetComponent<FileSystemComponent>(latheDriveUid);
            Assert.That(latheFs.Files.ContainsKey("/blueprints/security_hud.lathe"), Is.False,
                "Encrypted recipe file must NEVER be copied into lathe memory.");

            var hudFs = SEntMan.GetComponent<FileSystemComponent>(hudDiskUid);
            Assert.That(fsSys.TryGetFile<RecipeBlueprintFile>(hudDiskUid, "/blueprints/security_hud.lathe", out var hudBp, hudFs), Is.True);
            Assert.That(hudBp!.IsEncrypted, Is.True, "DecryptBlueprint must not strip encryption from encrypted recipe disk.");
        });

        // Exhaust all 5 uses of OxydRecipeDiskEncrypted
        for (var i = 0; i < 5; i++)
        {
            await SendBui(ProgramUiKey.Key,
                new LatheControlActionMessage(LatheControlAction.PrintRecipe, "ClothingEyesHudSecurity", "/blueprints/security_hud.lathe", 1, fromDisk: true),
                FromServer(latheProgUid));
        }
        await RunTicks(8);

        var queueBeforeRefusal = 0;
        await Server.WaitAssertion(() =>
        {
            var fsSys = Server.System<SharedFileSystemSystem>();
            var hudFs = SEntMan.GetComponent<FileSystemComponent>(hudDiskUid);
            Assert.That(fsSys.TryGetFile<RecipeBlueprintFile>(hudDiskUid, "/blueprints/security_hud.lathe", out var hudBp, hudFs), Is.True);
            Assert.That(hudBp!.RemainingUses, Is.EqualTo(0), "Printing 5 times from OxydRecipeDiskEncrypted must exhaust all 5 uses.");

            var latheComp = SEntMan.GetComponent<LatheComponent>(latheUid);
            queueBeforeRefusal = latheComp.Queue.Sum(b => Math.Max(0, b.ItemsRequested - b.ItemsPrinted));
        });

        // 6th print attempt at 0 uses must be refused!
        await SendBui(ProgramUiKey.Key,
            new LatheControlActionMessage(LatheControlAction.PrintRecipe, "ClothingEyesHudSecurity", "/blueprints/security_hud.lathe", 1, fromDisk: true),
            FromServer(latheProgUid));
        await RunTicks(4);

        await Server.WaitAssertion(() =>
        {
            var latheComp = SEntMan.GetComponent<LatheComponent>(latheUid);
            var queueAfterRefusal = latheComp.Queue.Sum(b => Math.Max(0, b.ItemsRequested - b.ItemsPrinted));
            Assert.That(queueAfterRefusal, Is.LessThanOrEqualTo(queueBeforeRefusal),
                "Autolathe must refuse printing from an encrypted disk with 0 remaining uses.");
        });

        // Cancel remaining queue cleanly so we can test unencrypted disks & PDA FileBrowser copy protection
        await SendBui(ProgramUiKey.Key, new LatheControlActionMessage(LatheControlAction.CancelAll), FromServer(latheProgUid));
        await SendBui(ProgramUiKey.Key, new LatheControlActionMessage(LatheControlAction.EjectDisk), FromServer(latheProgUid));
        await RunTicks(6);

        // 5) Insert unencrypted OxydDataDiskEngineering: instant load + direct print + secondary save (2.5 KB fits!)
        await Server.WaitAssertion(() =>
        {
            var itemSlots = Server.System<ItemSlotsSystem>();
            Assert.That(itemSlots.TryInsert(latheUid, SharedLatheSystem.diskSlot, engDiskUid, null), Is.True);
        });
        await RunTicks(8);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var latheUi = GetControlFromChildren<LatheControlUiControl>(_ => true, win);
            var buttons = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(latheUi!, buttons);
            Assert.That(buttons.Any(b => b.Name == "LatheSaveBlueprintButton" && b.VisibleInTree), Is.True,
                "Unencrypted disk blueprints must expose a secondary Save button.");
            Assert.That(buttons.Any(b => b.Name == "LatheSaveDiskButton" && b.VisibleInTree), Is.True,
                "Save Unencrypted button must be visible for unencrypted disks.");
        });

        // Print directly from disk BEFORE saving to memory
        await SendBui(ProgramUiKey.Key,
            new LatheControlActionMessage(LatheControlAction.PrintRecipe, "DoorElectronics", "/blueprints/door_electronics.lathe", 1, fromDisk: true),
            FromServer(latheProgUid));
        await RunTicks(4);

        await Server.WaitAssertion(() =>
        {
            var latheComp = SEntMan.GetComponent<LatheComponent>(latheUid);
            Assert.That(latheComp.CurrentRecipe?.Id, Is.EqualTo("DoorElectronics"),
                "Unencrypted disk designs must print directly from disk without needing to save to memory first.");
        });

        // Secondary action: save /blueprints/door_electronics.lathe (2.5 KB) to lathe memory
        await SendBui(ProgramUiKey.Key,
            new LatheControlActionMessage(LatheControlAction.ImportDiskBlueprints, "DoorElectronics", "/blueprints/door_electronics.lathe"),
            FromServer(latheProgUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var latheFs = SEntMan.GetComponent<FileSystemComponent>(latheDriveUid);
            Assert.That(latheFs.Files.ContainsKey("/blueprints/door_electronics.lathe"), Is.True,
                "Saving a small unencrypted blueprint file (2.5 KB) to lathe memory must succeed.");
        });

        // 6) Swap to 10-recipe multi-blueprint disk (20.5 KB): prints directly off disk, but saving to tight lathe memory is refused!
        await SendBui(ProgramUiKey.Key, new LatheControlActionMessage(LatheControlAction.CancelAll), FromServer(latheProgUid));
        await SendBui(ProgramUiKey.Key, new LatheControlActionMessage(LatheControlAction.EjectDisk), FromServer(latheProgUid));
        await RunTicks(4);

        await Server.WaitAssertion(() =>
        {
            var itemSlots = Server.System<ItemSlotsSystem>();
            Assert.That(itemSlots.TryInsert(latheUid, SharedLatheSystem.diskSlot, bigPackDiskUid, null), Is.True);
        });
        await RunTicks(8);

        await SendBui(ProgramUiKey.Key,
            new LatheControlActionMessage(LatheControlAction.PrintRecipe, "APCElectronics", "/blueprints/mega_engineering_pack.lathe", 1, fromDisk: true),
            FromServer(latheProgUid));
        await SendBui(ProgramUiKey.Key,
            new LatheControlActionMessage(LatheControlAction.ImportDiskBlueprints, "APCElectronics", "/blueprints/mega_engineering_pack.lathe"),
            FromServer(latheProgUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var latheComp = SEntMan.GetComponent<LatheComponent>(latheUid);
            Assert.That(latheComp.CurrentRecipe?.Id, Is.EqualTo("APCElectronics"),
                "Large multi-blueprint disk must still print directly from disk even when too big for lathe memory.");

            var latheFs = SEntMan.GetComponent<FileSystemComponent>(latheDriveUid);
            Assert.That(latheFs.Files.ContainsKey("/blueprints/mega_engineering_pack.lathe"), Is.False,
                "Large multi-blueprint file (20.5 KB) must be rejected when lathe memory has insufficient free space.");

            var progComp = SEntMan.GetComponent<LatheControlProgramComponent>(latheProgUid);
            Assert.That(progComp.StatusMessage, Does.Contain("Insufficient lathe memory").IgnoreCase);
        });

        await Client.WaitPost(() => GetWindow<ComputerDisplayWindow>().Close());
        await RunTicks(4);

        // 7) Verify PDA FileBrowser ALSO hides copy button & blocks copying encrypted recipe disk files!
        await Server.WaitAssertion(() =>
        {
            var itemSlots = Server.System<ItemSlotsSystem>();
            Assert.That(itemSlots.TryInsert(pdaUid, PdaComponent.PdaDiskSlotId, secDiskUid, null), Is.True);

            var compSys = Server.System<OxydComputerSystem>();
            Assert.That(compSys.TryLaunchExecutable(pdaCompUid, "/bin/file_browser.nte", out var launched, out _, SPlayer), Is.True);
            pdaBrowserProg = launched!.Value;
            compSys.SwitchActiveProgram(pdaCompUid, pdaBrowserProg, actor: SPlayer);
            compSys.OpenComputerForActor(pdaCompUid, SPlayer);
        });
        await RunTicks(8);

        await SendBui(ProgramUiKey.Key, new FileBrowserActionMessage(FileBrowserAction.ChangeDirectory, "/disk/"), FromServer(pdaBrowserProg));
        await RunTicks(6);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var fbUi = GetControlFromChildren<FileBrowserUiControl>(_ => true, win);
            Assert.That(fbUi, Is.Not.Null);

            var labels = new List<Content.Client.UserInterface.Controls.ContentLabel>();
            CollectDescendants(fbUi!, labels);
            Assert.That(labels.Any(l => (l.Text ?? string.Empty).Contains("security_gear.lathe") && (l.Text ?? string.Empty).Contains("[ENC]")), Is.True,
                "FileBrowser must mark encrypted recipe files with [ENC].");

            var buttons = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(fbUi!, buttons);
            Assert.That(buttons.Any(b => b.Text == "->HDD"), Is.False,
                "FileBrowser must NOT display ->HDD copy button for encrypted recipe files.");
        });

        // Even sending CopyToInternal directly over the network must be refused!
        await SendBui(ProgramUiKey.Key, new FileBrowserActionMessage(FileBrowserAction.CopyToInternal, "/blueprints/security_gear.lathe"), FromServer(pdaBrowserProg));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var fsSys = Server.System<SharedFileSystemSystem>();
            Assert.That(fsSys.TryFindFileSystem(pdaCompUid, out _, out var pdaFs), Is.True);
            Assert.That(pdaFs!.Files.ContainsKey("/blueprints/security_gear.lathe"), Is.False,
                "FileBrowser CopyToInternal must refuse copying encrypted recipe files to PDA hard drive.");
        });

        await Client.WaitPost(() => GetWindow<ComputerDisplayWindow>().Close());
        await RunTicks(4);
    }

    [Test]
    public async Task TestAutolatheMaterialEjectRemoteDiskEjectAndMixedDiskSaveUnencrypted()
    {
        await SpawnTarget("Autolathe");
        var latheUid = ToServer(Target)!.Value;
        EntityUid latheCompUid = default;
        EntityUid latheProgUid = default;
        EntityUid latheDriveUid = default;
        EntityUid handcuffsDiskUid = default;
        EntityUid engDiskUid = default;
        EntityUid mixedDiskUid = default;
        EntityUid remoteDiskUid = default;

        await Server.WaitAssertion(() =>
        {
            var coords = SEntMan.GetComponent<TransformComponent>(latheUid).Coordinates;
            Server.System<PowerReceiverSystem>().SetNeedsPower(latheUid, false);
            latheCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(latheUid).ComputerEntity!.Value;

            var matSys = Server.System<MaterialStorageSystem>();
            Assert.That(matSys.TryChangeMaterialAmount(latheUid, "Steel", 500), Is.True);
            Assert.That(matSys.TryChangeMaterialAmount(latheUid, "Glass", 50), Is.True);

            handcuffsDiskUid = SEntMan.SpawnAtPosition("OxydRecipeDisk", coords);
            engDiskUid = SEntMan.SpawnAtPosition("OxydDataDiskEngineering", coords);
            mixedDiskUid = SEntMan.SpawnAtPosition("OxydDataDiskBlank", coords);
            remoteDiskUid = SEntMan.SpawnAtPosition("BaseRecipeDisk", coords);

            // Populate mixedDiskUid with 1 unencrypted copyable file + 3 protected files (encrypted, drm-locked, limited-use)
            var fsSys = Server.System<SharedFileSystemSystem>();
            var mixedFs = SEntMan.GetComponent<FileSystemComponent>(mixedDiskUid);
            Assert.That(fsSys.TryWriteFile(mixedDiskUid, new RecipeBlueprintFile
            {
                Name = "custom_unencrypted",
                Extension = "lathe",
                Directory = "/blueprints/",
                Recipe = "DoorElectronics",
                IsEncrypted = false,
                DrmLocked = false,
                RemainingUses = null
            }, mixedFs), Is.True);
            Assert.That(fsSys.TryWriteFile(mixedDiskUid, new RecipeBlueprintFile
            {
                Name = "mixed_encrypted",
                Extension = "lathe",
                Directory = "/blueprints/",
                Recipe = "ClothingEyesHudSecurity",
                IsEncrypted = true,
                DrmLocked = true,
                RemainingUses = 10
            }, mixedFs), Is.True);
            Assert.That(fsSys.TryWriteFile(mixedDiskUid, new RecipeBlueprintFile
            {
                Name = "mixed_drm",
                Extension = "lathe",
                Directory = "/blueprints/",
                Recipe = "DRMCuffs",
                IsEncrypted = false,
                DrmLocked = true,
                RemainingUses = null
            }, mixedFs), Is.True);
            Assert.That(fsSys.TryWriteFile(mixedDiskUid, new RecipeBlueprintFile
            {
                Name = "mixed_limited",
                Extension = "lathe",
                Directory = "/blueprints/",
                Recipe = "Handcuffs",
                IsEncrypted = false,
                DrmLocked = false,
                RemainingUses = 4
            }, mixedFs), Is.True);
        });

        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            var fsSys = Server.System<SharedFileSystemSystem>();
            var cComp = SEntMan.GetComponent<OxydComputerComponent>(latheCompUid);
            Assert.That(cComp.IsBooted, Is.True);
            latheProgUid = cComp.ActiveProgram!.Value;
            Assert.That(fsSys.TryFindFileSystem(latheCompUid, out var drive, out _), Is.True);
            latheDriveUid = drive!.Value;

            // Verify OxydDataDiskEngineering only carries its 3 unencrypted engineering files (no inherited encrypted file).
            var engFs = SEntMan.GetComponent<FileSystemComponent>(engDiskUid);
            Assert.That(engFs.Files.Count, Is.EqualTo(3), "OxydDataDiskEngineering must not inherit BaseRecipeDisk encrypted files.");

            Server.System<OxydComputerSystem>().OpenComputerForActor(latheCompUid, SPlayer);
        });
        await RunTicks(8);

        // 1) Scroll wheel on Steel MaterialIconTile (1 -> 3 sheets), verify plain sheet count ("5") & click ejects 3 Steel sheets (300 units)!
        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var latheUi = GetControlFromChildren<LatheControlUiControl>(_ => true, win);
            Assert.That(latheUi, Is.Not.Null);

            // Verify scrollable recipe list is clearly denoted and window expands for up to 8 recipes without clipping
            var recipeScroll = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentScrollContainer>(
                s => s.Name == "LatheRecipeScroll", latheUi!);
            Assert.That(recipeScroll, Is.Not.Null);
            Assert.That(recipeScroll!.ShowScrollVisuals, Is.True);
            Assert.That(recipeScroll.AlwaysShowBackground, Is.True);
            Assert.That(latheUi!.DesiredExpandedWindowHeight, Is.GreaterThanOrEqualTo(760f),
                "Autolathe UI must request expanded height for up to 8 recipes.");
            Assert.That(win.MinSize.X, Is.GreaterThanOrEqualTo(700f),
                "Autolathe window must enforce a proper minimum width so controls are not cramped.");
            Assert.That(win.MinSize.Y, Is.GreaterThanOrEqualTo(600f),
                "Autolathe window must enforce a proper minimum height so controls are not smushed.");
            Assert.That(win.SetWidth, Is.GreaterThanOrEqualTo(700f),
                "Autolathe window must open at its full proper width.");
            Assert.That(win.SetHeight, Is.GreaterThanOrEqualTo(680f),
                "Autolathe window must auto-expand beyond its base height when multiple recipes are loaded.");

            var searchInput = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLineEdit>(
                e => e.Name == "LatheRecipeSearch", latheUi!);
            Assert.That(searchInput, Is.Not.Null);
            Assert.That(searchInput!.VisibleInTree, Is.True);
            Assert.That(searchInput.Size.X, Is.GreaterThanOrEqualTo(200f),
                "Autolathe recipe search bar must not be horizontally smushed.");
            Assert.That(searchInput.Size.Y, Is.GreaterThanOrEqualTo(24f),
                "Autolathe recipe search bar must not be vertically smushed.");

            AssertAllVisibleChildrenWithinViewport(win, latheUi!, "Autolathe UI");

            var tiles = new List<MaterialIconTile>();
            CollectDescendants(latheUi!, tiles);
            var steelTile = tiles.FirstOrDefault(t => t.MaterialId == "Steel");
            Assert.That(steelTile, Is.Not.Null, "Steel MaterialIconTile must be present in Autolathe UI.");
            Assert.That(steelTile!.CountText, Is.EqualTo("5"), "Material tile must display sheet count (5), not raw units (500).");
            Assert.That(steelTile.SelectedSheets, Is.EqualTo(1));
            Assert.That(steelTile.MaxEjectable, Is.EqualTo(5));

            // Scroll wheel up twice -> select 3 sheets to eject
            var wheelUp = new GUIMouseWheelEventArgs(new Vector2(0f, 1f), steelTile, new Vector2(10, 10), default, new Vector2(10, 10), new Vector2(10, 10));
            steelTile.RelayMouseWheel(wheelUp);
            steelTile.RelayMouseWheel(new GUIMouseWheelEventArgs(new Vector2(0f, 1f), steelTile, new Vector2(10, 10), default, new Vector2(10, 10), new Vector2(10, 10)));
            Assert.That(steelTile.SelectedSheets, Is.EqualTo(3), "Mouse wheel up twice must adjust selected eject amount to 3 sheets.");

            var downArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, default, false, new Vector2(10, 10), new Vector2(10, 10));
            var upArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, default, false, new Vector2(10, 10), new Vector2(10, 10));
            steelTile.RelayKeyBindDown(downArgs);
            steelTile.RelayKeyBindUp(upArgs);
        });
        await RunTicks(8);

        await Server.WaitAssertion(() =>
        {
            var matSys = Server.System<MaterialStorageSystem>();
            var steelAfter = matSys.GetMaterialAmount(latheUid, "Steel");
            Assert.That(steelAfter, Is.EqualTo(200), "Clicking Steel tile after scrolling to 3 sheets must eject 3 sheets (300 units: 500 -> 200).");

            var xformSys = Server.System<SharedTransformSystem>();
            var lathePos = xformSys.GetMapCoordinates(latheUid);
            var spawnedNearLathe = false;
            var sheetQuery = SEntMan.EntityQueryEnumerator<Content.Shared.Stacks.StackComponent, MetaDataComponent, TransformComponent>();
            while (sheetQuery.MoveNext(out var sheetUid, out var stackComp, out var meta, out var sheetXform))
            {
                if (meta.EntityPrototype?.ID != "SheetSteel1")
                    continue;
                if ((xformSys.GetMapCoordinates(sheetUid, sheetXform).Position - lathePos.Position).Length() < 1.5f &&
                    stackComp.Count == 3)
                {
                    spawnedNearLathe = true;
                    break;
                }
            }
            Assert.That(spawnedNearLathe, Is.True, "Clicking Steel tile after scrolling to 3 must physically spawn a 3-sheet Steel stack at the Autolathe.");
        });

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var latheUi = GetControlFromChildren<LatheControlUiControl>(_ => true, win);
            var labels = new List<Content.Client.UserInterface.Controls.ContentLabel>();
            CollectDescendants(latheUi!, labels);
            Assert.That(labels.Any(l => l.VisibleInTree && (l.Text ?? string.Empty).Contains("Ejected", StringComparison.OrdinalIgnoreCase)), Is.False,
                "Autolathe menu must not display action messages like Ejected.");
        });

        // Clicking Glass tile with only 50 units (< 1 sheet) must not crash or deduct partial material and should show feedback
        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var latheUi = GetControlFromChildren<LatheControlUiControl>(_ => true, win);
            var tiles = new List<MaterialIconTile>();
            CollectDescendants(latheUi!, tiles);
            var glassTile = tiles.FirstOrDefault(t => t.MaterialId == "Glass");
            Assert.That(glassTile, Is.Not.Null);

            var downArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, default, false, new Vector2(10, 10), new Vector2(10, 10));
            var upArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, default, false, new Vector2(10, 10), new Vector2(10, 10));
            glassTile!.RelayKeyBindDown(downArgs);
            glassTile.RelayKeyBindUp(upArgs);
        });
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var matSys = Server.System<MaterialStorageSystem>();
            Assert.That(matSys.GetMaterialAmount(latheUid, "Glass"), Is.EqualTo(50), "Less than 1 sheet of Glass must stay in storage.");
        });

        // 2) Verify OxydRecipeDisk (10 uses) is copy-protected (no Save Unencrypted button, refused on import)
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<ItemSlotsSystem>().TryInsert(latheUid, SharedLatheSystem.diskSlot, handcuffsDiskUid, null), Is.True);
        });
        await RunTicks(8);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var latheUi = GetControlFromChildren<LatheControlUiControl>(_ => true, win);
            var labels = new List<Content.Client.UserInterface.Controls.ContentLabel>();
            CollectDescendants(latheUi!, labels);
            Assert.That(labels.Any(l => l.Name == "LatheDiskLabel" && (l.Text ?? string.Empty).Contains("Encrypted", StringComparison.OrdinalIgnoreCase)), Is.False,
                "Autolathe disk label must not display [Encrypted — Disk-Only].");

            var buttons = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(latheUi!, buttons);
            Assert.That(buttons.Any(b => b.Name == "LatheSaveDiskButton" && b.VisibleInTree), Is.False,
                "Limited-use OxydRecipeDisk must NOT expose Save Unencrypted.");
            Assert.That(buttons.Any(b => b.Name == "LatheSaveBlueprintButton" && b.VisibleInTree), Is.False,
                "Limited-use OxydRecipeDisk must NOT expose per-card Save button.");
        });

        await SendBui(ProgramUiKey.Key, new LatheControlActionMessage(LatheControlAction.ImportDiskBlueprints), FromServer(latheProgUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var latheFs = SEntMan.GetComponent<FileSystemComponent>(latheDriveUid);
            Assert.That(latheFs.Files.ContainsKey("/blueprints/handcuffs.lathe"), Is.False,
                "Limited-use OxydRecipeDisk must never be copied into lathe memory.");
            Assert.That(Server.System<ItemSlotsSystem>().TryEject(latheUid, SharedLatheSystem.diskSlot, null, out _), Is.True);
            Assert.That(Server.System<ItemSlotsSystem>().TryInsert(latheUid, SharedLatheSystem.diskSlot, mixedDiskUid, null), Is.True);
        });
        await RunTicks(8);

        // 3) Mixed disk (1 unencrypted + 3 encrypted/DRM/limited): clicking "Save Unencrypted" saves ONLY the unencrypted file!
        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var latheUi = GetControlFromChildren<LatheControlUiControl>(_ => true, win);
            var buttons = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(latheUi!, buttons);

            var saveUnencBtn = buttons.FirstOrDefault(b => b.Name == "LatheSaveDiskButton" && b.VisibleInTree);
            Assert.That(saveUnencBtn, Is.Not.Null, "Save Unencrypted must be visible when disk has an unencrypted blueprint.");

            // Only the 1 unencrypted card should have a per-card Save button
            var perCardSaveCount = buttons.Count(b => b.Name == "LatheSaveBlueprintButton" && b.VisibleInTree);
            Assert.That(perCardSaveCount, Is.EqualTo(1), "Only the unencrypted card on a mixed disk may show a Save button.");

            var downArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, default, false, new Vector2(10, 10), new Vector2(10, 10));
            var upArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, default, false, new Vector2(10, 10), new Vector2(10, 10));
            saveUnencBtn!.RelayKeyBindDown(downArgs);
            saveUnencBtn.RelayKeyBindUp(upArgs);
        });
        await RunTicks(8);

        await Server.WaitAssertion(() =>
        {
            var latheFs = SEntMan.GetComponent<FileSystemComponent>(latheDriveUid);
            Assert.That(latheFs.Files.ContainsKey("/blueprints/custom_unencrypted.lathe"), Is.True,
                "Save Unencrypted must save the unencrypted blueprint file.");
            Assert.That(latheFs.Files.ContainsKey("/blueprints/mixed_encrypted.lathe"), Is.False,
                "Save Unencrypted must NEVER save encrypted blueprint files.");
            Assert.That(latheFs.Files.ContainsKey("/blueprints/mixed_drm.lathe"), Is.False,
                "Save Unencrypted must NEVER save DRM-locked blueprint files.");
            Assert.That(latheFs.Files.ContainsKey("/blueprints/mixed_limited.lathe"), Is.False,
                "Save Unencrypted must NEVER save finite-use blueprint files.");
        });

        await Client.WaitPost(() => GetWindow<ComputerDisplayWindow>().Close());
        await RunTicks(4);

        // 4) Remote screen disk ejection: when actor is far away (> 10 tiles), EjectDisk must drop disk AT THE LATHE, NOT teleport to player hand!
        EntityCoordinates origPlayerCoords = default;
        await Server.WaitAssertion(() =>
        {
            var xformSys = Server.System<SharedTransformSystem>();
            var itemSlots = Server.System<ItemSlotsSystem>();
            Assert.That(itemSlots.TryEject(latheUid, SharedLatheSystem.diskSlot, null, out _), Is.True);
            Assert.That(itemSlots.TryInsert(latheUid, SharedLatheSystem.diskSlot, remoteDiskUid, null), Is.True);

            origPlayerCoords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;

            // Move player 15 tiles away from the Autolathe (simulating remote screen operation)
            var latheCoords = SEntMan.GetComponent<TransformComponent>(latheUid).Coordinates;
            xformSys.SetCoordinates(SPlayer, latheCoords.Offset(new Vector2(15f, 0f)));
        });
        await RunTicks(4);

        // Send EjectDisk as SPlayer (same packet path as clicking Eject Disk through a remote screen)
        await Server.WaitAssertion(() =>
        {
            var compSys = Server.System<OxydComputerSystem>();
            var pkt = new Content.Shared._Oxyd.Computers.Packets.ComputerPacket("eject_disk");
            pkt.SetReference("Actor", SPlayer);
            Assert.That(compSys.TryInvokeDeviceLinkPort(latheCompUid, "AutolatheCommandSink", isHostPort: true, "eject_disk", string.Empty, out _, SPlayer, pkt), Is.True);
        });
        await RunTicks(4);

        await Server.WaitAssertion(() =>
        {
            var xformSys = Server.System<SharedTransformSystem>();
            var handsSys = Server.System<Content.Server.Hands.Systems.HandsSystem>();
            var latheComp = SEntMan.GetComponent<LatheComponent>(latheUid);
            Assert.That(latheComp.diskSlot?.Item, Is.Null, "Remote EjectDisk must eject the disk from the Autolathe.");

            var heldByPlayer = handsSys.EnumerateHeld(SPlayer).Contains(remoteDiskUid);
            Assert.That(heldByPlayer, Is.False, "Remote EjectDisk must NOT teleport the disk into the remote player's hand.");

            var lathePos = xformSys.GetMapCoordinates(latheUid).Position;
            var diskPos = xformSys.GetMapCoordinates(remoteDiskUid).Position;
            var playerPos = xformSys.GetMapCoordinates(SPlayer).Position;
            Assert.That((diskPos - lathePos).Length(), Is.LessThan(1.5f), "Remotely ejected disk must drop right next to the Autolathe.");
            Assert.That((diskPos - playerPos).Length(), Is.GreaterThan(10f), "Remotely ejected disk must stay at the Autolathe, far from the remote player.");

            // Now move player back within interaction range, re-insert disk, and verify local eject DOES put disk into hand!
            xformSys.SetCoordinates(SPlayer, origPlayerCoords);
            Assert.That(Server.System<ItemSlotsSystem>().TryInsert(latheUid, SharedLatheSystem.diskSlot, remoteDiskUid, null), Is.True);
        });
        await RunTicks(4);

        await Server.WaitAssertion(() =>
        {
            var compSys = Server.System<OxydComputerSystem>();
            var pkt = new Content.Shared._Oxyd.Computers.Packets.ComputerPacket("eject_disk");
            pkt.SetReference("Actor", SPlayer);
            Assert.That(compSys.TryInvokeDeviceLinkPort(latheCompUid, "AutolatheCommandSink", isHostPort: true, "eject_disk", string.Empty, out _, SPlayer, pkt), Is.True);
        });
        await RunTicks(4);

        await Server.WaitAssertion(() =>
        {
            var handsSys = Server.System<Content.Server.Hands.Systems.HandsSystem>();
            Assert.That(handsSys.EnumerateHeld(SPlayer).Contains(remoteDiskUid), Is.True,
                "Local in-range EjectDisk should place the ejected disk into the player's hand.");
        });
    }

    [Test]
    public async Task TestAirAlarmApcUnlockedLinkingPdaAccessAndLatheSetRecipeMemory()
    {
        EntityUid apcUid = default;
        EntityUid alarmUid = default;
        EntityUid ventUid = default;
        EntityUid apcButtonUid = default;
        EntityUid latheUid = default;
        EntityUid latheCompUid = default;
        EntityUid latheProgUid = default;
        EntityUid latheDriveUid = default;
        EntityUid lathePrintBtnUid = default;
        EntityUid latheSetRecipeBtnUid = default;
        EntityUid recipeDiskUid = default;
        EntityUid multitoolUid = default;
        EntityUid pdaUid = default;

        await Server.WaitAssertion(() =>
        {
            var coords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            var xform = Server.System<SharedTransformSystem>();
            var handsSys = Server.System<Content.Server.Hands.Systems.HandsSystem>();
            var devLink = Server.System<Content.Shared.DeviceLinking.Systems.DeviceLinkSystem>();
            var accessReader = Server.System<Content.Shared.Access.Systems.AccessReaderSystem>();
            var lockSys = Server.System<LockSystem>();
            var matSys = Server.System<SharedMaterialStorageSystem>();
            var compSys = Server.System<OxydComputerSystem>();
            var powerSys = Server.System<PowerReceiverSystem>();

            apcUid = SEntMan.SpawnEntity("APCBasic", coords);
            alarmUid = SEntMan.SpawnEntity("AirAlarm", coords);
            ventUid = SEntMan.SpawnEntity("GasVentPump", coords);
            apcButtonUid = SEntMan.SpawnEntity("SignalButton", coords);
            latheUid = SEntMan.SpawnEntity("Autolathe", coords);
            lathePrintBtnUid = SEntMan.SpawnEntity("SignalButton", coords);
            latheSetRecipeBtnUid = SEntMan.SpawnEntity("SignalButton", coords);
            recipeDiskUid = SEntMan.SpawnEntity("OxydRecipeDisk", coords);
            multitoolUid = SEntMan.SpawnEntity("Multitool", coords);

            powerSys.SetNeedsPower(alarmUid, false);
            powerSys.SetNeedsPower(latheUid, false);

            // 1) Verify holding a PDA with an ID card inside grants access via AccessReaderSystem!
            pdaUid = SEntMan.SpawnEntity("CaptainPDA", coords);
            Assert.That(handsSys.TryPickupAnyHand(SPlayer, pdaUid), Is.True);
            Assert.That(accessReader.IsAllowed(SPlayer, alarmUid), Is.True,
                "Holding/wearing a PDA with an authorized ID card inside must grant AirAlarm AccessReader access.");
            Assert.That(accessReader.IsAllowed(SPlayer, apcUid), Is.True,
                "Holding/wearing a PDA with an authorized ID card inside must grant APC AccessReader access.");

            // EVEN WHILE holding an authorized CaptainPDA, a physically LOCKED APC or AirAlarm MUST refuse linking!
            Assert.That(lockSys.IsLocked(apcUid), Is.True);
            Assert.That(lockSys.IsLocked(alarmUid), Is.True);
            Assert.That(devLink.ToggleLink(SPlayer, apcButtonUid, apcUid, "Pressed", "MachineCommandReceiver"), Is.False,
                "Locked APC must refuse DeviceLink even when the user wears/holds an authorized ID.");

            var interSysEarly = Server.System<Content.Shared.Interaction.SharedInteractionSystem>();
            handsSys.AddHand(SPlayer, "second_hand", Content.Shared.Hands.Components.HandLocation.Left);
            Assert.That(handsSys.TryPickupAnyHand(SPlayer, multitoolUid), Is.True);
            interSysEarly.InteractUsing(SPlayer, multitoolUid, alarmUid, coords);
            var cfgLocked = SEntMan.GetComponent<Content.Shared.DeviceConfigurator.Components.NetworkConfiguratorComponent>(multitoolUid);
            Assert.That(cfgLocked.ActiveDeviceList, Is.Null,
                "Locked AirAlarm must refuse multitool DeviceList configuration even when user holds CaptainPDA.");
            Assert.That(handsSys.TryDrop(SPlayer, multitoolUid, checkActionBlocker: false), Is.True);

            // Drop PDA so the player now has ZERO ID access.
            Assert.That(handsSys.TryDrop(SPlayer, pdaUid, checkActionBlocker: false), Is.True);
            xform.SetCoordinates(pdaUid, coords.Offset(new Vector2(20f, 0f)));
            Assert.That(accessReader.IsAllowed(SPlayer, alarmUid), Is.False);
            Assert.That(accessReader.IsAllowed(SPlayer, apcUid), Is.False);

            // 2) Locked APC rejects linking; UNLOCKED APC allows linking a SignalButton without an ID card!
            Assert.That(lockSys.IsLocked(apcUid), Is.True);
            Assert.That(devLink.ToggleLink(SPlayer, apcButtonUid, apcUid, "Pressed", "MachineCommandReceiver"), Is.False,
                "Locked APC must refuse DeviceLink from an unauthorized user.");

            lockSys.Unlock(apcUid, null);
            Assert.That(devLink.ToggleLink(SPlayer, apcButtonUid, apcUid, "Pressed", "MachineCommandReceiver"), Is.True,
                "Unlocked APC must allow DeviceLink from a multitool even without an ID card.");

            // Pressing the linked SignalButton on MachineCommandReceiver must toggle the APC main breaker!
            var apcComp = SEntMan.GetComponent<ApcComponent>(apcUid);
            var breakerBefore = apcComp.MainBreakerEnabled;
            var pressEv = new Content.Shared.Interaction.ActivateInWorldEvent(SPlayer, apcButtonUid, true);
            SEntMan.EventBus.RaiseLocalEvent(apcButtonUid, pressEv);
            Assert.That(apcComp.MainBreakerEnabled, Is.EqualTo(!breakerBefore),
                "Pressing a SignalButton linked to APC MachineCommandReceiver must toggle its breaker.");

            // 3) Unlocked AirAlarm must allow multitool DeviceList configuration (linking a GasVentPump) without Access Denied!
            Assert.That(lockSys.IsLocked(alarmUid), Is.True);
            lockSys.Unlock(alarmUid, null);
            Assert.That(handsSys.TryPickupAnyHand(SPlayer, multitoolUid), Is.True);

            // 4) Prepare Autolathe with materials + linked SignalButtons (print + SetRecipe)
            Assert.That(compSys.EnsureComputerBoundToHost((latheUid, SEntMan.GetComponent<EmbeddedComputerHostComponent>(latheUid)), out latheCompUid), Is.True);
            Assert.That(matSys.TryChangeMaterialAmount(latheUid, "Steel", 2000), Is.True);
            Assert.That(matSys.TryChangeMaterialAmount(latheUid, "Plastic", 2000), Is.True);
            Assert.That(matSys.TryChangeMaterialAmount(latheUid, "Glass", 2000), Is.True);

            Assert.That(devLink.ToggleLink(SPlayer, lathePrintBtnUid, latheUid, "Pressed", "AutolatheCommandSink"), Is.True);
            Assert.That(devLink.ToggleLink(SPlayer, latheSetRecipeBtnUid, latheUid, "Pressed", "AutolatheSetRecipeSink"), Is.True);
        });

        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            var coords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            var interSys = Server.System<Content.Shared.Interaction.SharedInteractionSystem>();
            // Save vent to multitool list mode, then use multitool on unlocked AirAlarm to open Configure UI
            interSys.InteractUsing(SPlayer, multitoolUid, ventUid, coords);
            interSys.InteractUsing(SPlayer, multitoolUid, alarmUid, coords);
            var cfgComp = SEntMan.GetComponent<Content.Shared.DeviceConfigurator.Components.NetworkConfiguratorComponent>(multitoolUid);
            Assert.That(cfgComp.ActiveDeviceList, Is.EqualTo(alarmUid),
                "Unlocked AirAlarm must allow opening multitool DeviceList configuration to link vents without Access Denied.");
            Server.System<SharedUserInterfaceSystem>().CloseUi(multitoolUid, Content.Shared.DeviceConfigurator.NetworkConfiguratorUiKey.Configure);

            var fsSys = Server.System<SharedFileSystemSystem>();
            var cComp = SEntMan.GetComponent<OxydComputerComponent>(latheCompUid);
            Assert.That(cComp.IsBooted, Is.True);
            latheProgUid = cComp.ActiveProgram!.Value;
            Assert.That(fsSys.TryFindFileSystem(latheCompUid, out var drive, out _), Is.True);
            latheDriveUid = drive!.Value;

            // Pressing print button BEFORE any recipe is selected must NOT start printing
            var pressEmpty = new Content.Shared.Interaction.ActivateInWorldEvent(SPlayer, lathePrintBtnUid, true);
            SEntMan.EventBus.RaiseLocalEvent(lathePrintBtnUid, pressEmpty);
            Assert.That(SEntMan.GetComponent<LatheComponent>(latheUid).CurrentRecipe, Is.Null,
                "Pressing a linked button on a lathe with no selected recipe must not print.");

            // Open Autolathe UI
            Server.System<OxydComputerSystem>().OpenComputerForActor(latheCompUid, SPlayer);
        });
        await RunTicks(8);

        // Select Wirecutter via UI action (routes through AutolatheSetRecipeSink)
        await SendBui(ProgramUiKey.Key,
            new LatheControlActionMessage(LatheControlAction.SelectRecipe, "Wirecutter", "/blueprints/tools.lathe", 1, fromDisk: false),
            FromServer(latheProgUid));
        await RunTicks(6);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var latheUi = GetControlFromChildren<LatheControlUiControl>(_ => true, win);
            var selLabel = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Name == "LatheSelectedRecipeLabel", latheUi!);
            Assert.That(selLabel, Is.Not.Null);
            Assert.That(selLabel!.Text, Does.Contain("wirecutter").IgnoreCase);

            // Close the UI -> selected recipe MUST persist!
            win.Close();
        });
        await RunTicks(6);

        // Press linked print button AFTER closing UI -> must print remembered Wirecutter!
        await Server.WaitAssertion(() =>
        {
            var latheComp = SEntMan.GetComponent<LatheComponent>(latheUid);
            Assert.That(latheComp.SelectedRecipeId?.Id, Is.EqualTo("Wirecutter"),
                "Closing the Autolathe UI must NOT reset the remembered selected recipe.");

            var pressEv = new Content.Shared.Interaction.ActivateInWorldEvent(SPlayer, lathePrintBtnUid, true);
            SEntMan.EventBus.RaiseLocalEvent(lathePrintBtnUid, pressEv);
            Assert.That(latheComp.CurrentRecipe?.Id, Is.EqualTo("Wirecutter"),
                "Pressing a linked button after closing the UI must print the remembered selected recipe.");
        });
        await RunTicks(4);

        // Delete /blueprints/tools.lathe from lathe HDD memory -> physical file removed -> selected recipe MUST clear!
        await Server.WaitAssertion(() =>
        {
            var fsSys = Server.System<SharedFileSystemSystem>();
            Assert.That(fsSys.TryDeleteFile(latheDriveUid, "/blueprints/tools.lathe"), Is.True);
            var latheComp = SEntMan.GetComponent<LatheComponent>(latheUid);
            Assert.That(latheComp.SelectedRecipeId, Is.Null,
                "Deleting the blueprint file from lathe memory must clear the selected recipe.");
        });
        await RunTicks(4);

        // Insert OxydRecipeDisk (Handcuffs, 10 uses), re-open UI, select from disk, close UI, press button -> prints & subtracts use (10 -> 9)!
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<ItemSlotsSystem>().TryInsert(latheUid, SharedLatheSystem.diskSlot, recipeDiskUid, null), Is.True);
            Server.System<OxydComputerSystem>().OpenComputerForActor(latheCompUid, SPlayer);
        });
        await RunTicks(8);

        await SendBui(ProgramUiKey.Key, new LatheControlActionMessage(LatheControlAction.CancelAll), FromServer(latheProgUid));
        await SendBui(ProgramUiKey.Key,
            new LatheControlActionMessage(LatheControlAction.SelectRecipe, "Handcuffs", "/blueprints/handcuffs.lathe", 1, fromDisk: true),
            FromServer(latheProgUid));
        await RunTicks(6);

        await Client.WaitPost(() =>
        {
            if (TryFindWindow(typeof(ComputerDisplayWindow), out var openWin))
                openWin!.Close();
        });
        await RunTicks(4);

        await Server.WaitAssertion(() =>
        {
            var latheComp = SEntMan.GetComponent<LatheComponent>(latheUid);
            Assert.That(latheComp.SelectedRecipeId?.Id, Is.EqualTo("Handcuffs"));
            Assert.That(latheComp.SelectedRecipeFromDisk, Is.True);

            var pressEv = new Content.Shared.Interaction.ActivateInWorldEvent(SPlayer, lathePrintBtnUid, true);
            SEntMan.EventBus.RaiseLocalEvent(lathePrintBtnUid, pressEv);
            Assert.That(latheComp.CurrentRecipe?.Id, Is.EqualTo("Handcuffs"),
                "Signal-triggered print of a selected disk recipe must start printing Handcuffs.");

            var fsSys = Server.System<SharedFileSystemSystem>();
            Assert.That(fsSys.TryGetFile<RecipeBlueprintFile>(recipeDiskUid, "/blueprints/handcuffs.lathe", out var bp), Is.True);
            Assert.That(bp!.RemainingUses, Is.EqualTo(9), "Signal-triggered print from recipe disk must subtract 1 use (10 -> 9).");

            // Eject physical disk -> selected disk recipe MUST immediately clear!
            Assert.That(Server.System<ItemSlotsSystem>().TryEject(latheUid, SharedLatheSystem.diskSlot, null, out _), Is.True);
            Assert.That(latheComp.SelectedRecipeId, Is.Null,
                "Ejecting the recipe disk must immediately clear a disk-sourced selected recipe.");

            // Pulsing SetRecipe button directly must cycle and select an accessible memory recipe!
            var pulseSet = new Content.Shared.Interaction.ActivateInWorldEvent(SPlayer, latheSetRecipeBtnUid, true);
            SEntMan.EventBus.RaiseLocalEvent(latheSetRecipeBtnUid, pulseSet);
            Assert.That(latheComp.SelectedRecipeId, Is.Not.Null,
                "Pulsing AutolatheSetRecipeSink must cycle and select an accessible blueprint.");

            Server.System<Content.Server.Hands.Systems.HandsSystem>().RemoveHand(SPlayer, "second_hand");
        });
    }

    [Test]
    public async Task TestAirAlarmLinkedDevicesScrollControlsAndTemplates()
    {
        EntityUid alarmUid = default;
        EntityUid alarmCompUid = default;
        EntityUid vent1Uid = default;
        EntityUid vent2Uid = default;
        EntityUid scrubberUid = default;
        EntityUid sensorUid = default;

        float initialVent1Ext = 0f;
        float initialScrubberRate = 0f;
        bool initialScrubberHadO2 = false;
        float initialSensorPresHiWrn = 0f;
        string sensorAddrStr = string.Empty;

        await Server.WaitAssertion(() =>
        {
            var coords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            var powerSys = Server.System<PowerReceiverSystem>();
            var lockSys = Server.System<LockSystem>();
            var compSys = Server.System<OxydComputerSystem>();
            var devListSys = Server.System<Content.Shared.DeviceConfigurator.Systems.DeviceListSystem>();

            alarmUid = SEntMan.SpawnEntity("AirAlarm", coords);
            vent1Uid = SEntMan.SpawnEntity("GasVentPump", coords);
            vent2Uid = SEntMan.SpawnEntity("GasVentPump", coords);
            scrubberUid = SEntMan.SpawnEntity("GasVentScrubber", coords);
            sensorUid = SEntMan.SpawnEntity("AirSensor", coords);

            powerSys.SetNeedsPower(alarmUid, false);
            powerSys.SetNeedsPower(vent1Uid, false);
            powerSys.SetNeedsPower(vent2Uid, false);
            powerSys.SetNeedsPower(scrubberUid, false);
            powerSys.SetNeedsPower(sensorUid, false);

            lockSys.Unlock(alarmUid, null);

            Assert.That(compSys.EnsureComputerBoundToHost((alarmUid, SEntMan.GetComponent<EmbeddedComputerHostComponent>(alarmUid)), out alarmCompUid), Is.True);

            // Same component NetworkConfiguratorSystem ensures when linking devices via multitool
            SEntMan.EnsureComponent<Content.Shared.DeviceConfigurator.Components.LinkedDeviceNetworkComponent>(vent1Uid);
            SEntMan.EnsureComponent<Content.Shared.DeviceConfigurator.Components.LinkedDeviceNetworkComponent>(vent2Uid);
            SEntMan.EnsureComponent<Content.Shared.DeviceConfigurator.Components.LinkedDeviceNetworkComponent>(scrubberUid);
            SEntMan.EnsureComponent<Content.Shared.DeviceConfigurator.Components.LinkedDeviceNetworkComponent>(sensorUid);

            var res = devListSys.UpdateDeviceList(alarmUid, new[] { vent1Uid, vent2Uid, scrubberUid, sensorUid });
            Assert.That(res, Is.EqualTo(Content.Shared.DeviceConfigurator.DeviceListUpdateResult.UpdateOk));

            var v1 = SEntMan.GetComponent<Content.Server.Atmos.Piping.Unary.Components.GasVentPumpComponent>(vent1Uid);
            initialVent1Ext = v1.ExternalPressureBound;

            var sc = SEntMan.GetComponent<Content.Server.Atmos.Piping.Unary.Components.GasVentScrubberComponent>(scrubberUid);
            initialScrubberRate = sc.TransferRate;
            initialScrubberHadO2 = Enumerable.Contains(sc.FilterGases, Content.Shared.Atmos.Gas.Oxygen);

            var sens = SEntMan.GetComponent<AtmosMonitorComponent>(sensorUid);
            initialSensorPresHiWrn = sens.PressureThreshold?.UpperWarningBound.Value ?? 0f;
            var sensorNet = SEntMan.GetComponent<Content.Shared.DeviceNetwork.Components.DeviceNetworkComponent>(sensorUid);
            sensorAddrStr = new Content.Shared.DeviceNetwork.LocDeviceAddress(sensorNet.Address, sensorNet.Prefix).ToString();
        });

        await RunTicks(12);

        await Server.WaitAssertion(() =>
        {
            var cComp = SEntMan.GetComponent<OxydComputerComponent>(alarmCompUid);
            Assert.That(cComp.IsBooted, Is.True);
            Server.System<OxydComputerSystem>().OpenComputerForActor(alarmCompUid, SPlayer);
        });
        await RunTicks(12);

        // 1) Verify NO 'All' tab exists (strictly categorized: Vents, Scrubbers, Sensors), and Vents view renders on open
        ScrollNumberChip? firstVentExtChip = null;
        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            Assert.That(win.MinSize.X, Is.GreaterThanOrEqualTo(740f), "AirAlarm window must open wide enough for expanded controls.");
            Assert.That(win.MinSize.Y, Is.GreaterThanOrEqualTo(600f), "AirAlarm window must enforce tall enough minimum height.");

            var atmosUi = GetControlFromChildren<AtmosControlUiControl>(_ => true, win);
            Assert.That(atmosUi, Is.Not.Null);
            AssertAllVisibleChildrenWithinViewport(win, atmosUi!, "AirAlarm Expanded Controller UI");

            var allStatusLabels = new List<Content.Client.UserInterface.Controls.ContentLabel>();
            CollectDescendants(atmosUi!, allStatusLabels);
            Assert.That(allStatusLabels.Any(l => l.VisibleInTree && l.Text != null &&
                (l.Text.Contains("Devices:", StringComparison.OrdinalIgnoreCase) ||
                 l.Text.Contains("(Auto:", StringComparison.OrdinalIgnoreCase) ||
                 l.Text.Contains("0.0 mol", StringComparison.OrdinalIgnoreCase))),
                Is.False, "Formatted AirAlarm status must not display redundant device counts, Auto duplicate, or 0.0 mol entries.");

            var devicesScroll = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentScrollContainer>(
                s => s.Name == "AtmosDevicesScroll", atmosUi!);
            Assert.That(devicesScroll, Is.Not.Null);
            Assert.That(devicesScroll!.ShowScrollVisuals, Is.True);

            var catRow = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentWrapContainer>(
                w => w.Name == "AtmosCategoryRow", atmosUi!);
            var catBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(catRow!, catBtns);
            Assert.That(catBtns.Count, Is.EqualTo(3), "Category strip must contain strictly Vents, Scrubbers, and Sensors (no All tab).");
            Assert.That(catBtns.Any(b => b.Text != null && b.Text.StartsWith("All", StringComparison.OrdinalIgnoreCase)), Is.False,
                "All tab must be removed so devices are always categorized.");

            // Single-template UX: no template name input and no multi-template chip buttons
            var allEdits = new List<Content.Client.UserInterface.Controls.ContentLineEdit>();
            CollectDescendants(atmosUi!, allEdits);
            Assert.That(allEdits.Any(e => e.Name == "AtmosTemplateNameInput"), Is.False,
                "Single-template Atmos UI must not show multi-template name input.");

            var panels = new List<Content.Client.UserInterface.Controls.ContentPanelContainer>();
            CollectDescendants(atmosUi!, panels);
            var ventCards = panels.Where(p => p.Name == "AtmosVentCard" && p.VisibleInTree).ToList();
            var scrubberCards = panels.Where(p => p.Name == "AtmosScrubberCard" && p.VisibleInTree).ToList();
            var sensorCards = panels.Where(p => p.Name == "AtmosSensorCard" && p.VisibleInTree).ToList();

            Assert.That(ventCards.Count, Is.EqualTo(2), "Vents category must render both linked vents immediately on open.");
            Assert.That(scrubberCards.Count, Is.EqualTo(0), "Scrubbers must stay in their own category tab.");
            Assert.That(sensorCards.Count, Is.EqualTo(0), "Sensors must stay in their own category tab.");

            firstVentExtChip = GetControlFromChildren<ScrollNumberChip>(c => c.Name == "AtmosVentExtBoundChip", ventCards[0]);
            Assert.That(firstVentExtChip, Is.Not.Null);
        });

        // 2) Sustained multi-step scrolling spread over server round-trips (10 notches x 5 ticks = 50 ticks / ~1.65s):
        //    In-flight server echoes MUST NEVER override or rubber-band the chip back!
        float prevScrolledVal = initialVent1Ext;
        for (var i = 0; i < 10; i++)
        {
            await Client.WaitAssertion(() =>
            {
                Assert.That(firstVentExtChip!.Value, Is.GreaterThanOrEqualTo(prevScrolledVal - 0.01f),
                    $"Server state must not rubber-band chip backward before scroll #{i + 1} (was {firstVentExtChip.Value}, expected >= {prevScrolledVal}).");
                firstVentExtChip.RelayMouseWheel(new GUIMouseWheelEventArgs(
                    new Vector2(0f, 1f), firstVentExtChip, new Vector2(10, 10), default, new Vector2(10, 10), new Vector2(10, 10)));
                Assert.That(firstVentExtChip.Value, Is.GreaterThan(prevScrolledVal),
                    $"Scroll #{i + 1} must monotonically advance the chip value.");
                prevScrolledVal = firstVentExtChip.Value;
            });
            // 5 ticks (~167ms) exceeds the 140ms debounce so intermediate server packets arrive DURING scrolling!
            await RunTicks(5);
        }

        await RunTicks(14);

        EntityUid modifiedVentUid = default;
        EntityUid otherVentUid = default;
        await Server.WaitAssertion(() =>
        {
            var v1 = SEntMan.GetComponent<Content.Server.Atmos.Piping.Unary.Components.GasVentPumpComponent>(vent1Uid);
            var v2 = SEntMan.GetComponent<Content.Server.Atmos.Piping.Unary.Components.GasVentPumpComponent>(vent2Uid);
            float modifiedVentExt;
            if (MathF.Abs(v1.ExternalPressureBound - initialVent1Ext) > 1f)
            {
                modifiedVentUid = vent1Uid;
                otherVentUid = vent2Uid;
                modifiedVentExt = v1.ExternalPressureBound;
            }
            else
            {
                modifiedVentUid = vent2Uid;
                otherVentUid = vent1Uid;
                modifiedVentExt = v2.ExternalPressureBound;
            }

            Assert.That(modifiedVentExt, Is.EqualTo(prevScrolledVal).Within(0.5f),
                "After 10 spread-out scroll notches, server vent ExternalPressureBound must reach the full final value without rubber-banding.");
            Assert.That(modifiedVentExt, Is.GreaterThanOrEqualTo(149.5f),
                "10 scroll-up notches (+5 kPa each from 101.325 kPa) must reach 150 kPa.");
        });

        // 3) Click-to-edit manual numeric entry on ScrollNumberChip: click chip -> edit number -> Enter -> commits immediately!
        await Client.WaitAssertion(() =>
        {
            var downArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, default, false, new Vector2(10, 10), new Vector2(10, 10));
            firstVentExtChip!.RelayKeyBindDown(downArgs);
            Assert.That(firstVentExtChip.IsEditing, Is.True, "Clicking ScrollNumberChip must open inline numeric edit mode.");
            Assert.That(firstVentExtChip.Editor.Visible, Is.True, "Inline ContentLineEdit must become visible when editing.");

            firstVentExtChip.Editor.Text = "234";
            var submitArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.TextSubmit, BoundKeyState.Down, default, false, new Vector2(5, 5), new Vector2(5, 5));
            firstVentExtChip.Editor.RelayKeyBindDown(submitArgs);
            Assert.That(firstVentExtChip.IsEditing, Is.False, "Pressing Enter must commit and close inline edit mode.");
            Assert.That(firstVentExtChip.Value, Is.EqualTo(234f).Within(0.01f));
        });
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            var v = SEntMan.GetComponent<Content.Server.Atmos.Piping.Unary.Components.GasVentPumpComponent>(modifiedVentUid);
            Assert.That(v.ExternalPressureBound, Is.EqualTo(234f).Within(0.5f),
                "Manually typing 234 into ScrollNumberChip and pressing Enter must update the server vent to 234 kPa.");
        });

        // 4) Single Active Template: Save Tpl on Vent 1 -> Apply Tpl & Apply All Vents are IMMEDIATELY shown without selecting any chip!
        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var atmosUi = GetControlFromChildren<AtmosControlUiControl>(_ => true, win)!;
            var panels = new List<Content.Client.UserInterface.Controls.ContentPanelContainer>();
            CollectDescendants(atmosUi, panels);
            var ventCards = panels.Where(p => p.Name == "AtmosVentCard" && p.VisibleInTree).ToList();
            var saveBtn = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentButton>(
                b => b.Name == "AtmosSaveVentTemplateButton", ventCards[0]);
            var downArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, default, false, new Vector2(10, 10), new Vector2(10, 10));
            var upArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, default, false, new Vector2(10, 10), new Vector2(10, 10));
            saveBtn!.RelayKeyBindDown(downArgs);
            saveBtn.RelayKeyBindUp(upArgs);
        });
        await RunTicks(8);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var atmosUi = GetControlFromChildren<AtmosControlUiControl>(_ => true, win)!;

            var summary = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Name == "AtmosTemplateSummaryLabel", atmosUi);
            Assert.That(summary!.Text, Does.Contain("[VENT]").And.Contain("234"),
                "Saving a vent template must immediately show the single active template summary.");

            var applyAllBtn = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentButton>(
                b => b.Name == "AtmosApplyTemplateAllButton", atmosUi);
            Assert.That(applyAllBtn!.VisibleInTree, Is.True, "Apply All button must immediately show whenever a template is active.");
            Assert.That(applyAllBtn.Text, Is.EqualTo("Apply All Vents"));

            var panels = new List<Content.Client.UserInterface.Controls.ContentPanelContainer>();
            CollectDescendants(atmosUi, panels);
            var ventCards = panels.Where(p => p.Name == "AtmosVentCard" && p.VisibleInTree).ToList();
            var applySecondBtn = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentButton>(
                b => b.Name == "AtmosApplyVentTemplateButton", ventCards[1]);
            Assert.That(applySecondBtn!.VisibleInTree, Is.True,
                "Apply Tpl button must immediately appear on matching vent cards whenever a vent template is active.");

            var downArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, default, false, new Vector2(10, 10), new Vector2(10, 10));
            var upArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, default, false, new Vector2(10, 10), new Vector2(10, 10));
            applySecondBtn.RelayKeyBindDown(downArgs);
            applySecondBtn.RelayKeyBindUp(upArgs);
        });
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            var otherVent = SEntMan.GetComponent<Content.Server.Atmos.Piping.Unary.Components.GasVentPumpComponent>(otherVentUid);
            Assert.That(otherVent.ExternalPressureBound, Is.EqualTo(234f).Within(0.5f),
                "Clicking Apply Tpl on Vent 2 must apply the active single vent template (234 kPa).");
        });

        // 5) Switch to Scrubbers tab: scroll rate down 3 times spread across ticks + toggle O2 + Save Tpl (replaces single template!)
        ScrollNumberChip? scrubRateChip = null;
        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var atmosUi = GetControlFromChildren<AtmosControlUiControl>(_ => true, win)!;
            var scrubTab = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentButton>(
                b => b.Name == "AtmosCategoryScrubbersButton", atmosUi);
            var downArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, default, false, new Vector2(10, 10), new Vector2(10, 10));
            var upArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, default, false, new Vector2(10, 10), new Vector2(10, 10));
            scrubTab!.RelayKeyBindDown(downArgs);
            scrubTab.RelayKeyBindUp(upArgs);

            var panels = new List<Content.Client.UserInterface.Controls.ContentPanelContainer>();
            CollectDescendants(atmosUi, panels);
            var scrubberCards = panels.Where(p => p.Name == "AtmosScrubberCard" && p.VisibleInTree).ToList();
            Assert.That(scrubberCards.Count, Is.EqualTo(1), "Scrubbers tab must render the linked scrubber.");
            Assert.That(panels.Any(p => p.Name == "AtmosVentCard" && p.VisibleInTree), Is.False, "Vents must hide when Scrubbers tab is active.");

            scrubRateChip = GetControlFromChildren<ScrollNumberChip>(c => c.Name == "AtmosScrubberRateChip", scrubberCards[0]);
            var o2Btn = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentButton>(
                b => b.Name == "AtmosScrubberGas_Oxygen", scrubberCards[0]);
            o2Btn!.RelayKeyBindDown(downArgs);
            o2Btn.RelayKeyBindUp(upArgs);
        });

        for (var i = 0; i < 3; i++)
        {
            await Client.WaitAssertion(() =>
            {
                scrubRateChip!.RelayMouseWheel(new GUIMouseWheelEventArgs(
                    new Vector2(0f, -1f), scrubRateChip, new Vector2(10, 10), default, new Vector2(10, 10), new Vector2(10, 10)));
            });
            await RunTicks(5);
        }
        await RunTicks(12);

        await Server.WaitAssertion(() =>
        {
            var sc = SEntMan.GetComponent<Content.Server.Atmos.Piping.Unary.Components.GasVentScrubberComponent>(scrubberUid);
            Assert.That(sc.TransferRate, Is.EqualTo(initialScrubberRate - 30f).Within(0.5f),
                "3 spread-out scroll-down notches on AtmosScrubberRateChip must lower TransferRate by -30 L/s without rubber-banding.");
            Assert.That(Enumerable.Contains(sc.FilterGases, Content.Shared.Atmos.Gas.Oxygen), Is.EqualTo(!initialScrubberHadO2),
                "Clicking AtmosScrubberGas_Oxygen must toggle Oxygen filtering on the server scrubber.");
        });

        // Save scrubber template -> replaces previous vent template (only 1 template exists!)
        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var atmosUi = GetControlFromChildren<AtmosControlUiControl>(_ => true, win)!;
            var saveScrubBtn = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentButton>(
                b => b.Name == "AtmosSaveScrubberTemplateButton", atmosUi);
            var downArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, default, false, new Vector2(10, 10), new Vector2(10, 10));
            var upArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, default, false, new Vector2(10, 10), new Vector2(10, 10));
            saveScrubBtn!.RelayKeyBindDown(downArgs);
            saveScrubBtn.RelayKeyBindUp(upArgs);
        });
        await RunTicks(8);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var atmosUi = GetControlFromChildren<AtmosControlUiControl>(_ => true, win)!;
            var summary = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Name == "AtmosTemplateSummaryLabel", atmosUi);
            Assert.That(summary!.Text, Does.Contain("[SCRUBBER]"),
                "Saving a scrubber template must replace the single active template with the scrubber template.");
            var applyAllBtn = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentButton>(
                b => b.Name == "AtmosApplyTemplateAllButton", atmosUi);
            Assert.That(applyAllBtn!.Text, Is.EqualTo("Apply All Scrubbers"));
            var applyScrubBtn = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentButton>(
                b => b.Name == "AtmosApplyScrubberTemplateButton", atmosUi);
            Assert.That(applyScrubBtn!.VisibleInTree, Is.True,
                "Apply Tpl must immediately appear on scrubber cards when scrubber template is active.");

            // 6) Switch to Sensors tab and scroll the AirSensor Pressure 'Hi Wrn' chip up (+5 kPa)
            var sensTab = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentButton>(
                b => b.Name == "AtmosCategorySensorsButton", atmosUi);
            var downArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, default, false, new Vector2(10, 10), new Vector2(10, 10));
            var upArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, default, false, new Vector2(10, 10), new Vector2(10, 10));
            sensTab!.RelayKeyBindDown(downArgs);
            sensTab.RelayKeyBindUp(upArgs);

            var panels = new List<Content.Client.UserInterface.Controls.ContentPanelContainer>();
            CollectDescendants(atmosUi, panels);
            var sensorCards = panels.Where(p => p.Name == "AtmosSensorCard" && p.VisibleInTree).ToList();
            Content.Client.UserInterface.Controls.ContentPanelContainer? targetSensorCard = null;
            foreach (var card in sensorCards)
            {
                var cardLabels = new List<Content.Client.UserInterface.Controls.ContentLabel>();
                CollectDescendants(card, cardLabels);
                if (cardLabels.Any(l => l.Text != null && l.Text.Contains(sensorAddrStr, StringComparison.OrdinalIgnoreCase)))
                {
                    targetSensorCard = card;
                    break;
                }
            }
            Assert.That(targetSensorCard, Is.Not.Null, $"AirSensor card for {sensorAddrStr} must render on Sensors tab.");
            var sensorChips = new List<ScrollNumberChip>();
            CollectDescendants(targetSensorCard!, sensorChips);
            var presHiWrnChip = sensorChips.FirstOrDefault(c => c.Label == "Hi Wrn");
            Assert.That(presHiWrnChip, Is.Not.Null);
            presHiWrnChip!.RelayMouseWheel(new GUIMouseWheelEventArgs(new Vector2(0f, 1f), presHiWrnChip, new Vector2(10, 10), default, new Vector2(10, 10), new Vector2(10, 10)));
        });
        await RunTicks(14);

        await Server.WaitAssertion(() =>
        {
            var sens = SEntMan.GetComponent<AtmosMonitorComponent>(sensorUid);
            Assert.That(sens.PressureThreshold?.UpperWarningBound.Value ?? 0f, Is.GreaterThan(initialSensorPresHiWrn + 1f),
                "Mouse-wheel scrolling AirSensor Pressure Hi Wrn chip must update the server AtmosMonitorComponent threshold.");
        });

        // 7) Clear the single template and verify Apply All hides cleanly
        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var atmosUi = GetControlFromChildren<AtmosControlUiControl>(_ => true, win)!;
            var delBtn = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentButton>(
                b => b.Name == "AtmosDeleteTemplateButton", atmosUi);
            Assert.That(delBtn!.VisibleInTree, Is.True);
            var downArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, default, false, new Vector2(10, 10), new Vector2(10, 10));
            var upArgs = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, default, false, new Vector2(10, 10), new Vector2(10, 10));
            delBtn.RelayKeyBindDown(downArgs);
            delBtn.RelayKeyBindUp(upArgs);
        });
        await RunTicks(8);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var atmosUi = GetControlFromChildren<AtmosControlUiControl>(_ => true, win)!;
            var applyAllBtn = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentButton>(
                b => b.Name == "AtmosApplyTemplateAllButton", atmosUi);
            Assert.That(applyAllBtn!.VisibleInTree, Is.False, "Clearing the active template must hide Apply All.");
            var summary = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Name == "AtmosTemplateSummaryLabel", atmosUi);
            Assert.That(summary!.Text, Does.Contain("None"));
            win.Close();
        });
        await RunTicks(4);
    }

    [Test]
    public async Task TestNetworkCoreOsAutoSyncAndDataComputerRecordsConsoles()
    {
        var compSys = Server.System<OxydComputerSystem>();
        var fsSys = Server.System<SharedFileSystemSystem>();
        var powerSys = Server.System<PowerReceiverSystem>();
        var lockSys = Server.System<LockSystem>();

        EntityUid routerHost = default;
        EntityUid routerCompUid = default;
        EntityUid routerBootUid = default;
        EntityUid serverHost = default;
        EntityUid serverCompUid = default;
        EntityUid serverDriveUid = default;
        EntityUid stationRecHost = default;
        EntityUid stationRecCompUid = default;
        EntityUid stationRecProgUid = default;
        EntityUid crimRecHost = default;
        EntityUid crimRecCompUid = default;
        EntityUid crimRecProgUid = default;
        EntityUid stationUid = default;
        EntityUid joiningMobUid = default;
        Content.Shared.StationRecords.StationRecordKey novaLiveKey = default;

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(TargetCoords);
            var gridUid = SEntMan.GetComponent<TransformComponent>(SPlayer).GridUid!.Value;

            // Create a live station entity linked to the test grid so live StationRecords / CriminalRecords sync is tested too.
            stationUid = SEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            SEntMan.System<MetaDataSystem>().SetEntityName(stationUid, "NSS Oxyd Core");
            SEntMan.EnsureComponent<Content.Shared.Station.Components.StationDataComponent>(stationUid);
            SEntMan.EnsureComponent<Content.Shared.StationRecords.Components.StationRecordsComponent>(stationUid);
            Server.System<Content.Server.Station.Systems.ServerStationSystem>().AddGridToStation(stationUid, gridUid);

            routerHost = SEntMan.SpawnEntity("StationNetworkRouter", coords);
            powerSys.SetNeedsPower(routerHost, false);
            routerCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(routerHost).ComputerEntity!.Value;

            serverHost = SEntMan.SpawnEntity("BaseDataServer", coords);
            powerSys.SetNeedsPower(serverHost, false);
            serverCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(serverHost).ComputerEntity!.Value;

            stationRecHost = SEntMan.SpawnEntity("ComputerStationRecords", coords);
            powerSys.SetNeedsPower(stationRecHost, false);
            stationRecCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(stationRecHost).ComputerEntity!.Value;

            crimRecHost = SEntMan.SpawnEntity("ComputerCriminalRecords", coords);
            powerSys.SetNeedsPower(crimRecHost, false);
            crimRecCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(crimRecHost).ComputerEntity!.Value;
        });
        await RunTicks(8);

        // 1) Verify StationNetworkRouter boots NetworkCoreOsProgramComponent (/bin/netcore_os.nte) + auto-launches /bin/router.nte,
        //    and BaseDataServer HDD ships with all record & camera .nte apps.
        await Server.WaitAssertion(() =>
        {
            var rComp = SEntMan.GetComponent<OxydComputerComponent>(routerCompUid);
            Assert.That(rComp.IsBooted, Is.True);
            Assert.That(rComp.BootProgram, Is.Not.Null);
            routerBootUid = rComp.BootProgram!.Value;
            Assert.That(SEntMan.HasComponent<NetworkCoreOsProgramComponent>(routerBootUid), Is.True,
                "StationNetworkRouter must boot the specialized NetworkCoreOsProgramComponent (/bin/netcore_os.nte).");
            Assert.That(rComp.RunningPrograms.Any(p => SEntMan.HasComponent<RouterProgramComponent>(p)), Is.True,
                "Network Core OS must auto-launch Router Admin (/bin/router.nte) as a child program.");

            Assert.That(fsSys.TryFindFileSystem(serverCompUid, out var sDrive, out var sFs), Is.True);
            serverDriveUid = sDrive!.Value;
            Assert.That(sFs!.Files.ContainsKey("/bin/crew_manifest.nte"), Is.True);
            Assert.That(sFs.Files.ContainsKey("/bin/station_records.nte"), Is.True);
            Assert.That(sFs.Files.ContainsKey("/bin/criminal_records.nte"), Is.True);
            Assert.That(sFs.Files.ContainsKey("/bin/wanted_list.nte"), Is.True);
            Assert.That(sFs.Files.ContainsKey("/bin/cameras.nte"), Is.True);

            // Verify migrated physical consoles run native NTOS record programs without legacy console components.
            var stComp = SEntMan.GetComponent<OxydComputerComponent>(stationRecCompUid);
            Assert.That(stComp.IsBooted, Is.True);
            Assert.That(stComp.ActiveProgram, Is.Not.Null);
            stationRecProgUid = stComp.ActiveProgram!.Value;
            Assert.That(SEntMan.HasComponent<StationRecordsProgramComponent>(stationRecProgUid), Is.True);
            Assert.That(SEntMan.HasComponent<Content.Shared.StationRecords.Components.GeneralStationRecordConsoleComponent>(stationRecHost), Is.False);

            var crComp = SEntMan.GetComponent<OxydComputerComponent>(crimRecCompUid);
            Assert.That(crComp.IsBooted, Is.True);
            Assert.That(crComp.ActiveProgram, Is.Not.Null);
            crimRecProgUid = crComp.ActiveProgram!.Value;
            Assert.That(SEntMan.HasComponent<CriminalRecordsProgramComponent>(crimRecProgUid), Is.True);
            Assert.That(lockSys.IsLocked(crimRecHost), Is.True, "ComputerCriminalRecords must start locked.");

            // Seed a pre-existing custom row in /records/crew_manifest.db on BaseDataServer HDD to verify incremental append preserves it!
            Assert.That(fsSys.TryWriteFile(serverDriveUid, new DatabaseVirtualFile
            {
                Name = "crew_manifest",
                Extension = "db",
                Directory = "/records/",
                TableName = "crew_manifest",
                Data = new Dictionary<string, string>
                {
                    ["__station"] = "NSS Oxyd Core",
                    ["row_0"] = "Legacy Chief\tChief Engineer\tChiefEngineer\tJobIconChiefEngineer"
                },
                Size = 192
            }, sFs), Is.True);
        });

        // 2) Open StationNetworkRouter UI and verify NetworkCoreOsUiControl renders cleanly.
        await Server.WaitPost(() => compSys.OpenComputerForActor(routerCompUid, SPlayer));
        await RunTicks(8);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var netOsUi = GetControlFromChildren<NetworkCoreOsUiControl>(_ => true, win);
            Assert.That(netOsUi, Is.Not.Null, "StationNetworkRouter must render NetworkCoreOsUiControl.");
            var dsLabel = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Name == "NetCoreDataServerLabel", netOsUi!);
            Assert.That(dsLabel!.Text, Does.Not.Contain("None online").And.Not.Contain("Checking"),
                $"Network Core OS must detect the online Data Server (label was: '{dsLabel.Text}').");
            win.Close();
        });
        await RunTicks(4);

        // 3) Simulate a new player joining (PlayerSpawnCompleteEvent) -> Network Core OS must INCREMENTALLY append
        //    to /records/crew_manifest.db, /records/station_records.db, and /records/criminal_records.db WITHOUT wiping Legacy Chief!
        await Server.WaitAssertion(() =>
        {
            var coords = SEntMan.GetCoordinates(TargetCoords);
            joiningMobUid = SEntMan.SpawnEntity("MobHuman", coords);
            SEntMan.System<MetaDataSystem>().SetEntityName(joiningMobUid, "Nova Vance");

            var stRecSys = Server.System<Content.Shared.StationRecords.Systems.StationRecordsSystem>();
            var gen = new Content.Shared.StationRecords.GeneralStationRecord
            {
                Name = "Nova Vance",
                Age = 31,
                JobTitle = "Security Officer",
                JobPrototype = "SecurityOfficer",
                JobIcon = "JobIconSecurityOfficer",
                Species = "Human",
                Gender = Robust.Shared.Enums.Gender.Female,
                Fingerprint = "FP-NOVA-99",
                DNA = "DNA-NOVA-99"
            };
            novaLiveKey = stRecSys.AddRecordEntry(stationUid, gen);
            stRecSys.AddRecordEntry(novaLiveKey, new Content.Shared.CriminalRecords.CriminalRecord());

            var profile = Content.Shared.Preferences.HumanoidCharacterProfile.RandomWithSpecies()
                .WithName("Nova Vance")
                .WithAge(31);
            var playerSession = SEntMan.GetComponent<Robust.Shared.Player.ActorComponent>(SPlayer).PlayerSession;
            var joinEv = new Content.Shared.GameTicking.PlayerSpawnCompleteEvent(
                joiningMobUid,
                playerSession,
                "SecurityOfficer",
                lateJoin: true,
                silent: true,
                joinOrder: 2,
                stationUid,
                profile);
            SEntMan.EventBus.RaiseLocalEvent(joiningMobUid, joinEv, broadcast: true);

            var coreComp = SEntMan.GetComponent<NetworkCoreOsProgramComponent>(routerBootUid);
            var routerSys = Server.System<NetworkRouterSystem>();
            Assert.That(coreComp.AutoSyncCount, Is.GreaterThanOrEqualTo(1),
                $"AutoSyncCount was 0! IsRouterOp={routerSys.IsRouterOperational(routerHost)}, AutoSync={coreComp.AutoSyncRecords}, Logs=[{string.Join(" | ", coreComp.SyncLogs)}]");
            Assert.That(coreComp.SyncLogs.Any(l => l.Contains("Nova Vance")), Is.True);

            Assert.That(fsSys.TryGetFile<DatabaseVirtualFile>(serverDriveUid, CrewManifestProgramComponent.CacheFilePath, out var manDb), Is.True);
            Assert.That(manDb!.Data.Values.Any(v => v.StartsWith("Legacy Chief\t")), Is.True,
                "Incremental join sync must NOT overwrite existing rows in /records/crew_manifest.db.");
            Assert.That(manDb.Data.Values.Any(v => v.StartsWith("Nova Vance\t")), Is.True,
                "Network Core OS must append the joining player to /records/crew_manifest.db.");

            Assert.That(fsSys.TryGetFile<DatabaseVirtualFile>(serverDriveUid, StationRecordsProgramComponent.CacheFilePath, out var stDb), Is.True);
            Assert.That(stDb!.Data.Values.Any(v => v.StartsWith("Nova Vance\t")), Is.True,
                "Network Core OS must append the joining player to /records/station_records.db.");

            Assert.That(fsSys.TryGetFile<DatabaseVirtualFile>(serverDriveUid, CriminalRecordsProgramComponent.CacheFilePath, out var crDb), Is.True);
            Assert.That(crDb!.Data.Values.Any(v => v.StartsWith("Nova Vance\t")), Is.True,
                "Network Core OS must create a criminal records entry for the joining player in /records/criminal_records.db.");
        });

        // 4) Open migrated ComputerStationRecords -> verify StationRecordsUiControl renders & can create a new record persisted to DataServer + Crew Manifest!
        await Server.WaitPost(() => compSys.OpenComputerForActor(stationRecCompUid, SPlayer));
        await RunTicks(8);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var recUi = GetControlFromChildren<StationRecordsUiControl>(_ => true, win);
            Assert.That(recUi, Is.Not.Null, "ComputerStationRecords must render StationRecordsUiControl.");
            var nameEdit = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLineEdit>(
                e => e.Name == "StationRecordsNameEdit", recUi!);
            Assert.That(nameEdit!.Text, Is.EqualTo("Nova Vance"));
        });

        await SendBui(ProgramUiKey.Key, new StationRecordsActionMessage(
            StationRecordsAction.CreateEntry,
            name: "Dr. Kira Sol",
            jobTitle: "Chief Medical Officer",
            age: 42,
            species: "Human",
            gender: "Female",
            fingerprint: "FP-KIRA-01",
            dna: "DNA-KIRA-01"), FromServer(stationRecProgUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            Assert.That(fsSys.TryGetFile<DatabaseVirtualFile>(serverDriveUid, StationRecordsProgramComponent.CacheFilePath, out var stDb), Is.True);
            Assert.That(stDb!.Data.Values.Any(v => v.StartsWith("Dr. Kira Sol\tChief Medical Officer\t")), Is.True,
                "Creating a record in StationRecordsProgram must persist directly to DataServer /records/station_records.db.");
            Assert.That(fsSys.TryGetFile<DatabaseVirtualFile>(serverDriveUid, CrewManifestProgramComponent.CacheFilePath, out var manDb), Is.True);
            Assert.That(manDb!.Data.Values.Any(v => v.StartsWith("Dr. Kira Sol\tChief Medical Officer\t")), Is.True,
                "Creating a record in StationRecordsProgram must mirror to DataServer /records/crew_manifest.db.");
        });

        await Client.WaitPost(() =>
        {
            if (TryFindWindow(typeof(ComputerDisplayWindow), out var openWin))
                openWin!.Close();
        });
        await RunTicks(4);

        // 5) Open migrated ComputerCriminalRecords while locked -> edits blocked; unlock -> mark Nova Vance Wanted + add history -> verify DataServer criminal_records.db, wanted_list.db, and live CriminalRecord!
        string novaCrimKey = string.Empty;
        await Server.WaitPost(() =>
        {
            fsSys.TryGetFile<DatabaseVirtualFile>(serverDriveUid, CriminalRecordsProgramComponent.CacheFilePath, out var crDb);
            novaCrimKey = crDb!.Data.First(kv => kv.Key.StartsWith("crim_") && kv.Value.StartsWith("Nova Vance\t")).Key;
            SEntMan.System<SharedTransformSystem>().SetCoordinates(SPlayer, SEntMan.GetComponent<TransformComponent>(crimRecHost).Coordinates); compSys.OpenComputerForActor(crimRecCompUid, SPlayer);
        });
        await RunTicks(8);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var crimUi = GetControlFromChildren<CriminalRecordsUiControl>(_ => true, win);
            Assert.That(crimUi, Is.Not.Null, "ComputerCriminalRecords must render CriminalRecordsUiControl.");
            var applyBtn = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentButton>(
                b => b.Name == "CriminalApplyStatusBtn", crimUi!);
            Assert.That(applyBtn!.Disabled, Is.True, "Locked ComputerCriminalRecords must disable edit buttons.");
        });

        // Attempt mutation while locked -> rejected!
        await SendBui(ProgramUiKey.Key, new CriminalRecordsActionMessage(
            CriminalRecordsAction.SetStatus, key: novaCrimKey, status: SecurityStatus.Wanted, text: "Unauthorized edit"), FromServer(crimRecProgUid));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(fsSys.TryGetFile<DatabaseVirtualFile>(serverDriveUid, CriminalRecordsProgramComponent.CacheFilePath, out var crDb), Is.True);
            Assert.That(crDb!.Data[novaCrimKey], Does.Not.Contain("Unauthorized edit"));
            lockSys.Unlock(crimRecHost, null);
        });
        await RunTicks(6);

        // Unlocking must automatically enable edit controls on the open BUI!
        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var crimUi = GetControlFromChildren<CriminalRecordsUiControl>(_ => true, win)!;
            var applyBtn = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentButton>(
                b => b.Name == "CriminalApplyStatusBtn", crimUi);
            Assert.That(applyBtn!.Disabled, Is.False, "Unlocking ComputerCriminalRecords must immediately enable editing.");
        });

        await SendBui(ProgramUiKey.Key, new CriminalRecordsActionMessage(
            CriminalRecordsAction.SetStatus, key: novaCrimKey, status: SecurityStatus.Wanted, text: "Stolen captain spare"), FromServer(crimRecProgUid));
        await SendBui(ProgramUiKey.Key, new CriminalRecordsActionMessage(
            CriminalRecordsAction.AddHistory, key: novaCrimKey, text: "Broke into bridge"), FromServer(crimRecProgUid));
        await RunTicks(8);

        await Server.WaitAssertion(() =>
        {
            Assert.That(fsSys.TryGetFile<DatabaseVirtualFile>(serverDriveUid, CriminalRecordsProgramComponent.CacheFilePath, out var crDb), Is.True);
            Assert.That(crDb!.Data[novaCrimKey], Does.Contain("Wanted").And.Contain("Stolen captain spare").And.Contain("Broke into bridge"),
                "CriminalRecordsProgram must persist status & history to DataServer /records/criminal_records.db.");

            Assert.That(fsSys.TryGetFile<DatabaseVirtualFile>(serverDriveUid, WantedListProgramComponent.CacheFilePath, out var wantDb), Is.True);
            Assert.That(wantDb!.Data.Values.Any(v => v.Contains("Nova Vance") && v.Contains("Wanted")), Is.True,
                "Setting Wanted in CriminalRecordsProgram must sync /records/wanted_list.db on DataServer.");

            var stRecSys = Server.System<Content.Shared.StationRecords.Systems.StationRecordsSystem>();
            Assert.That(stRecSys.TryGetRecord(novaLiveKey, out Content.Shared.CriminalRecords.CriminalRecord? liveCrim), Is.True);
            Assert.That(liveCrim!.Status, Is.EqualTo(SecurityStatus.Wanted),
                "Setting Wanted in CriminalRecordsProgram must sync to live CriminalRecord (Security HUD).");
            Assert.That(liveCrim.Reason, Is.EqualTo("Stolen captain spare"));
            Assert.That(liveCrim.History.Any(h => h.Crime.Contains("Broke into bridge")), Is.True);
        });

        await Client.WaitPost(() =>
        {
            if (TryFindWindow(typeof(ComputerDisplayWindow), out var openWin))
                openWin!.Close();
        });
        await RunTicks(4);
    }

    [Test]
    public async Task TestWifiCameraAppExamineThroughCameraWitnessLogsAndMicUpgrade()
    {
        var compSys = Server.System<OxydComputerSystem>();
        var fsSys = Server.System<SharedFileSystemSystem>();
        var powerSys = Server.System<PowerReceiverSystem>();
        var camSys = Server.System<OxydCameraSystem>();
        var examineSys = Server.System<ExamineSystemShared>();
        var interSys = Server.System<Content.Shared.Interaction.SharedInteractionSystem>();
        var itemSlots = Server.System<ItemSlotsSystem>();

        EntityUid nearRouterHost = default;
        EntityUid farRouterHost = default;
        EntityUid monitorHost = default;
        EntityUid monitorCompUid = default;
        EntityUid camProgUid = default;
        EntityUid secCamUid = default;
        EntityUid farEngCamUid = default;
        EntityUid subjectUid = default;
        EntityUid micUpgradeUid = default;
        EntityUid dataDiskUid = default;

        await Server.WaitPost(() =>
        {
            var baseCoords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            var gridUid = SEntMan.GetComponent<TransformComponent>(SPlayer).GridUid!.Value;
            var mapSys = Server.System<SharedMapSystem>();
            var grid = SEntMan.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(gridUid);
            var tile = new Tile(1);

            // Lay tiles out to (0, 22) and (26, 0) so distant cameras & mobs sit on valid floor tiles.
            for (var y = -1; y <= 24; y++)
            {
                mapSys.SetTile(gridUid, grid, new Robust.Shared.Maths.Vector2i(0, y), tile);
                mapSys.SetTile(gridUid, grid, new Robust.Shared.Maths.Vector2i(1, y), tile);
            }
            for (var x = 0; x <= 26; x++)
            {
                mapSys.SetTile(gridUid, grid, new Robust.Shared.Maths.Vector2i(x, 0), tile);
            }

            // Spawn solid wall row between player and distant Security camera room to prevent direct LOS!
            SEntMan.SpawnEntity("WallSolid", new EntityCoordinates(gridUid, 0.5f, 4.5f));
            SEntMan.SpawnEntity("WallSolid", new EntityCoordinates(gridUid, 1.5f, 4.5f));
            SEntMan.SpawnEntity("WallSolid", new EntityCoordinates(gridUid, -0.5f, 4.5f));

            nearRouterHost = SEntMan.SpawnEntity("StationNetworkRouter", baseCoords);
            powerSys.SetNeedsPower(nearRouterHost, false);

            farRouterHost = SEntMan.SpawnEntity("StationNetworkRouter", new EntityCoordinates(gridUid, 24.5f, 0.5f));
            powerSys.SetNeedsPower(farRouterHost, false);

            monitorHost = SEntMan.SpawnEntity("ComputerSurveillanceCameraMonitor", baseCoords);
            powerSys.SetNeedsPower(monitorHost, false);
            monitorCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(monitorHost).ComputerEntity!.Value;

            // Security camera at (0.5, 19.5): closer to nearRouterHost (dist ~19) than farRouterHost (dist ~31),
            // and well beyond player's direct ExamineRange (16f) + blocked by WallSolid!
            secCamUid = SEntMan.SpawnEntity("SurveillanceCameraSecurity", new EntityCoordinates(gridUid, 0.5f, 19.5f));
            powerSys.SetNeedsPower(secCamUid, false);
            SEntMan.System<MetaDataSystem>().SetEntityName(secCamUid, "Sec Hallway Cam");

            // Engineering camera at (25.5, 0.5): right next to farRouterHost (dist ~1) -> Wi-Fi connects to farRouterHost, NOT nearRouterHost!
            farEngCamUid = SEntMan.SpawnEntity("SurveillanceCameraEngineering", new EntityCoordinates(gridUid, 25.5f, 0.5f));
            powerSys.SetNeedsPower(farEngCamUid, false);

            // Distant mob directly in view of Sec Hallway Cam (1m away from camera, ~20.5m from player behind walls!)
            subjectUid = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(gridUid, 0.5f, 20.5f));
            SEntMan.System<MetaDataSystem>().SetEntityName(subjectUid, "Suspicious Subject");

            micUpgradeUid = SEntMan.SpawnEntity("CameraMicrophoneUpgrade", baseCoords);
            dataDiskUid = SEntMan.SpawnEntity("ProgramDiskCameras", baseCoords);

            // InteractionTestMob is minimal and lacks ExaminerComponent; add it so we test real player examine rules.
            SEntMan.EnsureComponent<ExaminerComponent>(SPlayer);
        });
        await RunTicks(10);

        // 1) Verify migrated ComputerSurveillanceCameraMonitor runs /bin/cameras.nte, discovers Wi-Fi cameras via closest router,
        //    and BEFORE viewing the camera, player can examine nearby objects but CANNOT examine or physically reach the distant subject!
        await Server.WaitAssertion(() =>
        {
            var mComp = SEntMan.GetComponent<OxydComputerComponent>(monitorCompUid);
            Assert.That(mComp.IsBooted, Is.True);
            Assert.That(mComp.ActiveProgram, Is.Not.Null);
            camProgUid = mComp.ActiveProgram!.Value;
            Assert.That(SEntMan.HasComponent<SurveillanceCameraProgramComponent>(camProgUid), Is.True);
            Assert.That(SEntMan.HasComponent<Content.Server.SurveillanceCamera.SurveillanceCameraMonitorComponent>(monitorHost), Is.False);

            // Closest-router Wi-Fi routing verification!
            var secRouter = camSys.FindClosestRouterForCamera(secCamUid);
            Assert.That(secRouter, Is.Not.Null);
            Assert.That(secRouter!.Value.Owner, Is.EqualTo(nearRouterHost));

            var farRouter = camSys.FindClosestRouterForCamera(farEngCamUid);
            Assert.That(farRouter, Is.Not.Null);
            Assert.That(farRouter!.Value.Owner, Is.EqualTo(farRouterHost));

            // Sanity check: player can examine adjacent monitor, but CANNOT examine or physically reach distant mob behind walls
            Assert.That(examineSys.CanExamine(SPlayer, monitorHost), Is.True,
                "Player with ExaminerComponent must be able to examine adjacent objects.");
            Assert.That(examineSys.CanExamine(SPlayer, subjectUid), Is.False,
                "Player must NOT be able to examine a distant mob behind walls before viewing a camera.");
            Assert.That(interSys.InRangeUnobstructed(SPlayer, subjectUid), Is.False,
                "Player must NOT be within physical interaction range of the distant mob.");

            compSys.OpenComputerForActor(monitorCompUid, SPlayer);
        });
        await RunTicks(10);

        // Verify Client Camera UI renders and lists Sec Hallway Cam (NOT farEngCamUid on farRouterHost!)
        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var camUi = GetControlFromChildren<SurveillanceCameraUiControl>(_ => true, win);
            Assert.That(camUi, Is.Not.Null, "ComputerSurveillanceCameraMonitor must render SurveillanceCameraUiControl.");

            var vp = GetControlFromChildren<Content.Client.Viewport.ScalingViewport>(
                v => v.Name == "OxydCameraViewport", camUi!);
            Assert.That(vp, Is.Not.Null);
            Assert.That(vp!.MouseFilter, Is.EqualTo(Control.MouseFilterMode.Stop),
                "Camera ScalingViewport must have MouseFilter=Stop so players can Shift+Click / Right-Click to examine entities through it.");

            var camBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(camUi!, camBtns);
            Assert.That(camBtns.Any(b => b.Text != null && b.Text.Contains("Sec Hallway Cam")), Is.True,
                "Camera app must list Wi-Fi cameras connected to its closest StationNetworkRouter.");
            Assert.That(camBtns.Any(b => b.Text != null && b.Text.Contains("Engineering")), Is.False,
                "Camera app must NOT list cameras Wi-Fi connected to a different farther router.");
        });

        // 2) Shortfall 1: Camera monitoring requires authorization by swiping ID card or .acc file!
        //    First verify connecting without authorization is rejected, then authorize via .acc file and connect!
        await SendBui(ProgramUiKey.Key, new SurveillanceCameraActionMessage(
            SurveillanceCameraAction.SelectCamera, cameraEntity: FromServer(secCamUid)), FromServer(camProgUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var progComp = SEntMan.GetComponent<SurveillanceCameraProgramComponent>(camProgUid);
            Assert.That(progComp.IsAuthorized, Is.False, "Camera monitoring must start unauthorized.");
            Assert.That(progComp.ActiveCameraUids, Is.Empty, "Unauthorized camera monitor must block camera connection.");

            // Write a Security .acc file to /keys/sec_cam.acc on the monitor's hard drive
            Assert.That(fsSys.TryFindFileSystem(monitorCompUid, out var monDrive, out var monFs), Is.True);
            var secAcc = new AccessCodesVirtualFile
            {
                Name = "sec_cam",
                Extension = "acc",
                Directory = "/keys/",
                OwnerName = "Officer Vance",
                JobTitle = "Security Officer",
                AccessTags = new List<ProtoId<Content.Shared.Access.AccessLevelPrototype>> { "Security" }
            };
            Assert.That(fsSys.TryWriteFile(monDrive!.Value, secAcc, monFs!), Is.True);
        });

        await SendBui(ProgramUiKey.Key, new SurveillanceCameraActionMessage(
            SurveillanceCameraAction.AuthorizeWithAcc, zone: "/keys/sec_cam.acc"), FromServer(camProgUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var progComp = SEntMan.GetComponent<SurveillanceCameraProgramComponent>(camProgUid);
            Assert.That(progComp.IsAuthorized, Is.True, "Camera monitoring must authorize via valid .acc file.");
        });

        // Connect to Sec Hallway Cam -> Examine through camera WORKS, Details range WORKS,
        // while physical interaction reach remains strictly blocked!
        await SendBui(ProgramUiKey.Key, new SurveillanceCameraActionMessage(
            SurveillanceCameraAction.SelectCamera, cameraEntity: FromServer(secCamUid)), FromServer(camProgUid));
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<ActiveCameraViewerComponent>(SPlayer, out var viewer), Is.True,
                "Viewing a camera must grant ActiveCameraViewerComponent to the player.");
            Assert.That(viewer!.ActiveCameras, Contains.Item(SEntMan.GetNetEntity(secCamUid)));

            Assert.That(examineSys.CanExamine(SPlayer, subjectUid), Is.True,
                "Player actively viewing a camera MUST be able to examine people in the camera's line of sight.");
            Assert.That(examineSys.IsInDetailsRange(SPlayer, subjectUid), Is.True,
                "Examine details range must also resolve from the active camera when the target is near the camera.");
            Assert.That(interSys.InRangeUnobstructed(SPlayer, subjectUid), Is.False,
                "Viewing a camera must NEVER grant physical interaction range (anti-exploit).");
        });

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var camUi = GetControlFromChildren<SurveillanceCameraUiControl>(_ => true, win)!;
            var vp = GetControlFromChildren<Content.Client.Viewport.ScalingViewport>(
                v => v.Name == "OxydCameraViewport", camUi)!;
            Assert.That(vp.Visible, Is.True, "Camera viewport must become visible after selecting a camera.");
            Assert.That(vp.Eye, Is.Not.Null);
            Assert.That(vp.ViewportSize, Is.EqualTo(new Robust.Shared.Maths.Vector2i(480, 480)),
                "Camera viewport must render at full 480x480 native resolution.");
        });

        // 3) Shortfall 2: Bounded timestamped witness logs (damage, melee attacks, throwing) + FIFO memory cap + remote download + local disk extraction!
        await Server.WaitAssertion(() =>
        {
            var dmgSys = Server.System<Content.Shared.Damage.Systems.DamageableSystem>();
            var bluntProto = Server.ResolveDependency<IPrototypeManager>().Index<Content.Shared.Damage.Prototypes.DamageTypePrototype>("Blunt");
            dmgSys.TryChangeDamage(subjectUid, new Content.Shared.Damage.DamageSpecifier(bluntProto, Content.Shared.FixedPoint.FixedPoint2.New(15)), true);

            // Melee attack in front of camera
            var hitEv = new Content.Shared.Weapons.Melee.Events.MeleeHitEvent(
                new HashSet<EntityUid> { subjectUid },
                subjectUid,
                subjectUid,
                new Content.Shared.Damage.DamageSpecifier(bluntProto, Content.Shared.FixedPoint.FixedPoint2.New(2)),
                null);
            SEntMan.EventBus.RaiseLocalEvent(subjectUid, hitEv);

            // Throw an item in front of camera
            var thrownItem = SEntMan.SpawnEntity("Crowbar", SEntMan.GetComponent<TransformComponent>(subjectUid).Coordinates);
            var throwSys = Server.System<Content.Shared.Throwing.ThrowingSystem>();
            throwSys.TryThrow(thrownItem, new Vector2(1f, 0f), 5f, subjectUid);

            var logComp = SEntMan.GetComponent<SurveillanceCameraLogComponent>(secCamUid);
            Assert.That(logComp.Entries.Any(e => e.Category == "COMBAT" && e.Message.Contains("Suspicious Subject")), Is.True,
                "Camera must record timestamped [COMBAT] witness logs when someone is injured in its line of sight.");
            Assert.That(logComp.Entries.Any(e => e.Category == "WEAPON" && e.Message.Contains("Suspicious Subject")), Is.True,
                "Camera must record timestamped [WEAPON] witness logs when a melee attack occurs in its line of sight.");
            Assert.That(logComp.Entries.Any(e => e.Category == "THROW" && e.Message.Contains("Suspicious Subject") && e.Message.Contains("threw")), Is.True,
                "Camera must record timestamped [THROW] witness logs when someone throws an item in its line of sight.");

            // Verify FIFO bounded memory eviction when exceeding camera memory cap!
            var origMax = logComp.MaxMemoryBytes;
            logComp.MaxMemoryBytes = 256;
            for (var i = 0; i < 12; i++)
                camSys.AppendCameraLog((secCamUid, logComp), "SIGHT", $"Patrol checkpoint event #{i} witnessed in sector.");

            Assert.That(logComp.UsedMemoryBytes, Is.LessThanOrEqualTo(logComp.MaxMemoryBytes),
                "Camera witness log ring buffer must strictly enforce MaxMemoryBytes.");
            Assert.That(logComp.DroppedEntries, Is.GreaterThan(0),
                "Oldest witness logs must be evicted when camera memory capacity is exceeded.");
            logComp.MaxMemoryBytes = origMax;
        });

        // Download witness logs remotely via Camera App -> writes /logs/cam_*.txt onto monitor HDD!
        await SendBui(ProgramUiKey.Key, new SurveillanceCameraActionMessage(SurveillanceCameraAction.DownloadLogs), FromServer(camProgUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            Assert.That(fsSys.TryFindFileSystem(monitorCompUid, out var monDrive, out var monFs), Is.True);
            var remoteLog = monFs!.Files.Values.OfType<TextVirtualFile>()
                .FirstOrDefault(f => f.FullPath.StartsWith("/logs/cam_", StringComparison.OrdinalIgnoreCase));
            Assert.That(remoteLog, Is.Not.Null, "Downloading logs through /bin/cameras.nte must write a /logs/cam_*.txt file onto computer HDD.");
            Assert.That(remoteLog!.TextContent, Does.Contain("Sec Hallway Cam").And.Contain("Patrol checkpoint event #11"));

            // Local physical Data Disk extraction directly from the camera!
            var logComp = SEntMan.GetComponent<SurveillanceCameraLogComponent>(secCamUid);
            Assert.That(fsSys.TryFindFileSystem(dataDiskUid, out var diskDrive, out var diskFs), Is.True);
            Assert.That(camSys.TryDumpCameraLogsToDrive((secCamUid, logComp), diskDrive!.Value, diskFs!, out var dumpedPath), Is.True);
            Assert.That(fsSys.TryGetFile<TextVirtualFile>(diskDrive.Value, dumpedPath, out var diskFile, diskFs), Is.True);
            Assert.That(diskFile!.TextContent, Does.Contain("Sec Hallway Cam").And.Contain("Patrol checkpoint event #11"));
        });

        // 4) Shortfall 3: Camera Microphone Upgrade behind locked WiresPanel -> enables hearing, live audio & [AUDIO] witness logs!
        await Server.WaitAssertion(() =>
        {
            var logComp = SEntMan.GetComponent<SurveillanceCameraLogComponent>(secCamUid);
            Assert.That(logComp.HasMicrophoneUpgrade, Is.False, "Standard wallmount camera must start without microphone upgrade.");
            Assert.That(SEntMan.HasComponent<Content.Shared.Speech.Components.ActiveListenerComponent>(secCamUid), Is.False);

            // Before mic upgrade: speech near camera must NOT be heard or logged!
            var listenSys = Server.System<Content.Shared.Speech.EntitySystems.ListeningSystem>();
            listenSys.PingListeners(subjectUid, "Unheard secret before mic install", null);
            Assert.That(logComp.Entries.Any(e => e.Message.Contains("Unheard secret")), Is.False,
                "Un-upgraded camera must NOT hear or log speech.");

            // Panel is closed -> microphone slot MUST be locked!
            Assert.That(itemSlots.TryInsert(secCamUid, SurveillanceCameraLogComponent.MicrophoneSlotId, micUpgradeUid, SPlayer), Is.False,
                "Camera microphone upgrade slot must be locked while maintenance panel is closed.");

            // Open maintenance panel -> slot unlocks -> install CameraMicrophoneUpgrade!
            var wiresSys = Server.System<Content.Shared.Wires.SharedWiresSystem>();
            var panel = SEntMan.GetComponent<Content.Shared.Wires.WiresPanelComponent>(secCamUid);
            wiresSys.TogglePanel(secCamUid, panel, true);
            Assert.That(itemSlots.TryInsert(secCamUid, SurveillanceCameraLogComponent.MicrophoneSlotId, micUpgradeUid, SPlayer), Is.True,
                "Opening camera maintenance panel must allow installing CameraMicrophoneUpgrade.");

            Assert.That(logComp.HasMicrophoneUpgrade, Is.True);
            Assert.That(SEntMan.HasComponent<Content.Shared.Speech.Components.ActiveListenerComponent>(secCamUid), Is.True,
                "Installing CameraMicrophoneUpgrade must enable ActiveListenerComponent on the camera.");

            // Subject speaks near upgraded camera -> logged as [AUDIO] AND streamed live to SurveillanceCameraProgram!
            listenSys.PingListeners(subjectUid, "Meet me in maint at 04:00", null);

            Assert.That(logComp.Entries.Any(e => e.Category == "AUDIO" && e.Message.Contains("Meet me in maint at 04:00")), Is.True,
                "Upgraded camera must log overheard speech into its timestamped witness logs.");

            var progComp = SEntMan.GetComponent<SurveillanceCameraProgramComponent>(camProgUid);
            Assert.That(progComp.LiveAudioFeed.Any(l => l.Message.Contains("Meet me in maint at 04:00")), Is.True,
                "Upgraded camera must relay live overheard audio to the viewing SurveillanceCameraProgram.");
        });
        await RunTicks(8);

        Content.Client.UserInterface.Controls.ContentButton? logsToggleBtn = null;
        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var camUi = GetControlFromChildren<SurveillanceCameraUiControl>(_ => true, win)!;
            var audioBadge = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentLabel>(
                l => l.Name == "CameraAudioBadgeLabel", camUi);
            Assert.That(audioBadge!.Text, Is.EqualTo("AUDIO: ON"));
            var drawer = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentPanelContainer>(
                p => p.Name == "CameraInspectorDrawer", camUi);
            Assert.That(drawer!.Visible, Is.False,
                "Witness logs & audio tab must start closed by default so live camera feeds get maximum space.");
            logsToggleBtn = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentButton>(
                b => b.Name == "CameraLogsToggleBtn", camUi);
            Assert.That(logsToggleBtn, Is.Not.Null);
        });

        await ClickViaHitTest(logsToggleBtn!);
        await RunTicks(6);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var camUi = GetControlFromChildren<SurveillanceCameraUiControl>(_ => true, win)!;
            var audioPanel = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentPanelContainer>(
                p => p.Name == "CameraLiveAudioPanel", camUi);
            Assert.That(audioPanel!.VisibleInTree, Is.True,
                "Clicking the Logs top-bar button must open the Logs & Live Audio tab showing overheard audio.");
        });

        // 5) Disconnect -> ActiveCameraViewerComponent removed -> distant examine disabled again!
        await SendBui(ProgramUiKey.Key, new SurveillanceCameraActionMessage(SurveillanceCameraAction.Disconnect), FromServer(camProgUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<ActiveCameraViewerComponent>(SPlayer), Is.False,
                "Disconnecting from camera must remove ActiveCameraViewerComponent.");
            Assert.That(examineSys.CanExamine(SPlayer, subjectUid), Is.False,
                "After disconnecting from camera, distant mob must no longer be examinable.");
        });

        await Client.WaitPost(() =>
        {
            if (TryFindWindow(typeof(ComputerDisplayWindow), out var openWin))
                openWin!.Close();
        });
        await RunTicks(4);
    }

    [Test]
    public async Task TestRefinedWorkstationUiIconsAndMultiCameraSixFeedExactPovFit()
    {
        var compSys = SEntMan.System<OxydComputerSystem>();
        var examineSys = SEntMan.System<Content.Shared.Examine.ExamineSystemShared>();
        var powerSys = SEntMan.System<Content.Server.Power.EntitySystems.PowerReceiverSystem>();
        var metaSys = SEntMan.System<MetaDataSystem>();

        // =====================================================================
        // PART 1: REFINED WORKSTATION UI, LARGER DEFAULT SIZE, SINGLE TAB BAR & APP ICONS
        // =====================================================================
        EntityUid wsHost = default;
        EntityUid wsCompUid = default;

        await Server.WaitPost(() =>
        {
            var baseCoords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            wsHost = SEntMan.SpawnEntity("OxydWorkstationComputer", baseCoords);
            powerSys.SetNeedsPower(wsHost, false);
            wsCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(wsHost).ComputerEntity!.Value;
        });
        await RunTicks(10);

        await Server.WaitPost(() => compSys.OpenComputerForActor(wsCompUid, SPlayer));
        await RunTicks(10);

        Content.Client.UserInterface.Controls.ContentButton? noteRunBtn = null;
        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            Assert.That(win.SetSize.X, Is.GreaterThanOrEqualTo(1000f),
                "Workstation UI should open at a larger default width (>= 1000px).");
            Assert.That(win.SetSize.Y, Is.GreaterThanOrEqualTo(700f),
                "Workstation UI should open at a larger default height (>= 700px).");

            var vpUi = UiMan.GetUIController<Content.Client.UserInterface.Systems.Viewport.ViewportUIController>();
            Assert.That(win.SupportsViewportDock, Is.True,
                "Stationary Workstation console must support docking inside ViewportContainer.");
            Assert.That(win.IsDockedInViewport, Is.False,
                "Workstation console must NOT force focus mode when not viewing >2 cameras.");
            Assert.That(vpUi.IsWorldRenderPaused, Is.False,
                "Opening a normal Workstation console must leave normal world rendering active.");

            var osUi = GetControlFromChildren<OsUiControl>(_ => true, win);
            Assert.That(osUi, Is.Not.Null);

            var tabBar = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentWrapContainer>(
                c => c.Name == "WorkstationUnifiedTabBar", osUi!);
            Assert.That(tabBar, Is.Not.Null, "Workstation OS must use a single unified top tab bar.");

            var tabBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(tabBar!, tabBtns);
            Assert.That(tabBtns.Any(b => b.Text == "Home"), Is.True, "Unified tab bar must contain Home tab.");
            Assert.That(tabBtns.Any(b => b.Text == "System"), Is.True, "Unified tab bar must contain System tab.");
            Assert.That(tabBtns.Any(b => b.Text == "Apps" || b.Text == "Ports"), Is.False,
                "Old separate Apps and Ports tabs must be removed from Workstation OS.");

            // Desktop app grid must show app icons resolved locally from program prototypes
            var grid = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentWrapContainer>(
                c => c.Name == "DesktopAppsGrid", osUi!);
            Assert.That(grid, Is.Not.Null, "Workstation Desktop must render DesktopAppsGrid.");

            var appIcons = new List<Content.Client.UserInterface.Controls.ContentTextureRect>();
            CollectDescendants(grid!, appIcons);
            Assert.That(appIcons.Count, Is.GreaterThanOrEqualTo(3),
                "Installed Workstation apps must display icons on their launcher tiles.");
            Assert.That(appIcons.All(ic => ic.Texture != null), Is.True,
                "Every Workstation app tile icon must resolve to a valid texture.");

            noteRunBtn = FindRowRunButton(osUi!, "Notekeeper");
            Assert.That(noteRunBtn, Is.Not.Null, "Merged Workstation Desktop must let users launch apps directly.");

            AssertAllVisibleChildrenWithinViewport(win, osUi!, "Workstation Desktop");
        });

        // Launch Notekeeper -> inline running app tab with icon appears inside WorkstationUnifiedTabBar!
        await ClickViaHitTest(noteRunBtn!);
        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var tabBar = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentWrapContainer>(
                c => c.Name == "WorkstationUnifiedTabBar", win)!;
            var tabBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(tabBar, tabBtns);
            var runningTab = tabBtns.FirstOrDefault(b => b.Text != null && b.Text.Contains("Notekeeper", StringComparison.OrdinalIgnoreCase));
            Assert.That(runningTab, Is.Not.Null, "Running app tab must live inline inside the single unified tab bar.");
            Assert.That(runningTab!.Icon, Is.Not.Null, "Running app tab must render the app's icon.");
        });

        await Client.WaitPost(() =>
        {
            if (TryFindWindow(typeof(ComputerDisplayWindow), out var openWin))
                openWin!.Close();
        });
        await RunTicks(6);

        // =====================================================================
        // PART 2: CAMERA APP — UP TO 6 SIMULTANEOUS FEEDS, EXACT 1:1 POV FIT & ZERO OVERLAP
        // =====================================================================
        EntityUid routerHost = default;
        EntityUid monitorHost = default;
        EntityUid monitorCompUid = default;
        EntityUid camProgUid = default;
        var camUids = new List<EntityUid>();
        EntityUid distantMobA = default;
        EntityUid distantMobB = default;

        await Server.WaitPost(() =>
        {
            var baseCoords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            var gridUid = SEntMan.GetComponent<TransformComponent>(SPlayer).GridUid!.Value;
            var mapSys = Server.System<SharedMapSystem>();
            var grid = SEntMan.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(gridUid);
            var tile = new Tile(1);

            for (var y = -1; y <= 32; y++)
            {
                mapSys.SetTile(gridUid, grid, new Robust.Shared.Maths.Vector2i(0, y), tile);
                mapSys.SetTile(gridUid, grid, new Robust.Shared.Maths.Vector2i(1, y), tile);
                mapSys.SetTile(gridUid, grid, new Robust.Shared.Maths.Vector2i(2, y), tile);
            }

            // Place solid wall barrier so distant subjects are occluded from the player directly.
            SEntMan.SpawnEntity("WallSolid", new EntityCoordinates(gridUid, 0.5f, 4.5f));
            SEntMan.SpawnEntity("WallSolid", new EntityCoordinates(gridUid, 1.5f, 4.5f));
            SEntMan.SpawnEntity("WallSolid", new EntityCoordinates(gridUid, -0.5f, 4.5f));

            routerHost = SEntMan.SpawnEntity("StationNetworkRouter", baseCoords);
            powerSys.SetNeedsPower(routerHost, false);

            monitorHost = SEntMan.SpawnEntity("ComputerSurveillanceCameraMonitor", baseCoords);
            powerSys.SetNeedsPower(monitorHost, false);
            monitorCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(monitorHost).ComputerEntity!.Value;

            // Spawn 7 operational Wi-Fi cameras connected to the closest router.
            for (var i = 1; i <= 7; i++)
            {
                var yPos = i == 1 ? 18.5f : i == 6 ? 28.5f : 10.5f + i * 2f;
                var xPos = i is 1 or 6 ? 0.5f : 2.5f;
                var camUid = SEntMan.SpawnEntity("SurveillanceCameraGeneral",
                    new EntityCoordinates(gridUid, xPos, yPos));
                powerSys.SetNeedsPower(camUid, false);
                metaSys.SetEntityName(camUid, $"MultiCam {i:00}");
                camUids.Add(camUid);
            }

            distantMobA = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(gridUid, 0.5f, 19.5f));
            distantMobB = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(gridUid, 0.5f, 29.5f));

            SEntMan.EnsureComponent<ExaminerComponent>(SPlayer);

            Assert.That(examineSys.CanExamine(SPlayer, distantMobA), Is.False);
            Assert.That(examineSys.CanExamine(SPlayer, distantMobB), Is.False);
        });
        await RunTicks(10);

        await Server.WaitPost(() =>
        {
            var mComp = SEntMan.GetComponent<OxydComputerComponent>(monitorCompUid);
            camProgUid = mComp.ActiveProgram!.Value;

            // Authorize camera monitor by swiping a Security ID card on the monitor host!
            var secCard = SEntMan.SpawnEntity("SecurityIDCard", SEntMan.GetComponent<TransformComponent>(monitorHost).Coordinates);
            var swipeEv = new InteractUsingEvent(SPlayer, secCard, monitorHost, SEntMan.GetComponent<TransformComponent>(monitorHost).Coordinates);
            SEntMan.EventBus.RaiseLocalEvent(monitorHost, swipeEv);
            Assert.That(SEntMan.GetComponent<SurveillanceCameraProgramComponent>(camProgUid).IsAuthorized, Is.True,
                "Swiping a Security ID card on the camera monitor must authorize camera monitoring.");

            compSys.OpenComputerForActor(monitorCompUid, SPlayer);
        });
        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            Assert.That(win.SetSize.X, Is.GreaterThanOrEqualTo(1100f),
                "Camera Monitor should open larger by default to comfortably display multi-cam feeds.");
            Assert.That(win.SetSize.Y, Is.GreaterThanOrEqualTo(740f));

            var vpUi = UiMan.GetUIController<Content.Client.UserInterface.Systems.Viewport.ViewportUIController>();
            Assert.That(win.IsDockedInViewport, Is.False,
                "Camera Monitor with 0 cameras active must stay floating (not forced into focus mode).");
            Assert.That(vpUi.IsWorldRenderPaused, Is.False);
        });

        // Connect camera 1 -> must remain floating AND use maximum (480, 480) resolution in 1-camera view!
        await SendBui(ProgramUiKey.Key, new SurveillanceCameraActionMessage(
            SurveillanceCameraAction.SelectCamera, cameraEntity: FromServer(camUids[0])), FromServer(camProgUid));
        await RunTicks(8);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var vpUi = UiMan.GetUIController<Content.Client.UserInterface.Systems.Viewport.ViewportUIController>();
            Assert.That(win.IsDockedInViewport, Is.False);
            Assert.That(vpUi.IsWorldRenderPaused, Is.False);

            var camUi = GetControlFromChildren<SurveillanceCameraUiControl>(_ => true, win)!;
            var grid = GetControlFromChildren<Control>(c => c.Name == "CameraFeedGrid", camUi)!;
            var vps = new List<Content.Client.Viewport.ScalingViewport>();
            CollectDescendants(grid, vps);
            var activeVps = vps.Where(v => v.VisibleInTree).ToList();
            Assert.That(activeVps.Count, Is.EqualTo(1));
            Assert.That(activeVps[0].ViewportSize, Is.EqualTo(new Robust.Shared.Maths.Vector2i(480, 480)),
                "Single active camera view must always use maximum 480x480 resolution.");
        });

        // Connect camera 2 -> must remain floating (<= 2 cameras active)!
        await SendBui(ProgramUiKey.Key, new SurveillanceCameraActionMessage(
            SurveillanceCameraAction.SelectCamera, cameraEntity: FromServer(camUids[1])), FromServer(camProgUid));
        await RunTicks(8);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var vpUi = UiMan.GetUIController<Content.Client.UserInterface.Systems.Viewport.ViewportUIController>();
            Assert.That(win.IsDockedInViewport, Is.False,
                "Camera Monitor with 2 cameras active must stay floating (focus is forced only when >2 cameras are active).");
            Assert.That(vpUi.IsWorldRenderPaused, Is.False);
        });

        // Connect camera 3 (>2 cameras active!) -> must automatically force focus mode & pause world rendering!
        await SendBui(ProgramUiKey.Key, new SurveillanceCameraActionMessage(
            SurveillanceCameraAction.SelectCamera, cameraEntity: FromServer(camUids[2])), FromServer(camProgUid));
        await RunTicks(8);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var vpUi = UiMan.GetUIController<Content.Client.UserInterface.Systems.Viewport.ViewportUIController>();
            Assert.That(win.IsCameraFocusForced, Is.True,
                "Having >2 cameras active in the camera app must force focus mode.");
            Assert.That(win.IsDockedInViewport, Is.True,
                "Camera Monitor must automatically dock into focus mode once >2 cameras are active.");
            Assert.That(vpUi.IsWorldRenderPaused, Is.True,
                "Main world rendering must pause automatically once >2 cameras are active.");
            var winBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(win, winBtns);
            Assert.That(winBtns.Any(b => b.Name == "ViewportDockToggleButton"), Is.False,
                "Players must not have a button to manually unfocus when focus mode is forced.");

            Assert.That(vpUi.TryGetViewportFocusBounds(out var focusBounds), Is.True);
            Assert.That(focusBounds.Left, Is.EqualTo(0f).Within(1f),
                "Focus mode must cover over the entire left side of the screen (LeftRect).");
            Assert.That(focusBounds.Top, Is.EqualTo(0f).Within(1f));
            Assert.That(focusBounds.Bottom, Is.EqualTo(UiMan.RootControl.Size.Y).Within(2f),
                "Focus mode must cover down over the bottom hands/hotbar bar (LowerBar).");
            Assert.That(focusBounds.Right, Is.LessThan(UiMan.RootControl.Size.X - 200f),
                "Focus mode must stop at RightRect so the right-side Chat and StatPanel tabs stay uncovered and reachable.");
            Assert.That(win.SetSize.X, Is.EqualTo(focusBounds.Width).Within(1f));
            Assert.That(win.SetSize.Y, Is.EqualTo(focusBounds.Height).Within(1f));
        });

        // Connect cameras 4..6 simultaneously!
        for (var i = 3; i < 6; i++)
        {
            await SendBui(ProgramUiKey.Key, new SurveillanceCameraActionMessage(
                SurveillanceCameraAction.SelectCamera, cameraEntity: FromServer(camUids[i])), FromServer(camProgUid));
            await RunTicks(6);
        }
        await RunTicks(8);

        await Server.WaitAssertion(() =>
        {
            var progComp = SEntMan.GetComponent<SurveillanceCameraProgramComponent>(camProgUid);
            Assert.That(progComp.ActiveCameraUids.Count, Is.EqualTo(6),
                "Surveillance camera app must support watching up to 6 cameras simultaneously.");

            var viewer = SEntMan.GetComponent<ActiveCameraViewerComponent>(SPlayer);
            Assert.That(viewer.ActiveCameras.Count, Is.EqualTo(6));

            Assert.That(examineSys.CanExamine(SPlayer, distantMobA), Is.True,
                "Player must be able to examine entities visible through Camera #1 while 6 feeds are active.");
            Assert.That(examineSys.CanExamine(SPlayer, distantMobB), Is.True,
                "Player must ALSO be able to examine entities visible through Camera #6 simultaneously.");
        });

        // Attempt to add a 7th camera -> capped cleanly at 6 with status message!
        await SendBui(ProgramUiKey.Key, new SurveillanceCameraActionMessage(
            SurveillanceCameraAction.SelectCamera, cameraEntity: FromServer(camUids[6])), FromServer(camProgUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var progComp = SEntMan.GetComponent<SurveillanceCameraProgramComponent>(camProgUid);
            Assert.That(progComp.ActiveCameraUids.Count, Is.EqualTo(6),
                "Camera grid must stay capped at 6 simultaneous feeds.");
            Assert.That(progComp.StatusMessage, Does.Contain("6/6"));
        });

        // Verify all 6 viewports render simultaneously, each dynamically adapting its ViewportSize resolution
        // to fit its rectangular grid cell cleanly without dead bars, camera names are visible,
        // Logs tab starts collapsed, and zero overlap occurs!
        Content.Client.UserInterface.Controls.ContentButton? logsTabBtn = null;
        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var camUi = GetControlFromChildren<SurveillanceCameraUiControl>(_ => true, win)!;
            var grid = GetControlFromChildren<Control>(c => c.Name == "CameraFeedGrid", camUi)!;
            var drawer = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentPanelContainer>(
                p => p.Name == "CameraInspectorDrawer", camUi)!;
            logsTabBtn = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentButton>(
                b => b.Name == "CameraLogsToggleBtn", camUi)!;

            Assert.That(drawer.Visible, Is.False,
                "Logs tab must stay collapsed by default so the 6x camera grid gets full UI height.");
            Assert.That(grid.VisibleInTree, Is.True);

            // Verify removed top-bar clutter (no SURVEILLANCE CAMERAS header, no LIVE count badge, no Refresh button)
            var allCamLabels = new List<Content.Client.UserInterface.Controls.ContentLabel>();
            CollectDescendants(camUi, allCamLabels);
            Assert.That(allCamLabels.Any(l => l.VisibleInTree && l.Text != null &&
                (l.Text.Contains("SURVEILLANCE CAMERAS", StringComparison.OrdinalIgnoreCase) ||
                 l.Text.Contains("/6 LIVE", StringComparison.OrdinalIgnoreCase))), Is.False,
                "Camera app must not render the removed top title or live-camera counter.");

            var allCamBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(camUi, allCamBtns);
            Assert.That(allCamBtns.Any(b => b.VisibleInTree && b.Text != null &&
                b.Text.Equals("Refresh", StringComparison.OrdinalIgnoreCase)), Is.False,
                "Camera app must not render a manual Refresh button.");

            // Verify sidebar camera directory buttons actually render their camera names with non-zero width!
            var camListScroll = GetControlFromChildren<Control>(c => c.Name == "CameraListScroll", camUi)!;
            var sidebarBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(camListScroll, sidebarBtns);
            var namedSidebarBtns = sidebarBtns.Where(b => b.VisibleInTree && b.Text != null && b.Text.Contains("MultiCam")).ToList();
            Assert.That(namedSidebarBtns.Count, Is.EqualTo(7), "All 7 camera directory buttons must appear.");
            foreach (var sBtn in namedSidebarBtns)
            {
                Assert.That(sBtn.Label.Size.X, Is.GreaterThanOrEqualTo(60f),
                    $"Sidebar camera button '{sBtn.Text}' label must have real visible width (not collapsed to 0px).");
                Assert.That(sBtn.Label.Size.Y, Is.GreaterThanOrEqualTo(10f));
            }

            // Verify every active camera tile header displays its camera name with real visible width!
            var tileBtns = new List<Content.Client.UserInterface.Controls.ContentButton>();
            CollectDescendants(grid, tileBtns);
            var focusBtns = tileBtns.Where(b => b.Name == "CameraTileFocusBtn" && b.VisibleInTree).ToList();
            Assert.That(focusBtns.Count, Is.EqualTo(6));
            foreach (var fBtn in focusBtns)
            {
                Assert.That(fBtn.Text, Does.Contain("MultiCam"));
                Assert.That(fBtn.Label.Size.X, Is.GreaterThanOrEqualTo(40f),
                    $"Camera tile header '{fBtn.Text}' label must have visible non-zero width.");
            }

            var vps = new List<Content.Client.Viewport.ScalingViewport>();
            CollectDescendants(grid, vps);
            var activeVps = vps.Where(v => v.VisibleInTree).ToList();
            Assert.That(activeVps.Count, Is.EqualTo(6),
                "All 6 active camera feeds must render simultaneously inside CameraFeedGrid.");

            var frames = new List<Content.Client.UserInterface.Controls.ContentPanelContainer>();
            CollectDescendants(grid, frames);
            var vpFrames = frames.Where(f => f.Name == "CameraViewportFrame" && f.VisibleInTree).ToList();
            Assert.That(vpFrames.Count, Is.EqualTo(6));

            foreach (var vp in activeVps)
            {
                Assert.That(vp.MouseFilter, Is.EqualTo(Control.MouseFilterMode.Stop));
                Assert.That(vp.Eye, Is.Not.Null);
                Assert.That(vp.DeferRender, Is.True, "Native ScalingViewport camera feeds must use staggered deferred rendering.");
                Assert.That(Math.Max(vp.ViewportSize.X, vp.ViewportSize.Y), Is.EqualTo(480),
                    "Every camera feed must keep its major axis at full 480 max resolution.");
                Assert.That(Math.Min(vp.ViewportSize.X, vp.ViewportSize.Y), Is.InRange(192, 480),
                    "Every camera feed must dynamically scale its minor-axis resolution to fit its cell aspect ratio.");
            }

            foreach (var frame in vpFrames)
            {
                var vp = GetControlFromChildren<Content.Client.Viewport.ScalingViewport>(_ => true, frame)!;
                Assert.That(MathF.Abs(frame.Size.X - vp.Size.X), Is.LessThanOrEqualTo(3f),
                    $"CameraViewportFrame width ({frame.Size.X:0.#}) must hug ScalingViewport width ({vp.Size.X:0.#}) with zero dead bars.");
                Assert.That(MathF.Abs(frame.Size.Y - vp.Size.Y), Is.LessThanOrEqualTo(3f),
                    $"CameraViewportFrame height ({frame.Size.Y:0.#}) must hug ScalingViewport height ({vp.Size.Y:0.#}) with zero dead bars.");
            }

            AssertNoOverlappingSiblings(camUi, "CameraApp 6-cam normal size");
            AssertAllVisibleChildrenWithinViewport(win, camUi, "CameraApp 6-cam normal size");
        });

        // Clicking Logs button on top bar opens the Logs tab cleanly; clicking again returns to the 6x feeds!
        await ClickViaHitTest(logsTabBtn!);
        await RunTicks(6);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var camUi = GetControlFromChildren<SurveillanceCameraUiControl>(_ => true, win)!;
            var grid = GetControlFromChildren<Control>(c => c.Name == "CameraFeedGrid", camUi)!;
            var drawer = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentPanelContainer>(
                p => p.Name == "CameraInspectorDrawer", camUi)!;
            Assert.That(drawer.VisibleInTree, Is.True, "Clicking Logs top-bar button must open the Logs tab.");
            Assert.That(grid.Visible, Is.False, "Opening the full-space Logs tab must hide the feed grid.");
            AssertNoOverlappingSiblings(camUi, "CameraApp Logs tab open");
        });

        await ClickViaHitTest(logsTabBtn!);
        await RunTicks(6);

        Content.Client.UserInterface.Controls.ContentButton? soloBtn = null;
        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var vpUi = UiMan.GetUIController<Content.Client.UserInterface.Systems.Viewport.ViewportUIController>();
            var camUi = GetControlFromChildren<SurveillanceCameraUiControl>(_ => true, win)!;
            var grid = GetControlFromChildren<Control>(c => c.Name == "CameraFeedGrid", camUi)!;
            var drawer = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentPanelContainer>(
                p => p.Name == "CameraInspectorDrawer", camUi)!;
            soloBtn = GetControlFromChildren<Content.Client.UserInterface.Controls.ContentButton>(
                b => b.Name == "CameraSoloToggleBtn", camUi)!;

            Assert.That(drawer.Visible, Is.False);
            Assert.That(grid.VisibleInTree, Is.True);
            Assert.That(win.IsDockedInViewport, Is.True);
            Assert.That(vpUi.IsWorldRenderPaused, Is.True);
        });

        // Clicking Solo while 6 cameras are active focuses on 1 camera view -> must use full maximum (480, 480)
        // resolution AND keep focus mode forced!
        await ClickViaHitTest(soloBtn!);
        await RunTicks(6);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var vpUi = UiMan.GetUIController<Content.Client.UserInterface.Systems.Viewport.ViewportUIController>();
            var camUi = GetControlFromChildren<SurveillanceCameraUiControl>(_ => true, win)!;
            var grid = GetControlFromChildren<Control>(c => c.Name == "CameraFeedGrid", camUi)!;

            Assert.That(win.IsDockedInViewport, Is.True, "Solo view with >2 selected cameras must remain in forced focus mode.");
            Assert.That(vpUi.IsWorldRenderPaused, Is.True);

            var vps = new List<Content.Client.Viewport.ScalingViewport>();
            CollectDescendants(grid, vps);
            var activeVps = vps.Where(v => v.VisibleInTree).ToList();
            Assert.That(activeVps.Count, Is.EqualTo(1));
            Assert.That(activeVps[0].ViewportSize, Is.EqualTo(new Robust.Shared.Maths.Vector2i(480, 480)),
                "Focusing on 1 camera view (Solo) when multiple cameras are selected must use maximum 480x480 resolution.");
            Assert.That(MathF.Abs(activeVps[0].Size.X - activeVps[0].Size.Y), Is.LessThanOrEqualTo(1.5f),
                "Solo 1-camera view must render as a 1:1 square matching its 480x480 POV.");
        });

        // Return to Grid view -> all 6 cameras visible again!
        await ClickViaHitTest(soloBtn!);
        await RunTicks(6);

        // Disconnect just Camera #2 -> 5 cameras stay streaming & both distant mobs remain examinable!
        await SendBui(ProgramUiKey.Key, new SurveillanceCameraActionMessage(
            SurveillanceCameraAction.DisconnectCamera, cameraEntity: FromServer(camUids[1])), FromServer(camProgUid));
        await RunTicks(8);

        await Server.WaitAssertion(() =>
        {
            var progComp = SEntMan.GetComponent<SurveillanceCameraProgramComponent>(camProgUid);
            Assert.That(progComp.ActiveCameraUids.Count, Is.EqualTo(5),
                "Disconnecting one camera must keep the other 5 feeds active.");
            Assert.That(examineSys.CanExamine(SPlayer, distantMobA), Is.True);
            Assert.That(examineSys.CanExamine(SPlayer, distantMobB), Is.True);
        });

        // Safety safeguard: dealing damage to the player while focused in a docked console must
        // automatically close the docked display and immediately resume normal world rendering!
        await Server.WaitPost(() =>
        {
            SEntMan.EnsureComponent<Content.Shared.Damage.Components.DamageableComponent>(SPlayer);
            SEntMan.EnsureComponent<Content.Shared.Damage.Components.InjurableComponent>(SPlayer);
            var dmgSys = Server.System<Content.Shared.Damage.Systems.DamageableSystem>();
            var bluntProto = Server.ResolveDependency<IPrototypeManager>().Index<Content.Shared.Damage.Prototypes.DamageTypePrototype>("Blunt");
            dmgSys.TryChangeDamage(SPlayer, new Content.Shared.Damage.DamageSpecifier(bluntProto, Content.Shared.FixedPoint.FixedPoint2.New(5)));
        });
        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            var vpUi = UiMan.GetUIController<Content.Client.UserInterface.Systems.Viewport.ViewportUIController>();
            Assert.That(vpUi.IsWorldRenderPaused, Is.False,
                "Taking damage while focused in a docked console must auto-exit focus and resume world rendering.");
            Assert.That(TryFindWindow(typeof(ComputerDisplayWindow), out _), Is.False,
                "Docked console window must close automatically when the player takes interrupting damage.");
        });
        await RunTicks(4);
    }

    private static void AssertNoOverlappingSiblings(Control root, string context)
    {
        var containers = new List<Control>();
        CollectDescendants(root, containers);

        foreach (var container in containers)
        {
            if (!container.VisibleInTree)
                continue;

            var checkContainer = container is Content.Client.UserInterface.Controls.ContentBoxContainer ||
                                 container.Name == "CameraFeedGrid" ||
                                 (container.Name != null && container.Name.StartsWith("CameraFeedTile_", StringComparison.Ordinal));
            if (!checkContainer)
                continue;

            var visChildren = new List<Control>();
            foreach (var child in container.Children)
            {
                if (child.VisibleInTree && child.Size.X > 1f && child.Size.Y > 1f)
                    visChildren.Add(child);
            }

            for (var i = 0; i < visChildren.Count; i++)
            {
                var a = visChildren[i];
                var aBox = Robust.Shared.Maths.UIBox2.FromDimensions(a.GlobalPosition, a.Size);
                for (var j = i + 1; j < visChildren.Count; j++)
                {
                    var b = visChildren[j];
                    var bBox = Robust.Shared.Maths.UIBox2.FromDimensions(b.GlobalPosition, b.Size);

                    var overlapW = MathF.Min(aBox.Right, bBox.Right) - MathF.Max(aBox.Left, bBox.Left);
                    var overlapH = MathF.Min(aBox.Bottom, bBox.Bottom) - MathF.Max(aBox.Top, bBox.Top);
                    Assert.That(overlapW > 1.5f && overlapH > 1.5f, Is.False,
                        $"[{context}] Overlapping siblings inside {container.GetType().Name}({container.Name}): " +
                        $"{a.GetType().Name}({a.Name}) {aBox} vs {b.GetType().Name}({b.Name}) {bBox}.");
                }
            }
        }
    }

    private static void AssertAllVisibleChildrenWithinViewport(ComputerDisplayWindow win, Control root, string context)
    {
        var viewportRight = win.ScreenInsetControl.GlobalPosition.X + win.ScreenInsetControl.Size.X;
        var all = new List<Control>();
        CollectDescendants(root, all);

        foreach (var ctrl in all)
        {
            if (!ctrl.VisibleInTree || ctrl.Size.X <= 0f || ctrl.Size.Y <= 0f)
                continue;

            // Only verify interactive/leaf UI elements (buttons, labels, line edits, wrap containers)
            if (ctrl is not (Content.Client.UserInterface.Controls.ContentButton or
                Content.Client.UserInterface.Controls.ContentLabel or
                Content.Client.UserInterface.Controls.ContentLineEdit or
                Content.Client.UserInterface.Controls.ContentWrapContainer))
                continue;

            var right = ctrl.GlobalPosition.X + ctrl.Size.X;
            Assert.That(right, Is.LessThanOrEqualTo(viewportRight + 2f),
                $"[{context}] Control {ctrl.GetType().Name} overflowed viewport right edge ({right:0.#} > {viewportRight:0.#}).");
        }
    }

    [Test]
    public async Task TestPortedConsolesBankCargoRecordsPhotosIdAccAndPerApcAuth()
    {
        var compSys = Server.System<OxydComputerSystem>();
        var fsSys = Server.System<SharedFileSystemSystem>();
        var powerSys = Server.System<Content.Server.Power.EntitySystems.PowerReceiverSystem>();
        var cargoSys = Server.System<Content.Server.Cargo.Systems.CargoSystem>();
        var accessSys = Server.System<Content.Shared.Access.Systems.SharedAccessSystem>();
        var itemSlotsSys = Server.System<Content.Shared.Containers.ItemSlots.ItemSlotsSystem>();

        var gridUid = SEntMan.GetComponent<TransformComponent>(SPlayer).GridUid!.Value;
        var baseCoords = new EntityCoordinates(gridUid, 0.5f, 0.5f);

        EntityUid routerHost = default;
        EntityUid dataServerHost = default;
        EntityUid stationRecHost = default;
        EntityUid crimRecHost = default;
        EntityUid cargoHost = default;
        EntityUid idConsoleHost = default;
        EntityUid powerConsoleHost = default;
        EntityUid atmosConsoleHost = default;
        EntityUid apcAUid = default;
        EntityUid apcBUid = default;
        EntityUid alarmAUid = default;
        EntityUid alarmBUid = default;
        EntityUid targetIdCardUid = default;
        EntityUid engIdCardUid = default;

        await Server.WaitPost(() =>
        {
            for (var x = 0; x <= 14; x++)
            {
                for (var y = 0; y <= 14; y++)
                    Server.System<SharedMapSystem>().SetTile(gridUid, SEntMan.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(gridUid), new Robust.Shared.Maths.Vector2i(x, y), new Tile(1));
            }

            routerHost = SEntMan.SpawnEntity("StationNetworkRouter", baseCoords);
            powerSys.SetNeedsPower(routerHost, false);

            dataServerHost = SEntMan.SpawnEntity("StationRecordsServer", new EntityCoordinates(gridUid, 1.5f, 0.5f));
            powerSys.SetNeedsPower(dataServerHost, false);

            stationRecHost = SEntMan.SpawnEntity("ComputerStationRecords", new EntityCoordinates(gridUid, 2.5f, 0.5f));
            powerSys.SetNeedsPower(stationRecHost, false);

            crimRecHost = SEntMan.SpawnEntity("ComputerCriminalRecords", new EntityCoordinates(gridUid, 3.5f, 0.5f));
            powerSys.SetNeedsPower(crimRecHost, false);

            cargoHost = SEntMan.SpawnEntity("ComputerCargoOrders", new EntityCoordinates(gridUid, 4.5f, 0.5f));
            powerSys.SetNeedsPower(cargoHost, false);

            idConsoleHost = SEntMan.SpawnEntity("ComputerId", new EntityCoordinates(gridUid, 5.5f, 0.5f));
            powerSys.SetNeedsPower(idConsoleHost, false);

            powerConsoleHost = SEntMan.SpawnEntity("ComputerPowerMonitoring", new EntityCoordinates(gridUid, 6.5f, 0.5f));
            powerSys.SetNeedsPower(powerConsoleHost, false);

            atmosConsoleHost = SEntMan.SpawnEntity("ComputerAlert", new EntityCoordinates(gridUid, 7.5f, 0.5f));
            powerSys.SetNeedsPower(atmosConsoleHost, false);

            apcAUid = SEntMan.SpawnEntity("APCBasic", new EntityCoordinates(gridUid, 2.5f, 4.5f));
            SEntMan.System<MetaDataSystem>().SetEntityName(apcAUid, "APC Alpha");

            apcBUid = SEntMan.SpawnEntity("APCBasic", new EntityCoordinates(gridUid, 8.5f, 4.5f));
            SEntMan.System<MetaDataSystem>().SetEntityName(apcBUid, "APC Beta");

            alarmAUid = SEntMan.SpawnEntity("AirAlarm", new EntityCoordinates(gridUid, 2.5f, 5.5f));
            powerSys.SetNeedsPower(alarmAUid, false);
            SEntMan.System<MetaDataSystem>().SetEntityName(alarmAUid, "Air Alarm Alpha");

            alarmBUid = SEntMan.SpawnEntity("AirAlarm", new EntityCoordinates(gridUid, 8.5f, 5.5f));
            powerSys.SetNeedsPower(alarmBUid, false);
            SEntMan.System<MetaDataSystem>().SetEntityName(alarmBUid, "Air Alarm Beta");

            targetIdCardUid = SEntMan.SpawnEntity("PassengerIDCard", baseCoords);
            engIdCardUid = SEntMan.SpawnEntity("EngineeringIDCard", baseCoords);
        });
        await RunTicks(10);

        // =====================================================================
        // 1) CARGO ORDERS: Bank Account ID + PIN Auth & Money Withdrawal
        // =====================================================================
        EntityUid cargoCompUid = default;
        EntityUid cargoProgUid = default;
        string firstProductId = string.Empty;
        int firstProductCost = 0;

        await Server.WaitAssertion(() =>
        {
            cargoCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(cargoHost).ComputerEntity!.Value;
            var cComp = SEntMan.GetComponent<OxydComputerComponent>(cargoCompUid);
            Assert.That(cComp.IsBooted, Is.True);
            cargoProgUid = cComp.ActiveProgram!.Value;
            Assert.That(SEntMan.HasComponent<CargoOrdersProgramComponent>(cargoProgUid), Is.True,
                "Ported ComputerCargoOrders must boot into CargoOrdersProgramComponent.");

            var protoMan = Server.ResolveDependency<IPrototypeManager>();
            var prod = protoMan.EnumeratePrototypes<Content.Shared.Cargo.Prototypes.CargoProductPrototype>()
                .First(p => p.Group == "market" && p.Cost > 0 && p.Cost <= 1000);
            firstProductId = prod.ID;
            firstProductCost = prod.Cost;
        });

        await Client.WaitPost(() => { while (TryFindWindow<ComputerDisplayWindow>(out var staleWin)) staleWin.Close(); });
        await Server.WaitPost(() => { SEntMan.System<SharedTransformSystem>().SetCoordinates(SPlayer, SEntMan.GetComponent<TransformComponent>(cargoHost).Coordinates); compSys.OpenComputerForActor(SEntMan.GetComponent<EmbeddedComputerHostComponent>(cargoHost).ComputerEntity!.Value, SPlayer); });
        await RunTicks(10);

        // Ordering without Bank Account ID + PIN must be rejected and not deduct funds
        await SendBui(ProgramUiKey.Key, new CargoOrdersActionMessage(
            CargoOrdersAction.PlaceAndPayOrder,
            productId: firstProductId,
            quantity: 1), FromServer(cargoProgUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var bankComp = SEntMan.GetComponent<Content.Shared.Cargo.Components.StationBankAccountComponent>(gridUid);
            Assert.That(bankComp.Accounts["Cargo"], Is.EqualTo(2000),
                "Unauthenticated cargo order must NOT withdraw money from bank account.");
        });

        // Wrong PIN must fail authentication
        await SendBui(ProgramUiKey.Key, new CargoOrdersActionMessage(
            CargoOrdersAction.AuthenticateBank,
            accountId: "CARGO-01",
            pin: "9999"), FromServer(cargoProgUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var progComp = SEntMan.GetComponent<CargoOrdersProgramComponent>(cargoProgUid);
            Assert.That(progComp.IsAuthenticated, Is.False, "Wrong bank PIN must not authenticate.");
        });

        // Valid Bank Account ID ("CARGO-01") + PIN ("1111") must authenticate and withdraw money during order!
        await SendBui(ProgramUiKey.Key, new CargoOrdersActionMessage(
            CargoOrdersAction.AuthenticateBank,
            accountId: "CARGO-01",
            pin: "1111"), FromServer(cargoProgUid));
        await RunTicks(6);

        await SendBui(ProgramUiKey.Key, new CargoOrdersActionMessage(
            CargoOrdersAction.PlaceAndPayOrder,
            productId: firstProductId,
            quantity: 1,
            requester: "QM Bell",
            reason: "Station supply test"), FromServer(cargoProgUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var progComp = SEntMan.GetComponent<CargoOrdersProgramComponent>(cargoProgUid);
            Assert.That(progComp.IsAuthenticated, Is.True);

            var bankComp = SEntMan.GetComponent<Content.Shared.Cargo.Components.StationBankAccountComponent>(gridUid);
            Assert.That(bankComp.Accounts["Cargo"], Is.EqualTo(2000 - firstProductCost),
                "Authenticated cargo order must withdraw the order cost from the authenticated bank account.");
        });

        // =====================================================================
        // 2 & 3) CREW RECORDS HEADSHOT & SECURITY RECORDS GRAY JUMPSUIT + BLACK SHOES PHOTO
        // =====================================================================
        EntityUid stationRecCompUid = default;
        EntityUid stationRecProgUid = default;
        EntityUid crimRecCompUid = default;
        EntityUid crimRecProgUid = default;

        await Client.WaitPost(() => { while (TryFindWindow<ComputerDisplayWindow>(out var staleWin)) staleWin.Close(); });
        await Server.WaitPost(() =>
        {
            // Seed a crew record on the DataServer so both Station Records and Criminal Records display a subject
            var dsCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(dataServerHost).ComputerEntity!.Value;
            fsSys.TryFindFileSystem(dsCompUid, out var dsDrive, out var dsFs);

            var stDb = new DatabaseVirtualFile
            {
                Name = "station_records",
                Extension = "db",
                Directory = "/records/",
                TableName = "station_records"
            };
            stDb.Data["__station"] = "NSS Oxyd";
            stDb.Data["rec_1"] = RecordsDbCodec.EncodeStationRecordRow(
                "Alice Mercer", "Scientist", "Scientist", "JobIconScientist", 29, "Human", "Female", "FP-101", "DNA-101");
            fsSys.TryWriteFile(dsDrive!.Value, stDb, dsFs!);

            var crDb = new DatabaseVirtualFile
            {
                Name = "criminal_records",
                Extension = "db",
                Directory = "/records/",
                TableName = "criminal_records"
            };
            crDb.Data["crim_1"] = RecordsDbCodec.EncodeCriminalRecordRow(
                "Alice Mercer", "Scientist", Content.Shared.Security.SecurityStatus.Suspected, "Contraband possession", "Sec", new List<CrimeHistoryEntryDto>());
            fsSys.TryWriteFile(dsDrive.Value, crDb, dsFs);

            stationRecCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(stationRecHost).ComputerEntity!.Value;
            stationRecProgUid = SEntMan.GetComponent<OxydComputerComponent>(stationRecCompUid).ActiveProgram!.Value;
            SEntMan.System<SharedTransformSystem>().SetCoordinates(SPlayer, SEntMan.GetComponent<TransformComponent>(stationRecHost).Coordinates); compSys.OpenComputerForActor(stationRecCompUid, SPlayer);
        });
        await RunTicks(10);

        await SendBui(ProgramUiKey.Key, new StationRecordsActionMessage(StationRecordsAction.Refresh), FromServer(stationRecProgUid));
        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var stUi = GetControlFromChildren<StationRecordsUiControl>(_ => true, win);
            Assert.That(stUi, Is.Not.Null);
            Assert.That(stUi!.HeadshotControl, Is.Not.Null);
            Assert.That(stUi.HeadshotControl.IsHeadshot, Is.True, "Crew Records must showcase a headshot.");
            Assert.That(stUi.HeadshotControl.WearPrisonAttire, Is.False);
            Assert.That(CEntMan.EntityExists(stUi.HeadshotControl.PreviewDummy), Is.True,
                "Crew Records headshot control must spawn and render a valid subject preview dummy.");
            win.Close();
        });
        await RunTicks(6);

        await Client.WaitPost(() => { while (TryFindWindow<ComputerDisplayWindow>(out var staleWin)) staleWin.Close(); });
        await Server.WaitPost(() =>
        {
            crimRecCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(crimRecHost).ComputerEntity!.Value;
            crimRecProgUid = SEntMan.GetComponent<OxydComputerComponent>(crimRecCompUid).ActiveProgram!.Value;
            SEntMan.System<SharedTransformSystem>().SetCoordinates(SPlayer, SEntMan.GetComponent<TransformComponent>(crimRecHost).Coordinates); compSys.OpenComputerForActor(crimRecCompUid, SPlayer);
        });
        await RunTicks(10);

        await SendBui(ProgramUiKey.Key, new CriminalRecordsActionMessage(CriminalRecordsAction.Refresh), FromServer(crimRecProgUid));
        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            var win = GetWindow<ComputerDisplayWindow>();
            var crUi = GetControlFromChildren<CriminalRecordsUiControl>(_ => true, win);
            Assert.That(crUi, Is.Not.Null);
            Assert.That(crUi!.SubjectPhotoControl, Is.Not.Null);
            Assert.That(crUi.SubjectPhotoControl.IsHeadshot, Is.False,
                "Security Records must showcase a full-body photo of the entire subject.");
            Assert.That(crUi.SubjectPhotoControl.WearPrisonAttire, Is.True);
            Assert.That(CEntMan.EntityExists(crUi.SubjectPhotoControl.PreviewDummy), Is.True,
                "Security Records photo control must spawn a valid full-body preview dummy.");
            Assert.That(crUi.SubjectPhotoControl.EquippedJumpsuitPrototype,
                Is.EqualTo(RecordSubjectPhotoControl.PrisonJumpsuitProtoId),
                "Security Records subject photo must wear a gray jumpsuit (ClothingUniformJumpsuitColorGrey).");
            Assert.That(crUi.SubjectPhotoControl.EquippedShoesPrototype,
                Is.EqualTo(RecordSubjectPhotoControl.PrisonShoesProtoId),
                "Security Records subject photo must wear black shoes (ClothingShoesColorBlack).");
            win.Close();
        });
        await RunTicks(6);

        // =====================================================================
        // 4) ID AUTHORIZATION / MODIFICATION CONSOLE PROGRAM WITH .ACC FILES
        // =====================================================================
        EntityUid idCompUid = default;
        EntityUid idProgUid = default;
        EntityUid removedSecondSlotModule = default;
        string originalTargetName = string.Empty;

        await Server.WaitPost(() =>
        {
            idCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(idConsoleHost).ComputerEntity!.Value;
            idProgUid = SEntMan.GetComponent<OxydComputerComponent>(idCompUid).ActiveProgram!.Value;
            Assert.That(SEntMan.HasComponent<AccessEncoderProgramComponent>(idProgUid), Is.True,
                "Ported ComputerId must run the ID Authorization & Modification program.");

            originalTargetName = SEntMan.GetComponent<Content.Shared.Access.Components.IdCardComponent>(targetIdCardUid).FullName ?? string.Empty;

            // ID modification requires TWO card slot hardware modules: one for the modifying ID, one for the target ID.
            var cardSys = Server.System<Content.Server._Oxyd.Computers.Hardware.ComputerCardReaderSystem>();
            var idSlots = cardSys.GetSlotReaders(idCompUid);
            Assert.That(idSlots, Has.Count.EqualTo(2), "ID console must have exactly two ID card slot hardware modules.");
            Assert.That(cardSys.GetReaders(idCompUid), Has.Count.EqualTo(2));
            Assert.That(itemSlotsSys.TryGetSlot(idSlots[0].Owner, idSlots[0].Comp.CardSlotId, out _), Is.True);

            // Write an Engineering + Command .acc file to /keys/chief.acc on ComputerId's hard drive
            Assert.That(fsSys.TryFindFileSystem(idCompUid, out var idDrive, out var idFs), Is.True);
            var chiefAcc = new AccessCodesVirtualFile
            {
                Name = "chief",
                Extension = "acc",
                Directory = "/keys/",
                OwnerName = "CE Sterling",
                JobTitle = "Chief Engineer",
                AccessTags = new List<ProtoId<Content.Shared.Access.AccessLevelPrototype>>
                {
                    "Engineering",
                    "Atmospherics",
                    "ChiefEngineer"
                }
            };
            Assert.That(fsSys.TryWriteFile(idDrive!.Value, chiefAcc, idFs!), Is.True);

            // Remove the second card slot hardware module -> with only 1/2 card slot modules, ID modification MUST be rejected!
            removedSecondSlotModule = idSlots[1].Owner;
            Assert.That(cardSys.TryRemoveReaderModule(idCompUid, removedSecondSlotModule, null, idConsoleHost), Is.True);
            Assert.That(cardSys.GetSlotReaders(idCompUid), Has.Count.EqualTo(1));

            var idHostCoords = SEntMan.GetComponent<TransformComponent>(idConsoleHost).Coordinates;
            var singleSlotSwipe = new InteractUsingEvent(SPlayer, targetIdCardUid, idConsoleHost, idHostCoords);
            SEntMan.EventBus.RaiseLocalEvent(idConsoleHost, singleSlotSwipe);
        });
        await RunTicks(6);

        await Client.WaitPost(() => { while (TryFindWindow<ComputerDisplayWindow>(out var staleWin)) staleWin.Close(); });
        await Server.WaitPost(() => { SEntMan.System<SharedTransformSystem>().SetCoordinates(SPlayer, SEntMan.GetComponent<TransformComponent>(idConsoleHost).Coordinates); compSys.OpenComputerForActor(SEntMan.GetComponent<EmbeddedComputerHostComponent>(idConsoleHost).ComputerEntity!.Value, SPlayer); });
        await RunTicks(10);

        await SendBui(ProgramUiKey.Key, new AccessEncoderActionMessage(
            AccessEncoderAction.SelectFile, "/keys/chief.acc"), FromServer(idProgUid));
        await RunTicks(4);
        await SendBui(ProgramUiKey.Key, new AccessEncoderActionMessage(
            AccessEncoderAction.WriteTargetIdentity, "Should Fail", "Unauthorized"), FromServer(idProgUid));
        await RunTicks(4);
        await SendBui(ProgramUiKey.Key, new AccessEncoderActionMessage(
            AccessEncoderAction.ApplyAccFileToTarget, "/keys/chief.acc"), FromServer(idProgUid));
        await RunTicks(4);

        await Server.WaitAssertion(() =>
        {
            var idCard = SEntMan.GetComponent<Content.Shared.Access.Components.IdCardComponent>(targetIdCardUid);
            Assert.That(idCard.FullName ?? string.Empty, Is.EqualTo(originalTargetName),
                "ID modification must be rejected when fewer than 2 ID card slot hardware modules are installed.");
            var tags = accessSys.TryGetTags(targetIdCardUid)!.ToList();
            Assert.That(tags, Does.Not.Contain(new ProtoId<Content.Shared.Access.AccessLevelPrototype>("ChiefEngineer")),
                "Applying .acc access tags must be rejected when fewer than 2 ID card slot hardware modules are installed.");

            // Reinstall the 2nd card slot hardware module, eject target card from slot #1, and insert Modifying ID (#1) + Target ID (#2)!
            var cardSys = Server.System<Content.Server._Oxyd.Computers.Hardware.ComputerCardReaderSystem>();
            var remainingSlots = cardSys.GetSlotReaders(idCompUid);
            Assert.That(cardSys.TryEjectCard(remainingSlots[0], null, idConsoleHost), Is.True);
            Assert.That(cardSys.TryInstallReaderModule(idCompUid, removedSecondSlotModule), Is.True);
            Assert.That(cardSys.GetSlotReaders(idCompUid), Has.Count.EqualTo(2));

            var idHostCoords = SEntMan.GetComponent<TransformComponent>(idConsoleHost).Coordinates;
            var firstSwipe = new InteractUsingEvent(SPlayer, engIdCardUid, idConsoleHost, idHostCoords);
            SEntMan.EventBus.RaiseLocalEvent(idConsoleHost, firstSwipe);
            var secondSwipe = new InteractUsingEvent(SPlayer, targetIdCardUid, idConsoleHost, idHostCoords);
            SEntMan.EventBus.RaiseLocalEvent(idConsoleHost, secondSwipe);
            Assert.That(firstSwipe.Handled, Is.True, "Presenting an ID card on the ID console must be handled by card slot hardware.");
            Assert.That(cardSys.TryGetSlotCard(idCompUid, 0, out var modifyingCard) && modifyingCard == engIdCardUid, Is.True,
                "Modifying ID must be retained by the first card slot module.");
            Assert.That(cardSys.TryGetSlotCard(idCompUid, 1, out var targetCard) && targetCard == targetIdCardUid, Is.True,
                "Target ID must be retained by the second card slot module.");
        });
        await RunTicks(6);

        // Test SwapSlots action swaps Modifying ID <-> Target ID and back!
        await SendBui(ProgramUiKey.Key, new AccessEncoderActionMessage(AccessEncoderAction.SwapSlots), FromServer(idProgUid));
        await RunTicks(4);
        await Server.WaitAssertion(() =>
        {
            var cardSys = Server.System<Content.Server._Oxyd.Computers.Hardware.ComputerCardReaderSystem>();
            Assert.That(cardSys.TryGetSlotCard(idCompUid, 0, out var slot0) && slot0 == targetIdCardUid, Is.True);
            Assert.That(cardSys.TryGetSlotCard(idCompUid, 1, out var slot1) && slot1 == engIdCardUid, Is.True);
        });
        await SendBui(ProgramUiKey.Key, new AccessEncoderActionMessage(AccessEncoderAction.SwapSlots), FromServer(idProgUid));
        await RunTicks(4);

        // Select /keys/chief.acc as the authorization source, update target ID identity, and apply .acc access codes!
        await SendBui(ProgramUiKey.Key, new AccessEncoderActionMessage(
            AccessEncoderAction.SelectFile, "/keys/chief.acc"), FromServer(idProgUid));
        await RunTicks(4);

        await SendBui(ProgramUiKey.Key, new AccessEncoderActionMessage(
            AccessEncoderAction.WriteTargetIdentity, " Jordan Vega", "Senior Engineer"), FromServer(idProgUid));
        await RunTicks(4);

        await SendBui(ProgramUiKey.Key, new AccessEncoderActionMessage(
            AccessEncoderAction.ApplyAccFileToTarget, "/keys/chief.acc"), FromServer(idProgUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            var idCard = SEntMan.GetComponent<Content.Shared.Access.Components.IdCardComponent>(targetIdCardUid);
            Assert.That(idCard.FullName, Is.EqualTo("Jordan Vega"),
                "ID console program must update target ID card full name when authorized by a .acc file.");
            Assert.That(idCard.LocalizedJobTitle, Is.EqualTo("Senior Engineer"),
                "ID console program must update target ID card job title when authorized by a .acc file.");

            var tags = accessSys.TryGetTags(targetIdCardUid)!.ToList();
            Assert.That(tags, Contains.Item(new ProtoId<Content.Shared.Access.AccessLevelPrototype>("Engineering")));
            Assert.That(tags, Contains.Item(new ProtoId<Content.Shared.Access.AccessLevelPrototype>("Atmospherics")));
            Assert.That(tags, Contains.Item(new ProtoId<Content.Shared.Access.AccessLevelPrototype>("ChiefEngineer")));

            // Both cards can be ejected from their card slot modules again.
            var ejectSys = Server.System<Content.Server._Oxyd.Computers.Hardware.ComputerCardReaderSystem>();
            var ejectSlots = ejectSys.GetSlotReaders(idCompUid);
            Assert.That(ejectSys.TryEjectCard(ejectSlots[0], SPlayer), Is.True);
            Assert.That(ejectSys.TryEjectCard(ejectSlots[1], SPlayer), Is.True);
            Assert.That(ejectSys.TryGetAnyRetainedCard(idCompUid, out _), Is.False);
        });

        // =====================================================================
        // 5) ENGINEERING CONTROL PROGRAMS: Per-APC Authorization via .acc or ID Swipe
        // =====================================================================
        EntityUid powerCompUid = default;
        EntityUid powerProgUid = default;
        EntityUid atmosCompUid = default;
        EntityUid atmosProgUid = default;
        string apcAGuid = string.Empty;
        string apcBGuid = string.Empty;
        string alarmBGuid = string.Empty;
        bool initialApcABreaker = false;
        bool initialApcBBreaker = false;

        await Server.WaitPost(() =>
        {
            powerCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(powerConsoleHost).ComputerEntity!.Value;
            powerProgUid = SEntMan.GetComponent<OxydComputerComponent>(powerCompUid).ActiveProgram!.Value;
            Assert.That(SEntMan.HasComponent<ApcControlProgramComponent>(powerProgUid), Is.True);

            atmosCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(atmosConsoleHost).ComputerEntity!.Value;
            atmosProgUid = SEntMan.GetComponent<OxydComputerComponent>(atmosCompUid).ActiveProgram!.Value;
            Assert.That(SEntMan.HasComponent<AtmosControlProgramComponent>(atmosProgUid), Is.True);

            var routerSys = Server.System<Content.Server._Oxyd.Computers.Network.NetworkRouterSystem>();
            var apcACompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(apcAUid).ComputerEntity!.Value;
            var apcBCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(apcBUid).ComputerEntity!.Value;
            var alarmBCompUid = SEntMan.GetComponent<EmbeddedComputerHostComponent>(alarmBUid).ComputerEntity!.Value;
            apcAGuid = routerSys.EnsureDeviceGuid(apcACompUid);
            apcBGuid = routerSys.EnsureDeviceGuid(apcBCompUid);
            alarmBGuid = routerSys.EnsureDeviceGuid(alarmBCompUid);

            initialApcABreaker = SEntMan.GetComponent<Content.Server.Power.Components.ApcComponent>(apcAUid).MainBreakerEnabled;
            initialApcBBreaker = SEntMan.GetComponent<Content.Server.Power.Components.ApcComponent>(apcBUid).MainBreakerEnabled;

            // Put an Engineering .acc file on powerConsoleHost's drive
            Assert.That(fsSys.TryFindFileSystem(powerCompUid, out var pDrive, out var pFs), Is.True);
            var engAcc = new AccessCodesVirtualFile
            {
                Name = "eng",
                Extension = "acc",
                Directory = "/keys/",
                OwnerName = "Eng Tech",
                JobTitle = "Station Engineer",
                AccessTags = new List<ProtoId<Content.Shared.Access.AccessLevelPrototype>> { "Engineering", "Atmospherics" }
            };
            Assert.That(fsSys.TryWriteFile(pDrive!.Value, engAcc, pFs!), Is.True);
        });
        await RunTicks(6);

        // Select APC Alpha on Power Console -> try toggling breaker before authorization -> must be blocked!
        await Client.WaitPost(() => { while (TryFindWindow<ComputerDisplayWindow>(out var staleWin)) staleWin.Close(); });
        await Server.WaitPost(() => { SEntMan.System<SharedTransformSystem>().SetCoordinates(SPlayer, SEntMan.GetComponent<TransformComponent>(powerConsoleHost).Coordinates); compSys.OpenComputerForActor(SEntMan.GetComponent<EmbeddedComputerHostComponent>(powerConsoleHost).ComputerEntity!.Value, SPlayer); });
        await RunTicks(10);
        await SendBui(ProgramUiKey.Key, new ApcControlActionMessage(ApcControlAction.SelectTarget, apcAGuid), FromServer(powerProgUid));
        await RunTicks(4);
        await SendBui(ProgramUiKey.Key, new ApcControlActionMessage(ApcControlAction.ToggleBreaker), FromServer(powerProgUid));
        await RunTicks(4);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<Content.Server.Power.Components.ApcComponent>(apcAUid).MainBreakerEnabled,
                Is.EqualTo(initialApcABreaker),
                "Remote APC Control must block breaker toggle before authorizing to that APC.");
        });

        // Authorize APC Alpha using /keys/eng.acc -> now toggling APC Alpha's breaker succeeds!
        await SendBui(ProgramUiKey.Key, new ApcControlActionMessage(ApcControlAction.AuthorizeWithAcc, "/keys/eng.acc"), FromServer(powerProgUid));
        await RunTicks(4);
        await SendBui(ProgramUiKey.Key, new ApcControlActionMessage(ApcControlAction.ToggleBreaker), FromServer(powerProgUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<Content.Server.Power.Components.ApcComponent>(apcAUid).MainBreakerEnabled,
                Is.EqualTo(!initialApcABreaker),
                "Authorizing to APC Alpha via .acc file must allow toggling APC Alpha's breaker.");
        });

        // Switch to APC Beta -> APC Beta must STILL require its own authorization!
        await SendBui(ProgramUiKey.Key, new ApcControlActionMessage(ApcControlAction.SelectTarget, apcBGuid), FromServer(powerProgUid));
        await RunTicks(4);
        await SendBui(ProgramUiKey.Key, new ApcControlActionMessage(ApcControlAction.ToggleBreaker), FromServer(powerProgUid));
        await RunTicks(4);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<Content.Server.Power.Components.ApcComponent>(apcBUid).MainBreakerEnabled,
                Is.EqualTo(initialApcBBreaker),
                "Authorizing APC Alpha must NOT automatically authorize APC Beta (per-APC authorization required).");

            // Now authorize APC Beta by swiping an Engineering ID card on powerConsoleHost!
            var swipeEv = new InteractUsingEvent(SPlayer, engIdCardUid, powerConsoleHost, baseCoords);
            SEntMan.EventBus.RaiseLocalEvent(powerConsoleHost, swipeEv);
        });
        await RunTicks(4);

        await SendBui(ProgramUiKey.Key, new ApcControlActionMessage(ApcControlAction.ToggleBreaker), FromServer(powerProgUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<Content.Server.Power.Components.ApcComponent>(apcBUid).MainBreakerEnabled,
                Is.EqualTo(!initialApcBBreaker),
                "Swiping an Engineering ID card while APC Beta is selected must authorize APC Beta.");
        });

        // Verify Remote Atmos Control (ComputerAlert) also requires per-APC authorization via ID swipe or .acc file!
        await Client.WaitPost(() => { while (TryFindWindow<ComputerDisplayWindow>(out var staleWin)) staleWin.Close(); });
        await Server.WaitPost(() => { SEntMan.System<SharedTransformSystem>().SetCoordinates(SPlayer, SEntMan.GetComponent<TransformComponent>(atmosConsoleHost).Coordinates); compSys.OpenComputerForActor(SEntMan.GetComponent<EmbeddedComputerHostComponent>(atmosConsoleHost).ComputerEntity!.Value, SPlayer); });
        await RunTicks(10);
        await SendBui(ProgramUiKey.Key, new AtmosControlActionMessage(AtmosControlAction.SelectTarget, alarmBGuid), FromServer(atmosProgUid));
        await RunTicks(4);
        await SendBui(ProgramUiKey.Key, new AtmosControlActionMessage(AtmosControlAction.SetModePanic), FromServer(atmosProgUid));
        await RunTicks(4);

        EntityUid removedAtmosScanner = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<Content.Server.Atmos.Monitor.Components.AirAlarmComponent>(alarmBUid).CurrentMode,
                Is.Not.EqualTo(AirAlarmMode.Panic),
                "Remote Atmos Control must block mode changes before authorizing to that area's APC.");

            // Remove the Card Scanner hardware module from atmosConsoleHost -> swiping ID card must NOT authorize!
            var cardSys = Server.System<Content.Server._Oxyd.Computers.Hardware.ComputerCardReaderSystem>();
            var readers = cardSys.GetReaders(atmosCompUid);
            Assert.That(readers, Has.Count.EqualTo(1));
            removedAtmosScanner = readers[0].Owner;
            Assert.That(cardSys.TryRemoveReaderModule(atmosCompUid, removedAtmosScanner, null, atmosConsoleHost), Is.True);
            Assert.That(cardSys.HasReaders(atmosCompUid), Is.False);

            var noReaderSwipe = new InteractUsingEvent(SPlayer, engIdCardUid, atmosConsoleHost, baseCoords);
            SEntMan.EventBus.RaiseLocalEvent(atmosConsoleHost, noReaderSwipe);
        });
        await RunTicks(4);

        await SendBui(ProgramUiKey.Key, new AtmosControlActionMessage(AtmosControlAction.SetModePanic), FromServer(atmosProgUid));
        await RunTicks(4);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<Content.Server.Atmos.Monitor.Components.AirAlarmComponent>(alarmBUid).CurrentMode,
                Is.Not.EqualTo(AirAlarmMode.Panic),
                "Swiping an ID card on a console with no Card Scanner / Card Slot hardware installed must NOT authorize.");

            // Reinstall the Card Scanner hardware module and swipe Engineering ID card -> now it authorizes!
            var cardSys = Server.System<Content.Server._Oxyd.Computers.Hardware.ComputerCardReaderSystem>();
            Assert.That(cardSys.TryInstallReaderModule(atmosCompUid, removedAtmosScanner), Is.True);
            var swipeEv = new InteractUsingEvent(SPlayer, engIdCardUid, atmosConsoleHost, baseCoords);
            SEntMan.EventBus.RaiseLocalEvent(atmosConsoleHost, swipeEv);
        });
        await RunTicks(4);

        await SendBui(ProgramUiKey.Key, new AtmosControlActionMessage(AtmosControlAction.SetModePanic), FromServer(atmosProgUid));
        await RunTicks(6);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<Content.Server.Atmos.Monitor.Components.AirAlarmComponent>(alarmBUid).CurrentMode,
                Is.EqualTo(AirAlarmMode.Panic),
                "After authorizing to APC Beta via ID swipe, Remote Atmos Control must switch Air Alarm Beta to Panic mode.");
        });
    }
}