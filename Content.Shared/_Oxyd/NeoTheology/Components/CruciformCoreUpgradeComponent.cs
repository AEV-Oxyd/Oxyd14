using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>A removable coreimplant_upgrade item, separate from the physical attachment slot.</summary>
[RegisterComponent]
public sealed partial class CruciformCoreUpgradeComponent : Component
{
    [DataField(required: true)]
    public ProtoId<CoreModulePrototype> Module;

    [DataField] public string Commander = string.Empty;
}
