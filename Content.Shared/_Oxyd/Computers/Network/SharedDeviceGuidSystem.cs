using System.Diagnostics.CodeAnalysis;
using Content.Shared.GameTicking;
using Robust.Shared.Network;

namespace Content.Shared._Oxyd.Computers.Network;

public sealed partial class SharedDeviceGuidSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;

    [Dependency] private EntityQuery<EmbeddedComputerHostComponent> _hostQuery = default!;

    private readonly Dictionary<string, EntityUid> _guidToEntity = new(StringComparer.OrdinalIgnoreCase);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _guidToEntity.Clear();
    }

    [SubscribeLocalEvent]
    private void OnStartup(Entity<DeviceGuidComponent> ent, ref ComponentStartup args)
    {
        EnsureGuid(ent.Owner, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<DeviceGuidComponent> ent, ref MapInitEvent args)
    {
        EnsureGuid(ent.Owner, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnAfterAutoHandleState(Entity<DeviceGuidComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!string.IsNullOrEmpty(ent.Comp.Guid))
            _guidToEntity[ent.Comp.Guid] = ent.Owner;
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<DeviceGuidComponent> ent, ref ComponentShutdown args)
    {
        if (!string.IsNullOrEmpty(ent.Comp.Guid) &&
            _guidToEntity.TryGetValue(ent.Comp.Guid, out var existing) &&
            existing == ent.Owner)
        {
            _guidToEntity.Remove(ent.Comp.Guid);
        }
    }

    public void EnsureGuid(EntityUid uid, DeviceGuidComponent comp)
    {
        if (string.IsNullOrEmpty(comp.Guid))
        {
            if (!_netManager.IsServer)
                return;

            string candidate;
            do
            {
                candidate = System.Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            } while (_guidToEntity.ContainsKey(candidate));

            comp.Guid = candidate;
            Dirty(uid, comp);
        }

        _guidToEntity[comp.Guid] = uid;
    }

    public string GetGuid(EntityUid uid, DeviceGuidComponent? comp = null)
    {
        // Always resolve through the embedded computer entity when called with a host machine.
        if (comp == null && _hostQuery.TryComp(uid, out var host) && host.ComputerEntity is { } compUid)
            uid = compUid;

        if (!Resolve(uid, ref comp, false))
            return string.Empty;

        if (string.IsNullOrEmpty(comp.Guid))
            EnsureGuid(uid, comp);

        return comp.Guid;
    }

    public bool TryGetEntityByGuid(string guid, [NotNullWhen(true)] out EntityUid? entity)
    {
        if (string.IsNullOrWhiteSpace(guid))
        {
            entity = null;
            return false;
        }

        if (_guidToEntity.TryGetValue(guid.Trim(), out var uid) && Exists(uid) && !TerminatingOrDeleted(uid))
        {
            entity = uid;
            return true;
        }

        entity = null;
        return false;
    }
}
