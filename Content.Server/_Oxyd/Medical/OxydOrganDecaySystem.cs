using Content.Shared._Oxyd.Medical;
using Content.Shared.Body;
using Content.Shared.Examine;
using Content.Shared.Random.Helpers;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Eris organ Process(): an organic organ out of a body decays (prob(5) per
/// tick → +12 damage) unless it sits in a stasis container (organ freezer,
/// smartfridge, stasis/cryo bag). At max damage the organ dies (ORGAN_DEAD):
/// examine shows "The decay has set in." and transplant is refused.
/// </summary>
public sealed partial class OxydOrganDecaySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    private const float TickInterval = 2f;
    private const int DecayChance = 5;
    private const float DecayDamage = 12f;

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydOrganSurgeryComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<OrganComponent, MapInitEvent>(OnOrganMapInit);
    }

    private void OnOrganMapInit(Entity<OrganComponent> ent, ref MapInitEvent args)
    {
        EnsureComp<OxydOrganSurgeryComponent>(ent);
    }

    private void OnExamined(Entity<OxydOrganSurgeryComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Decayed)
            args.PushMarkup(Loc.GetString("oxyd-organ-decayed"));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;

        var query = EntityQueryEnumerator<OrganComponent>();
        while (query.MoveNext(out var uid, out var organ))
        {
            if (organ.Body != null)
                continue;

            if (!TryComp<OxydOrganSurgeryComponent>(uid, out var surg) ||
                surg.Decayed || surg.Robotic || now < surg.NextDecay)
                continue;

            surg.NextDecay = now + TimeSpan.FromSeconds(TickInterval);

            if (InStasis(uid))
                continue;

            // OXYD: per-organ scheduled roll on a predicted stream so it doesn't
            // shift the global RNG sequence mid-frame.
            if (!SharedRandomExtensions.PredictedProb(_timing, DecayChance / 100f, GetNetEntity(uid)))
                continue;

            OxydWoundSystem.AddOrganDamage(surg, DecayDamage);
            if (surg.OrganDamage >= OxydOrganSurgeryComponent.OrganMaxDamage)
            {
                surg.OrganDamage = OxydOrganSurgeryComponent.OrganMaxDamage;
                surg.Decayed = true;
            }
            Dirty(uid, surg);
        }
    }

    /// <summary>True when any entity up the container chain suspends organ decay.</summary>
    private bool InStasis(EntityUid uid)
    {
        var parent = Transform(uid).ParentUid;
        while (parent.IsValid())
        {
            if (HasComp<OxydOrganStasisComponent>(parent))
                return true;
            parent = Transform(parent).ParentUid;
        }
        return false;
    }
}
