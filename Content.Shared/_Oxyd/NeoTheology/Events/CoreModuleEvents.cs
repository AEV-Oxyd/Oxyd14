using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology;

[ByRefEvent]
public readonly record struct CoreModuleInstalledEvent(ProtoId<CoreModulePrototype> Module);

[ByRefEvent]
public readonly record struct CoreModuleUninstalledEvent(ProtoId<CoreModulePrototype> Module);
