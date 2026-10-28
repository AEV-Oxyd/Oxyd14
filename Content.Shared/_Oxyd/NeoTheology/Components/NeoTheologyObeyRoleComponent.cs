using Content.Shared.Roles.Components;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>Native character briefing for the source optional Obey core upgrade; not an extra litany.</summary>
[RegisterComponent]
public sealed partial class NeoTheologyObeyRoleComponent : BaseMindRoleComponent
{
    [DataField] public string Commander = string.Empty;
}
