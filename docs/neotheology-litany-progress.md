# NeoTheology litany progress

Status: **Phase 4 packet work is active on `neotheology/fix-pass`. 49 of 60 entries are enabled.**

Completion plan: `.hermes/plans/2026-09-11_135754-neotheology-completion.md`.
Implementation checkpoint: commits through `fa8a5996c7`.

## Current implementation

- 60 catalog entries and 9 sets: 49 foundation entries (all enabled) and 11
  dependency-gated entries.
- **49 implemented and enabled.** `LitanyHandlerCatalog.Implemented` matches the
  enabled set.
- **11 remain disabled:** `PoundingWhisper`, `RevelationOfSecrets`, `LispOfVitae`,
  `CantoOfCourage`, `ChantOfObservance`, `ReclamationOfEndurance`, `Sanctify`,
  `Crusade`, `EternalBrotherhood`, `CallToBattle`, `SearingRevelation` (the
  ceremony block).
- The inquisitor's hidden uplink runs (NtUplink): the rank installs the module,
  the first litany use creates the nullspace store and the balance survives a
  module swap.
- Server speech recognition, DoAfter casting, ownership/entitlement checks,
  cruciform lifecycle, holiness, profiles and access are implemented.
- The cast transaction is atomic: an unexpected apply failure refunds the debit
  and skips the cooldown; success-only cooldowns; the actor receives
  `LitanyResultMessage` and `LitanyProgressMessage` on the open book UI.
- A book cast of a choice-requiring litany pauses in the `Choosing` stage. The
  server publishes server-authored options (targets, designations) and the typed
  text is limited to 512 characters. Manual speech keeps a deterministic fallback
  and is a registered divergence.
- The five behavioural cruciform upgrades run while installed: speed, martyr
  burst, nature blessing aura, cleansing presence and the wrath melee bonus.
- The Eye economy matches `eotp.dm` (Stage 1 sign-off). No deviation remains to
  register for P3.5.

## Review remediation

All ten findings from `.hermes/review/neotheology-evaluation.md` are closed:

| Finding | State |
| --- | --- |
| 1. Obelisk attacks normal crew | Fixed; carp faction corrected to `Dragon`; positive and negative tests pass. |
| 2. Shared reclaimer output changed | Fixed; `OxydNtBiomatterReclaimer` child added. World placement remains an integration task. |
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
| 4. Bible UI and common effects | Substantially complete | Live connected-client polish. |
| 5. Ranks and medical/social foundation | Handlers complete | `Sending` text, `Confirmation` designations and `Scrying` target choice are live; closed in `89e4cc1784` and `17b7908f45`. |
| 6. Foundation integration review | Not complete | Manual two-client scenario, live round. |
| 7. Dependency packets | In progress | Ceremonies (11). |
| 8. Full catalog audit | Not complete | Source-to-runtime audit of all 60 entries. |

## Packet record

| Packet | Litanies | State |
| --- | --- | --- |
| PlantGrowth | `AcceleratedGrowth` | Landed in `0c3cdb1294`. |
| Purity | `Rejection` | Landed; implant scan and brute rider. |
| ThreatClassification | `RevealAdversaries` | Landed; faction scan plus landmine scan. |
| Addiction | `WordsOfPurging` | Landed; reagent purge (named divergence). |
| Pain | `Atonement`, `Penance` | Landed; stamina damage (named divergence). |
| CoreModules | `Asacris` | Landed; strips every cruciform upgrade. |
| Persistence | `BaptismalRecord` | Landed in `738ad6dfcf`; altar paper, live bearer scan. |
| Fidelity | Upgrade behaviours | Landed in `17b7908f45`; five upgrades, 4 integration tests. |
| Fidelity | `Sending`, `Scrying`, `Confirmation` | Landed in `89e4cc1784`; server-owned book choice flow, 4 integration tests. |
| ConstructionCatalog | `DivineGuidance` | Landed in `45b24941f2`; blueprint choice plus material list. |
| Construction | `Manifestation`, `Uproot` | Landed in `45b24941f2`; front-tile build and refund. |
| NtUplink | `Knowledge`, `Bounty` | Landed in `fa8a5996c7`; hidden cruciform store plus the NeoTheology category. |
| Ceremonies | 11 litanies | Pending; engine required. |

## Immediate follow-up

1. Ceremony engine plus the 11 ceremony litanies.
2. Map the NT machine set into a station area.
3. Final catalog audit, manual two-client scenario, release cleanup.

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
