using Content.Server.Cargo.Components;
using Content.Server.Station.Systems;
using Content.Shared._Oxyd.Ntos;
using Content.Shared.Cargo;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.CartridgeLoader;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Ntos;

/// <summary>Live cargo bounty board PDA program.</summary>
public sealed partial class OxydBountyCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridges = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private IPrototypeManager _protos = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydBountyCartridgeComponent, CartridgeUiReadyEvent>(OnReady);
        SubscribeLocalEvent<OxydBountyCartridgeComponent, CartridgeMessageEvent>(OnMessage);
    }

    private void OnReady(EntityUid uid, OxydBountyCartridgeComponent component, CartridgeUiReadyEvent args)
        => Push(args.Loader);

    private void OnMessage(EntityUid uid, OxydBountyCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is OxydBountyUiMessageEvent)
            Push(GetEntity(args.LoaderUid));
    }

    private void Push(EntityUid loader)
    {
        var state = new OxydBountyUiState();
        var station = _station.GetOwningStation(loader);
        if (station is null || !TryComp<StationCargoBountyDatabaseComponent>(station, out var db))
        {
            state.Error = "No cargo bounty database on this station";
            _cartridges.UpdateCartridgeUiState(loader, state);
            return;
        }

        state.StationName = MetaData(station.Value).EntityName;
        foreach (var bounty in db.Bounties)
            state.Active.Add(ToEntry(bounty, "Active"));

        foreach (var hist in db.History)
        {
            state.History.Add(new OxydBountyListEntry
            {
                Id = hist.Id,
                Title = hist.Id,
                Description = hist.Result.ToString(),
                Reward = 0,
                Status = $"{hist.Result}{(hist.ActorName is { } a ? $" by {a}" : string.Empty)}",
            });
        }

        _cartridges.UpdateCartridgeUiState(loader, state);
    }

    private OxydBountyListEntry ToEntry(CargoBountyData bounty, string status)
    {
        var title = bounty.Id;
        var desc = string.Empty;
        var reward = 0;
        if (_protos.TryIndex(bounty.Bounty, out CargoBountyPrototype? proto))
        {
            title = bounty.Id;
            desc = Loc.GetString(proto.Description);
            reward = proto.Reward;
            foreach (var entry in proto.Entries)
                desc += $"\n• {entry.Amount}× {Loc.GetString(entry.Name)}";
        }

        return new OxydBountyListEntry
        {
            Id = bounty.Id,
            Title = title,
            Description = desc,
            Reward = reward,
            Status = status,
        };
    }
}
