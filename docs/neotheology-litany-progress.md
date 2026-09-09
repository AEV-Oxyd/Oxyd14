# NeoTheology litany progress

Status: **In progress — partway through Milestone 4; not yet a playable foundation.**

Implementation checkpoint: PR #33 at `7796e4c17dfbeb2463718b186b64db0770c04584`, reviewed 2026-09-09. This documentation update ran no tests or builds. Historical results below are retained as reported evidence, not certification of the current head.

## Current implementation

- 60 catalog entries and 9 sets: 23 foundation entries and 37 dependency-gated entries.
- **4 implemented and enabled:** `Relief`, `SoulHunger`, `Entreaty`, `CruciformSense`.
- **19 foundation entries remain disabled** pending their handlers; all **37 dependency entries remain disabled** with named reasons.
- Server speech recognition, DoAfter casting, ownership/entitlement checks, cruciform lifecycle, holiness, profiles and access are implemented. Book presentation and private snapshots exist; ongoing cast feedback and refresh wiring are incomplete.
- The skill-system prerequisite updates unique-buff amount/expiry and recalculates totals after updates/removal.

## Milestone ledger

Implementation status is distinct from historical validation. Earlier checkpoints do not establish that all current requirements pass.

| Milestone | Current assessment | Remaining work / evidence |
| --- | --- | --- |
| 1. Baseline and contracts | Substantially implemented | Catalog/schema/phrase validation and private UI contracts exist; historical unit/prototype checks below. |
| 2. Cruciform lifecycle | Substantially implemented | Implant linkage, activation, extraction/death, roles, access and elapsed regeneration exist. Historical lifecycle coverage includes extracted-state persistence; full implanted-body map-save coverage remains limited. |
| 3. Speech and cast transaction | In progress — contract gaps | Manual/book speech, DoAfter, rate limits, replay checks and spending exist. Complete reversible debit/application-failure handling, success-only cooldowns, audit/result publication and consistent selected-rules tolerance. |
| 4. Bible UI and common effects | In progress — current milestone | Four handlers and medical/social tests exist. Finish progress/results/resource refresh, target tokens, Revelation, Commitment, Deprivation, Activate Door and an isolated scenario. Repair Door belongs to the biomatter dependency packet. |
| 5. Ranks and medical/social foundation | Preparatory work only | Profile/specialization infrastructure and skill prerequisite fix exist; all 15 milestone-specific handlers remain outstanding. |
| 6. Foundation integration review | Not complete | Full builds/tests, prototype/map checks, logging/privacy/localization review and manual two-client scenario remain outstanding. |
| 7. Dependency packets | Gated / not implemented here | All 37 rows remain unavailable pending their named subsystem implementations and validation. |
| 8. Full catalog audit | Inventory present; final audit outstanding | 60 entries represented; complete source-to-runtime comparison and final validation remain outstanding. |

## Historical validation evidence

These are previously reported checkpoints, not results from this documentation update. No passing run for the four-handler head is established here.

- Baseline recorded RobustToolbox `af2a7d0406` and SDK `10.0.203`.
- M1 (`acc944a96a`): NeoTheology unit tests **16/16** and `LitanyPrototypeTest` **3/3** reported passing. At that checkpoint all 60 entries were disabled and the handler registry was empty; that availability count is obsolete.
- M2 (`6b9b3de457` plus then-uncommitted edits): `CruciformLifecycleTest` **8/8** reported passing, including duplicate rejection, extraction, pending cancellation, promotion without refill, access and regeneration.
- M3 (`988219dcee`): `LitanyCastTest` **11/11**, `LitanySecurityTest` **3/3**, lifecycle **8/8**, and NeoTheology unit **16/16** reported passing. Tests used availability overrides/no-op effects at that stage; this is not full real-handler transaction validation.
- M4 UI (`7cf78157be`): `LitanyUiTest` **9/9** reported passing. This does not establish a connected-client cast/progress/result loop.
- Medical packet: **6/6** reported, with one dirty-pool skip and a reported successful isolation rerun; exact commit not recorded in the previous ledger. Social-effect tests are now present, but this checkpoint records no fresh execution result.
- Earlier PR description reported skill tests **4/4** and DebugOpt builds with 0 errors (Server: 3 warnings; Client: 201; Shared: 185), plus a whitespace check. Those results were not tied to the reviewed head.
- At the review checkpoint, GitHub build/test checks were skipped; size-label failed. The administrative failure is not evidence of a compilation failure.

## Foundation effect disposition

Implemented and enabled: `Relief`, `SoulHunger`, `Entreaty`, `CruciformSense`.

Remaining common/machinery foundation: `Revelation`, `Commitment`, `Deprivation`, `ActivateDoor`.

Remaining milestone-5 foundation: `HandOfMercy`, `AbsolutionOfWounds`, `Epiphany`, `GraceOfPerseverance`, `UpholdHolyWord`, `BaptismalRecord`, `DivineBlessing`, `Confirmation`, `Adoption`, `Ordination`, `Omission`, `Excommunication`, `Convalescence`, `Succour`, `Sending`.

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

## Immediate follow-up

1. Complete the cast transaction contract: current application failure can retain debit/cooldown; no-op success stubs must not substitute for implemented effects. Add the required fault/audit/result handling.
2. Wire private begin/cancel outcomes, progress, completion and bounded resource/permission refreshes. Preserve a begun chant when only its window closes; cancel on required book loss.
3. Resolve rules through the selected rules prototype consistently: casting currently looks up `OxydNtRules`, while the shipped selected ID is `OxydNtDefaultRules`.
4. Remove or bound production accumulation in `_testingSocialNotices`, including round cleanup.
5. Implement server-owned target choices, then the remaining foundation handlers and isolated scenario before the foundation integration review.
6. Review unrelated `.freebuff` preview files and the `Oxyd14-port-eris-ironhammer` gitlink included in the PR as separate scope cleanup; preserve local work.

Dependency gates must remain intact until their owning APIs and tests exist. Neither catalog coverage nor historical focused-test counts certify a playable foundation or exact Eris parity.
