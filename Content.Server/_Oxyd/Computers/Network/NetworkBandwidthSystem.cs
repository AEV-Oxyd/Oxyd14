using Content.Shared._Oxyd.Computers.Network;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.DeviceNetwork.Events;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.Network;

public sealed partial class NetworkBandwidthSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NetworkBandwidthComponent, BeforePacketSentEvent>(OnBeforePacketSent);
        SubscribeLocalEvent<NetworkBandwidthComponent, DeviceNetworkPacketEvent<INetworkPayload>>(OnPacketReceived);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var curTime = _timing.CurTime;

        var query = EntityQueryEnumerator<NetworkBandwidthComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (curTime < comp.NextRateReset)
                continue;

            comp.NextRateReset = curTime + TimeSpan.FromSeconds(1);
            comp.CurrentBytesReceived = 0;
            comp.CurrentQueuedPackets = Math.Max(0, comp.CurrentQueuedPackets - 20);

            var oldSaturated = comp.IsSaturated;
            comp.IsSaturated = comp.CurrentQueuedPackets > comp.BufferQueueLimit;
            if (oldSaturated != comp.IsSaturated)
                Dirty(uid, comp);
        }
    }

    private void OnBeforePacketSent(EntityUid uid, NetworkBandwidthComponent comp, ref BeforePacketSentEvent args)
    {
        if (comp.IsSaturated)
        {
            // Drop packet under DDoS condition
            args.Cancelled = true;
        }
    }

    private void OnPacketReceived(EntityUid uid, NetworkBandwidthComponent comp, ref DeviceNetworkPacketEvent<INetworkPayload> args)
    {
        var approxSize = 256;
        if (args.Data is FileTransferPayload ft)
            approxSize += ft.FileContent.Length;
        else if (args.Data is DdosFloodPayload df)
            approxSize += df.FloodData.Length;

        comp.CurrentBytesReceived += approxSize;
        if (comp.CurrentBytesReceived > comp.MaxBps)
        {
            comp.CurrentQueuedPackets++;
            if (comp.CurrentQueuedPackets > comp.BufferQueueLimit)
            {
                if (!comp.IsSaturated)
                {
                    comp.IsSaturated = true;
                    Dirty(uid, comp);
                }
            }
        }
    }
}
