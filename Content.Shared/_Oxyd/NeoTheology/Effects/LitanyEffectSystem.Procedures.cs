using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Standing;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

public sealed partial class LitanyEffectSystem
{
    [Dependency] private SharedBuckleSystem _buckles = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private StandingStateSystem _standing = default!;

    /// <summary>Checks the actual altar occupant, posture, position, and optional clothing restriction.</summary>
    public bool TryGetProcedureAltar(EntityUid target, bool undressed, out EntityUid altar, out LocId? failure)
    {
        altar = EntityUid.Invalid;
        failure = "oxyd-litany-procedure-posture";
        if (!TryComp<BuckleComponent>(target, out var buckle) || buckle.BuckledTo is not { } seat ||
            !HasComp<NeoTheologyAltarComponent>(seat) ||
            !TryComp<StrapComponent>(seat, out var strap) || !strap.Enabled ||
            !_buckles.IsBuckledTo(target, seat, buckle) || strap.Position != StrapPosition.Down ||
            !_standing.IsDown(target) || !_xform.InRange(target, seat, 0.5f))
            return false;

        if (undressed)
        {
            var slots = _inventory.GetSlotEnumerator(target, SlotFlags.All);
            while (slots.NextItem(out var item))
            {
                if (!HasComp<ClothingComponent>(item))
                    continue;

                failure = "oxyd-litany-procedure-clothing";
                return false;
            }
        }

        altar = seat;
        failure = null;
        return true;
    }
}
