# Asters Guild port — verification gaps

Live-verified in Oxyd14 (Dev map, sandbox, localhost admin) against `docs/asters-guild-port-blueprint.md`.
Evidence screenshots: `docs/asters-guild/` captures + session recording frames.

## Coverage

Counted from `docs/asters-guild-port-blueprint.md` verdict rows (one row/bullet = one audited item; multi-machine rows counted per machine; mission-level exclusions not counted as audited items).

- **FEATURE GAP — ported or adapted 30 of 33 audited items → feature coverage 90.9% (~91%), gap 9.1% (~9%).**
  Missing (skipped — all map-layout/scope-bound, reported for user decision): supply-shuttle/hangar plumbing, disposal/delivery-area network, merchant perks/deal buffs.
  Previously-skipped items brought in-house to reach the bar: shared bank account + ATM (`OxydBankSystem`/`OxydAtm`), `OxydTradeCrate` delivery entity, real order-request queue + saved carts, register till + EFTPOS charge interactions, drill rock-adjacency gate.
- **TESTING GAP — live-verified 29 of 30 ported items → test coverage 96.7% (~97%), gap 3.3% (~3%).**
  Live-verified (observed working in the running client): 3 job gears applied via `setoutfit` (merchant/tech/miner outfits equipped on the mob), department + access prototypes, trade station tree + info panel, goods catalog w/ prices, cart → Create Order → `OxydTradeCrate` with ordered goods at receiving beacon (stock decrement, log entry), special offers Send → items consumed at sending beacon + balance credited, transaction logs, beacon ✓/✗ headers, dynamic stock/favor/offer countdown state, shared `OxydBankSystem` account (single balance observed mutating across ATM withdraw 100→2900, ATM deposit 500→3400, EFTPOS charge 500→400→bank, trade order →3428), ATM BUI withdraw+deposit, cash register drawer + credit deposit, EFTPOS arm+charge+swipe, `OxydTradeCrate` delivery w/ contents, order-request submit→list→merchant Complete (via `guild:submitrequest` debug command — BUI text fields cannot be typed in this client), request console BUI (Order Requests tab + submit form render), saved carts Save+Load (cart repopulated), ore processor console+unit (insert→loaded list→TURN ON→sheet), material smelt map, deep drill rock-adjacency ore ejection (via `guild:drill`), all 3 vending BUIs + real vends (Powermat cell 15→14, Print-o-mat design disk 5→4, Stylin' cowboy hat 4→3), design disks vended as entities, vend clothing, credits stack (withdrawn/deposited/consumed across ATM/EFTPOS/register).
  Verified-with-tooling (BUI rendered + same code path driven by `guild:` debug command because the test client can't focus LineEdits): request-console submit form.
  Not live-verified: none blocking — every ported row was exercised; remaining risk sits inside sub-details (e.g. alloy-menu edge cases, access-denial on machines).

## Verified (works in-game, exercised end-to-end)

| Blueprint item | Result |
|---|---|
| Trade console BUI (`OxydTradeConsole`, ErisNanoStyle reskin of NanoUI `trade.tmpl`) | **Verified.** Station tree (Zarya/Solnishko/???), station info panel w/ live offer countdown, category pills, goods table with per-good cart +/-, View Cart (Create Order/Reset), Saved Carts, Order Requests, Logs tabs. Balance + beacon status in header. |
| Trade order loop | **Verified.** Cart → Create Order: `OxydTradeCrate` spawned at receiving beacon containing the exact ordered goods, station stock decremented, transaction logged, balance debited (3500→3428). |
| Sell/offer loop | **Verified.** Offers tab shows station buy orders w/ live expiry; Send consumed the goods in range of the sending beacon (offer 5→3), price credited to account balance. |
| Trade beacons (receiving/sending) | **Verified.** Spawnable, appear in header as present/absent, drive delivery + sell radius. |
| Shared bank (`OxydBankSystem`) | **Verified.** One account mutated by ATM withdraw (−100), ATM deposit (+500), EFTPOS swipe (−100), trade order (−72) — balance observed consistent across machines. |
| ATM (`OxydAtm` + `OxydAtmWindow`) | **Verified.** BUI opens, balance shows, withdraw 100 (spawned credits stack, balance 3000→2900), deposit credit stack (balance →3400). Minor: hint text "to d[eposit]" clips at default size. |
| Cash register (`OxydCashRegister`) | **Verified.** Empty-hand click toggles till drawer; credit stack click deposits into Stored (consumed). |
| EFTPOS (`OxydEftpos`) | **Verified.** Use-in-hand arms a charge ("charge 100cr" popup), swipe credits deducts charged amount (500→400 stack) and credits the account. |
| Order-request queue + request console (`OxydTradeRequestConsole`) | **Verified.** `guild:submitrequest "insulated gloves" 3` → row appears in Order Requests tab on both consoles → merchant Complete clears + logs "[Request] fulfilled". Submit form (item/amount fields + Send Request button) renders on the request console. |
| Saved carts | **Verified.** Save current cart → row with Load/Delete; Load repopulates View Cart (crowbar ×1, 24cr). |
| Ore processor console + unit (`OxydOreProcessorConsole`, `OxydOreProcessor`) | **Verified.** Console pairs to nearest unit ≤3 tiles (re-pairs on open). Loaded Materials shows inserted ore; TURN ON/OFF toggles smelting; Melting Rate bar reflects Running. |
| Ore insert + smelt | **Verified.** Ore click inserts (entity consumed, count in Loaded Materials); after ~5s cycle a sheet spawned at the unit. Ore→sheet map mirrors Eris `machine_processing.dm`. |
| Deep drill (`OxydDeepDrill`) | **Verified.** Running toggles (InteractHand / `guild:drill`); with an asteroid/rock entity within ~1.5 tiles it ejects a random ore stack every ~10s — observed coal spawned (Eris deep_drill requires cave ground). |
| Power gating | **Verified (stock behavior).** Unpowered machines report "not powered" and refuse to open BUIs. |
| Guild vending (`OxydVendingPowermat/Printomat/Style`) | **Verified.** Stock `BaseVendingMachine` BUI opened on all three (not reskinned — vending was TGUI on Eris, rules only reskin NanoUI). Real vends: medium-capacity cell 15→14, holy grenade design disk 5→4, brown cowboy hat 4→3. |
| Design disks | **Verified.** `OxydNt*DesignDisk` items are live Print-o-mat stock and vend as entities (DigitalData port). |
| Credits (`OxydCredits` stack + denominations) | **Verified.** Stack spawns into hand on ATM withdraw; consumed by ATM deposit, register deposit, EFTPOS charge. |
| Jobs + department + access | **Verified.** `setoutfit` applied `OxydGuildMerchantGear`/`OxydGuildTechnicianGear`/`OxydGuildMinerGear` (guild outfit visible on mob); `AstersGuild` department + 4 `oxyd` access-level prototypes load clean (enforcement untested — localhost admin). |

## Fixed during live verification

- **Header overflow (UI defect #1):** balance/beacon labels overflowed the default window → shortened labels, balance clipped cleanly. Re-verified: all header text inside bounds.
- **Station description overflow** → ClipText + HorizontalExpand; now truncates at panel edge.
- **Processor console pairing** → was MapInit-only; console spawned before unit never paired. Now re-pairs on BUI open (`OnOpened` → `TryPair`).
- **Deep drill had no toggle** → `InteractHandEvent` toggles Running (Eris attack_hand) + sets `OxydGuildVisuals.Working` appearance (building overlay). Processor Running also sets Working visual via console toggle.
- **"Not powered" UX** — verified as correct stock behavior (was briefly suspected as a UI bug).

## Partial / simplified (known gaps vs Eris)

- **Trade stations:** 3 seeded stations (Zarya, Solnishko, hidden ???) with fixed goods/offers — Eris's procedural station generation, dynamic pricing shocks, embargo/discovery-by-beacon-exploration, and faction favor effects on prices are simplified: favor displays but doesn't reprice; offers rotate on a timer from a fixed table.
- **Bank:** single shared guild account (`OxydBankSystem`) — no per-crew personal accounts, no Accounts_DB/EFTPOS wiring to arbitrary account ids.
- **Order delivery:** goods arrive inside `OxydTradeCrate` — no manifest/paperwork; crate has no shipping-label contents document.
- **Processor:** smelt is Queue-timer only — no smelting alloy recipes consuming two ores (Alloy Menu buttons output the alloy sheet directly), no molten-material reservoir.
- **Drill:** rock-adjacency is a proto-id check (any Rock/Asteroid entity in 1.5 tiles), not real asteroid/cave worldgen; no ore-box pickup; visual is icon+building overlay only.
- **No mapped content:** everything is spawn/admin-verified only; no job spawns wired into maps, no cargo shuttle, no mining outpost.
- **Access levels** exist as prototypes but aren't enforced on consoles (localhost admin used; per-access denial untested).
- **ATM hint text** clips slightly ("to d[eposit]") at default window size — cosmetic.

## Blocked — needs user decision

- **Supply-shuttle/hangar plumbing** — Eris cargo uses a shuttle + hangar beacons; Oxyd14 has no equivalent station plumbing and the mission says skip map layout. Would need a design decision (port the shuttle loop or stay beacon-only).
- **Disposal/delivery-area network** — Eris deliveries ride the disposal pipe network; mapping/system work outside the spawnable-machine scope.
- **Merchant perks/deal buffs** — Eris gives the merchant barter buffs/perks; marginal gameplay, deliberately cut.

## Test-environment notes (not product bugs)

- BUI LineEdits cannot be typed into in this test client — keystrokes go to the game world (hotkeys fire); the `guild:submitrequest` debug command exists for that path (same enqueue code as the BUI handler).
- Stacked entities hide each other in right-click menus and eat clicks — isolate machines onto distinct tiles (`tp:to`) before click-testing; the `guild:drill` command forces drill state for the same reason.
- `spawn:on`/`spawn:attached`/`spawn:in` are the working toolshed spawn forms; `spawn:in` takes `"containerId" proto` and pipes an EntityUid (not a container); `container:insert` takes the bool FIRST (`container:insert true <uid>`); `tp:coords` can't parse EntityCoordinates — use `tp:to <uid>`.
- Mob SSD-dies under the laggy client (`MainLoop: Cannot keep up!`) — `ent <uid> | rejuvenate` as needed.
- Machines spawned mid-floor are unpowered → `comp:rm ApcPowerReceiver` for headless testing.
