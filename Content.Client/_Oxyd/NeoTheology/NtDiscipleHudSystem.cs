using Content.Client.Overlays;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._Oxyd.NeoTheology;

/// <summary>
/// Eternal Brotherhood's disciple HUD (Eris <c>datum/core_module/cruciform/neotheologyhud</c>).
/// While the local player carries <see cref="NtDiscipleHudComponent"/>, every active cruciform
/// bearer shows the disciple icon. Eris draws its own overlay image; the fork uses the shared
/// status-icon path so the icon also honors the status-icon display options (named divergence).
/// </summary>
public sealed partial class NtDiscipleHudSystem : EquipmentHudSystem<NtDiscipleHudComponent>
{
    private static readonly ProtoId<SecurityIconPrototype> DiscipleIcon = "OxydNtDiscipleIcon";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CruciformBearerComponent, GetStatusIconsEvent>(OnGetStatusIcons);
    }

    private void OnGetStatusIcons(Entity<CruciformBearerComponent> ent, ref GetStatusIconsEvent args)
    {
        if (!IsActive ||
            ent.Comp.Cruciform is not { } cruciform ||
            !TryComp<CruciformComponent>(cruciform, out var state) ||
            !state.Active)
            return;

        args.StatusIcons.Add(ProtoMan.Index(DiscipleIcon));
    }
}
