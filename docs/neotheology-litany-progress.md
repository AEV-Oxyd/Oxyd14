# NeoTheology litany progress

Status: **60/60 catalog entries enabled; gameplay parity not verified.** PR #33
(`C4BRA:eris-litany-port`) is the only open litany PR. Its branch includes the
medical and altar recovery pass, the client sandbox fix, and AEV-Oxyd `master`
through `97c050d91a`. The upstream merge brings the newer game UI and the
upstream `RobustToolbox` revision. It keeps the PR's visibility fix in
`ViewCalcSystem` where the two branches conflicted.

[neotheology-litany-dependencies.md](neotheology-litany-dependencies.md) lists
the implemented dependency packets and their limits. The tests check catalog
coverage, selected effects, and security rules. They do not prove Eris gameplay
parity. The current merged build still needs a live-round check.

## What works in code and focused tests

- All 60 entries have enabled definitions and handlers. Nine sets assign
  litanies by bearer profile. No dependency-gated entries remain.
- Speech and the private Bible UI start server-validated casts. The server
  checks rank, target, holiness, and cooldown. It refunds an unsuccessful cast.
  The book supplies target and blueprint choices. Speech uses a fixed fallback
  when a choice is necessary.
- The six profiles exist in `profiles.yml`. Only Chaplain maps to
  `OxydNtPreacher` in `rules.yml`. Six development-only
  `RandomHumanoidSpawnerOxydNt*` markers in `test_roles.yml` create test ghost
  roles. Each role receives its matching cruciform when a mind takes it and
  starts with `OxydNtBible`. An administrator must spawn a marker in a live
  round before the role appears in the ghost-role list. These roles do not
  add station jobs or job slots. Chaplain still does not receive
  `OxydNtBible` automatically.
- Medical rites use local reagents, pain, addiction, implants, robotic organs,
  and the cloner. Altar procedures check position, restraint, posture, and
  clothing as applicable. `Asacris` removes installed upgrades, not rank
  modules.
- The Eye, machines, hidden store, construction, upgrade effects, and group
  ceremony engine have implementations and focused integration tests.

## Checks after the upstream merge

| Check | Result |
| --- | --- |
| Server build | 0 errors, 496 warnings. |
| Client build | 0 errors, 250 warnings. |
| NeoTheology integration tests after test-role fix | 232 passed, 0 failed, 0 skipped. |
| Client build after test-role fix | 0 errors. |
| NeoTheology and shared-skill unit tests | 21 passed, 0 failed, 0 skipped. |
| Merged client and server smoke check | The server reached `Ready` on port 1212. The client reached `InGame`. Startup still reported prototype and sprite errors. No litany cast was checked. |
| Full YAML linter | Stopped at the existing syntax error in `Resources/Prototypes/_Oxyd/erisPorted/mask/fake_moustache.yml` (line 5). The litany PR does not change that file. |

These are counts for the merged checkout. Earlier test counts in past commits
are historical results, not results for this merge. The merge did not replace
NeoTheology files from the PR with older upstream versions.

## Known differences from Eris

- **Target choice:** `Atonement` and `Penance` use `VisibleFollower`.
  `Excommunication`, `Scrying`, and `Sending` use `StationFollower`. Adjacent
  rites check the actor or faced tile within 1.5 m. `OrderArmaments` selects
  the local armaments printer. Manual speech cannot show the book's choices.
- **Cooldown:** `Relief`, `SoulHunger`, `Entreaty`, `RevealAdversaries`,
  `CruciformSense`, and `Revelation` have a one-minute personal cooldown.
  Ceremonies have a one-second per-starter guard against overlap.
- **Cost:** `DivineGuidance` and `Knowledge` cost zero. Eris lists a cost for
  these rites but does not debit it when their effect returns false.
- **Medicine:** Native reagent metabolism replaces Eris reagent behavior.
  Local pain and stamina replace hallucination loss. `WordsOfPurging` advances
  addiction recovery without removing blood reagents. `Rejection` removes
  foreign implants and robotic organs but keeps natural organs and the
  cruciform. Exact recovery timing needs a live check.
- **Cloning and ranks:** The local cloner grows the stored soul profile; it
  does not apply Eris rank-specific clone damage. Rank changes replace Eris
  clearance fields. `Initiation` uses one promotion rite.
- **Machines and construction:** Eris multipart machines use local single
  entities. `ActivateDoor` has no facing requirement or broken-door state.
  `DivineGuidance` uses a local blueprint catalog, including the biomatter
  reclaimer. Construction delays follow local litany data.
- **Economy:** The Eye uses local observation rewards. Oddity rewards and
  faithless penalties lack the necessary entities. `Knowledge` and `Bounty`
  use a local hidden store and banked balance, not Eris equipment or its
  hidden uplink item.
- **Ceremonies:** A ceremony times out after five minutes and requires nearby
  participants. Skill effects have a fixed time rather than Eris stacking.
  Area sanctification, the Crusade world flag, and atheist mutation are absent.
  The disciple HUD uses no power and stops with the implant.

## Open gaps and live-round checks

1. Spawn each `RandomHumanoidSpawnerOxydNt*` marker with administrator tools
   in a live round. Take the ghost role. Check its cruciform, rank, Bible,
   and book UI with the new game UI. The focused test checks role creation,
   mind transfer, implants, and books; it does not check live client use.
2. Check spoken phrases, front and grabbed targets, book choices, costs,
   cooldowns, failed casts, and target privacy with two players.
3. Check healing, local NT reagents, addiction recovery, pain, and
   `Rejection` on implants and robotic organs.
4. Check `Reincarnation`, `Resurrection`, corpse handling, and the stored
   profile. Check altar posture, restraint, clothing, and material use.
5. Check `Asacris` on installed upgrades. Check that the rite keeps rank
   modules. Check `Initiation` and the hidden Inquisitor store.
6. Check the Eye, armaments disks, powered machines, construction, and the
   reclaimer in a live round. Oddity and faithless behavior remain incomplete.
7. Check followers, distance, interrupted casts, `Sanctify`, `Crusade`,
   `EternalBrotherhood`, and `SearingRevelation` in live ceremonies.
8. Check the Sanctify church-objective signal, Crusade objectives, normal
   Inquisitor access, and Chaplain Bible access. These gaps still need work;
   the passing focused tests do not close them.

The six test ghost roles require administrator-spawned markers. They are not
round-start jobs. Check each role with a real ghost in a live round before
claiming that the test path or its litanies work in the client.
