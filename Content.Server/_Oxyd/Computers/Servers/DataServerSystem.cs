using System.Linq;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Servers;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Events;
using Content.Shared.DeviceNetwork.Systems;

namespace Content.Server._Oxyd.Computers.Servers;

public sealed partial class DataServerSystem : EntitySystem
{
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private DeviceNetworkSystem _deviceNetwork = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DataServerComponent, DeviceNetworkPacketEvent<ServiceRequestPayload>>(OnServiceRequest);
    }

    private void OnServiceRequest(EntityUid uid, DataServerComponent comp, ref DeviceNetworkPacketEvent<ServiceRequestPayload> args)
    {
        var request = args.Data;
        if (!comp.ServerProtocols.Contains(request.ServiceName))
            return;

        // Check authentication or public reads
        var authorized = comp.AcceptsPublicReads || request.AuthToken == comp.NetworkMasterKey;
        if (!authorized)
            return;

        if (TryComp<FileSystemComponent>(uid, out var fsComp))
        {
            // If requesting manifest service, query /records/manifest.db
            if (request.ServiceName == "ManifestService")
            {
                if (_fileSystem.TryGetFile<DatabaseVirtualFile>(uid, "/records/manifest.db", out var manifestFile, fsComp))
                {
                    var response = new FileTransferPayload
                    {
                        Path = "/records/",
                        FileName = "manifest",
                        Extension = "db",
                        FileContent = string.Join(";", manifestFile.Data.Select(kv => $"{kv.Key}={kv.Value}"))
                    };

                    _deviceNetwork.SendPacket(uid, args.SenderAddress, ref response);
                }
            }
        }
    }
}
