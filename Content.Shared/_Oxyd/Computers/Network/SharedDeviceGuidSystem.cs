using System.Diagnostics.CodeAnalysis;
using Content.Shared.Examine;
using Robust.Shared.Network;

namespace Content.Shared._Oxyd.Computers.Network;

public sealed partial class SharedDeviceGuidSystem : EntitySystem
{
    private readonly Dictionary<string, EntityUid> _guidToEntity = new(StringComparer.OrdinalIgnoreCase);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DeviceGuidComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<DeviceGuidComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<DeviceGuidComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<DeviceGuidComponent, ExaminedEvent>(OnExamined);
    }

    private void OnInit(EntityUid uid, DeviceGuidComponent comp, ComponentInit args)
    {
        EnsureGuid(uid, comp);
    }

    private void OnMapInit(EntityUid uid, DeviceGuidComponent comp, MapInitEvent args)
    {
        EnsureGuid(uid, comp);
    }

    private void OnShutdown(EntityUid uid, DeviceGuidComponent comp, ComponentShutdown args)
    {
        if (!string.IsNullOrEmpty(comp.Guid) && _guidToEntity.TryGetValue(comp.Guid, out var existing) && existing == uid)
        {
            _guidToEntity.Remove(comp.Guid);
        }
    }

    private void OnExamined(EntityUid uid, DeviceGuidComponent comp, ExaminedEvent args)
    {
        if (string.IsNullOrEmpty(comp.Guid))
            return;

        args.PushMarkup(Loc.GetString("device-guid-examine-text", ("guid", comp.Guid)));
    }

    public void EnsureGuid(EntityUid uid, DeviceGuidComponent comp)
    {
        if (string.IsNullOrEmpty(comp.Guid))
        {
            comp.Guid = System.Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            Dirty(uid, comp);
        }

        _guidToEntity[comp.Guid] = uid;
    }

    public string GetGuid(EntityUid uid, DeviceGuidComponent? comp = null)
    {
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
