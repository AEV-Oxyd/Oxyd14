using System.Linq;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared.DeviceNetwork.Events;
using Content.Shared.DeviceNetwork.Systems;

namespace Content.Server._Oxyd.Computers.Network;

public sealed partial class NetworkCommandSystem : EntitySystem
{
    [Dependency] private DeviceNetworkSystem _deviceNet = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private SharedDeviceGuidSystem _guid = default!;
    [Dependency] private NetworkBandwidthSystem _bandwidth = default!;
    [Dependency] private OxydComputerSystem _computer = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OxydComputerComponent, DeviceNetworkPacketEvent<PingPayload>>(OnPingReceived);
        SubscribeLocalEvent<OxydComputerComponent, DeviceNetworkPacketEvent<FileDownloadRequestPayload>>(OnFileDownloadRequest);
        SubscribeLocalEvent<OxydComputerComponent, DeviceNetworkPacketEvent<FileTransferPayload>>(OnFileTransferReceived);
        SubscribeLocalEvent<OxydComputerComponent, DeviceNetworkPacketEvent<FileDeleteRequestPayload>>(OnFileDeleteRequest);
        SubscribeLocalEvent<OxydComputerComponent, DeviceNetworkPacketEvent<RemoteControlRequestPayload>>(OnRemoteControlRequest);
    }

    private void OnPingReceived(EntityUid uid, OxydComputerComponent comp, ref DeviceNetworkPacketEvent<PingPayload> args)
    {
        if (comp.NetworkWireCut)
            return;

        TryComp<NetworkBandwidthComponent>(uid, out var bw);
        var reply = new PongPayload
        {
            TargetGuid = _guid.GetGuid(uid),
            SentTime = args.Data.SentTime,
            IsSaturated = bw?.IsSaturated ?? false,
            QueuedPackets = bw?.CurrentQueuedPackets ?? 0,
            BandwidthLimitBps = bw?.MaxBps ?? 0
        };
        _deviceNet.SendPacket(uid, args.SenderAddress, ref reply, args.Frequency);
    }

    private void OnFileDownloadRequest(EntityUid uid, OxydComputerComponent comp, ref DeviceNetworkPacketEvent<FileDownloadRequestPayload> args)
    {
        if (comp.NetworkWireCut || !_fileSystem.TryFindFileSystem(uid, out var driveUid, out var fsComp))
            return;

        if (!_fileSystem.TryGetFile<VirtualFile>(driveUid.Value, args.Data.TargetPath, out var file, fsComp))
            return;

        var content = file switch
        {
            TextVirtualFile txt => txt.TextContent,
            DatabaseVirtualFile db => string.Join("\n", db.Data.Select(kv => $"{kv.Key}={kv.Value}")),
            RecipeBlueprintFile bp => bp.Recipes.Count > 0
                ? string.Join(",", bp.Recipes.Select(r => r.Id))
                : bp.Recipe?.Id ?? string.Empty,
            ProgramVirtualFile prog => $"program:{prog.ProgramId}:{prog.Title}",
            _ => string.Empty
        };

        _bandwidth.RecordDataTransmitted(uid, file.Size, isUpload: true);
        var resp = new FileTransferPayload
        {
            Path = file.Directory,
            FileName = file.Name,
            Extension = file.Extension,
            FileContent = content,
            AuthToken = args.Data.AuthToken
        };
        _deviceNet.SendPacket(uid, args.SenderAddress, ref resp, args.Frequency);
    }

    private void OnFileTransferReceived(EntityUid uid, OxydComputerComponent comp, ref DeviceNetworkPacketEvent<FileTransferPayload> args)
    {
        if (comp.NetworkWireCut || !_fileSystem.TryFindFileSystem(uid, out var driveUid, out var fsComp))
            return;

        var file = new TextVirtualFile
        {
            Directory = string.IsNullOrWhiteSpace(args.Data.Path) ? "/" : args.Data.Path,
            Name = string.IsNullOrWhiteSpace(args.Data.FileName) ? "upload" : args.Data.FileName,
            Extension = string.IsNullOrWhiteSpace(args.Data.Extension) ? "txt" : args.Data.Extension,
            TextContent = args.Data.FileContent,
            Size = Math.Max(16, args.Data.FileContent.Length)
        };

        if (_fileSystem.TryWriteFile(driveUid.Value, file, fsComp))
        {
            _bandwidth.RecordDataTransmitted(uid, file.Size, isUpload: false);
            _fileSystem.LogSystemActivity(driveUid.Value, "NET-UPLOAD", $"Received upload {file.FullPath} from {args.SenderAddress}");
        }
    }

    private void OnFileDeleteRequest(EntityUid uid, OxydComputerComponent comp, ref DeviceNetworkPacketEvent<FileDeleteRequestPayload> args)
    {
        if (comp.NetworkWireCut || !_fileSystem.TryFindFileSystem(uid, out var driveUid, out var fsComp))
            return;

        var success = _fileSystem.TryDeleteFile(driveUid.Value, args.Data.TargetPath, fsComp);
        var resp = new FileDeleteResponsePayload
        {
            TargetPath = args.Data.TargetPath,
            Success = success,
            Message = success ? "File deleted." : "File locked or not found."
        };
        _deviceNet.SendPacket(uid, args.SenderAddress, ref resp, args.Frequency);
    }

    private void OnRemoteControlRequest(EntityUid uid, OxydComputerComponent comp, ref DeviceNetworkPacketEvent<RemoteControlRequestPayload> args)
    {
        if (comp.NetworkWireCut || comp.RemoteControlWireCut || !comp.AllowRemoteControl)
        {
            var err = new RemoteControlResponsePayload
            {
                Command = args.Data.Command,
                Success = false,
                ResultMessage = "Remote control disabled or network severed."
            };
            _deviceNet.SendPacket(uid, args.SenderAddress, ref err, args.Frequency);
            return;
        }

        var handled = _computer.ExecuteComputerCommand(uid, args.Data.Command, out var output);
        var ack = new RemoteControlResponsePayload
        {
            Command = args.Data.Command,
            Success = handled,
            ResultMessage = handled ? output : $"Unknown command '{args.Data.Command}'."
        };
        _deviceNet.SendPacket(uid, args.SenderAddress, ref ack, args.Frequency);
    }
}
