using System.Linq;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Servers;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.DeviceNetwork.Events;
using Content.Shared.DeviceNetwork.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.Network;

public sealed partial class NetworkCommandSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private SharedDeviceGuidSystem _guidSystem = default!;
    [Dependency] private NetworkBandwidthSystem _bandwidth = default!;
    [Dependency] private DeviceNetworkSystem _deviceNetwork = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DeviceNetworkComponent, DeviceNetworkPacketEvent<PingPayload>>(OnPingReceived);
        SubscribeLocalEvent<DeviceNetworkComponent, DeviceNetworkPacketEvent<FileDownloadRequestPayload>>(OnFileDownloadRequest);
        SubscribeLocalEvent<DeviceNetworkComponent, DeviceNetworkPacketEvent<FileDeleteRequestPayload>>(OnFileDeleteRequest);
        SubscribeLocalEvent<DeviceNetworkComponent, DeviceNetworkPacketEvent<RemoteControlRequestPayload>>(OnRemoteControlRequest);
    }

    private void OnPingReceived(EntityUid uid, DeviceNetworkComponent comp, ref DeviceNetworkPacketEvent<PingPayload> args)
    {
        var myGuid = _guidSystem.GetGuid(uid);
        TryComp<NetworkBandwidthComponent>(uid, out var bandwidth);

        var pong = new PongPayload
        {
            TargetGuid = myGuid,
            SentTime = args.Data.SentTime,
            IsSaturated = bandwidth?.IsSaturated ?? false,
            QueuedPackets = bandwidth?.CurrentQueuedPackets ?? 0,
            BandwidthLimitBps = bandwidth?.MaxBps ?? 100000
        };

        _deviceNetwork.SendPacket(uid, args.SenderAddress, ref pong);
    }

    private void OnFileDownloadRequest(EntityUid uid, DeviceNetworkComponent comp, ref DeviceNetworkPacketEvent<FileDownloadRequestPayload> args)
    {
        var request = args.Data;

        // Check authentication if this is a configured DataServer
        if (TryComp<DataServerComponent>(uid, out var serverComp))
        {
            var authorized = serverComp.AcceptsPublicReads || request.AuthToken == serverComp.NetworkMasterKey;
            if (!authorized)
                return;
        }

        if (!TryComp<FileSystemComponent>(uid, out var fsComp))
            return;

        if (_fileSystem.TryGetFile<VirtualFile>(uid, request.TargetPath, out var file, fsComp))
        {
            var content = string.Empty;
            if (file is TextVirtualFile tf)
                content = tf.TextContent;
            else if (file is DatabaseVirtualFile db)
                content = string.Join(";", db.Data.Select(kv => $"{kv.Key}={kv.Value}"));
            else if (file is RecipeBlueprintFile rFile)
                content = $"Recipe={rFile.Recipe};Uses={rFile.RemainingUses};Encrypted={rFile.IsEncrypted}";

            var response = new FileTransferPayload
            {
                Path = file.Directory,
                FileName = file.Name,
                Extension = file.Extension,
                FileContent = content,
                AuthToken = request.AuthToken
            };

            // Account for outgoing bandwidth load
            _bandwidth.RecordDataTransmitted(uid, file.Size + content.Length);

            _deviceNetwork.SendPacket(uid, args.SenderAddress, ref response);
        }
    }

    private void OnFileDeleteRequest(EntityUid uid, DeviceNetworkComponent comp, ref DeviceNetworkPacketEvent<FileDeleteRequestPayload> args)
    {
        var request = args.Data;
        var success = false;
        var message = "File not found or permission denied.";

        if (TryComp<DataServerComponent>(uid, out var serverComp))
        {
            if (request.AuthToken != serverComp.NetworkMasterKey)
            {
                var deniedResp = new FileDeleteResponsePayload
                {
                    TargetPath = request.TargetPath,
                    Success = false,
                    Message = "Access Denied: Invalid Master Key"
                };
                _deviceNetwork.SendPacket(uid, args.SenderAddress, ref deniedResp);
                return;
            }
        }

        if (TryComp<FileSystemComponent>(uid, out var fsComp))
        {
            if (_fileSystem.TryGetFile<VirtualFile>(uid, request.TargetPath, out var file, fsComp))
            {
                if (file.IsReadOnly)
                {
                    message = "Error: File is marked Read-Only.";
                }
                else
                {
                    success = _fileSystem.TryDeleteFile(uid, request.TargetPath, fsComp);
                    message = success ? $"Deleted {request.TargetPath}" : "Error: Deletion failed.";
                }
            }
        }

        var response = new FileDeleteResponsePayload
        {
            TargetPath = request.TargetPath,
            Success = success,
            Message = message
        };

        _deviceNetwork.SendPacket(uid, args.SenderAddress, ref response);
    }

    private void OnRemoteControlRequest(EntityUid uid, DeviceNetworkComponent comp, ref DeviceNetworkPacketEvent<RemoteControlRequestPayload> args)
    {
        var request = args.Data;
        var success = true;
        var resultMessage = string.Empty;

        // Check authentication if a master key is expected
        if (TryComp<DataServerComponent>(uid, out var serverComp) && !string.IsNullOrEmpty(serverComp.NetworkMasterKey))
        {
            if (request.AuthToken != serverComp.NetworkMasterKey)
            {
                var authFail = new RemoteControlResponsePayload
                {
                    Command = request.Command,
                    Success = false,
                    ResultMessage = "Access Denied: Invalid Authentication Token"
                };
                _deviceNetwork.SendPacket(uid, args.SenderAddress, ref authFail);
                return;
            }
        }

        switch (request.Command.ToLowerInvariant())
        {
            case "status":
                TryComp<ComputerStatsComponent>(uid, out var stats);
                TryComp<NetworkBandwidthComponent>(uid, out var bw);
                resultMessage = $"CPU: {stats?.CpuFrequencyMhz ?? 100}MHz | RAM: {stats?.RamMb ?? 64}MB | Queue: {bw?.CurrentQueuedPackets ?? 0} | Saturated: {bw?.IsSaturated ?? false}";
                break;
            case "reboot":
                if (TryComp<NetworkBandwidthComponent>(uid, out var resetBw))
                {
                    resetBw.CurrentQueuedPackets = 0;
                    resetBw.IsSaturated = false;
                    Dirty(uid, resetBw);
                }
                resultMessage = "Interface rebooted. Network buffers cleared.";
                break;
            default:
                resultMessage = $"Command '{request.Command}' acknowledged.";
                break;
        }

        var response = new RemoteControlResponsePayload
        {
            Command = request.Command,
            Success = success,
            ResultMessage = resultMessage
        };

        _deviceNetwork.SendPacket(uid, args.SenderAddress, ref response);
    }
}
