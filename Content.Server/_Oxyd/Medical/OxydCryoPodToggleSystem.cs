using Content.Server.Medical;
using Content.Shared._Oxyd.Medical;
using Content.Shared.Medical.Cryogenics;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Eris cryo cell On/Off (cryo.tmpl): Oxyd pods get an explicit power toggle in the
/// NanoUI. On = add ActiveCryoPodComponent (the stock tick loop drives treatment +
/// UI pushes); Off = remove it (transfers and updates stop, like the Eris off state).
/// Stock pods never send the message and are unaffected.
/// </summary>
public sealed partial class OxydCryoPodToggleSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CryoPodComponent, OxydCryoPodPowerMessage>(OnPowerMessage);
    }

    private void OnPowerMessage(EntityUid uid, CryoPodComponent comp, OxydCryoPodPowerMessage args)
    {
        if (args.On)
        {
            if (!_power.IsPowered(uid))
                return;
            EnsureComp<ActiveCryoPodComponent>(uid);
            comp.NextInjectionTime = _timing.CurTime + comp.BeakerTransferTime;
            Dirty(uid, comp);
        }
        else
        {
            RemComp<ActiveCryoPodComponent>(uid);
        }

        _appearance.SetData(uid, CryoPodVisuals.IsOn, HasComp<ActiveCryoPodComponent>(uid));
    }
}
