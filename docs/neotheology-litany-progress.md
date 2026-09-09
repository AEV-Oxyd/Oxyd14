# NeoTheology litany progress

Status: **In progress** (Milestones 1–3 verified; later milestones scaffolded)

This ledger records the current foundation/catalog work on the litany port. It is
not a claim that the litany runtime is playable or at exact Eris parity. The
worktree already contains broader port work; this slice covers shared catalog and
rules support, private UI contracts/client presentation, and their focused tests.

## Baseline and validation evidence

- Branch: `eris-litany-port`; M1 feature tip `acc944a96a` (docs may tip later). Milestone 1 verified.
- RobustToolbox: `af2a7d0406`. SDK: `10.0.203`.
- M1 policy: catalog has 60 rows (23 foundation + 37 dependency-gated); **0**
  `IsAvailable` until a runtime handler is registered. Foundation rows use
  `enabled: false` + `oxyd-litany-unavailable-foundation`. `LitanyHandlerCatalog.Implemented`
  is empty; `AllowsEnabledCatalogEntry` requires Implemented only.
- Server `LitanyPrototypeValidationSystem` fail-closed: validation errors clear
  indexes, set `CatalogReady=false`, and throw on initial load / reload.
- Focused NeoTheology unit filter: **16/16** passed (Content.Tests).
- `LitanyPrototypeTest` (integration): **3/3** passed (`LoadedCatalogHasNoStructuralValidationErrors`, `CatalogHasExpectedAvailabilityAndStableIdentity`, `CatalogSerializationRoundTrips`); `CatalogReady` asserted true.
- M2 `CruciformLifecycleTest` (integration): **8/8** passed on `eris-litany-port` @ `6b9b3de457` + local uncommitted M2 edits. Duplicate-insert cancel gap patched in `CruciformSystem` (nested container Remove during ImplantImplantedEvent was illegal).

## Milestone ledger

| Milestone | Status | Evidence / remaining work |
| --- | --- | --- |
| 1. Baseline and shared contracts | **Verified** | Contracts + fail-closed validation. Evidence: unit NeoTheology **16/16**; integration LitanyPrototypeTest **3/3**; 60 `enabled:false` / 0 IsAvailable; no gameplay handlers. Later milestones remain scaffold-only. |
| 2. Cruciform lifecycle | **Verified** | Implant/bearer lifecycle + focused tests. Evidence: `CruciformLifecycleTest` **8/8** (`FullyQualifiedName~CruciformLifecycle`); NeoTheology unit **16/16** still green. Covered: duplicate rejection (insert cancel, recoverable), extraction clears bearer/pending, DataField serialization + extracted-state persistence (full implanted-mob map-save not harness-supported), pending-cast clear on remove, promotion without refill, access tags when active, elapsed-time holiness regen, death deactivate + pending clear + reimplant resume. M1 catalog policy unchanged (60 `enabled:false`). Content-only; no RobustToolbox/engine edits. Not a claim that foundation is playable. |
| 3. Speech and cast transaction | **Verified** | Narrow `LitanySpeechAcceptedEvent` + `LitanySystem` cast state machine. Evidence @ feature tip `988219dcee` (branch may tip later): `LitanyCastTest` **11/11**, `LitanySecurityTest` **3/3**, `CruciformLifecycleTest` **8/8**, NeoTheology unit **16/16**. Covered: manual/book speech, stutter exception (OriginalMessage), radio/emote/NPC rejection, duplicate begin, stale UI revision, no debit on failed preconditions, chat Speak hook begins cast, shipped catalog still **0 IsAvailable**. Availability for runtime exercised via test-local `TestingSetAvailabilityOverride` (no production `enabled:true`). Intentional deferrals: real effect handlers (M4+; M3 uses no-op success stub when available), full Bible UI polish/target tokens (M4), non-Self targeting, choice prompts, private denial popups/admin log polish. Content-only; dirty submodule `Oxyd14-port-eris-ironhammer` untouched. |
| 4. Bible UI and common effects | In progress | Foundation catalog and private client UI contracts/presentation build successfully. Runtime handlers, interruption behavior, privacy isolation, and manual use are not verified. |
| 5. Rank/medical/social foundation | In progress | The 15 milestone-5 chants are catalogued and shared holiness rules are testable. Effect handlers and their integration tests are not evidenced by the checkpoint. |
| 6. Foundation integration review | Not started | Requires the complete build/test suite, prototype and map validation, logs/privacy review, and the two-client manual scenario. |
| 7. Dependency packets | Blocked | The named subsystem APIs below are not available for these rows; no gated chant is enabled as a fallback. |
| 8. Full catalog audit | In progress | The catalog contains 60 entries and structural validation scaffolding. A complete source-to-port audit and passing validation run remain outstanding. |

## Planned foundation chants (23)

These are the ungated foundation catalog rows. Under the M1 policy they are
`enabled: false` (`IsAvailable == false`) until a runtime handler lands; they
are reference material only, not castable.

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
