using System.Diagnostics.CodeAnalysis;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.Shared._Oxyd.Computers.Network;

/// <summary>
/// Singleton registry mapping 8-character Device GUIDs to live entities.
/// Keeps lookup state in an ECS singleton entity rather than an EntitySystem field.
/// </summary>
[RegisterComponent]
public sealed partial class DeviceGuidRegistryComponent : Component
{
    [ViewVariables]
    public Dictionary<string, EntityUid> GuidToEntity = new(StringComparer.OrdinalIgnoreCase);
}

public sealed partial class SharedDeviceGuidSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;

    [Dependency] private EntityQuery<EmbeddedComputerHostComponent> _hostQuery = default!;

    private Dictionary<string, EntityUid> GetRegistryMap()
    {
        var query = EntityQueryEnumerator<DeviceGuidRegistryComponent>();
        if (query.MoveNext(out _, out var reg))
            return reg.GuidToEntity;

        var singleton = Spawn(null, MapCoordinates.Nullspace);
        var created = EnsureComp<DeviceGuidRegistryComponent>(singleton);
        return created.GuidToEntity;
    }

    [SubscribeLocalEvent]
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        var query = EntityQueryEnumerator<DeviceGuidRegistryComponent>();
        while (query.MoveNext(out _, out var reg))
            reg.GuidToEntity.Clear();
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
            GetRegistryMap()[ent.Comp.Guid] = ent.Owner;
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<DeviceGuidComponent> ent, ref ComponentShutdown args)
    {
        if (string.IsNullOrEmpty(ent.Comp.Guid))
            return;

        var query = EntityQueryEnumerator<DeviceGuidRegistryComponent>();
        if (query.MoveNext(out _, out var reg) &&
            reg.GuidToEntity.TryGetValue(ent.Comp.Guid, out var existing) &&
            existing == ent.Owner)
        {
            reg.GuidToEntity.Remove(ent.Comp.Guid);
        }
    }

    public void EnsureGuid(EntityUid uid, DeviceGuidComponent comp)
    {
        var map = GetRegistryMap();
        if (string.IsNullOrEmpty(comp.Guid))
        {
            if (!_netManager.IsServer)
                return;

            string candidate;
            do
            {
                candidate = System.Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            } while (map.ContainsKey(candidate));

            comp.Guid = candidate;
            Dirty(uid, comp);
        }

        map[comp.Guid] = uid;
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

        var query = EntityQueryEnumerator<DeviceGuidRegistryComponent>();
        if (query.MoveNext(out _, out var reg) &&
            reg.GuidToEntity.TryGetValue(guid.Trim(), out var uid) &&
            Exists(uid) &&
            !TerminatingOrDeleted(uid))
        {
            entity = uid;
            return true;
        }

        entity = null;
        return false;
    }
}
