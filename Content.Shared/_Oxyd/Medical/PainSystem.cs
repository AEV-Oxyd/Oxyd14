using System.Linq;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Rejuvenate;
using Robust.Shared.GameStates;
using Robust.Shared.Network;

namespace Content.Shared._Oxyd.Medical;

/// <summary>Uses existing damage for wound pain. Analgesics suppress pain without healing that damage.</summary>
public sealed partial class PainSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private INetManager _net = default!;

    [SubscribeLocalEvent]
    private void OnMovement(EntityUid uid, PainComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        if (comp.CurrentPain >= comp.SevereThreshold)
            args.ModifySpeed(0.5f);
        else if (comp.CurrentPain >= comp.SlowdownThreshold)
            args.ModifySpeed(0.8f);
    }

    [SubscribeLocalEvent]
    private void OnState(Entity<PainComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _movement.RefreshMovementSpeedModifiers(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnRejuvenate(EntityUid uid, PainComponent comp, RejuvenateEvent args)
    {
        comp.TemporaryPain = 0;
        comp.Analgesics.Clear();
        Refresh((uid, comp));
    }

    public void AddPain(EntityUid uid, float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0 || !TryComp<PainComponent>(uid, out var pain))
            return;

        pain.TemporaryPain += amount;
        Refresh((uid, pain));
    }

    public void SuppressPain(EntityUid uid, string source, float strength, float seconds)
    {
        if (!float.IsFinite(strength) || !float.IsFinite(seconds) || strength <= 0 || seconds <= 0 ||
            !TryComp<PainComponent>(uid, out var pain))
            return;

        // Repeated metabolism refreshes one source. Different analgesics combine, as in Eris chemical effects.
        pain.Analgesics[source] = new AnalgesicDose { Strength = strength, Remaining = seconds };
        Refresh((uid, pain));
    }

    public void SetNumb(EntityUid uid, bool numb)
    {
        if (!TryComp<PainComponent>(uid, out var pain))
            return;

        pain.Numb = numb;
        Refresh((uid, pain));
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        var query = EntityQueryEnumerator<PainComponent>();
        while (query.MoveNext(out var uid, out var pain))
        {
            pain.TemporaryPain = Math.Max(0, pain.TemporaryPain - pain.RecoveryPerSecond * frameTime);
            foreach (var (source, dose) in pain.Analgesics.ToArray())
            {
                dose.Remaining -= frameTime;
                if (dose.Remaining <= 0)
                    pain.Analgesics.Remove(source);
            }

            pain.UpdateRemaining -= frameTime;
            if (pain.UpdateRemaining > 0)
                continue;

            pain.UpdateRemaining = 1f;
            Refresh((uid, pain));
        }
    }

    public void Refresh(Entity<PainComponent> ent)
    {
        var woundPain = 0f;
        if (TryComp<DamageableComponent>(ent.Owner, out var damage))
        {
            foreach (var (type, amount) in _damage.GetAllDamage((ent.Owner, damage)).DamageDict)
            {
                if (type.Id is "Blunt" or "Slash" or "Piercing" or "Heat" or "Cold" or "Shock")
                    woundPain += amount.Float();
            }
        }

        var total = ent.Comp.Numb ? 0 : Math.Max(0,
            woundPain + ent.Comp.TemporaryPain * ent.Comp.TemporaryPainMultiplier -
            ent.Comp.Analgesics.Values.Sum(dose => dose.Strength));
        if (MathHelper.CloseTo(total, ent.Comp.CurrentPain))
            return;

        ent.Comp.CurrentPain = total;
        Dirty(ent);
        _movement.RefreshMovementSpeedModifiers(ent.Owner);
    }
}
