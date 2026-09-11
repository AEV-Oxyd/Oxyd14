# NeoTheology litany progress

Status: **Complete.** All 60 catalog entries are enabled on `neotheology/fix-pass`
and the Stage 6 catalog audit is done. The divergence register is below; the full
source-by-source audit is `.hermes/review/neotheology-catalog-audit.md`.

Completion plan: `.hermes/plans/2026-09-11_135754-neotheology-completion.md`.
Ceremony contract: `.hermes/review/neotheology-ceremony-contract.md`.
Smoke runbook: `.hermes/review/neotheology-smoke-runbook.md`.
Implementation checkpoint: commits through `7e9c0afcb7`; Stage 6 corrections and
documentation land in the commits that follow it on this branch.

## Current implementation

- 60 catalog entries and 9 sets: all 60 are foundation entries and all 60 are
  enabled. No dependency-gated entries remain.
- **60 implemented and enabled.** `LitanyHandlerCatalog.Implemented` matches the
  enabled set (unit policy 17/17).
- The group-ritual engine runs: a ceremony opens with no followers, followers in
  range join by speaking the current phrase, the starter advances with the next
  phrase, and the payload runs for the starter and every recorded follower.
- The six stat rites grant 3 + 2-per-follower skill (plus one per follower with
  Channeling) and spend one miracle point; `Sanctify` forces every obelisk active
  for sixty seconds; `Crusade` teaches the crusader set from six followers.
- `EternalBrotherhood` toggles the disciple HUD, `CallToBattle` scales with the
  visible bearers, and `SearingRevelation` knocks down the caster and every
  cruciform-less creature in view.
- The inquisitor's hidden uplink runs (NtUplink): the rank installs the module,
  the first litany use creates the nullspace store and the balance survives a
  module swap.
- Server speech recognition, DoAfter casting, ownership/entitlement checks,
  cruciform lifecycle, holiness, profiles and access are implemented.
- The cast transaction is atomic: an unexpected apply failure refunds the debit
  and skips the cooldown; success-only cooldowns; the actor receives
  `LitanyResultMessage` and `LitanyProgressMessage` on the open book UI.
- A book cast of a choice-requiring litany pauses in the `Choosing` stage. The
  server publishes server-authored options (targets, designations, blueprints)
  and the typed text is limited to 512 characters. Manual speech keeps a
  deterministic fallback and is a registered divergence.
- The five behavioural cruciform upgrades run while installed: speed, martyr
  burst, nature blessing aura, cleansing presence and the wrath melee bonus.
- The Eye economy matches `eotp.dm` (Stage 1 sign-off).

## Stage 6 audit result

The audit compared all 60 entries against the Eris source on name, phrase, cost,
cooldown, target mode and effect payload. Field parity is 60/60. The audit found
two issues and fixed both:

1. `Asacris` targeted the caster (`Self`) instead of the Eris front/grabbed
   bearer. It is now `AdjacentFollower`, range 1.5, and the locale description
   matches the Eris text.
2. `OxydNtBiomatterReclaimer` appeared in no map and no blueprint. It joins the
   NT blueprint catalog (11th entry) so the biomatter path is reachable in a
   live round. Eris has no reclaimer (it bridges the upstream machine), so no
   Eris blueprint is displaced.

The audit also fixed two pre-existing YAML syntax errors in
`Resources/Prototypes/_Oxyd/erisPorted/mask/` (`fake_moustache.yml`,
`Vapour_mask.yml`) that stopped the prototype loader and the map tests from
starting.

## Review remediation

All ten findings from `.hermes/review/neotheology-evaluation.md` are closed:

| Finding | State |
| --- | --- |
| 1. Obelisk attacks normal crew | Fixed; carp faction corrected to `Dragon`; positive and negative tests pass. |
| 2. Shared reclaimer output changed | Fixed; `OxydNtBiomatterReclaimer` child added and given an in-round path in Stage 6. |
| 3. Resurrection needs the corpse | Fixed; grows the saved profile; corpse-deletion tests pass. |
| 4. Pre-debit validation missing | Fixed with the `ValidateOnly` bridge pass and full-cast tests. |
| 5. Scrying lifecycle cleanup | Fixed; death, detach and disconnect paths covered. |
| 6. Aura regeneration stale | Fixed; `RefreshRegeneration` plus shutdown cleanup, covered. |
| 7. Eye repeats the same presence | Fixed; shared scanned set plus 10-minute reversal; economy aligned to Eris values. |
| 8. Machine power not checked | Fixed with `NeoTheologyMachineSystem`; unpowered tests pass. |
| 9. Bioreactor without solution | Fixed; processing requires `ChamberSolution`. |
| 10. Test notice archive grows | Fixed; observer event replaces the dictionary. |

## Milestone ledger

| Milestone | Current assessment | Remaining work |
| --- | --- | --- |
| 1. Baseline and contracts | Complete | Keep catalog and schema checks green. |
| 2. Cruciform lifecycle | Complete | Keep lifecycle coverage. |
| 3. Speech and cast transaction | Complete | Keep atomicity and result tests. |
| 4. Bible UI and common effects | Complete | Live client render step is in the smoke runbook. |
| 5. Ranks and medical/social foundation | Complete | Closed in `89e4cc1784` and `17b7908f45`. |
| 6. Foundation integration review | Complete | Smoke runbook maps every step to its automated test; the live client pass stays a maintainer action. |
| 7. Dependency packets | Complete | Ceremonies landed in `7e9c0afcb7`; zero gated entries remain. |
| 8. Full catalog audit | Complete | Field parity 60/60; register below; fixes in the Stage 6 commit. |

## Packet record

| Packet | Litanies | State |
| --- | --- | --- |
| PlantGrowth | `AcceleratedGrowth` | Landed in `0c3cdb1294`. |
| Purity | `Rejection` | Landed; implant scan and brute rider. |
| ThreatClassification | `RevealAdversaries` | Landed; faction scan plus landmine scan. |
| Addiction | `WordsOfPurging` | Landed; reagent purge (named divergence). |
| Pain | `Atonement`, `Penance` | Landed; stamina damage (named divergence). |
| CoreModules | `Asacris` | Landed in `0c3cdb1294`; strips upgrades, target fixed in Stage 6. |
| Persistence | `BaptismalRecord` | Landed in `738ad6dfcf`; altar paper, live bearer scan. |
| Fidelity | Upgrade behaviours | Landed in `17b7908f45`; five upgrades, 4 integration tests. |
| Fidelity | `Sending`, `Scrying`, `Confirmation` | Landed in `89e4cc1784`; server-owned book choice flow, 4 integration tests. |
| ConstructionCatalog | `DivineGuidance` | Landed in `45b24941f2`; blueprint choice plus material list. |
| Construction | `Manifestation`, `Uproot` | Landed in `45b24941f2`; front-tile build and refund. |
| NtUplink | `Knowledge`, `Bounty` | Landed in `fa8a5996c7`; hidden cruciform store plus the NeoTheology category. |
| Ceremonies | 11 litanies | Landed in `7e9c0afcb7`; engine, obelisk force-active, crusader set grant, disciple HUD. |
| Audit | All 60 | Field parity 60/60; Asacris target fix and reclaimer blueprint in the Stage 6 commit. |

## Divergence register

Every deliberate difference from Eris, with the reason. The full per-entry audit
is in `.hermes/review/neotheology-catalog-audit.md`.

**Target-mode reductions** (no address registry, no global picker in the fork):
`Atonement`, `Penance` use `VisibleFollower`; `Excommunication`, `Scrying`,
`Sending` use `StationFollower`; the front/grabbed-reach litanies use
`AdjacentLiving`/`AdjacentFollower` (own or faced tile, 1.5 m); `Order Armaments`
keys off the armaments printer instead of the EOTP; `Asacris` uses
`AdjacentFollower` because Eris requires an active cruciform on the victim.

**Cooldown additions:** `Relief`, `Soul Hunger`, `Entreaty`, `Reveal Adversaries`,
`Cruciform Sense` and `Revelation` get a personal 60 s cooldown. Eris declares
`cooldown_time = 1 MINUTES` on the base ritual and calls `set_personal_cooldown`,
but never sets `cooldown = TRUE`, so its check is a no-op; the fork implements the
declared intent. The eight group ceremonies carry a shared 1 s per-starter key
(Eris has none) so a second start cannot overlap a running rite.

**Costs:** `Divine Guidance` and `Knowledge` cost 0 in the fork. Eris lists
`power = 5` for both but charges nothing because `perform` returns null/false.

**Payload reductions and substitutions (with reason):**

- `Relief`, `Hand of Mercy`, `Absolution of Wounds`, `Convalescence`, `Succour`:
  the Eris NT reagents (`angelsbalm`, `deusblessing`, `holyinaprovaline`,
  `holydexalin`) do not exist in the fork, so these heal the matching damage
  channels. `Convalescence`/`Succour` keep the exact Eris amounts.
- `Atonement`, `Penance`: Eris `adjustHalLoss(50)` becomes 50 stamina damage
  (no pain value exists).
- `Words of Purging`: no addiction model; purges habit-forming reagents. The
  Eris painkiller message stays.
- `Rejection`: no external limbs; strips implants and applies the brute rider.
- `Reveal Adversaries`: `NpcFactionMember` scan and `LandMineComponent`; the
  hidden 20 % miss and the wire-splicing entity do not exist.
- `Offerings`: observation (1000 / 500) instead of the Eris miracle list; the
  Holy Guidance oddity half is deferred (no oddity prototype).
- `Eye of the Protector`: power, armament and observation values match `eotp.dm`.
  The `ODDITY` miracle no-ops (empty reward list) and `ObservationPerFaithless`
  is unused (no faithless marker). The offering miracle lists are not used.
- `Baptismal Record`: live bearer scan instead of the disciple registry, and
  round-scoped (no persistence API).
- `Adoption` / `Ordination` / `Omission` / `Excommunication`: profile rank swaps,
  because the fork has no separate clearance field.
- `Initiation`: single ritual instead of the two-stage ascension kit; promotes to
  Preacher, as the Eris `priest_convert` profile says.
- `Commitment` / `Install Upgrade` / `Uninstall Upgrade`: the lying-on-altar and
  undressed gates are not implemented.
- `Reincarnation` / `Resurrection`: upstream cloning pod grows the stored
  profile; the Eris per-rank `clone_damage` is not applied.
- Machinery: the multi-part biogenerator/bioreactor commands map onto the
  flattened machines; power is checked at the point of use; `ActivateDoor` has no
  facing requirement and no broken-door state.
- `Knowledge` / `Bounty`: fork-local store catalog; no NT-pattern weapons, ritual
  blade or ascension kit; nullspace store and banked balance instead of the
  hidden uplink item.
- Ceremonies: 5-minute timeout; participants stay in range; timed per-litany
  skill entries instead of Eris stacking; no area-sanctify flag, no crusade world
  flag, no atheist mutation; HUD drains no power and dies with the implant;
  `Searing Revelation` forces the fall past the SS14 gravity rule.
- Upgrades: the martyr burst fires from the one-second tick; the nature aura uses
  one-second units; brute healing lands on the Blunt channel.
- Construction: no last-shelter device; multi-part machines collapse into one
  entity each; per-blueprint `build_time` becomes the litany `extraDelay`; the
  reclaimer joins the blueprint catalog (Stage 6).

**Open capability gaps (visible, not silent):**

| Gap | State |
| --- | --- |
| Oddity entity | No prototype carries `OddityComponent`, so `Divine Blessing` fails closed and the Eye `ODDITY` miracle no-ops. |
| Faithless / mutant / carrion penalties | `ObservationPerFaithless` is unused; the fork has no marker. |
| Eye blessing stat payoff | `OxydNtEyeBlessing` is mechanism-only; no stat numbers assigned. |
| Addiction model | Replaced by the reagent purge above. |

## Historical validation evidence

- Baseline recorded RobustToolbox `af2a7d0406` and SDK `10.0.203`.
- M1 `acc944a96a`: unit 16/16, `LitanyPrototypeTest` 3/3.
- M2 `6b9b3de457`: `CruciformLifecycleTest` 8/8.
- M3 `988219dcee`: `LitanyCastTest` 11/11, `LitanySecurityTest` 3/3.
- M4 UI `7cf78157be`: `LitanyUiTest` 9/9.
- Review checkpoint `6564fbe61d`: unit 17/17; integration 141 passed, 3 skipped.
- Fix pass `4b5970f3c4`: integration 166/166, zero skips.
- Packet wave `0c3cdb1294`: unit 17/17; integration 168/168, zero skips.
- `BaptismalRecord` `738ad6dfcf`: records test 2/2.
- Choice flow `89e4cc1784`: unit 17/17; integration 172/172, zero skips.
- Upgrade behaviours `17b7908f45`: unit 17/17; integration 176/176, zero skips.
- Construction packet `45b24941f2`: unit 17/17; integration 181/181, zero skips.
- NtUplink packet `fa8a5996c7`: unit 17/17; integration 184/184, zero skips.
- Ceremony packet `7e9c0afcb7`: unit 17/17; integration 193/193, zero skips; 9 new ceremony tests.
- Stage 6 audit corrections: unit 17/17; integration 193/193, zero skips
  (TRX `bad: []`); construction + prototype tests 9/9.

## Release state and open items

- The NeoTheology suite is green: unit 17/17 and integration 193/193 with zero
  skips on the final tree.
- Pre-existing, not NeoTheology: the station-map load tests fail on this branch
  with `Duplicate chunk entity` (`ChunkEntitySystem.AddChunk`), and the YAML
  linter reports 8 field/localization errors in unrelated fork prototypes
  (`newGuns.yml`, `auto.yml`, `restingObjectives.yml`, `bundle.yml`). Both are
  owned by the map/engine and gun workstreams.
- The `Oxyd14-port-eris-ironhammer` gitlink stays out of the NeoTheology
  commits; its ` m` working-tree state is owned by the Ironhammer port.
- The `.freebuff` preview files are removed from the branch.
- No push and no PR without owner approval.
