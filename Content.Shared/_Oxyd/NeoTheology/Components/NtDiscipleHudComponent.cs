using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// Eternal Brotherhood's disciple HUD (Eris <c>datum/core_module/cruciform/neotheologyhud</c>):
/// while the component is on a mob, that mob's client shows the disciple icon over active
/// cruciform bearers. Named divergence: Eris drains one cruciform power per life tick and
/// drops the module when power runs out; the fork has no per-tick power drain, so the HUD
/// stays until the litany toggles it off or the cruciform goes away.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NtDiscipleHudComponent : Component;
