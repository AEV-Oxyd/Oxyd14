using System.Linq;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.Station;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// M4 Packet C: Entreaty (station-wide call) and CruciformSense (7 m LOS scan).
/// </summary>
public sealed partial class LitanySystem
{
    private static readonly ProtoId<NeoTheologyProfilePrototype> PreacherProfile = "OxydNtPreacher";
    private static readonly ProtoId<NeoTheologyProfilePrototype> InquisitorProfile = "OxydNtInquisitor";

    /// <summary>Eris cruciform sense view range; catalog range must match.</summary>
    public const float CruciformSenseRangeMeters = 7f;

    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStationSystem _stations = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    /// <summary>Disconnected-fixture capture of private social litany notices.</summary>
    private readonly Dictionary<EntityUid, List<string>> _testingSocialNotices = new();

    public void TestingClearSocialNotices()
    {
        _testingSocialNotices.Clear();
    }

    public IReadOnlyList<string> TestingGetSocialNotices(EntityUid recipient)
    {
        if (_testingSocialNotices.TryGetValue(recipient, out var list))
            return list;

        return Array.Empty<string>();
    }

    private bool TryValidateEntreaty(EntityUid actor, LitanyPrototype litany, out LocId? failure)
    {
        failure = null;
        // Cost-0 information broadcast: succeed even with zero other followers (Eris always returns TRUE).
        _ = actor;
        _ = litany;
        return true;
    }

    private bool TryApplyEntreaty(EntityUid actor, LitanyPrototype litany)
    {
        _ = litany;
        var escapedName = FormattedMessage.EscapeText(Identity.Name(actor, EntityManager));
        var escapedLocation = FormattedMessage.EscapeText(DescribeCasterLocation(actor));

        var recipients = EnumerateSameStationActiveFollowers(actor)
            .OrderBy(uid => uid)
            .ToList();

        foreach (var recipient in recipients)
        {
            if (!TryComp(recipient, out CruciformBearerComponent? _) ||
                !_cruciform.TryGetCruciform(recipient, out _, out var cruciformComp))
                continue;

            var always = IsClergyProfile(cruciformComp.Profile);
            if (!always && !_random.Prob(0.5f))
                continue;

            var message = Loc.GetString(
                "oxyd-litany-entreaty-notice",
                ("name", escapedName),
                ("location", escapedLocation));
            DeliverSocialNotice(recipient, message);
        }

        return true;
    }

    private bool TryValidateCruciformSense(EntityUid actor, LitanyPrototype litany, out LocId? failure)
    {
        failure = null;
        var range = litany.Range > 0 ? litany.Range : CruciformSenseRangeMeters;
        if (CollectVisibleActiveFollowers(actor, range).Count == 0)
        {
            failure = "oxyd-litany-no-target";
            return false;
        }

        return true;
    }

    private bool TryApplyCruciformSense(EntityUid actor, LitanyPrototype litany)
    {
        var range = litany.Range > 0 ? litany.Range : CruciformSenseRangeMeters;
        var visible = CollectVisibleActiveFollowers(actor, range);
        if (visible.Count == 0)
            return false;

        foreach (var follower in visible.OrderBy(uid => uid))
        {
            var escaped = FormattedMessage.EscapeText(Identity.Name(follower, EntityManager, actor));
            var message = Loc.GetString("oxyd-litany-cruciform-sense-notice", ("name", escaped));
            DeliverSocialNotice(actor, message);
        }

        return true;
    }

    private List<EntityUid> CollectVisibleActiveFollowers(EntityUid actor, float range)
    {
        var results = new List<EntityUid>();
        if (!TryComp(actor, out TransformComponent? actorXform))
            return results;

        var actorMap = actorXform.MapID;
        if (actorMap == MapId.Nullspace)
            return results;

        var query = EntityQueryEnumerator<CruciformBearerComponent, TransformComponent>();
        while (query.MoveNext(out var body, out _, out var xform))
        {
            if (body == actor)
                continue;
            if (xform.MapID != actorMap)
                continue;
            if (!_cruciform.TryGetCruciform(body, out _, out _))
                continue;
            if (!_examine.InRangeUnOccluded(actor, body, range, predicate: null))
                continue;

            results.Add(body);
        }

        return results;
    }

    private IEnumerable<EntityUid> EnumerateSameStationActiveFollowers(EntityUid actor)
    {
        var actorStation = _stations.GetOwningStation(actor);
        if (!TryComp(actor, out TransformComponent? actorXform))
            yield break;

        var actorMap = actorXform.MapID;
        var query = EntityQueryEnumerator<CruciformBearerComponent, TransformComponent>();
        while (query.MoveNext(out var body, out _, out var xform))
        {
            if (body == actor)
                continue;
            if (!_cruciform.TryGetCruciform(body, out _, out _))
                continue;

            if (actorStation is { } station)
            {
                if (_stations.GetOwningStation(body) != station)
                    continue;
            }
            else
            {
                // Plan §7.1: no station → same map only; never link null stations across maps.
                if (xform.MapID != actorMap || actorMap == MapId.Nullspace)
                    continue;
            }

            yield return body;
        }
    }

    private static bool IsClergyProfile(ProtoId<NeoTheologyProfilePrototype> profile)
    {
        return profile == PreacherProfile || profile == InquisitorProfile;
    }

    private string DescribeCasterLocation(EntityUid actor)
    {
        var xform = Transform(actor);
        var mapCoords = _xform.ToMapCoordinates(xform.Coordinates);
        var point = $"{mapCoords.X:F0}, {mapCoords.Y:F0}";

        if (_stations.GetOwningStation(actor) is { } station)
            return $"{Name(station)} ({point})";

        if (xform.GridUid is { } grid)
            return $"{Name(grid)} ({point})";

        return $"({point})";
    }

    private void DeliverSocialNotice(EntityUid recipient, string message)
    {
        if (!_testingSocialNotices.TryGetValue(recipient, out var list))
        {
            list = [];
            _testingSocialNotices[recipient] = list;
        }

        list.Add(message);
        _popup.PopupEntity(message, recipient, recipient, PopupType.MediumCaution);
    }
}
