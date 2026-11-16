using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Servers;
using Content.Shared.DeviceNetwork.Events;
using Content.Shared.DeviceNetwork.Systems;

namespace Content.Server._Oxyd.Computers.Servers;

/// <summary>
/// Physical mainframe server hosting station repository files, blueprints, executables (.nte),
/// and databases on its physical hard drive entity.
/// </summary>
public sealed partial class DataServerSystem : EntitySystem
{
    [Dependency] private DeviceNetworkSystem _deviceNet = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private NetworkRouterSystem _router = default!;
    [Dependency] private NetworkBandwidthSystem _bandwidth = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DataServerComponent, DeviceNetworkPacketEvent<ServiceRequestPayload>>(OnServiceRequest);
    }

    private void OnServiceRequest(Entity<DataServerComponent> ent, ref DeviceNetworkPacketEvent<ServiceRequestPayload> args)
    {
        if (!_router.IsRouterOperational(ent.Owner) || !_router.HasActiveNetworkCard(ent.Owner))
            return;

        if (!ent.Comp.AcceptsPublicReads && args.Data.AuthToken != ent.Comp.NetworkMasterKey)
            return;

        if (!ent.Comp.ServerProtocols.Contains(args.Data.ServiceName))
            return;

        var driveUid = GetServerDrive(ent.Owner, out var fs);
        if (driveUid == null || fs == null)
            return;

        if (args.Data.ServiceName == "ManifestService" &&
            _fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid.Value, "/records/manifest.db", out var manifestFile, fs))
        {
            _bandwidth.RecordDataTransmitted(ent.Owner, manifestFile.Size, isUpload: true);
            var response = new FileTransferPayload
            {
                Path = manifestFile.Directory,
                FileName = manifestFile.Name,
                Extension = manifestFile.Extension,
                FileContent = string.Join("\n", manifestFile.Data.Select(kv => $"{kv.Key}={kv.Value}")),
                AuthToken = args.Data.AuthToken
            };
            _deviceNet.SendPacket(ent.Owner, args.SenderAddress, ref response, args.Frequency);
        }
    }

    public EntityUid? GetServerDrive(EntityUid serverUid, out FileSystemComponent? fs)
    {
        fs = null;
        if (_fileSystem.TryFindFileSystem(serverUid, out var driveUid, out fs))
            return driveUid;

        return null;
    }

    public bool TryGetFirstOnlineRepositoryServer(
        EntityUid originCompUid,
        out EntityUid serverUid,
        out EntityUid serverCompUid,
        [NotNullWhen(true)] out EntityUid? serverDriveUid,
        [NotNullWhen(true)] out FileSystemComponent? serverFs,
        out string error)
    {
        serverUid = EntityUid.Invalid;
        serverCompUid = EntityUid.Invalid;
        serverDriveUid = null;
        serverFs = null;
        error = string.Empty;

        if (!_router.HasActiveNetworkCard(originCompUid))
        {
            error = "No active network card installed.";
            return false;
        }

        if (!_router.IsNetworkConnected(originCompUid))
        {
            error = "Station network router unreachable or blacklisted.";
            return false;
        }

        var query = EntityQueryEnumerator<DataServerComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            var cUid = _router.UnwrapComputerEntity(uid);
            if (!_router.IsRouterOperational(uid) || !_router.HasActiveNetworkCard(cUid))
                continue;

            if (_fileSystem.TryFindFileSystem(uid, out var dUid, out var fsComp))
            {
                serverUid = uid;
                serverCompUid = cUid;
                serverDriveUid = dUid;
                serverFs = fsComp;
                return true;
            }
        }

        error = "No online Data Server reachable on station network.";
        return false;
    }
}
