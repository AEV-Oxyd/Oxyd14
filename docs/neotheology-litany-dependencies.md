# NeoTheology litany dependencies

Status on PR #33 after the merge of AEV-Oxyd `master` (`97c050d91a`): all 60
catalog entries are enabled. The code implements the 19 dependency categories
below. This is **catalog coverage, not proof of gameplay parity**. See
[neotheology-litany-progress.md](neotheology-litany-progress.md) for the
behavior differences, test results, and live-round checks.

## Casting foundation

`CruciformSystem` owns bearer profiles, modules, and holiness. `LitanySystem`
handles speech, cast validation, cooldowns, ceremonies, and book requests.
`LitanyEffectSystem` and its server handlers apply effects. The book shows
server-authored choices. These systems require an installed cruciform and the
correct rank; an enabled catalog entry alone does not grant a player access.

The six profiles are in `Resources/Prototypes/_Oxyd/NeoTheology/profiles.yml`.
`rules.yml` maps only the existing Chaplain job to `OxydNtPreacher`.
There are no NeoTheology ghost-role spawners or dedicated test mobs in the PR.
The Chaplain does not automatically receive `OxydNtBible`. A developer must
provide the Bible and suitable bearers to test all ranks. Adding six profiles
does not add station jobs or job slots.

## Dependency packets

| Dependency category | Litany or capability | Current implementation and limit |
| --- | --- | --- |
| `Purity` | `Rejection` | Local implant and robotic-organ scan; natural organs and the cruciform remain. |
| `ThreatClassification` | `RevealAdversaries` | Local faction and landmine scan; no Eris hidden miss chance. |
| `Attachments` | `InstallUpgrade`, `UninstallUpgrade` | Installed cruciform upgrades have live effects; the procedures check the altar and target. |
| `SoulCloning` | `Reincarnation`, `Resurrection` | Stored soul profile grows in the local cloner; Eris rank-specific damage is absent. |
| `PlantGrowth` | `AcceleratedGrowth` | Local plant growth multiplier. |
| `Addiction` | `WordsOfPurging` | Local dependence and recovery; Eris reagent removal does not occur. |
| `CoreModules` | `Asacris`, `Initiation` | Local cruciform modules and upgrades; `Asacris` must not remove rank modules. |
| `Pain` | `Atonement`, `Penance` | Local pain/stamina behavior replaces Eris hallucination loss. |
| `EyeEconomy` | `DivineIntervention`, `HolyGuidance` | Local Eye power and observation; oddity and faithless behavior remain incomplete. |
| `Armaments` | `OrderArmaments` | Local armaments printer and design-disk flow. |
| `ConstructionCatalog` | `DivineGuidance` | Local blueprint catalog and material choices. |
| `Construction` | `Manifestation`, `Uproot` | Local front-tile build and refund; Eris multipart machines map to single entities. |
| `ForgeMaterials` | `MakeCruciform` | Local cruciform forge and material accounting. |
| `BiomatterMaterials` | `RepairDoor` | Local biomatter and door repair. |
| `Biogenerator` | `PowerBiogenerator` | Local powered NeoTheology machine. |
| `Bioreactor` | `BioreactorSolution`, `BioreactorChamber` | Local bioreactor solution and chamber checks. |
| `Ceremonies` | Eleven group rites | Spoken participation and effect delivery work in focused tests; church-objective signaling and world effects remain incomplete. |
| `RemoteView` | `Scrying` | Bounded viewing session with a book target choice and speech fallback. |
| `NtUplink` | `Knowledge`, `Bounty` | Cruciform store with local banked balance; Eris equipment is not present. |

Every litany with no listed packet still needs its effect handler, profile access,
and valid targets. The enabled catalog and handler coverage checks are in the
NeoTheology unit and integration tests. Follow the live-round checks in the
progress document before claiming that a litany matches Eris gameplay.
