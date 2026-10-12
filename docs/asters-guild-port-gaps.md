# Asters Guild port — verification gaps

Live-verified in Oxyd14 (Dev map, sandbox, localhost admin) against `docs/asters-guild-port-blueprint.md`.
Evidence screenshots: `docs/asters-guild/` captures + session recording frames.

## Verified (works in-game, exercised end-to-end)

| Blueprint item | Result |
|---|---|
| Trade console BUI (`OxydTradeConsole`, ErisNanoStyle reskin of NanoUI `trade.tmpl`) | **Verified.** Station tree (Zarya/Solnishko/???), station info panel w/ live offer countdown, category pills, goods table with per-good cart +/-, View Cart (Create Order/Reset), Saved Carts + Order Requests stubs, Logs tab with transaction entries. Balance + beacon status in header. |
| Trade order loop | **Verified.** Cart → Create Order: goods spawned at the receiving beacon's tile (2x crowbar = 2 entities), station stock decremented (21→19), transaction logged. |
| Sell/offer loop | **Verified.** Offers tab shows station buy orders w/ live expiry; Send consumed the crowbars in range of the sending beacon (offer 5→3), price credited to account balance. |
| Trade beacons (receiving/sending) | **Verified.** Spawnable, appear in header as present/absent, drive delivery + sell radius. |
| Ore processor console + unit (`OxydOreProcessorConsole`, `OxydOreProcessor`) | **Verified.** Console pairs to nearest unit ≤3 tiles (re-pairs on open if the unit spawned after it — Eris LateInitialize analog). Loaded Materials table shows inserted ore counts; TURN ON/OFF toggles smelting; Melting Rate bar reflects Running. |
| Ore insert + smelt | **Verified.** Holding coal + clicking the unit inserts it (InteractUsing, entity consumed, `Coal: 1` in Loaded Materials). After ~5s cycle a `Coal1` sheet spawned at the unit. Ore→sheet map mirrors Eris `machine_processing.dm`. |
| Deep drill (`OxydDeepDrill`) | **Verified.** Empty-hand interact toggles Running (Eris `attack_hand` analog); while on it ejects a random ore stack (coal/plasma/gold/iron…) at its tile every ~10s — observed coal, plasma ore, gold ore accumulating. |
| Power gating | **Verified (stock behavior).** Unpowered machines report "not powered" and refuse to open BUIs; ApcPowerReceiver loads set per machine. |
| Cash register / EFTPOS (`OxydCashRegister`, `OxydEftpos`) | **Verified (spawnable).** Minimal entities — register is a powered machine stub, EFTPOS an Item. Eris cash-register/EFTPOS deep economy interactions are simplified (see Gaps). |
| Guild vending (`OxydVendingPowermat/Printomat/Style`) | **Verified (spawnable).** Stock `BaseVendingMachine` BUI — intentionally NOT reskinned: vending was TGUI on Eris, and the port rules only reskin NanoUI windows. Inventories: cells/power (Powermat), NT design disks (Printomat), guild clothing (Style). |
| Jobs + department + access | **Verified (prototype-level).** `guild_jobs.yml`: Guild Merchant (head), Guild Technician, Guild Miner w/ cargo jumpsuit + credits starting gear. `access.yml`: 4 oxyd access levels. `guild_roles.yml`: AstersGuild department (#C8A032) + weighting. YAMLLinter clean. |
| Credits (`OxydCredits` stack + 1/100/500 denominations) | **Verified (prototype-level).** Stock `Objects/Economy/cash.rsi` sprite, SpaceCash-pattern stack type; job gear spawns credit stacks. |

## Fixed during live verification

- **Header overflow (UI defect #1):** balance/beacon labels overflowed the default window → shortened labels, balance clipped cleanly. Re-verified: all header text inside bounds.
- **Station description overflow** → ClipText + HorizontalExpand; now truncates at panel edge.
- **Processor console pairing** → was MapInit-only; console spawned before unit never paired. Now re-pairs on BUI open (`OnOpened` → `TryPair`).
- **Deep drill had no toggle** → `InteractHandEvent` toggles Running (Eris attack_hand) + sets `OxydGuildVisuals.Working` appearance (building overlay). Processor Running also sets Working visual via console toggle.
- **"Not powered" UX** — verified as correct stock behavior (was briefly suspected as a UI bug).

## Partial / simplified (known gaps vs Eris)

- **Credits economy:** Guild account is a per-console `Balance` field (starts 3000cr), shared across consoles by copy — not a real station-wide bank/account entity. EFTPOS charge/transfer, cash-register till, and credit-to-cash conversion are stubs (entities exist, no UI/logic).
- **Trade stations:** 3 seeded stations (Zarya, Solnishko, hidden ???) with fixed goods/offers — Eris's procedural station generation, dynamic pricing shocks, embargo/discovery-by-beacon-exploration, and faction favor effects on prices are simplified: favor displays but doesn't reprice; offers rotate on a timer but from a fixed table.
- **Order delivery:** goods spawn loose at the beacon tile (no shipping crate manifest/paperwork); saved-carts + order-requests tabs are placeholders.
- **Drill:** spawns random ore on a timer — no real asteroid/rock integration, no drill-area requirement, no ore-box pickup; visual is icon+building overlay only.
- **Processor:** smelt is Queue-timer only — no smelting alloy recipes consuming two ores (Alloy Menu buttons exist and output the alloy sheet directly), no molten-material reservoir.
- **No mapped content:** everything is spawn/admin-verified only; no job spawns wired into maps, no cargo shuttle, no mining outpost.
- **Access levels** exist as prototypes but aren't enforced on consoles (localhost admin used; per-access denial untested).
- **Guild armor/weapon stock, merchant's ledger program, supply shuttle/crate pipeline:** skipped per blueprint (scope cut for core-loop completeness).

## Test-environment notes (not product bugs)

- Context-menu name-click = default interaction only for machines (opens BUI / InteractHand); it does NOT pick up floor items — ore pickup had to be done via direct sprite click or `container:insert` into a hand slot.
- `spawn <proto> <x> <y>` coord form fails to parse in this toolshed build; `spawn <proto> <uid>` lands on a snapped adjacent tile. `tp:to` jumps across grids/maps freely.
- Spawning every machine on one tile creates click piles — isolate entities (different tiles) before click-testing.
- Machines spawned mid-floor are unpowered → `comp:rm ApcPowerReceiver` for headless testing.
- Dev sandbox NPCs wander and can step between you and the machine; mob death by vacuum possible on off-station grids.
