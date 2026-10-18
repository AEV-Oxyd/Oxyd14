# Asters Guild Port — Blueprint

Port of the **Asters Merchant Guild** (CEV-Eris cargo department) into Oxyd14.
Source: `C4BRA/CEV-Eris` @ `ed5457d`. Live-audited in a running round on BYOND 516.1667
(R_HOST admin, observed → transformed to human; machines spawned at feet and opened via
`attack_hand` so NanoUI/TGUI panes render). Screenshots committed under `docs/asters-guild/`.

Legend: **Port** = bring over as-is, **Adapt** = port the mechanic to a stock SS14 system,
**Skip** = leave out (with reason).

---

## 1. Jobs & access

| Item | Eris (verified) | SS14 analog | Verdict |
|---|---|---|---|
| **Guild Merchant** (head) | `/datum/job/merchant` (`code/game/jobs/job/guild.dm:2`), `DEPARTMENT_GUILD`, `GUILD\|COMMAND`, supervisors "your greed". Access: `maint_tunnels, mailsorting, cargo, cargo_bot, merchant, mining, heads, mining_station, RC_announce, keycard_auth, sec_doors, eva, external_airlocks, change_cargo` (`guild.dm:15-18`) | JobPrototype `OxydGuildMerchant` + `oxyd-access-*` AccessLevels mirroring the list | **Port** |
| **Guild Technician** | `/datum/job/cargo_tech` (`guild.dm:48`), `GUILD`, `department_account_access = TRUE`, access: `maint_tunnels, mailsorting, cargo, cargo_bot, mining, mining_station` (`guild.dm:62-64`) | JobPrototype `OxydGuildTechnician` | **Port** |
| **Guild Miner** | `/datum/job/mining` (`guild.dm:119`), same access list as tech (`guild.dm:116-118`) | JobPrototype `OxydGuildMiner` | **Port** |
| Department wiring | `DEPARTMENT_GUILD` department flag, spawn landmarks in `/area/eris/quartermaster*` | Department prototype `AstersGuild`, spawnable job protos (map-agnostic per port rules) | **Port** |

## 2. Trade program (modular computer) — the core loop

`/obj/item/modular_computer/console/preset/trade` (`preset_console.dm:190`) installs
`/datum/computer_file/program/trade` → `/datum/nano_module/program/trade`
(`code/modules/modular_computers/file_system/programs/generic/trade.dm`).
NanoUI: `nano/templates/trade.tmpl` + `trade_goods/offers/cart/orders/saved/log.tmpl`.

Verified live (screenshots):

- `trade_program_main.png` — header (Exit/Minimize, battery, time, Shutdown), buttons
  "To Trade Screen | ✓Account | ✓Receiving Beacon | ✓Sending Beacon", "Exporter: NO SENDING
  BEACON FOUND" (beacon detection updates the header), and the **trade station tree**:
  green discovered stations linked to red `?` undiscovered ones in a branching graph.
- `trade_station_zarya.png` — selecting a station shows Name + description panel
  (TTB 'Zarya': "…sells electronics, construction, and anything related to engineering!").
- `trade_screen_categories.png` — Trade Screen: tabs `Goods | Offers | View Cart | Saved
  Carts | Order Requests | Logs`; account balance "0 credits"; station info with
  `Favor: 0/0` and `Offer Time Remaining: 01:45`; category buttons (Design Disks,
  Atmospherics, Supplies, Tools, Electronics, Power Generation, Machinery, Custom Flooring);
  cart row `0 | Create Order | Save | Load | Reset`.
- `trade_goods_tools.png` — goods table `Name | Price | Available | Cart` with live prices
  (crowbar 24, screwdriver 24, mechanical toolbox 300 …) and ± cart buttons.
- `trade_offers.png` — `Special Offers (time left: 01:27)`: rows of `Station | Name |
  Price | Amount | Send☐`; offers are requests FROM stations ("supermatter shard 20000",
  "modified crowbar (4 upgrades) 2800"). Bottom: `ERROR: No account linked.` — offers need
  a linked guild account.
- `trade_cart_empty.png`, `trade_logs.png` — cart ("There are no goods in the cart.") and
  log selector states.

| Feature | Verdict | SS14 analog |
|---|---|---|
| Station tree w/ discovery (green known / red `?` unknown, click → info panel) | **Port** | `OxydTradeConsoleBoundUserInterface` + `OxydTradeWindow.xaml` in `ErisNanoStyle` kit (ErisPanel/ErisRowButton/ErisStatusBox); station graph in `OxydTradeStationPrototype` list on server (`OxydTradeSystem`) |
| Goods catalog: category tabs → Name/Price/Available table with ± cart | **Port** | BUI state rows; `ErisRowButton` per good |
| Cart / Create Order / Save / Load / Reset | **Port** | cart state in `OxydTradeConsoleComponent`; order sends crate list to receiving beacon |
| Special Offers (station buy-requests, send checkbox, timer, favor) | **Port** | offer rows + send via sending beacon objects; favor accrues per fulfilled offer |
| Account linking ("No account linked" gate) | **Adapt** | minimal `OxydTradeAccountComponent` (name+balance) — no full banking UI |
| Order Requests tab | **Port** | incoming requests from crew `/preset/trade_orders` console — adapt: merchant sees request queue |
| Logs | **Port** | transaction log list in same window |
| Beacon status headers (✓/✗ receiving+sending, exporter line) | **Port** | scans for `OxydTradeBeaconComponent` entities in world |

## 3. Trade stations & dynamic pricing (backend)

`code/controllers/subsystems/trade.dm` (SStrade) + `code/modules/economy/TradeDestinations.dm`:
station tree graph (parents/children discovery), per-station goods categories with stock +
prices + buy price multipliers, favor level, rotating special offers with timers,
transaction logging. Verified live: prices/stock shown, offer timer counting down (01:45).

- **Port**: `OxydTradeSystem` (server) holding station prototypes, goods, dynamic stock/price
  state, favor, offers w/ countdown — seeded at map start (station tree is per-round data
  in Eris; keep fixed station list: Solnishko/Zarya + discovered stations from SStrade).
- **Port**: price/stock mutability — sells decrement stock, exports to stations pay out.

## 4. Trade beacons

`/obj/machinery/trade_beacon/{receiving,sending}` (`code/modules/trade/machinery/trade_beacon.dm`),
mapped as the cargo delivery mechanism (map: receiving (119,114,1), sending (125,114,1)).
Verified: trade program header detected "Receiving Beacon: Supply Shuttle Hangar (1, 1)".

- **Port**: `OxydTradeBeaconReceivingComponent` — orders placed in console materialize goods
  at the receiving beacon (crate spawn); `OxydTradeBeaconSendingComponent` — exports
  objects placed on/near it for offer fulfillment and direct sales. Both keyed into the
  trade console's header status.
- **Skip**: supply-shuttle hangar plumbing (map content, per mission).

## 5. Ore processing

`/obj/machinery/mineral/processing_unit_console` + `processing_unit`
(`code/modules/mining/machine_processing.dm`). TGUI "Processor" — verified
(`processor_tgui.png`): TURN ON button, Melting Rate dial, "Loaded Materials" table
(pitchblende, hematite, raw carbon, sand, plasma crystals, native silver/gold, diamond,
raw platinum, metallic hydrogen — each with a `Storing` action), "Alloy Menu"
(Plasteel / Steel / Borosilicate glass).

- **Port**: `OxydOreProcessor` (machine, takes ore items → sheets) + console entity with
  ErisNanoStyle BUI replicating the two panels (materials table + alloy buttons).
- **Adapt**: material set → SS14 stack protos (glass/plastic/steel/plasma/uranium/etc.);
  alloy recipes = composite smelts (plasteel etc.) as processor outputs.

## 6. Deep drill

`/obj/machinery/mining/deep_drill` (`code/modules/mining/drilling/deep_drill.dm`):
drills the tile beneath over time (heat + drill head wear), ejects ore items; needs cave
worldgen underneath to be useful. Examine-driven (no window).

- **Adapt**: port the machine as `OxydDeepDrill` — insert drill head, runs on tile,
  emits ore stacks on a timer; no window (examine/status overlay only). Skip the
  cave-generator (`cave_generator.dm`) — map content.

## 7. Guild vending machines

Verified TGUI fix live (`vending_powermat_tgui.png`): "Asters Guild Power-Mat" renders
products w/ sprite thumbs + prices (Robustcell 1000L ×10 @500 …).

- `/obj/machinery/vending/powermat` — power cells, contraband super cells (`vending.dm:1542`)
- `/obj/machinery/vending/printomat` — design disks + circuitboards, incl. armament disks
  (`vending.dm:1554`) — disk protos already exist via DigitalData port
- `/obj/machinery/vending/style` — clothing (`vending.dm:1712`)

- **Adapt**: SS14 `VendingMachine` protos `OxydVendingPowermat/Printomat/Style` with the same
  inv + `price:` + contraband lists. TGUI vending ≈ stock SS14 vending UI — no reskin needed.

## 8. Cash / register / EFTPOS

`code/modules/economy/{cash,cash_register,EFTPOS}.dm` — paper credits, storefront register,
card terminal. UIs are legacy `browse()` plain-HTML → **do NOT reskin** (port rule).

- **Adapt**: `OxydCredits` (stack-able cash item, values 1/5/10/20/50/100/500),
  `OxydCashRegister` (locks, drawer, transaction log — stock SS14 UI),
  `OxydEftpos` (charge terminal — stock UI).
- **Skip**: the full bank/ATM/Accounts_DB web of personal accounts — guild account covered
  by §2 Adapt item.

## 9. Supply crates & delivery

Eris trading delivers via the receiving beacon (§4), not a shuttle. `Order Requests` and
`trade_orders` console = crew-side ordering.

- **Port**: crate entity `OxydTradeCrate` spawning ordered goods; crew order console
  `OxydTradeRequestConsole` (ErisNanoStyle, request queue → merchant console).
- **Skip**: disposal/delivery-area map plumbing; supply shuttle.

## 10. Guild gear & perks

- Design disks (armament etc.) — **already ported** (DigitalData + OxydLathe whitelist);
  Print-o-Mat sells them (§7).
- Merchant perk/deal buffs in Eris are round-light; **Skip** perks (marginal gameplay,
  high complexity).
- Guild clothing/armor sold by Style-o-matic — **Adapt**: include as vend inventory only.

## Explicitly skipped per mission rules
Antag content, map layout/turfs, silicon, cosmetic tiles, disposal network, bank accounts,
supply shuttle, cave generation, crew PDAs' email system.

## Port layout

```
Content.Shared/_Oxyd/AstersGuild/   — components, protos, BUI messages, trade data
Content.Server/_Oxyd/AstersGuild/   — OxydTradeSystem, OxydOreProcessorSystem, beacon/drill systems
Content.Client/_Oxyd/AstersGuild/   — OxydTradeWindow/.xaml(+cs), OxydOreProcessor window, ErisNanoStyle reuse
Resources/Prototypes/_Oxyd/AstersGuild/ — access, jobs, machines, items, stacks, vending, trade
Resources/Locale/en-US/_Oxyd/astersguild.ftl — ent-<id>/desc + oxyd-access-*
```

Build checks: `dotnet build Content.{Server,Client}/... -v:m` + `dotnet run --project Content.YAMLLinter`.
