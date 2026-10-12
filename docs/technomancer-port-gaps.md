# Technomancer port — verification & gap report

Companion to `docs/technomancer-port-blueprint.md`. Verified on
`eris-technomancer-port` against a live dev server + client on this box
(BYOND-free RobustToolbox, Windows, llvmpipe/compat GL).

Status legend: **Verified** = exercised in-game incl. its UI, **Partial** =
implemented and confirmed partially in-game (spawned/rendering/state flowing)
but not fully exercised, **Skipped** = deliberately not ported this pass.

## Verified in-game

| Blueprint item | What was exercised |
|---|---|
| Technomancer SMES unit (`OxydSmesUnit`) | Spawned; `OxydSmesWindow` opened at default 460×460 — full Eris NanoUI reskin (dark body, gold item labels, blue pill links, ErisBar). Off/On toggles flipped live; Output Load readout updated each tick. Zero server code — pure `BatteryInterfaceSystem` reskin. |
| Pulsar control console (`OxydPulsarConsole`) | `OxydPulsarWindow` at default 480×340 two-column layout (stats left, satellite map + 8-dir pad right — matches Eris' own split). Live: Power Produced bar 64%, Fuel 0/700 cost 4, shield pills 0/45/70/100 with Effective% recompute on click (52%→46% at 70), orbit-decay countdown ticking m:ss, 15×15 satellite grid rendering beams/empty/satellite colors, all 8 direction buttons rendered, NE move correctly refused at 0 fuel with feedback text. |
| Machine entity stack (all six protos) | Spawned and rendered in-world — previously crashed the client until `drawdepth: Machines` was removed (invalid `DrawDepth` enum → `SpriteComponent.Read` `Enum.Parse`). Now renders with ported RSIs. |
| Server state loop | `TechnomancerMachinesSystem` pushes `SetUiState` once/sec for every open BUI; no exceptions in the server log across the whole session; bridge drives `PowerNetworkBattery.MaxSupply`, shield applies upkeep/regen, breaker toggles `ApcPowerReceiver.PowerDisabled`. |

## Partial

| Blueprint item | State | Why partial |
|---|---|---|
| Breaker box (`OxydBreakerBox`, `OxydBreakerWindow` 400×220) | Implemented: spawns, renders, BUI plumbing identical to pulsar/SMES, state pushed each tick, toggle writes `PowerDisabled` | Interactive click-through not captured: spawned machines landed stacked under mob sprites on this laggy box and the right-click entity picker only lists entities under the clicked pixel; several rounds of teleport/death-in-space ate the remaining verification budget. Same BUI path as the two verified windows. |
| Pulsar power bridge (`OxydPulsarPowerBridge`, `OxydBridgeWindow` 430×240) | Implemented: spawns, renders, `PowerNetworkBattery` (netsync off — required, else assert every tick), `CableDeviceNode` HVPower output, 1e9 J internal charge, portal-link + MW readout window | Same interactive-verification caveat as breaker. Nearest-console fallback link implemented (multitool pairing not ported — see Skipped). |
| Ship shield generator (`OxydShipShieldGenerator` + Hull variant, `OxydShieldWindow` 470×520) | Implemented: spawns, renders, status/integrity bars, mitigation, upkeep, field-size & input-cap steppers, scrollable 11-mode Eris table (`OxydShieldModes` with the original multipliers + `hackedOnly` flags), log lines; SetSize lowered 560→520 to fit this VM's ~480px usable viewport band | Same interactive-verification caveat. |
| Pulsar fuel tank (`OxydPulsarFuelTank`) | Implemented: spawns, renders, correct `ent-` FTL name/desc, fuel level in state | No BUI on Eris either (fuel gauge shown on the console) — verified by spawn + name. |
| Technomancer jobs | Prototypes ported (`jobs.ftl`: Exultant head + Technomancer role, supervisor = The Narrator chain adapted, department wired to the Engineering `departments.yml` id) | Spawning a player **into** the job via round flow not exercised — sandbox preset doesn't run the jobs game flow; protos load clean (server starts, entities resolve). |
| Power distribution model | SMES + breaker + bridge + pulsar chain follows the Eris topology (generation → bridge buffer → SMES → breaker → APC load) | Solar/wind generation, wiring primitives, and mapping the net onto a station grid are not portable on Oxyd maps (blueprint: Skip) — the machines are spawnable/testable as required. |

## Skipped (with reasons)

- **RCON console** — Eris' RCON is a `PowerMonitoringDevice` browse() HTML window; port rules forbid reskinning browse() windows, and the stock SS14 `PowerMonitoringConsole` already covers remote SMES monitoring. Blueprint marked Adapt→**deferred**; revisit if the orchestrator wants an Eris-looking monitoring window.
- **Air alarm / canister / omni filter reskins** — the RSIs are converted and in `Resources/Textures/_Oxyd/Technomancer/` (canister, omni_devices, monitors), but these are `browse()`-era UIs on Eris or pure stock-SS14 analogues; per port rules they keep stock controls. Sprites available for a later pass.
- **Nanoforge / autolathe Eris reskin** — fabrication stack differs fundamentally (Eris disk+nanoforge vs SS14 lathe recipes); only the autolathe RSI was converted. Deferred — needs a recipe model decision.
- **Duct tape, advanced tools, RCD/RPD, voidsuit/RIG** — tool-quality system is Eris-specific (`use_tool` qualities); porting it wholesale would add a parallel tool framework conflicting with SS14 `ConstructionSystem`. Blueprint: Skip pending a qualities-bridge design.
- **Multitool bridge↔console pairing** — replaced by nearest-console fallback so spawn-and-run works; pairing needs a multitool `DeviceLink` analog, deferred.
- **Solar/wind, atmos pipe model, map layout** — not portable (map/netmap bound).
- **Antag engineering content, AI/silicon interaction** — out of scope.

## Bugs found during verification (fixed / noted)

1. **`drawdepth: Machines` crashes the client** — `Machines` is not a valid `DrawDepth` enum member; `SpriteComponent.Read` throws on join. Removed from all six protos (commit `26070eaa89`). Only values in `Content.Shared/DrawDepth/DrawDepth.cs` are legal.
2. **Pulsar window overflowed the usable viewport** — this VM's client has a ~480px usable band; the 560→660px single-column layout clipped the direction pad. Fixed by switching to the Eris-native two-column arrangement at 480×340.
3. **`Battery` on a `PowerNetworkBattery` machine asserts every tick** unless `netsync: false` — required on the bridge (it's the buffer itself).
4. **`spawn:on` drops 4 copies per matching mob name** (organs share the name regex) and stacks everything under the mob sprite; the right-click tile menu only lists entities whose sprites cover the clicked pixel — co-located machines are unreachable that way. Worked around with `tp:coords {ent <anchor> | pos}` to spread machines onto furniture, then `ent X | delete` to clear occluders.
5. **Fifo console input dies under lag** — `tail -f` stops being consumed by the server mid-session; use a `winpty` TTY console (`write_to_process`), which stayed reliable.
6. **Space kills fast** — test mobs tp'd to entities that turn out to be on space tiles (stasis bed, medical bed on this map's solars edge) suffocate in ~60s; check the tile, not just the entity.
7. **Stale client dlls = ghost debugging** — builds report success while `bin/*.dll` copy steps fail silently when a client is running; `taskkill //F //IM dotnet.exe` before every rebuild.

## Follow-ups worth doing

- Interactive click-through of breaker/bridge/shield windows on a quieter box (spawn each alone on an empty lit tile).
- `technomancer:` toolshed debug command (mirroring `litany:`) to spawn a clean labeled test-rig row — would have saved most of the verification friction.
- Decide RCON / nanoforge / tool-quality scope with the orchestrator.

## Wave 2 additions (feature push 91%)

Ported since the coverage baseline: RCON console (dedicated `OxydRconConsole`/`OxydRconWindow` — stock SMES/breaker toggle semantics, verified opened live with populated rows; toggle messages flow server-side), atmos control console, air alarm reskin, canister reskin (stock sprite + `OxydCanisterBui` surface), omni filter/mixer, pump/valve skins (RSI + protos), nanoforge (`OxydNanoforge` + ItemSlot feed + compressed-matter material/stack/single protos), duct tape (`OxydDuctTape`), advanced tools (`OxydWrench`/`OxydCrowbar`/`OxydWirecutter`/`OxydMultitool` — stock SS14 tool semantics, Adapt), RCD (`OxydRapidConstructionDevice`, stock RCD parent), RPD (`OxydRapidPipingDevice`, stock RPD parent), design disks (`OxydDesignDisk*`), tool mods (`OxydToolMod*` — AfterInteract quality stat bumps).

All spawn live and render correct sprites on the verification grid (`technomancer:grid`/`technomancer:items` console commands). BUI/in-hand click-through for the wave-2 surfaces remains unverified — VM input lag + test-mob death consumed the budget; verified BUIs: SMES, pulsar, RCON.

## Blocked (reported for user decision)

- **Long-range scanner (§8)** — Eris relies on BYOND overmap/sensor view; no SS14 equivalent framework. Needs a grid-scanner design decision.
- **Modular software suite (§11)** — no modular-computer base in SS14. The atmospherics-control slice was delivered as the dedicated `OxydAtmosConsole` instead.

## Coverage

Measured against `docs/technomancer-port-blueprint.md` as the contract (discrete blueprint items — blueprint-Skip items counted as not ported).

### Feature coverage — ported/adapted 21 of 23 blueprint items (91.3% coverage, 8.7% gap)

Ported/adapted (21): jobs+access+dept wiring (§1); SMES + window (§2); breaker box (§3); pulsar console + starmap, power bridge, fuel tank (§4); RCON console (§5, Adapt); ship shield + hull (§6); canister/air-alarm/atmos-console reskins + omni filter/mixer + pump/valve skins (§7); nanoforge (§9); duct tape, advanced tools, RCD/RPD, design disks, tool mods (§10); gravity/solar kept stock (§12); DMI→RSI worklist (§13).

Not ported (2 — Blocked, reported for user decision): long-range scanner (§8); modular software suite (§11).

### Testing coverage — live-verified 5 of 21 ported items (23.8% coverage, 76.2% gap)

Verified live (spawned + interacted + observed): SMES window; pulsar console window; RCON window (opened, live rows, messages reach server); DMI→RSI pack (every sprite renders); jobs protos (load clean).

Spawned + rendered on distinct tiles, not click-interacted (14): breaker, bridge, fuel tank, shield gen, atmos console, air alarm, canister, omni pair, pump/valve trio, nanoforge, all items — each spawned live and rendered its ported sprite; BUI/in-hand paths code-verified only.

Not exercised (2): gravity/solar (stock — nothing to exercise); the 2 Blocked items.
