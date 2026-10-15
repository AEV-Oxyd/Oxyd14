using Content.Server.DeviceNetwork.Components;
using Content.Shared._Oxyd.Pda;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.Verbs;

namespace Content.Server._Oxyd.Pda;

/// <summary>Server modular PDA — wireless range sync + hardware report printing.</summary>
public sealed partial class OxydModularPdaSystem : SharedOxydModularPdaSystem
{
    [Dependency] private readonly PaperSystem _paper = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydModularPdaComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbs);
    }

    protected override void AfterRecalculate(Entity<OxydModularPdaComponent> ent)
    {
        if (TryComp<WirelessNetworkComponent>(ent, out var wireless))
            wireless.Range = ent.Comp.EffectiveNetworkRange;
    }

    private void OnVerbs(Entity<OxydModularPdaComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = "Hardware report",
            Act = () => PrintHardwareReport(ent, user),
            Priority = -2,
        });
    }

    private void PrintHardwareReport(Entity<OxydModularPdaComponent> ent, EntityUid user)
    {
        var paper = Spawn("Paper", Transform(ent).Coordinates);
        _meta.SetEntityName(paper, "PDA hardware report");
        _paper.SetContent(paper, DescribeHardware(ent));
        _popup.PopupEntity("Hardware report printed.", ent, user);
    }
}
