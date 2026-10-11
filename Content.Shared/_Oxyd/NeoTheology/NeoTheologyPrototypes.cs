using Content.Shared._Oxyd.NeoTheology.Prototypes;
using Content.Shared.Access;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Materials;
using Content.Shared.Metabolism;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology;

/// <summary>
/// Prototype ids shared by several NeoTheology systems. Systems keep per-file constants for
/// ids only they use; anything referenced across systems lives here once.
/// </summary>
public static class NeoTheologyPrototypes
{
    // Core modules (Resources/Prototypes/_Oxyd/NeoTheology/modules.yml)
    public static readonly ProtoId<CoreModulePrototype> BaseModule = "OxydNtModuleBase";
    public static readonly ProtoId<CoreModulePrototype> PriestModule = "OxydNtModulePriest";
    public static readonly ProtoId<CoreModulePrototype> InquisitorModule = "OxydNtModuleInquisitor";
    public static readonly ProtoId<CoreModulePrototype> AcolyteModule = "OxydNtModuleAcolyte";
    public static readonly ProtoId<CoreModulePrototype> AgrolyteModule = "OxydNtModuleAgrolyte";
    public static readonly ProtoId<CoreModulePrototype> CustodianModule = "OxydNtModuleCustodian";
    public static readonly ProtoId<CoreModulePrototype> CloningModule = "OxydNtModuleCloning";
    public static readonly ProtoId<CoreModulePrototype> UplinkModule = "OxydNtModuleUplink";
    public static readonly ProtoId<CoreModulePrototype> PriestConvertModule = "OxydNtModulePriestConvert";
    public static readonly ProtoId<CoreModulePrototype> ObeyModule = "OxydNtModuleObey";

    // Profiles (Resources/Prototypes/_Oxyd/NeoTheology/profiles.yml)
    public static readonly ProtoId<NeoTheologyProfilePrototype> DiscipleProfile = "OxydNtDisciple";
    public static readonly ProtoId<NeoTheologyProfilePrototype> PreacherProfile = "OxydNtPreacher";
    public static readonly ProtoId<NeoTheologyProfilePrototype> InquisitorProfile = "OxydNtInquisitor";
    public static readonly ProtoId<NeoTheologyProfilePrototype> AcolyteProfile = "OxydNtAcolyte";
    public static readonly ProtoId<NeoTheologyProfilePrototype> CustodianProfile = "OxydNtCustodian";
    public static readonly ProtoId<NeoTheologyProfilePrototype> AgrolyteProfile = "OxydNtAgrolyte";

    // Litany sets (Resources/Prototypes/_Oxyd/NeoTheology/litany_sets.yml)
    public static readonly ProtoId<LitanySetPrototype> CrusaderSet = "OxydLitanyCrusader";

    // Access levels (Resources/Prototypes/_Oxyd/NeoTheology/access.yml)
    public static readonly ProtoId<AccessLevelPrototype> CommonAccess = "OxydNtCommon";
    public static readonly ProtoId<AccessLevelPrototype> ClergyAccess = "OxydNtClergy";

    // Righteous-life metabolism inputs.
    public static readonly ProtoId<ReagentPrototype> CahorsReagent = "NTCahors";
    public static readonly ProtoId<ReagentPrototype> EthanolReagent = "Ethanol";
    public const string NarcoticsReagentGroup = "Narcotics";
    public static readonly ProtoId<MetabolismStagePrototype> BloodstreamStage = "Bloodstream";
    public static readonly ProtoId<MetabolismStagePrototype> DigestionStage = "Digestion";

    // Damage types used by litany effects and foundation rites.
    public static readonly ProtoId<DamageTypePrototype> DamageBlunt = "Blunt";
    public static readonly ProtoId<DamageTypePrototype> DamageHeat = "Heat";
    public static readonly ProtoId<DamageTypePrototype> DamageCellular = "Cellular";
    public static readonly ProtoId<DamageTypePrototype> DamageAsphyxiation = "Asphyxiation";
    public static readonly ProtoId<DamageTypePrototype> DamageRadiation = "Radiation";

    // Materials / stacks.
    public static readonly ProtoId<MaterialPrototype> BiomatterMaterial = "Biomatter";
    public static readonly ProtoId<StackPrototype> BiomatterStack = "Biomatter";
    public static readonly EntProtoId BiomatterEnt = "OxydNtBiomatter";

    // Cast/ritual VFX (Resources/Prototypes/_Oxyd/NeoTheology/effects.yml)
    public static readonly EntProtoId CastGlowEffect = "OxydNtCastGlow";
    public static readonly EntProtoId EpiphanyFlashEffect = "OxydNtEpiphanyFlash";

    // Entities.
    public static readonly EntProtoId CruciformEnt = "OxydNtCruciform";
    public static readonly EntProtoId OddityEnt = "OxydNtOddity";
    public static readonly EntProtoId EyeOfTheProtectorEnt = "OxydNtEyeOfTheProtector";
    public static readonly EntProtoId EyeBlessingStatusEnt = "OxydNtEyeBlessing";
    public static readonly EntProtoId ObeyRole = "OxydNtObeyRole";

    // World objectives (Resources/Prototypes/_Oxyd/NeoTheology/world_objectives.yml)
    public static readonly EntProtoId ConvertObjective = "OxydNtConvertObjective";
    public static readonly EntProtoId RevealObjective = "OxydNtRevealObjective";
    public static readonly EntProtoId SanctifyObjective = "OxydNtSanctifyObjective";
    public static readonly EntProtoId DestroyObjective = "OxydNtDestroyObjective";
}
