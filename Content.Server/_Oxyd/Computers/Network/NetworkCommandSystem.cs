using System.Linq;
using System.Text;
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
    [Dependency] private NetworkRouterSystem _router = default!;
    [Dependency] private OxydComputerSystem _computer = default!;

    private const int MaxRemotePayloadChars = 32768;

    [SubscribeLocalEvent]
    private void OnPingReceived(Entity<OxydComputerComponent> ent, ref DeviceNetworkPacketEvent<PingPayload> args)
    {
        var uid = ent.Owner;
        var comp = ent.Comp;
        if (comp.NetworkWireCut || !comp.IsBooted)
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

    [SubscribeLocalEvent]
    private void OnFileDownloadRequest(Entity<OxydComputerComponent> ent, ref DeviceNetworkPacketEvent<FileDownloadRequestPayload> args)
    {
        var uid = ent.Owner;
        var comp = ent.Comp;
        if (!ValidateRemotePacketAccess(uid, comp, args.Data.AuthToken, requirePassword: true))
            return;

        if (!_fileSystem.TryFindFileSystem(uid, out var driveUid, out var fsComp))
            return;

        var cleanPath = SharedFileSystemSystem.NormalizePath(args.Data.TargetPath);
        if (!_fileSystem.TryGetFile<VirtualFile>(driveUid.Value, cleanPath, out var file, fsComp))
            return;

        if (_fileSystem.IsCopyProtectedBlueprint(file))
            return;

        // Never leak raw access tags from encrypted .acc files over device network.
        if (file is AccessCodesVirtualFile accFile && accFile.IsEncrypted)
            return;

        var content = file switch
        {
            TextVirtualFile txt => txt.TextContent,
            DatabaseVirtualFile db => string.Join("\n", db.Data.Select(kv => $"{kv.Key}={kv.Value}")),
            RecipeBlueprintFile bp => bp.Recipes.Count > 0
                ? string.Join(",", bp.Recipes.Select(r => r.Id))
                : bp.Recipe?.Id ?? string.Empty,
            ProgramVirtualFile prog => $"program:{prog.ProgramId}:{prog.Title}",
            AccessCodesVirtualFile acc => $"access:{acc.OwnerName}:{string.Join(",", acc.AccessTags.Select(t => t.Id))}",
            _ => string.Empty
        };

        var senderAddr = args.SenderAddress;
        var freq = args.Frequency;
        var authToken = args.Data.AuthToken;
        var dir = file.Directory;
        var name = file.Name;
        var ext = file.Extension;
        var size = Math.Max(64, file.Size);

        var router = _router.FindNearestRouter(uid);
        if (router != null && Exists(args.Sender))
        {
            var dstComp = _router.UnwrapComputerEntity(args.Sender);
            _bandwidth.EnqueueRouterTransfer(router.Value, new QueuedRouterTransfer
            {
                Protocol = "DEVNET/GET",
                TargetPath = cleanPath,
                Description = $"DevNet Download {cleanPath}",
                IsUpload = true,
                SourceCompUid = uid,
                TargetCompUid = dstComp,
                TotalBytes = size,
                OnCompleted = _ =>
                {
                    if (!Exists(uid))
                        return;
                    var resp = new FileTransferPayload
                    {
                        Path = dir,
                        FileName = name,
                        Extension = ext,
                        FileContent = content,
                        AuthToken = authToken
                    };
                    _deviceNet.SendPacket(uid, senderAddr, ref resp, freq);
                }
            });
            return;
        }

        _bandwidth.RecordDataTransmitted(uid, size, isUpload: true);
        var immediateResp = new FileTransferPayload
        {
            Path = dir,
            FileName = name,
            Extension = ext,
            FileContent = content,
            AuthToken = authToken
        };
        _deviceNet.SendPacket(uid, senderAddr, ref immediateResp, freq);
    }

    [SubscribeLocalEvent]
    private void OnFileTransferReceived(Entity<OxydComputerComponent> ent, ref DeviceNetworkPacketEvent<FileTransferPayload> args)
    {
        var uid = ent.Owner;
        var comp = ent.Comp;
        if (!ValidateRemotePacketAccess(uid, comp, args.Data.AuthToken, requirePassword: true))
            return;

        if (!_fileSystem.TryFindFileSystem(uid, out var driveUid, out var fsComp))
            return;

        var rawContent = args.Data.FileContent ?? string.Empty;
        if (rawContent.Length > MaxRemotePayloadChars)
            rawContent = rawContent[..MaxRemotePayloadChars];

        var file = new TextVirtualFile
        {
            Directory = SharedFileSystemSystem.NormalizeDirectory(string.IsNullOrWhiteSpace(args.Data.Path) ? "/" : args.Data.Path),
            Name = string.IsNullOrWhiteSpace(args.Data.FileName) ? "upload" : args.Data.FileName,
            Extension = string.IsNullOrWhiteSpace(args.Data.Extension) ? "txt" : args.Data.Extension,
            TextContent = rawContent,
            Size = Math.Max(16, Encoding.UTF8.GetByteCount(rawContent))
        };

        var senderAddr = args.SenderAddress;
        var router = _router.FindNearestRouter(uid);
        if (router != null && Exists(args.Sender))
        {
            var srcComp = _router.UnwrapComputerEntity(args.Sender);
            _bandwidth.EnqueueRouterTransfer(router.Value, new QueuedRouterTransfer
            {
                Protocol = "DEVNET/PUT",
                TargetPath = file.FullPath,
                Description = $"DevNet Upload {file.FullPath}",
                IsUpload = false,
                SourceCompUid = srcComp,
                TargetCompUid = uid,
                TotalBytes = Math.Max(64, file.Size),
                PayloadFile = file,
                OnCompleted = xfer =>
                {
                    if (!Exists(driveUid.Value) || !TryComp<FileSystemComponent>(driveUid.Value, out var curFs) || xfer.PayloadFile == null)
                        return;

                    if (_fileSystem.TryWriteFile(driveUid.Value, xfer.PayloadFile, curFs))
                        _fileSystem.LogSystemActivity(driveUid.Value, "NET-UPLOAD", $"Received upload {xfer.PayloadFile.FullPath} from {senderAddr}");
                }
            });
            return;
        }

        if (_fileSystem.TryWriteFile(driveUid.Value, file, fsComp))
        {
            _bandwidth.RecordDataTransmitted(uid, file.Size, isUpload: false);
            _fileSystem.LogSystemActivity(driveUid.Value, "NET-UPLOAD", $"Received upload {file.FullPath} from {senderAddr}");
        }
    }

    [SubscribeLocalEvent]
    private void OnFileDeleteRequest(Entity<OxydComputerComponent> ent, ref DeviceNetworkPacketEvent<FileDeleteRequestPayload> args)
    {
        var uid = ent.Owner;
        var comp = ent.Comp;
        if (!ValidateRemotePacketAccess(uid, comp, args.Data.AuthToken, requirePassword: true))
        {
            var denied = new FileDeleteResponsePayload
            {
                TargetPath = args.Data.TargetPath,
                Success = false,
                Message = "Access denied: remote control disabled, locked, or invalid credentials."
            };
            _deviceNet.SendPacket(uid, args.SenderAddress, ref denied, args.Frequency);
            return;
        }

        if (!_fileSystem.TryFindFileSystem(uid, out var driveUid, out var fsComp))
            return;

        var cleanPath = SharedFileSystemSystem.NormalizePath(args.Data.TargetPath);
        if (cleanPath.StartsWith("/bin/", StringComparison.OrdinalIgnoreCase))
        {
            var protectedResp = new FileDeleteResponsePayload
            {
                TargetPath = cleanPath,
                Success = false,
                Message = "Access denied: system binaries in /bin/ cannot be remotely deleted."
            };
            _deviceNet.SendPacket(uid, args.SenderAddress, ref protectedResp, args.Frequency);
            return;
        }

        var success = _fileSystem.TryDeleteFile(driveUid.Value, cleanPath, fsComp);
        var resp = new FileDeleteResponsePayload
        {
            TargetPath = cleanPath,
            Success = success,
            Message = success ? "File deleted." : "File locked or not found."
        };
        _deviceNet.SendPacket(uid, args.SenderAddress, ref resp, args.Frequency);
    }

    [SubscribeLocalEvent]
    private void OnRemoteControlRequest(Entity<OxydComputerComponent> ent, ref DeviceNetworkPacketEvent<RemoteControlRequestPayload> args)
    {
        var uid = ent.Owner;
        var comp = ent.Comp;
        if (!ValidateRemotePacketAccess(uid, comp, args.Data.AuthToken, requirePassword: true))
        {
            var err = new RemoteControlResponsePayload
            {
                Command = args.Data.Command,
                Success = false,
                ResultMessage = "Remote control disabled, locked, or invalid authentication."
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

    private bool ValidateRemotePacketAccess(EntityUid uid, OxydComputerComponent comp, string? authToken, bool requirePassword)
    {
        if (!comp.IsBooted || comp.NetworkWireCut || comp.PowerWireCut || comp.RemoteControlWireCut || !comp.AllowRemoteControl)
            return false;

        if (!_router.HasActiveNetworkCard(uid) || !_router.IsNetworkConnected(uid))
            return false;

        if (_computer.IsHostLocked(uid, comp))
            return false;

        if (requirePassword && !string.IsNullOrEmpty(comp.SshPassword) && authToken != comp.SshPassword)
            return false;

        return true;
    }
}
