using Content.Shared._Oxyd.NeoTheology.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>Grants a test ghost role its cruciform when a mind takes the body.</summary>
[RegisterComponent]
public sealed partial class NeoTheologyTestRoleComponent : Component
{
    [DataField(required: true)]
    public ProtoId<NeoTheologyProfilePrototype> Profile;
}
