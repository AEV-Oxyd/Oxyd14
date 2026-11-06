# Moebius Research Wing — Port Blueprint (CEV-Eris → Oxyd14)

Scope: the **science/research** half of Moebius (`DEPARTMENT_SCIENCE` in Eris).
The medical half is already ported on `eris-litany-port`. Everything below was
audited in `C4BRA/CEV-Eris` source; live verification marks are added after the
in-game pass.

Eris source anchors: `code/game/jobs/job/science.dm`, `code/modules/research/*`,
`code/modules/mechs/*`, `code/modules/mob/living/silicon/robot/*`,
`code/modules/maps/shuttles-eris.dm`, `nano/templates/rdconsole.tmpl`.

## Port rules recap (from prior review feedback)

- Stock SS14 controls/styles only — replicate the Eris look inside those bounds,
  no custom rendering. Eris look: dark panels, gold labels, blue pill links
  (`ErisSurgeryStyle.cs` is the shared skin — reuse it).
- Do NOT reskin legacy `browse()` windows — keep stock elements. Only NanoUI
  windows get the Eris skin.
- UI overflow is the #1 flagged defect — screenshot every ported UI at default
  size; every label/button must stay inside its box.
- Entity protos use `ent-<id>` / `ent-<id>.desc` FTL keys. Stacks/accessLevels/
  reagents keep `oxyd-*` LocIds. `type: stack` needs a count-1 `<id>Single`
  spawn proto. Prototype components must be `[RegisterComponent]` in
  Content.Shared. Custom BUI class names must not end with an existing
  `*BoundUserInterface` name (LooseGetType suffix match).
- Cite Eris `.dm` file/proc in comments for fidelity.

---

## 1. Jobs & access — PORT

| Eris job (`science.dm`) | Flag | Access (Eris) | Port |
|---|---|---|---|
| Moebius Expedition Overseer | MEO, head | access_rd + broad (heads, toxins, robotics, xenobio, xenoarch, change_research, etc.) | PORT |
| Moebius Scientist | SCIENTIST | robotics, tox, tox_storage, moebius, maint, xenobio, xenoarch, research_equipment | PORT |
| Moebius Roboticist | ROBOTICIST | robotics, tox, morgue, tox_storage, moebius, maint, research_equipment | PORT |
| Moebius Psychiatrist | PSYCHIATRIST | robotics, tox, tox_storage, moebius, maint, research_equipment, psychiatrist, medical_equip | PORT (job+gear; sanity already ported) |

New `accessLevel` protos (mirroring `access.yml` medical pattern):
`OxydMoebiusResearch` (wing general = access_tox/moebius research),
`OxydMoebiusResearchEquipment` (consoles/lathe = access_research_equipment),
`OxydMoebiusRobotics`, `OxydMoebiusPsychiatrist`, `OxydMoebiusOverseer`
(head = access_rd). `access_xenobiology/xenoarch/genetics` fold into
`OxydMoebiusResearch` (no such SS14 subdepartments); `access_maint_tunnels`
→ stock `Maintenance`; `access_heads`/`access_change_research` → stock
`Command`/`Research`-family ids where they exist.

Job prototypes in `Resources/Prototypes/_Oxyd/Moebius/moebius_research_jobs.yml`
(new file; do NOT edit `moebius_jobs.yml` ids). Eris clothing already ported
(`erisport_moebius_scientists_jumpsuit`, `roboticists_jumpsuit`,
`moebius_expedition_overseers_jumpsuit`, `moebius_psychologists_turtleneck`,
`moebius_scientist_labcoat`). Department `MoebiusResearch` + playTimeTrackers +
job icons + test-role spawners, mirroring the medical pattern.

## 2. R&D core loop — PORT

Eris flow (verified in `research.dm`, `rdconsole.dm`, `rdmachines.dm`,
`destructive_analyzer.dm`, `experiment.dm`, `rdconsole.tmpl`):

- **R&D console** (`/obj/machinery/computer/rdconsole`) — NanoUI, the hub.
  Auto-links devices within ~3 tiles. Screens: `main` (menu buttons,
  Destructive Analyzer status + tech bars, tech-tree level bars, settings,
  device linkage, disk info), `tech_trees` (research points, tree selector,
  node map, node details + Research button), `protolathe` / `circuit_imprinter`
  (category pills, design list with materials/chems, build x1/x3/x5, material
  storage + eject, queue), `disk_management_{designs,tech,data}`, `locked`,
  `working`. req_access = research_equipment; Lock Console toggles access.
- **Destructive analyzer** (`/obj/machinery/r_n_d/destructive_analyzer`) —
  accepts any item with `origin_tech`; console "Deconstruct" destroys it after
  ~1-3s, grants research points
  (`experiment_data.get_object_research_value`: sum of `origin_tech_level *
  tech_points[tree]` for NEW tech levels/types; ~1 point for repeats), raises
  recorded tech levels, and returns the item's materials as stacks. Console
  "Eject" returns it. Deconstructing an item whose `origin_tech` contains an
  unknown tree's `item_tech_req` (covert) reveals the hidden tree.
- **Protolathe / circuit imprinter** (`/obj/machinery/autolathe/rnd/*`) —
  no own UI; driven by the console screens. Storage 120/60 sheets, queue 16.
  Imprinter also has a reagent beaker (glass+sacid costs).
- **Research datum** — 7 tech trees (`/datum/tech/*`: Engineering,
  Biotech, Combat, Power Manipulation, Blue-space, Robotics, Covert[hidden]),
  ~98 nodes (`/datum/technology`: cost, required_technologies,
  required_tech_levels, x/y/icon for the map, unlocks_designs). Unlocking a
  node costs points, raises its tree level by 1, grants its designs.
- **Experiment subsystem** (`experiment.dm`) — extra point sources:
  best explosion (Kinetic Energy Scanner beacon), autopsy-weapon scans,
  symptom scans, slime-core scans, science_tool datablocks.

### SS14 port shape (adapted to stock systems)

- `OxydRdConsole` entity + `OxydRdConsoleComponent` (Shared; points, researched
  node set, savedTechLevels, savedObjectTypes, disk slot ref) +
  `OxydResearchSystem` (Server) + one client window
  `OxydRdWindow` (custom BUI class whose name does NOT end in an existing
  `*BoundUserInterface`) reskinned with `ErisSurgeryStyle`. Screens map:
  - main → menu panel: link buttons to sub-screens, DA card (item, tech bars,
    research-value estimate, Deconstruct/Eject), tech-tree level bars,
    Settings (Sync database, Lock console, Reset), Device linkage
    (re-sync / disconnect), disk slot card.
  - tech_trees → points readout, tree selector pills, node list (stock
    controls table, not Eris's custom icon-map — custom rendering banned)
    with Researched/Available/Locked states + detail pane + Research button.
  - protolathe / imprinter → embedded lathe panel replicating Eris:
    categories, design rows (icon, name, per-material cost w/ missing in red,
    build x1/x3/x5), material storage + eject, queue + Clear.
  - disk screens → copy designs/tech/points to and from a portable HDD.
  - locked / working → status cards.
- `OxydDestructiveAnalyzer` entity: ItemSlot insert (or click-insert) accepts
  entities with `OxydOriginTechComponent` (`techLevels` dict) — added to
  ported science items + mapped stock items; deconstruct via console command
  or machine verb; spits material sheets; signals DA-completion for
  experiment hooks. `OxydOriginTech` also drives `get_object_research_value`
  (new tech level = points; repeats ~1).
- `OxydProtolathe` / `OxydCircuitImprinter` = lathe protos parented on the
  fork's `OxydLathe`/Autolathe line, but recipes are dynamic: recipe packs
  added per researched node (server grants recipe list to linked lathes).
  Imprinter gets a beaker slot + acid cost recipes.
- Tech trees/nodes/designs as prototypes:
  `OxydTechTreePrototype` (id, name, shortname, desc, rare, hidden +
  unlockTechTag) and `OxydTechNodePrototype` (id, tree, name, desc, cost,
  requiredNodes, requiredTreeLevels, unlocksRecipes → list of
  `LatheRecipePrototype` ids). Port ALL 7 trees + all ~98 nodes; designs
  subset below.
- Design port subset (~60-80 recipes, items that are useful standalone):
  cells (cell.dm — SS14 power cells exist), stock_parts (31 — machine part
  tiers), robot_parts (25 — borg limbs), computer_parts subset (disks/HDDs),
  devices (science_tool + light replacer + mesons), mining (5 — drills/PKA
  tier), toolmods subset, structures (4), weapon subset (basic Eris guns that
  already exist as erisport protos — check `Resources/Prototypes/_Oxyd/erisPorted/guns`
  before adding), prosthesis_implants → SKIP (medical owns), ai_boards/circuits
  targeting Eris-only machines → SKIP (no machine to build), illegal → SKIP
  (covert/antag), rigmodules → SKIP (no RIG port), mechs2 → see §4.
- HDD disks (`/obj/item/computer_hardware/hard_drive/portable`):
  `OxydResearchDisk` (holds tech nodes + designs + points), and the loot
  `OxydResearchPointsDisk` (`research_points` file = flat point grant,
  Eris `size*1000`). Console disk-management copies both ways.
- Research server (`server.dm`) network sync → ADAPT-lite: "Sync Database
  with Network" broadcasts the datum to all OxydRdConsoles (+ mechfab design
  caches); no dedicated server machine this pass (flagged gap).
- Experiment sources ported: science_tool (scan autopsy report → datablocks,
  insert into console → points; item research-value readout), explosion
  watcher beacon (points per explosion registered), autopsy scanner
  (dead mob → autopsy report paper). Symptom scans → SKIP (no virology);
  slime cores → SKIP (no xenobio port).

## 3. Robotics — ADAPT

- **Exosuit fabricator** (`/obj/machinery/autolathe/mechfab`) — PORT as a
  third lathe proto; prints robot parts + cyborg/exosuit part designs synced
  from a linked console ("Sync with R&D console" action; Eris requires the
  console's `sync=1`).
- **Cyborg** — Eris flow (`robot_parts.dm`, `robot.dm`): print l/r arm, l/r
  leg, torso (cell slot), head → combine into exoskeleton → insert
  MMI/posibrain (with a brain/soul) → live cyborg, then robot modules
  (Standard/Service/Medical/Miner/Engineering/Janitor/Security/Research),
  nanopaste repair, component swaps, `robot_upgrades.dm`.
  The fork already ships stock borg chassis
  (`Entities/Mobs/Cyborgs/*`). Port plan: robot_parts items + assembly chain
  → produces an `OxydRobotExoskeleton` item → insert brain (MMI item holding
  a brain organ; brains exist in the organ port) → spawns a stock borg mob
  via the fork's borg construction path. Module/upgrade depth → flagged
  partial (assemble + power + first module only).
- **MMI / posibrain** (`/obj/item/mmi`, `posibrain`) — items holding a brain
  organ that give the borg its identity; MMI = biological, posibrain =
  printed. PORT both (posibrain = mechfab design).
- **Maintenance drones** (`drone_fabricator` + `_drone.dm`) — player/ghost
  repair drones, fabricated over time. ADAPT-lite or flag: needs a small mob;
  decide after core loop lands.
- Robot repair console (`computer/robot.dm`) → SKIP (silicon-dept infra,
  flag).

## 4. Exosuits / mechs — SKIP (flagged, parts only)

True scope found: `code/modules/mechs/` ≈ 30 files — frame assembly
(`heavy_vehicle_frame` → weld reinforcements → install components → pilotable
exosuit with own HUD/motion/equipment), 74 designs (3 suits + 37 components +
34 equipment). A pilotable vehicle system this size can't land in this pass.
Decision: SKIP the vehicle. The mechfab still prints cyborg/drone parts
(its real round use). Flag: mech part designs exist; without the vehicle they
are dead ends — excluded from the ported design subset.

## 5. Expedition / overmap — SKIP (flagged)

`Vasiliy Dokuchaev` (`shuttles-eris.dm`) is an overmap shuttle needing the
overmap + points-of-interest maps (asteroid.dmm, spaceruins.dmm) — map-level
infra outside a department port. SS14 analog = stock `SalvageSystem`
(wreck/expedition missions). Flag as future work.

## 6. Xenoflora / xenobiology / xenoarchaeology — SKIP (flagged)

Named in the Scientist description as research-wing branches; all are
separate big systems (plant-DNA hydroponics, slime husbandry, artifact
analysis). Not ported; slime-core experiment points skipped with them.

## 7. Psychiatrist — PORT (job + gear only)

Eris psych = office + locker + medical equipment access; the actual therapy
surface is the sanity system (`code/modules/sanity/*`, already ported as
`_Oxyd/Sanity` on the base branch). Port = job, locker contents
(psych turtleneck, belt, hipster glasses — turtleneck already ported),
access. No dedicated machinery exists in Eris — nothing else to port.

## 8. Science gear — PORT

- `OxydScienceTool` (science_tool.dm in experiment.dm) — handheld scanner:
  use on item → research-value estimate; use on autopsy report → datablocks;
  insert into console → points. PORT (it is itself the first engineering
  design).
- `OxydAutopsyScanner` (`weapons/autopsy.dm`) — use on corpse → wound
  analysis → printable `OxydAutopsyReport` paper. PORT (needs `_Oxyd`
  wound records; degrade to damage-type list if wound detail absent).
- `OxydKineticEnergyScanner` (explosion_watcher) — beacon; explosions in
  range add to its record; console reads it into points. PORT.
- `OxydMentalImprinter` — Eris `spawn_blacklisted` antag-flavored loot
  (eye-stab stat boost + sanity damage). SKIP.
- Portable HDDs: `OxydPortableResearchDrive` (blank; holds designs/tech/
  points), `OxydResearchPointsDisk` (loot point grant). PORT.

## 9. Out of scope (explicit)

- Antag content, map layout/spawning into real maps, silicon AI/Robot jobs,
  cosmetic tiles, ALL of medical (already ported), NTNet message server,
  virology, stashes/loot tables for the ported items beyond a spawnable
  test set.

## Screenshot evidence list (live Eris pass)

- rdconsole main screen, tech_trees screen, protolathe screen, imprinter
  screen, disk screen, locked screen — `nano/templates/rdconsole.tmpl`.
- destructive analyzer idle/loaded icon states.
- mechfab autolathe UI (sync action).
- science_tool/autopsy use chat output.
- robotics: robot_parts + exoskeleton assembly steps + MMI.

(SS14-side evidence: each ported machine at default window size, each job's
spawn gear, DA → points → research → print loop end-to-end.)
