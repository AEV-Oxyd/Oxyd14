# NeoTheology litany progress

Status: **In progress**

This ledger records the current foundation/catalog work on `eris-litany-port`. It is
not a claim that the litany runtime is playable or at exact Eris parity. The
worktree already contains broader port work; this slice covers shared catalog and
rules support, private UI contracts/client presentation, and their focused tests.

## Baseline and validation evidence

- Branch: `eris-litany-port`.
- Current checkout is dirty with pre-existing NeoTheology port changes outside
  this support slice; those files are not part of this change.
- SDK observed in the checkout: `10.0.203`.
- Current validation evidence: the shared, server, and client projects build with
  **0 errors**; the shared build reports 185 warnings, the server build 3, and the
  client build 201. The focused NeoTheology test filter passes **16/16** tests.
- The current checks do not establish a complete test, integration-test,
  prototype-validation, or two-client manual-scenario pass. Those remain required
  before any foundation milestone can be marked `Verified`.

## Milestone ledger

| Milestone | Status | Evidence / remaining work |
| --- | --- | --- |
| 1. Baseline and shared contracts | In progress | Shared prototype/catalog contracts, phrase parsing, handler catalog, and holiness arithmetic are present. Focused pure-type tests pass 16/16; full catalog validation evidence is still outstanding. |
| 2. Cruciform lifecycle | In progress | Cruciform state/lifecycle scaffolding is present in the checkpoint worktree. Duplicate implant, extraction, save/load, death, pending-cast, and cleanup tests remain unverified. |
| 3. Speech and cast transaction | In progress | The accepted-speech integration hook exists, but the complete authoritative cast state machine and transaction tests are not evidenced by the checkpoint. |
| 4. Bible UI and common effects | In progress | Foundation catalog and private client UI contracts/presentation build successfully. Runtime handlers, interruption behavior, privacy isolation, and manual use are not verified. |
| 5. Rank/medical/social foundation | In progress | The 15 milestone-5 chants are catalogued and shared holiness rules are testable. Effect handlers and their integration tests are not evidenced by the checkpoint. |
| 6. Foundation integration review | Not started | Requires the complete build/test suite, prototype and map validation, logs/privacy review, and the two-client manual scenario. |
| 7. Dependency packets | Blocked | The named subsystem APIs below are not available for these rows; no gated chant is enabled as a fallback. |
| 8. Full catalog audit | In progress | The catalog contains 60 entries and structural validation scaffolding. A complete source-to-port audit and passing validation run remain outstanding. |

## Enabled foundation chants (23)

These are the currently enabled, ungated entries. They are catalog availability,
not proof of a committed runtime handler.

### Common and machinery foundation

`Relief`, `SoulHunger`, `Entreaty`, `CruciformSense`, `Revelation`, `Commitment`,
`Deprivation`, `ActivateDoor`.

### Milestone-5 foundation

`HandOfMercy`, `AbsolutionOfWounds`, `Epiphany`, `GraceOfPerseverance`,
`UpholdHolyWord`, `BaptismalRecord`, `DivineBlessing`, `Confirmation`, `Adoption`,
`Ordination`, `Omission`, `Excommunication`, `Convalescence`, `Succour`,
`Sending`.

## Dependency-gated entries

Every entry in this table remains `enabled: false` with the named dependency and
localized unavailable reason. The reason text is recorded so a future dependency
packet can replace the gate deliberately.

| Dependency | Gated entries | Unavailable reason |
| --- | --- | --- |
| Purity | `Rejection` | Requires the body-purity compatibility system. |
| ThreatClassification | `RevealAdversaries` | Requires explicit hostile and trap classifications. |
| Attachments | `InstallUpgrade`, `UninstallUpgrade` | Requires the cruciform attachment subsystem. |
| SoulCloning | `Reincarnation`, `Resurrection` | Requires the soul-safe cloning subsystem. |
| PlantGrowth | `AcceleratedGrowth` | Requires a per-plant growth modifier API. |
| Addiction | `WordsOfPurging` | Requires a modeled addiction progression API. |
| CoreModules | `Asacris`, `Initiation` | Requires the cruciform core-module subsystem. |
| Pain | `Atonement`, `Penance` | Requires a nonphysical pain API. |
| EyeEconomy | `DivineIntervention`, `HolyGuidance` | Requires the Eye of the Protector economy. |
| Armaments | `OrderArmaments` | Requires the NT armaments economy. |
| ConstructionCatalog | `DivineGuidance` | Requires the validated construction catalog. |
| Construction | `Manifestation`, `Uproot` | Requires the construction reservation subsystem. |
| ForgeMaterials | `MakeCruciform` | Requires normalized forge/material accounting. |
| BiomatterMaterials | `RepairDoor` | Requires the biomatter material subsystem. |
| Biogenerator | `PowerBiogenerator` | Requires the biogenerator command API. |
| Bioreactor | `BioreactorSolution`, `BioreactorChamber` | Requires the bioreactor command API. |
| Ceremonies | `PoundingWhisper`, `RevelationOfSecrets`, `LispOfVitae`, `CantoOfCourage`, `ChantOfObservance`, `ReclamationOfEndurance`, `Sanctify`, `Crusade`, `EternalBrotherhood`, `CallToBattle`, `SearingRevelation` | Requires the group ceremony engine. |
| RemoteView | `Scrying` | Requires the bounded remote-view session API. |
| NtUplink | `Knowledge`, `Bounty` | Requires the NT uplink subsystem. |

## Known risks and next evidence

- Enabled catalog rows still need authoritative server handlers and the
  invalid/interrupted/duplicate/no-debit transaction tests required by the plan.
- The current validation/build checkpoint is not full runtime validation; the
  successful project builds and focused tests do not verify integration tests,
  map, privacy, or manual behavior.
- Dependency rows must stay gated until their owning APIs and tests exist. In
  particular, pain, addiction, cloning, ceremonies, construction, material
  accounting, remote view, and uplink behavior must not be approximated by
  unrelated health, reagent, or inventory operations.
