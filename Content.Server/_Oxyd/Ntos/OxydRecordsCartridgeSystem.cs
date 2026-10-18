using System.Linq;
using Content.Server.Station.Systems;
using Content.Shared._Oxyd.Ntos;
using Content.Shared.CartridgeLoader;
using Content.Shared.StationRecords;
using Content.Shared.StationRecords.Systems;

namespace Content.Server._Oxyd.Ntos;

/// <summary>Live Eris crew records PDA program backed by StationRecords.</summary>
public sealed partial class OxydRecordsCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridges = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly StationRecordsSystem _records = default!;

    private readonly Dictionary<EntityUid, uint?> _active = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydRecordsCartridgeComponent, CartridgeUiReadyEvent>(OnReady);
        SubscribeLocalEvent<OxydRecordsCartridgeComponent, CartridgeMessageEvent>(OnMessage);
    }

    private void OnReady(EntityUid uid, OxydRecordsCartridgeComponent component, CartridgeUiReadyEvent args)
        => Push(uid, args.Loader);

    private void OnMessage(EntityUid uid, OxydRecordsCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not OxydRecordsUiMessageEvent msg)
            return;

        var loader = GetEntity(args.LoaderUid);
        switch (msg.Action)
        {
            case OxydRecordsUiAction.Select:
                _active[loader] = msg.RecordId;
                break;
            case OxydRecordsUiAction.Clear:
                _active.Remove(loader);
                break;
        }

        Push(uid, loader);
    }

    private void Push(EntityUid program, EntityUid loader, string? error = null)
    {
        var station = _station.GetOwningStation(loader);
        var state = new OxydRecordsUiState { Error = error };

        if (station is null)
        {
            state.Error = "No station records available";
            _cartridges.UpdateCartridgeUiState(loader, state);
            return;
        }

        state.StationName = MetaData(station.Value).EntityName;
        foreach (var (id, rec) in _records.GetRecordsOfType<GeneralStationRecord>(station.Value)
                     .OrderBy(r => r.Item2.Name))
        {
            state.Records.Add(new OxydRecordListEntry
            {
                Id = id,
                Name = rec.Name,
                Job = rec.JobTitle,
            });
        }

        if (_active.TryGetValue(loader, out var activeId) && activeId is { } sel)
        {
            var key = new StationRecordKey(sel, station.Value);
            if (_records.TryGetRecord<GeneralStationRecord>(key, out var detail))
            {
                state.Active = new OxydRecordDetail
                {
                    Id = sel,
                    Name = detail.Name,
                    Job = detail.JobTitle,
                    Age = detail.Age,
                    Species = detail.Species,
                    Gender = detail.Gender.ToString(),
                    Fingerprint = detail.Fingerprint,
                    Dna = detail.DNA,
                };
            }
        }

        _cartridges.UpdateCartridgeUiState(loader, state);
    }
}
