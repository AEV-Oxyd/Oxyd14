# NeoTheology litany progress

Status: **In progress — Phase 4 remediation and packet work are active.**

Implementation checkpoint: branch `neotheology/fix-pass`, commits `55974202b6` and
`6d8b73e3f4` (the committed review fix pass). The completion plan is
`.hermes/plans/2026-09-11_135754-neotheology-completion.md`.

## Current implementation

- 60 catalog entries and 9 sets: 37 foundation entries (36 enabled plus
  `BaptismalRecord`) and 23 dependency-gated entries.
- **36 implemented and enabled.** `LitanyHandlerCatalog.Implemented` matches the
  enabled set.
- **24 remain disabled:** `AcceleratedGrowth`, `WordsOfPurging`, `Asacris`,
  `Atonement`, `BaptismalRecord`, `DivineGuidance`, `Manifestation`, `Uproot`,
  `PoundingWhisper`, `RevelationOfSecrets`, `LispOfVitae`, `CantoOfCourage`,
  `ChantOfObservance`, `ReclamationOfEndurance`, `Sanctify`, `Crusade`,
  `Rejection`, `RevealAdversaries`, `Penance`, `Knowledge`, `Bounty`,
  `EternalBrotherhood`, `CallToBattle`, `SearingRevelation`.
- Server speech recognition, DoAfter casting, ownership/entitlement checks,
  cruciform lifecycle, holiness, profiles and access are implemented.
- Book presentation and private snapshots exist. The cast transaction is still
  not atomic: application failure can retain the debit and cooldown.

## Review remediation (committed)

The independent review `.hermes/review/neotheology-evaluation.md` found ten
issues. The committed fix pass closes them in direction:

| Finding | State | Remaining work |
| --- | --- | --- |
| 1. Obelisk attacks normal crew | Fixed in direction | Faction whitelist names a missing `Carp` id; `MobCarp` is `Dragon`. |
| 2. Shared reclaimer output changed | Fixed | `OxydNtBiomatterReclaimer` has no world placement. |
| 3. Resurrection needs the corpse | Fixed, tests pass | Keep covered. |
| 4. Pre-debit validation missing | Fixed in direction | Add full-cast refusal tests. |
| 5. Scrying lifecycle cleanup | Fixed in direction | Disconnect test fails on the dummy-session path. |
| 6. Aura regeneration stale | Fixed | Positive obelisk test is skipped. |
| 7. Eye repeats the same presence | Fixed in direction | Align armaments cap and accrual to Eris. |
| 8. Machine power not checked | Fixed | Keep covered. |
| 9. Bioreactor processes without solution | Fixed | Keep covered. |
| 10. Test notice archive grows | Fixed | Keep covered. |

## Milestone ledger

| Milestone | Current assessment | Remaining work |
| --- | --- | --- |
| 1. Baseline and contracts | Complete | Keep catalog and schema checks green. |
| 2. Cruciform lifecycle | Complete | Keep lifecycle coverage. |
| 3. Speech and cast transaction | In progress | Atomic debit and refund, success-only cooldown, result publication. |
| 4. Bible UI and common effects | In progress | UI progress and result refresh, target tokens, `BaptismalRecord`. |
| 5. Ranks and medical/social foundation | Complete in handlers | Fidelity gaps: upgrade effects, `Sending`, `Confirmation`, `Scrying`. |
| 6. Foundation integration review | Not complete | Full suite, manual two-client scenario. |
| 7. Dependency packets | In progress | 10 packets remain, ceremonies last. |
| 8. Full catalog audit | Not complete | Source-to-runtime audit of all 60 entries. |

## Immediate follow-up

1. Fix the obelisk faction default (`Dragon`, not `Carp`) and make the positive test run.
2. Fix the `Dirty` call on `NeoTheologyDoorComponent`.
3. Fix the scrying disconnect test and the `dirty-disposed` skips.
4. Align the Eye armaments cap and accrual to Eris (`eotp.dm:36-49`).
5. Place `OxydNtBiomatterReclaimer` in the world.
6. Complete the cast transaction and the Bible UI.
7. Then packets in the completion-plan order, ceremonies last.

## Historical validation evidence

These are earlier checkpoints, not results from the current branch.

- Baseline recorded RobustToolbox `af2a7d0406` and SDK `10.0.203`.
- M1 (`acc944a96a`): NeoTheology unit tests 16/16 and `LitanyPrototypeTest` 3/3.
- M2 (`6b9b3de457`): `CruciformLifecycleTest` 8/8.
- M3 (`988219dcee`): `LitanyCastTest` 11/11, `LitanySecurityTest` 3/3.
- M4 UI (`7cf78157be`): `LitanyUiTest` 9/9.
- Review checkpoint `6564fbe61d`: unit 17/17; integration 141 passed, 3 skipped;
  catalog pins 24 passed, 1 skipped. The review found the defects listed above.
- Current fix-pass baseline: unit 17/17; integration 159 passed, 2–3 failed,
  4–5 skipped of 166. The failures are the three defects in Immediate follow-up.
