using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared.Metabolism;

// OXYD: cruciform Righteous Life listens for reagent metabolization.
/// <summary>Raised on the body once per successfully processed reagent/stage, before transfer.</summary>
[ByRefEvent]
public readonly record struct ReagentMetabolizedEvent(ReagentPrototype Reagent, ProtoId<MetabolismStagePrototype> Stage);
