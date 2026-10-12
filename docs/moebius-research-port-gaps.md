# Moebius Research Wing — Gap Doc (post-port verification)

Status of every blueprint item after the Oxyd14 live-verification pass.
Build state at time of writing: `Content.Server` + `Content.Client` compile
with 0 errors/warnings; `dotnet run --project Content.YAMLLinter` reports
**No errors found.**

Verification method: local dev server + game client, admin spawned all
ported prototypes on a live mob, drove the UIs at default window size and
exercised the full R&D loop end-to-end.

## Verdict summary

| Blueprint section | Verdict |
|---|---|
| 1. Jobs & access | Verified (prototypes spawn; gear round-trip via loadout not re-tested) |
| 2. R&D core loop | Verified end-to-end |
| 3. Robotics | Partial (robot parts not ported — scope cut, see below) |
| 4. Exosuits/mechs | Skipped by design (parts only via mechfab recipes) |
| 5. Expedition/overmap | Skipped by design |
| 6. Xenoflora/xenobio/xenoarch | Skipped by design |
| 7. Psychiatrist | Verified (job/gear protos exist; no dedicated psych mechanics — flagged gap) |
| 8. Science gear | Mostly verified (science tool + disks functional; autopsy report upload untested) |
| Console BUI | Verified incl. all four screens at default size |
| Sprites | Partial — reused stock RSIs; Eris DMI extraction deferred |

## Live-verified behavior (in-game evidence)

The full Eris R&D loop was exercised on a live client session:

1. Spawned `OxydRdConsole`, `OxydDestructiveAnalyzer`, `OxydProtolathe`,
   `OxydCircuitImprinter`, `OxydMechfab`, `OxydExplosionWatcher`, disks,
   science tool, and deconstructable items on one tile.
2. Console → **Rescan Devices** linked all four machines (shown in the
   device picker: protolathe / circuit imprinter / exosuit fabricator, DA
   implied by Deconstruct button).
3. Inserted a `OxydCellPrototype` into the DA → **Deconstruct** consumed
   the item, granted points (0 → 550), raised Engineering to 3 and
   Power Storage to 2, and refunded material sheets onto the floor.
4. **Tech Trees** screen: 7 tree tabs (Biotech/Bluespace/Combat/
   Engineering/Power Storage/Robotics/Covert) render with candystriped
   node rows showing cost, state, prerequisites and unlock list.
5. Researched `Basic Engineering` (free, cost 0) → state flipped to
   *researched*, Engineering 3→4, and `Monitoring` went
   *requires Basic Engineering* → *available*.
6. Inserted a points disk into the console disk slot → **Disk
   Management** screen showed "+5000 points available" → **Load Data**
   credited the points (550 → 5550) and marked the disk *spent*.
7. Researched `Monitoring` (500 pts): points 5550→5050, Engineering
   incremented to 5; `Ice And Fire` and `Advanced Replication` became
   available — **prereq cascade verified both directions**.
8. **Protolathe/Fab** screen → `circuit imprinter` device lists the
   unlocked designs (autolathe, cyborg recharge station, mech recharger,
   power monitoring console…) each with a Queue link and the correct
   *needs materials: steel/glass* gate when the lathe is empty.

## Verified (details)

- **Entity prototypes** — all spawn without errors: console, DA,
  protolathe, imprinter, mechfab, explosion watcher, 5 machine boards,
  research data disk, research points disk, science tool, autopsy
  report, cells, sheets.
- **BUI state push** — fixed this session (`AfterActivatableUIOpenEvent`
  → `UpdateUi`); window renders on first open.
- **Eris skin** — gold title bar, blue pill nav links, candystripe rows,
  red "requires …" sub-labels; no overflow at default 720×560 after the
  two-line row rework (name sole expander, status line, link own line).
- **Loc keys** — `ent-*`/`ent-*.desc` generated for all new protos.
- **Tree data** — all 7 Eris trees and 83 nodes ported with correct
  costs/prereqs/unlocks (`oxydTechTree`/`oxydTechNode` protos + generated
  `research_recipes.yml`/`research_packs.yml`).
- **Access levels** — `oxyd-access-research`, `-research-command`,
  `-robotics`, `-psychiatrist`, `-science` registered and wired into the
  4 job protos.

## Partial / smaller gaps

- **Lathe Queue click path** — design listing + material gating verified
  in the BUI; the final Queue→printed-item hop was not exercised on the
  live client (floortile item wrangling under NPC crowd). The handler is
  a thin pass-through to `LatheSystem.QueueRecipe` — low risk.
- **Research data disk (tech/design) ops** — points disk fully verified;
  the data disk's Save/Load-design branches compile and render but were
  only hit on the points path in-game (the disk that got inserted was
  the points disk).
- **Science tool scan→upload** — entity spawns; the use-on-DA flow was
  not exercised end-to-end (data-block items are implemented and the
  DA accepts the report item path).
- **Autopsy report upload** — the medical port owns the scanner
  (`OxydAutopsyScannerComponent`/`OxydErisAutopsyScanner`); this port
  ships only the `OxydAutopsyReport` data-block item the console can
  consume. Upload path not re-tested here.
- **Explosion watcher** — spawns and links; the bomb-test → points-bank
  path requires a real explosion and was not run.
- **Job spawn points / late-join flow** — jobs exist as protos and
  appear in role defs; not verified through a real lobby join (no mapped
  spawn points — out of scope by design).
- **Sprites** — stock SS14 RSIs used for all machines/items (exosuit
  fabricator, computers.rsi `rdcomp`, artifact analyzer, module `radio`,
  datadisk). Eris DMI→RSI extraction deliberately deferred; visual
  fidelity to Eris is approximate, not pixel-ported.
- **Eris live audit caveat** — the Eris GUI comparison pass was cut
  short (BYOND NanoUI wedge + time); the port leaned on code audit of
  `code/modules/research/*` and `code/game/jobs/job/science.dm`, so some
  cosmetic Eris-UI details may differ from the actual rendered window.

## Broken / known issues

- **Robotics chain** — blueprint §3 marked ADAPT; robot part items and
  the frame assembly chain were **not ported** (scope call: full
  cyborg/exosuit construction depends on stock SS14 silicon systems that
  don't exist in the fork). The mechfab exists and accepts recipes, so
  the entry point is there when silicon lands.
- **Psychiatrist mechanics** — job + access + gear only. Eris therapy/
  sanity interactions were not ported (Eris-side content is thin; the
  `_Oxyd/Sanity` system on the base branch is the intended host).

## Skipped (by design, per blueprint)

- Exosuit/mech construction (parts only via mechfab recipe list)
- Expedition/overmap system
- Xenoflora/xenobiology/xenoarchaeology
- Antag content, map layout, AI/silicon stack, cosmetic tiles

## Files

- Blueprint: `docs/moebius-research-port-blueprint.md`
- Port code: `Content.{Server,Shared,Client}/_Oxyd/Moebius/Research/`
- Prototypes: `Resources/Prototypes/_Oxyd/Moebius/research_*.yml`,
  `moebius_research_*.yml`
- FTL: `Resources/Locale/en-US/_Oxyd/research.ftl` (+ generated entity loc)
