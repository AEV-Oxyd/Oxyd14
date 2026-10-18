# Technomancer League (CEV-Eris Engineering) → Oxyd14 Port Blueprint

This document is the contract for porting the CEV-Eris engineering department
("Technomancer League") into Oxyd14. Every item lists what it is, how it
behaves in Eris (verified live on BYOND 516.1667 — see `~/eris_ui_captures/`
screenshots referenced inline), its Eris UI spec, the proposed SS14 analog, and
a Port / Adapt / Skip decision.

Audit method: booted `C4BRA/CEV-Eris` master (incl. TGUI fix ed5457d) on
dreamdaemon 1337, joined as admin ghost, spoofed living `stat` and drove
`nano_ui_interact(usr)` via SDQL2 to open real machine NanoUIs. Code citations
use `file:line` against that checkout.

---

## 1. Jobs, access & department wiring — **Port**

### What it is
Two jobs in `code/game/jobs/job/engineering.dm`:

- **Technomancer Exultant** (`/datum/job/chief_engineer`) — head of
  Engineering, 1 slot, flag `EXULTANT`, departments `ENGINEERING|COMMAND`.
  Access (~19): `access_engine`, `access_engine_equip`, `access_tech_storage`,
  `access_maint_tunnels`, `access_teleporter`, `access_network`,
  `access_external_airlocks`, `access_atmospherics`, `access_emergency_storage`,
  `access_eva`, `access_heads`, `access_construction`, `access_sec_doors`,
  `access_ce`, `access_RC_announce`, `access_keycard_auth`, `access_tcomsat`,
  `access_ai_upload`, `access_change_engineering`. Stats MEC40/COG20/TGH15/VIG15,
  perk `/datum/perk/inspiration`, software_on_spawn: comm, ntnetmonitor,
  power_monitor, alarm_monitor, atmos_control, rcon_console, camera_monitor,
  shield_control, reports.
- **Technomancer** (`/datum/job/technomancer`) — 3 slots. Access:
  `access_eva`, `access_engine`, `access_engine_equip`, `access_tech_storage`,
  `access_maint_tunnels`, `access_external_airlocks`, `access_construction`,
  `access_atmospherics`. Stats MEC30/COG15/TGH10/VIG10. Software: power/alarm/
  atmos/rcon/camera/shield monitors.

Outfits in `code/datums/outfits/jobs/engineering.dm`: utility belt, eng headset,
work boots, thick gloves, t_scanner; Exultant adds white hardhat, exultant
uniform+coat, Makarov + 2 rubber mags; engineer adds technomancer helmet,
uniform, insulated vest.

### Eris UI
Job selection is standard round-start UI — nothing to port.

### SS14 analog
Mirror `Resources/Prototypes/_Oxyd/Moebius/` conventions:
`technomancer_jobs.yml` (job protos + startingGear), `technomancer_access.yml`
(new `type:accessLevel` ids with `oxyd-*` LocIds only where no stock level fits),
`technomancer_roles.yml` (department + playTimeTracker, secondary Command dept
for Exultant), `technomancer_status_icons.yml`. FTL `ent-<id>` keys +
`oxyd-jobs-technomancer.ftl` for names/desc. Clothing comes from the existing
`Resources/Prototypes/_Oxyd/erisPorted/` catalog (already complete — verified
in-tree: jumpsuits, voidsuit/RIG, helmets, gloves, belts, berets, coats).
Command announce for Exultant via `AddComponentSpecial` like Moebius head.
**Port.**

---

## 2. Power: SMES — **Adapt**

### What it is / Eris behavior
`/obj/machinery/power/smes` (`code/modules/power/smes.dm`). Stores charge,
input (charge from net, Auto/Off, level W) vs output (discharge to net,
Online/Offline, level W + current load). Mapped variants `buildable` and
`buildable/engine`. RCON-tagged for remote control.

### Eris UI (verified live)
`nano/templates/smes.tmpl` — capture `~/eris_ui_captures/smes.png`.
Stored Capacity bar %, Input Management (Charge Mode Auto/Off pills +
Not/Charging badge; Input Level MIN/SET/MAX + W), Output Management (Online/
Offline pills + Outputting badge; level bar), Output Load bar. Dark panel,
amber bars, pill links.

### SS14 analog
Stock SS14 SMES machine (already a full power storage loop with RCON-ish
device network). **Adapt**: new proto `OxydSmesUnit` (stock components, Eris
dmi→RSI sprite `icons/obj/power.dmi`) + reskin its BUI with the shared Eris
skin (`ErisSurgeryStyle.cs` palette/widgets — dark bg, gold labels, blue
pills). Label layout mirrors smes.tmpl rows (Capacity / Input / Output / Load)
inside stock controls. **Adapt.**

## 3. Power: breaker box — **Port (small)**

`/obj/machinery/power/breakerbox` (`code/modules/power/breaker_box.dm`) —
portable circuit breaker: toggles whether its net passes power; RCON-visible.
Minimal UI (power on/off + watt readout, `breaker.tmpl`-adjacent).
**Port**: tiny stock-based entity `OxydBreakerBox` with PoweredDevice +
a one-button BUI (Eris skin); pairs with RCON list (§5). Low effort, keeps
the Technomancer power toolkit intact.

## 4. Pulsar engine (marquee) — **Port (core loop) — starmap minigame Adapted**

### What it is / Eris behavior
`code/modules/pulsar_engine/`:
- `/obj/machinery/pulsar` console (pulsar_consoles.dm:209+) opens
  `pulsar.tmpl` starmap: **Power Produced %** (linear distance-to-beam
  formula), **Fuel tank scan** (`/obj/structure/pulsar_fuel_tank`, 700u cap,
  refilled by item tanks, bursts ≥100 moles, explodes ≥125),
  **thrust_cost = round((1/specific_mass)*2)**, **Shield power reroute %**
  slider, **Effective = produced*(100-shield)/100**, **orbit decay 15 min**,
  8-dir move pad.
- `/obj/machinery/power/pulsar_power_bridge` — injects `PULSAR_100_POWER`
  (25 MW) × effective% via `add_avail`; disconnect/reconnect with 5-min
  `RECONNECT_CD`; shows PORTAL LINK status (pulsar.tmpl→
  pulsar_power_bridge.tmpl).
- `/obj/effect/pulsar_ship` on PULSAR map: random decay moves, beam
  collision → 3-min warn → `pulsar_rad_storm`; under-shielded → overcharge
  event (pulsar_map.dm, pulsar_events.dm). `PULSAR_COLOR #82C2D8`.

### Eris UI (verified live)
`pulsar_starmap.png`: "Satellite Control" — Power Produced 49% Watts (green
bar), Fuel Levels 87% (red bar), Movement fuel cost: 4, Shield power reroute
(needed 39.8% / set 45%, "Set shield power" pill), Effective power produced
26.95%, Orbit decay 15 min, 8-dir d-pad.
`pulsar_power_bridge.png`: Power Produced 6.7375 MW bar, "disconnect
satellite" pill, "PORTAL LINK ACTIVE AND STABLE".

### SS14 analog
**Port** the loop, **Adapt** the starmap: the 5-z PULSAR map minigame is a
BYOND map hack — Oxyd has no overmap; emulate with a `PulsarSatellite` entity
on a 3×3 logical grid held in components (position, fuel, orbit-decay timer)
driven by the console BUI: stock `GridContainer` buttons for the 8-dir pad +
slider for shield power, bars for power/fuel. Beam positions + decay moves
server-side. Fuel tank = stock `GasCanister`-style portable with fill/eject.
Bridge = stock `PoweredDevice` + `add_avail` equivalent (power net load
supplier). Events → StationEvent weighted rad-storm/overcharge clones or
server-side status text if events infra is too heavy (gap-doc it if cut).
Sprites: `icons/obj/pulsar_machinery.dmi`, `pulsar_power_bridge.dmi`,
`pulsar_fuel_tank` dmi → RSI.

## 5. RCON remote power console — **Adapt (console) / Port (SMES+breaker toggles)**

### Eris behavior
`/datum/nano_module/rcon` (rcon_console.dm) runs inside modular computers;
`rcon.tmpl` lists every SMES (charge bar, input AUTO/OFF + set W, output
ONLINE/OFFLINE + set W) and every breakerbox (ENABLED/DISABLED + toggle).

### SS14 analog
Stock power-monitoring console exists (power table readout). **Adapt**: an
`OxydRconConsole` BUI in Eris skin — scrollable `BoxContainer` of SMES rows:
charge bar + input/output pills + MIN/SET/MAX + W, plus breaker rows with
ENABLE/DISABLE pill. Backed by the device-network/powerednet API stock SS14
already exposes. **Adapt.**

## 6. Shipshield generator — **Port**

### What it is / Eris behavior
`/obj/machinery/power/shipside/shield_generator` (shield_generator.dm,
modes.dm). States: no-conduit / off / running. UI shows running/integrity%/
capacity MJ/mitigation EM-PH-HE-MAX/upkeep kW/total use/field size; mode
flags w/ multipliers (hyperkinetic 1.2, photonic 1.3, humanoids/silicon/
nonhumans 1.5, atmospheric 1.3, hull 1, modulate 2, bypass+overcharge 3
hacked-only, multiz); tendrils deploy to power conduits (anchored=FALSE,
right-click retract); star proximity upkeep mult; wires datum; event log.

### Eris UI (verified live)
`shield_generator.png` — "hull shield core": SYSTEM STATUS (Generator
Offline, Integrity 30%, Capacity 1500/5000 MJ, Mitigation 0/0/0/60% MAX,
Upkeep 0kW, Total 0kW (No Limit), Field 0/0 m² Radius 200), CONTROLS
(Turn on, Set Field Range, Set Input Cap), LOGGING box, FIELD CALIBRATION
rows (mode/desc/Status pill Disabled/Multiplier). `shieldgen.tmpl`.

### SS14 analog
Stock SS14 has no ship shield — **Port**: `OxydShipShield` component+system:
power draw, integrity/capacity fields, mode list (reuse multiplier table as
data on the component), event-log list in BUI, hull subtype via a second
proto. Tendrils → Adapt: replace physical tendrils with "must be anchored on
a powered cable node" check + status line (simpler, same gameplay gate).
BUI = big reskin (Eris skin): sections SYSTEM STATUS / CONTROLS / LOGGING /
FIELD CALIBRATION exactly per shieldgen.tmpl. Sprite `icons/obj/machines/
shielding.dmi`.

## 7. Atmospherics devices — **Adapt**

- **Canisters** (`/obj/machinery/portable_atmospherics/canister`,
  canister.tmpl: label+Relabel, kPa, port status, holding tank eject, release
  valve bar). Stock SS14 canisters exist → **Adapt** skin only (Eris skin on
  stock canister BUI) + `icons/obj/atmos.dmi` sprite.
- **Omni filter/mixer** (`/obj/machinery/atmospherics/omni/filter|mixer`,
  omni_devices.dmi): 4-port filter/mixer with per-port controls.
  → **Adapt**: build from stock `GasFilter`/`GasMixer` machines with Eris
  sprite + skinned BUI; omni (4-output) logic is Eris-specific — extend stock
  filter system if trivial, else port standard 3-port and gap-doc omni.
- **Pumps/valves/passive gates** — stock equivalents exist; **Skip** reskin
  unless trivial.
- **Air alarm** (`/obj/machinery/alarm`, air_alarm.tmpl) — stock SS14
  AirAlarm exists → **Adapt** skin + sprite (monitors.dmi).
- **Atmos control console** (modular program, atmos_control.tmpl) — like
  RCON: **Adapt** as fixed console with area/atmos alerts list in Eris skin.

## 8. Long-range scanner — **Skip**

`/obj/machinery/power/shipside/long_range_scanner` is bound to the BYOND
overmap (`obj/effect/overmap`) — no SS14 analog; gap-doc it. Its console UI
(near-stock readout) can be re-added later if Oxyd gains an overmap.

## 9. Nanoforge — **Port (small)**

`/obj/machinery/autolathe/nanoforge` (autolathe.dmi, nanoforge_actions.tmpl):
consumes items → compressed matter (per-material ratios, 0.6 eff, 240 storage,
oddities, no upgrades). **Port**: `OxydNanoforge` entity — insert items →
material bar fill + compressed-matter stack output (stock StackComponent +
stack spawn protos incl. `<id>Single`). UI = compact Eris-skin pane (item
slots + matter bar + eject).

## 10. Tools, construction & gear — **Port (curated) / Adapt**

Eris `/obj/item/tool` system (qualities, workspeed, degradation/health, cell/
fuel/stock, quality-swap toggles, mods) is a full alternate tool engine —
don't port the system. **Port curated items as stock SS14 tools with
OxydToolGetModifiersEvent compatibility** (`Content.Shared/_Oxyd/Tools/
Events.cs` exists):
- **Duct tape** (`tools/tape.dm`: QUALITY_ADHESIVE 30 + SEALING 30, stock 100)
  → `OxydDuctTape` roll item, stock-uses.
- **Advanced tools**: combi_driver (screwdriver+multitool), armature_cutter
  (wirecutter+crowbar), pneumatic_crowbar, powered_hammer, weldertool_adv —
  as stock tools with combined ToolQualities + charge cells where needed.
- **RCD / RPD** (design refs in IJIRO disk) → map to stock `RCD` proto +
  `RapidPipeDispenser` if present, else gap-doc.
- **Design disks** (ARK-034/IJIRO-451/ESPO-830/LAT-018, technomancers.dm) —
  **Adapt**: fold their design lists into TechDisk/recipe protos only if the
  port grows a matching lathe; else **Skip** (Oxyd has its own research/
  lathe model — disk licensing doesn't map).
- **Tool mods/complicator** (mods dir ~1600 lines) — **Skip** (alternate
  upgrade economy; too deep for this pass).

## 11. Modular-computer software suite — **Skip (infrastructure) — console adaptations covered above**

Eris runs power_monitor / alarm_monitor / atmos_control / rcon / camera /
ntnetmonitor / comm / reports / shield_control as modular-computer programs
on laptops/consoles. Porting the whole stack is out of scope. The
gameplay-relevant subset (RCON + atmos/power monitoring) is covered as fixed
consoles in §5/§7. Mark Skip in gaps.

## 12. Gravity generator & misc mapped machines — **Adapt**

Stock SS14 `GravityGenerator` exists → keep stock + note. Solar assemblies/
control consoles exist stock → keep stock. `repairstation`-type mapped
objects aren't distinct machinery in code → Skip.

## 13. Icons → RSI worklist

`icons/obj/pulsar_machinery.dmi`, `pulsar_power_bridge.dmi`,
`pulsar_fuel_tank`, `icons/obj/machines/shielding.dmi`, `icons/obj/power.dmi`
(smes/breaker), `icons/obj/atmos.dmi` (canister), `icons/atmos/
omni_devices.dmi`, `icons/obj/monitors.dmi` (air alarm),
`icons/obj/machines/autolathe.dmi` (nanoforge), tool/ item dmis.
32 px L→R,T→B; animated → strip PNG + meta.json delays; RSI `directions`
must match PNG width.

## Port rules compliance checklist (applied to every item)

- Stock SS14 controls/styles only; no browse() reskins — only NanoUI windows.
- Eris skin via `ErisSurgeryStyle.cs` palette/widgets (dark/gold/blue pills).
- `ent-<id>`/`ent-<id>.desc` FTL keys; `oxyd-*` for accessLevels/stacks.
- Stack protos need `<id>Single` spawn entities.
- Components `[RegisterComponent]` in Content.Shared.
- Custom BUI names must NOT end with an existing `*BoundUserInterface` name.
- Screenshot every BUI at default size — no overflowing labels.
- Cite Eris .dm file/proc in comments; run Content.YAMLLinter clean.
- Layout: `Content.{Server,Shared,Client}/_Oxyd/Technomancer/`,
  `Resources/Prototypes/_Oxyd/Technomancer/`, FTL `en-US/_Oxyd/`.

## Priority order (core loop first)

1. Jobs/access/roles/icons (ship needs crew first)
2. SMES + breakerbox + RCON console (power management loop)
3. Pulsar console + fuel tank + power bridge (marquee engine)
4. Shipshield generator
5. Air alarm + canister skin + omni filter
6. Nanoforge + duct tape + advanced tools
7. Gaps doc + verification pass
