# NeoTheology litany — complete dependency map

Source of truth: `~/Desktop/SS13-14/_eris-feature-audit/reports/PORTING_ROADMAP.md`
(+ `reports/graph.json` for the feature DAG, `papers/`. Eris sources under `code/modules/core_implant/`).

Target: `/Users/russellrozario/Desktop/SS13-14/Oxyd14`.

Evidence key: `[present]` / `[partial]` / `[divergent]` / `[missing]` are the roadmap's
assessment of the *Eris feature*; `PRESENT`/`MISSING` in the destination column are greps run
against the Oxyd14 tree on 2026-09-10 (scoped to `Content.{Shared,Server,Client}`,
`Resources/Prototypes`, `Resources/Locale`).

---

## Status update (2026-09-11)

The packet tables below are the original map. These packets have landed since the map:

| Packet | Litanies | State |
| --- | --- | --- |
| `RemoteView` | `Scrying` | Landed (P3.9 + P4.13). Bounded session; target selection stays a fidelity gap. |
| `CoreModules` | `Asacris`, `Initiation` | `Initiation` landed. `Asacris` remains gated. |
| `Attachments` | `InstallUpgrade`, `UninstallUpgrade` | Handlers enabled; effects are stubs. Completion plan Stage 3. |
| `SoulCloning` | `Reincarnation`, `Resurrection` | Landed. Resurrection restored from the stored profile. |
| `EyeEconomy` | `DivineIntervention`, `HolyGuidance` | Landed. Values still need Eris alignment. |
| `Armaments` | `OrderArmaments` | Landed. |
| `ForgeMaterials` | `MakeCruciform` | Landed. |
| `BiomatterMaterials` | `RepairDoor` | Landed. |
| `Biogenerator` | `PowerBiogenerator` | Landed. |
| `Bioreactor` | `BioreactorSolution`, `BioreactorChamber` | Landed. |
| `ConstructionCatalog` | `DivineGuidance` | Capability present, handler pending. |
| `PlantGrowth` | `AcceleratedGrowth` | Capability partial, handler pending. |

Remaining gated set: 24 entries. The completion plan
(`.hermes/plans/2026-09-11_135754-neotheology-completion.md`) drives the rest.

---

## 0. What already exists in Oxyd14

| Artifact | State |
| --- | --- |
| `Content.Shared/_Oxyd/NeoTheology/` | 25 files — types, 5 components, 4 prototypes, 7 effect files (base + system + 5 concretes), handler catalog, phrase parser, catalog validator, holiness model, UI messages |
| `Content.Server/_Oxyd/NeoTheology/` | 6 files — `CruciformSystem`, `LitanySystem{,.Casting,.Speech,.UI}`, `LitanyPrototypeValidationSystem` |
| `Content.Client/_Oxyd/NeoTheology/UI/` | 2 files — `LitanyBoundUserInterface` + `LitanyWindow.xaml{,.cs}` |
| `Content.Tests/Shared/_Oxyd/NeoTheology/` | 3 suites — catalog policy, phrase parser, rules |
| Prototypes `_Oxyd/NeoTheology/` | `litanies_{common,clergy,inquisitor,machines}.yml` (60 litanies), `litany_sets.yml` (9 sets), `profiles.yml` (6 profiles), `rules.yml`, `access.yml` (3 levels), `entities.yml` (cruciform, bible, clergy bible, altar, holy door) |
| Locale `en-US/_Oxyd/neotheology/` | `cruciform.ftl`, `litanies.ftl`, `messages.ftl`, `ui.ftl` — 170 `oxyd-litany-*` ids |

**60 catalog entries: 4 enabled, 56 disabled.** Enabled = `Relief`, `SoulHunger`, `Entreaty`,
`CruciformSense` — the only four with a commit handler in `LitanyHandlerCatalog.Implemented`.
23 entries are ungated *foundation*; 37 are gated behind one of 20 `NeoTheologyDependency` kinds.

Count method: 4 `litanies_*.yml` files × (first block + blocks matched by `- type: oxydLitany`) = 60.

---

## 1. The hard spine — three blockers that gate all 60 entries

Nothing casts without these. Roadmap depth 0→2.

| # | Blocker | Roadmap | Status | Blocks |
| --- | --- | --- | --- | --- |
| S1 | `found.access-id` — access levels / ID (Eris `found.access-id`) | `jobs-economy.access-levels-and-id`:893 | partial → `AccessReader`/`GetAccessTagsEvent`/access levels `PRESENT` | S2, cruciform access privileges |
| S2 | `sanity-religion.core-implant-base` — `/obj/item/implant/core_implant` + `/datum/core_module` | `:1023` **L**, needs `foundation` | partial | S3, and every module packet |
| S3 | `sanity-religion.cruciform-implant` — faith/power/righteous-life container, promotes & demotes wearer | `:2205` **XL** | divergent | all 60 litanies |

Oxyd14 has a *reduced* version of S2/S3: `CruciformComponent` + `CruciformBearerComponent` and
`CruciformSystem` implement lifecycle, roles, holiness and access — but **there is no
`/datum/core_module` equivalent**. Roadmap `core-modules` (`:2213`) verifies exactly this:
"Confirm Oxyd grants litanies via `NeoTheologyProfilePrototype.LitanySets` rather than a
module install/uninstall API." Oxyd's profile→litany-set table is a *substitute* for the
module system, not an implementation of it.

**Consequence:** every packet below that names `CoreModules`, `Attachments`, `SoulCloning`,
`EyeEconomy`, `Armaments`, `NtUplink` is blocked on the module install/uninstall API
existing at all — not on the individual feature.

---

## 2. The 20 dependency packets

Each row: the `NeoTheologyDependency` enum value in `NeoTheologyTypes.cs`, the litanies it
gates (`enabled: false` + `unavailableReason` in `litanies_*.yml`), the Eris subsystem, the
roadmap feature, and whether the destination capability exists in Oxyd14 today.

### 2a. Sanity / body / chemistry

| Dependency | Gated litanies | Roadmap feature | Destination capability | Oxyd14 |
| --- | --- | --- | --- | --- |
| `Purity` | `Rejection` | — (no roadmap feature; Eris `cruciform.dm` purity check) | body-purity compatibility system | **MISSING** — 0 hits for purity anywhere |
| `ThreatClassification` | `RevealAdversaries` | — | explicit hostile & trap classification on entities | **MISSING** — no `ThreatClass`/hostile-tag concept |
| `Addiction` | `WordsOfPurging` | — | modelled addiction progression API | **MISSING** — 0 hits |
| `Pain` | `Atonement`, `Penance` | — | nonphysical pain API | **PARTIAL** — `PainNumbnessSystem`/`Component` exist, but there is no pain *value* API to apply |
| `RemoteView` | `Scrying` | `sanity-religion.cruciform-hud-module` sibling (`:3542`) | bounded remote-view session API | **MISSING** — `SurveillanceCamera` present as a subsystem, no scrying session API on top |

`Rejection`, `RevealAdversaries`, `WordsOfPurging`, `Atonement`, `Penance`, `Scrying` = 6 litanies.
Purity / ThreatClassification / Addiction have **no roadmap feature at all** — they are
Oxyd-only gates invented during the port, so their Eris behaviour must be re-derived from
`cruciform.dm` / `rituals/priest.dm` / `rituals/inquisitor.dm` directly.

### 2b. Cruciform core modules and attachments

| Dependency | Gated litanies | Roadmap feature | Oxyd14 |
| --- | --- | --- | --- |
| `CoreModules` | `Asacris`, `Initiation` | `sanity-religion.core-modules` `:2213` **L** [missing]; `cruciform-cloning-module` `:3524` **L**; `cruciform-obey-module` `:3533` **M**; `cruciform-hud-module` `:3542` **M**; `cruciform-uplink-module` `:3551` **M** | **MISSING** — no module API, no `CoreModule`/`CruciformModule` symbol |
| `Attachments` | `InstallUpgrade`, `UninstallUpgrade` | `sanity-religion.cruciform-upgrades` `:3455` **L**, needs `biology-botany`, `combat-weapons` | **MISSING** — no `CruciformUpgrade` |
| `SoulCloning` | `Reincarnation`, `Resurrection` | `sanity-religion.cruciform-cloning-module` `:3524` + `sanity-religion.machinery-cloning` `:4073` **L**, needs `chemistry-medical` | **MISSING** — no soul snapshot/restore; `Content.Server/Cloning` is the SS14 genetic cloner, not this |
| `EyeEconomy` | `DivineIntervention`, `HolyGuidance` | `sanity-religion.machinery-eotp` `:3497` **M**, needs `power-engineering` | **MISSING** — no Eye of the Protector entity/system |
| `Armaments` | `OrderArmaments` | `sanity-religion.machinery-armaments` `:3515` **L**, needs `combat-weapons`, `materials-construction` | **MISSING** — no armaments printer |
| `NtUplink` | `Knowledge`, `Bounty` | `sanity-religion.cruciform-uplink-module` `:3551` **M** | **PARTIAL** — `UplinkComponent` + `SharedStoreSystem` exist; no NT store category or cruciform-hosted uplink |

`Asacris` (the Acolyte resurrection litany) and `Initiation` are the two CoreModules gates,
and both also need the module that grants them — a packet that needs itself.

### 2c. Machinery and materials

| Dependency | Gated litanies | Roadmap feature | Oxyd14 |
| --- | --- | --- | --- |
| `ForgeMaterials` | `MakeCruciform` | `sanity-religion.machinery-cruciformforge` `:3488` **L**, needs `materials-construction`, `chemistry-medical` | **PARTIAL** — `MaterialStorage` present (32 hits); no forge entity, no normalized forge accounting |
| `BiomatterMaterials` | `RepairDoor` | — | **PARTIAL** — `ActiveBiomassReclaimer`/biomass exist; no biomatter material for door repair. Ledger places `RepairDoor` in the biomatter packet |
| `Biogenerator` | `PowerBiogenerator` | — | **MISSING** — the 4 `biogenerator` hits are SS14 lathe prototypes, not the NT power machine |
| `Bioreactor` | `BioreactorSolution`, `BioreactorChamber` | — | **MISSING** — 0 hits |
| `Construction` | `Manifestation`, `Uproot` | `sanity-religion.ritual-catalog-construction` `:4064` **M** [missing], needs `materials-construction` | **MISSING** — no construction reservation subsystem |
| `ConstructionCatalog` | `DivineGuidance` | same as above | **PRESENT (partial)** — SS14 construction graphs exist; no NT-validated catalog |
| `PlantGrowth` | `AcceleratedGrowth` | `sanity-religion.ritual-catalog-agrolyte` `:4011` **M**, needs `biology-botany` | **PARTIAL** — `PlantGrowthSystem` has `AdjustWaterConsumption`/`AdjustNutrientConsumption` but **no growth-rate multiplier** — grep for `GrowthMultiplier`/`AdjustGrowth` = 0 |

### 2d. Group ceremonies

| Dependency | Gated litanies | Roadmap feature | Oxyd14 |
| --- | --- | --- | --- |
| `Ceremonies` | `PoundingWhisper`, `RevelationOfSecrets`, `LispOfVitae`, `CantoOfCourage`, `ChantOfObservance`, `ReclamationOfEndurance`, `Sanctify`, `Crusade`, `EternalBrotherhood`, `CallToBattle`, `SearingRevelation` | `sanity-religion.ritual-targeting` `:3995` **L** + `sanity-religion.ritual-catalog-group` `:4159` **L** | **MISSING** — no ceremony/group-ritual engine. `LitanyTargetMode.Ceremony` exists in the enum with no implementation |

**11 litanies** — the largest single block, and the one with the biggest destination cost:
group rituals need multi-participant target resolution (`group_ritual.dm`), a shared
ceremony state machine, and cross-follower effect fan-out. Roadmap `found.bui-ui` is present,
which is the only piece already there.

---

## 3. Foundation prerequisites (the 23 ungated entries)

These are gated by *the spine only*, not by a packet — but each still needs a real effect
implementation and any subsystem its effect touches.

| Effect | Subsystem it needs | Destination | State |
| --- | --- | --- | --- |
| `Relief` | damage heal | `LitanyHealEffect` | **DONE** |
| `SoulHunger` | damage/status | `LitanySoulHungerEffect` | **DONE** |
| `Entreaty` | skills | `LitanySkillEffect` / `SharedSkillSystem` | **DONE** |
| `CruciformSense` | cruciform + profiles | `LitanyCruciformSenseEffect` | **DONE** |
| `Revelation` | sanity | `SanitySystem` + `OddityComponent` **PRESENT** | todo |
| `Commitment`, `Deprivation` | status effects / reagents | `StatusEffectNew` `PRESENT`, `Chemistry` `PRESENT` | todo |
| `ActivateDoor` | access + door | `NeoTheologyDoorComponent`/`AccessReader` `PRESENT` | todo (cheapest of the 19) |
| `HandOfMercy`, `AbsolutionOfWounds` | medical heal | `Content.Server/Medical` `PRESENT` | todo |
| `Epiphany` | sanity + skills | `SanitySystem`, `SharedSkillSystem` | todo |
| `GraceOfPerseverance`, `UpholdHolyWord` | skills buff API | `SharedSkillSystem.SetUniqueBuff/RemoveBuff` `PRESENT` | todo |
| `BaptismalRecord` | persistent per-character record | **no persistence API** | todo — needs new storage |
| `DivineBlessing` | sanity | `ApplySanityDelta` `PRESENT` | todo |
| `Confirmation`, `Adoption`, `Ordination`, `Excommunication` | profile/role mutation | `CruciformSystem` role change `PRESENT` (partial) | todo — this is `cruciform-conversion-roles` `:3464` |
| `Omission`, `Sending` | chat/language + comms | `Content.Server/Chat` `PRESENT` | todo |
| `Convalescence`, `Succour` | medical/document | `Content.Server/Medical` | todo |

Foundation-only work is therefore **achievable today** for ~15 of the 19; it is the
dependency packets that are genuinely blocked. Ledger Milestone 5 lists these 15 as
"all milestone-specific handlers outstanding".

---

## 4. Destination-side capability audit (Oxyd14 tree)

Capabilities every packet depends on, verified by grep:

| Capability | Oxyd14 | Path |
| --- | --- | --- |
| Implants (subdermal) | **PRESENT** | `Content.Shared/Implants` (16), `Content.Server/Implants` (6) |
| Access / ID | **PRESENT** | `Content.Shared/Access` (24) |
| Sanity (level, insight, resting, oddities) | **PRESENT** | `Content.Shared/_Oxyd/Sanity` (4), `Content.Server/_Oxyd/SanityInsightAndResting` (2) |
| Skills (buff add/remove/unique) | **PRESENT** | `Content.Shared/_Oxyd/Skills` (3) |
| Cloning (SS14 genetic cloner) | **PRESENT** | `Content.Server/Cloning` (10) |
| Store / uplink | **PRESENT** | `Content.Server/Store` (14), `Content.Server/Traitor/Uplink` |
| Surveillance cameras | **PRESENT** | `Content.Server/SurveillanceCamera` (13) |
| Botany / plant growth | **PRESENT** (no growth multiplier) | `Content.Shared/Botany` (56); `Botany/Events/PlantGrowthEvent.cs` defines `PlantGrowEvent(NetEntity Tray)` — no rate field |
| Materials | **PRESENT** | `Content.Shared/Materials` (13) |
| Nutrition / hunger | **PRESENT** | `Content.Shared/Nutrition` (57) |
| Status effects | **PRESENT** | `Content.Shared/StatusEffectNew` (13) |
| Construction | **PRESENT** | `Content.Server/Construction` (61) |
| Power | **PRESENT** | `Content.Server/Power` (78), `Content.Shared/PowerCell` (10) |
| Chat / speech | **PRESENT** | `Content.Server/Chat` (30) |
| Bible UI | **PRESENT** | `Content.Shared/Bible` (7) |
| Cruciform module host | **MISSING** | — |
| Cruciform attachments | **MISSING** | — |
| Body purity | **MISSING** | — |
| Threat/trap classification | **MISSING** | — |
| Addiction | **MISSING** | — |
| Pain value API | **MISSING** (numbness only) | — |
| Remote-view/scrying session | **MISSING** | — |
| Biomatter material | **MISSING** (biomass exists) | — |
| Biogenerator (NT) | **MISSING** | — |
| Bioreactor | **MISSING** | — |
| Construction reservation | **MISSING** | — |
| Eye of the Protector | **MISSING** | — |
| Armaments printer | **MISSING** | — |
| Soul snapshot/restore | **MISSING** | — |

**15 present, 14 missing.** All 14 missing capabilities sit in packets 2a–2d; none are in the spine.

---

## 5. Integration gaps not expressed as a `NeoTheologyDependency`

These will block "playable" even after every packet lands:

1. **No job wires a profile.** grep for `OxydNt{Disciple,Preacher,Inquisitor,Agrolyte,Custodian,Acolyte}` outside `_Oxyd/NeoTheology` = **0 hits**. The only assignment is
   `CruciformComponent.Profile` defaulting to `"OxydNtDisciple"`. Until `core-modules` or a
   job→cruciform grant exists, no player can ever be Preacher/Inquisitor, so 5 of the 9
   litany sets are unreachable regardless of handler coverage. Roadmap: `cruciform-conversion-roles` `:3464` **M** [partial].
2. ~~Rules prototype ID mismatch.~~ **Resolved / never true.** `CruciformSystem.GetRules()`
   selects whichever `oxydNeoTheologyRules` prototype has `Selected: true`; `grep -rn "OxydNtRules" Content/ Resources/`
   returns no such id. Verified 2026-09-10.
3. **Cast transaction is not atomic.** Application failure retains debit + cooldown.
4. **UI refresh incomplete** — progress/results/resource/target-token refresh, and `begin`/`cancel` private outcomes.
5. **`_testingSocialNotices` production accumulation** is unbounded (no round cleanup).
6. **`range` is declared on every litany with no reader** — `LitanyTargetMode` includes
   `AdjacentLiving`/`VisibleFollower`/`FrontMachine`/`Ceremony` but `ritual-targeting` (`:3995`)
   is unimplemented, so `LitanyTargetMode` values other than `Self` are unreachable.
7. **`.freebuff` preview files and the `Oxyd14-port-eris-ironhammer` gitlink** are unrelated scope riding in the PR.

---

## 6. Build order (topological)

```
L0  found.species-body, found.access-id, found.bui-ui                    [PRESENT]
L1  core-implant-base          module host API                           [BUILD]
L2  cruciform-implant  +  core-modules                                   [BUILD]
       └── profile→litany-set substitute already works; convert to modules
L3  ├─ cruciform-power-regen (holiness substituted — divergence, keep)
    ├─ cruciform-conversion-roles      -> unlocks job/profile wiring (#5.1)
    ├─ ritual-framework (phrase cost/cooldown)   [divergent, already ported]
    ├─ machinery-altar
    ├─ cruciform-upgrades ──────► Attachments packet
    ├─ cruciform-{cloning,obey,hud,uplink}-module ──► CoreModules/SoulCloning/NtUplink
    └─ machinery-{armaments,eotp,obelisk,cruciformforge}
L4  ├─ ritual-targeting ─► Ceremonies packet
    ├─ ritual-book [PRESENT]
    └─ ritual-catalog-{base,priest,inquisitor,agrolyte,custodian,crusader,machinery,construction}
L5  ritual-catalog-group  (needs L4 ritual-targeting)
```

Free-standing packets (no dependency on each other, parallelisable once L3 lands):
`PlantGrowth` (add a growth multiplier to `PlantGrowthSystem`), `Biogenerator`,
`Bioreactor`, `BiomatterMaterials`, `ForgeMaterials`, `Construction`/`ConstructionCatalog`,
`Addiction`, `Purity`, `ThreatClassification`, `Pain`, `RemoteView`.

Implementation plan: .hermes/plans/2026-09-10_115854-neotheology-bottom-up-port.md

---

## 7. Cheapest path to "playable litany"

Not zero, but the smallest real deliverable:

1. **Land all 19 foundation handlers** (§3). Blocked by the spine only — no packet needed.
   `ActivateDoor` is free (`NeoTheologyDoorComponent` + `AccessReader` both present).
2. **Fix the 7 integration gaps** (§5) — especially the rules ID and atomic debit.
3. **Wire jobs → profiles** so 5 previously unreachable litany sets have a holder.
4. Then packets in §6 order, `Ceremonies` last (11 litanies, largest cost).

Skipped: no new subsystem is proposed here — the map is the deliverable.
Add per-packet port plans when a packet is actually picked up.
