using Robust.Shared.Configuration;

namespace Content.Shared._Oxyd;
[CVarDefs]
public sealed class OxydCvars
{

    public static readonly CVarDef<int> maxPastTicks =
        CVarDef.Create("oxydpred.maxPastTicks", 15, CVar.ARCHIVE | CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> maxFutureTicks =
        CVarDef.Create("oxydpred.maxFutureTicks", 15, CVar.ARCHIVE | CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> predictionTicks =
        CVarDef.Create("oxydpred.predictionTicks", 7, CVar.ARCHIVE | CVar.REPLICATED | CVar.SERVER);

}
