# Eris Economy + PDA Port (execution slice)

Implements the Eris economy + PDA plan vertical slices on Oxyd14:

- **P0/P1:** `OxydEconomySystem` personal accounts, PIN, transfers, transaction logs
- **P2:** `oxydJobEconomy` prototypes + spawn seeding (Club WAGE_NONE, Vagabond no-account)
- **P3:** Payday ticker + `economy:payday force`
- **PD1:** Eris `modular_pda.dmi` → `Resources/Textures/_Oxyd/Devices/eris_pda.rsi` + job PDA protos
- **PD3/PD4:** TaxQuickly 2565 cartridge UI + Club drink/cook catalog cartridges
- **Tools:** `economy:*` and `pda:*` Toolshed debug commands

## Also in this PR

- ATM BUI (login/withdraw/deposit/transfer) — `OxydAtm`
- Cash register / EFTPOS / tip jar / ewallet — Credit + linked Service account
- Paid Club vendor (`OxydVendingClubSnacks`) via `VendingMachineEjectAttemptEvent` + `OxydVendorEarnings`
- Accounts DB HoP terminal — `OxydAccountsDb` / `OxydAccountsDbOpen` (list/create/suspend/adjust/revoke)
- Artist bench — materials + oddity inspiration + ClubArtist perk (Eris parity)
- Guild trade bridge — `EntitySoldEvent` syncs CargoAccount balances into Oxyd department ledgers
- Live NTOS — records / bounty / email / NTNRC chat (+ trade_orders); signaller & scanner programs
- `economy:place|givecash|link|setup club-pos|accountsdb|artist|list|suspend|sync`

## Also — full trade stack (P7)

- `OxydTradeStation` prototypes (Vermouth, AstersCapital, Caduceus)
- Cart → order queue → receiving beacon fulfill (Guild free fee; others +10%)
- Sending beacon offer export (80/20 slave/Guild cut)
- Real `OxydTradeOrdersCartridge` UI (stations/goods/cart/orders/offers/logs)
- Toolshed: `economy:setup trade`, `economy:trade stations|discover|orders|restock|buy`, `place send|recv`

## Also — modular PDA hardware (PD6)

- `OxydModularPda` bay: CPU / HDD / Network / Peripheral ItemSlots
- Parts: micro→adv CPUs, micro→advanced HDDs, basic/advanced net, tesla-link, mini printer
- Peripherals: signaller, medical/atmos/price/reagent scanners
- HDD sets `CartridgeLoader.DiskSpace`; CPU adds disk bonus + max program threads; net sets wireless range
- Eris PDAs ship with starter kits (Club printer, Guild long-range + tesla, Moebius medical scanner)
- Toolshed: `pda:hw`, `pda:install …`, `pda:give cpu|hdd|net|tesla|printer|signaller|medscan|atmos|price|reagent`
- Alt-verb: Hardware report (prints paper)

## Still deferred

- Overmap trade station discovery tree / hidden inventory thresholds
- Map placement polish for ATM / Accounts DB / beacons / artist bench

## QA

```
economy:create Alice 1000
economy:transfer <from> <to> 100
economy:dept Service
economy:dept Guild
economy:payday force
economy:setup club-pos
economy:setup accountsdb
economy:givecash 500
economy:place atm
economy:place accountsdb
economy:place bench
economy:list
economy:suspend <acct> true
economy:sync
economy:setup trade
economy:trade stations
economy:trade discover Caduceus
economy:trade buy Vermouth:DrinkGlass
economy:place send
economy:place recv
entities with MobState named ".*Surname.*" | economy:status
pda:spritecheck
pda:programs
pda:hw
pda:give hdd
pda:install hdd OxydHwHddAdvanced
```
