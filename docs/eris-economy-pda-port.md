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
- Artist bench — `OxydArtistBench` spends `SanityComponent.Insight`, spawns Guild-tagged `StaticPrice` art
- Guild trade bridge — `EntitySoldEvent` syncs CargoAccount balances into Oxyd department ledgers
- NTOS stubs — records / bounty / trade_orders catalog cartridges on Club/Artist/Guild PDAs
- `economy:place|givecash|link|setup club-pos|accountsdb|list|suspend|sync`

## Still deferred

- Full Eris trade_orders buy/sell lists (P7 deep)
- Optional modular PDA hardware (PD6)
- Material-consuming autolathe artist_bench parity (insight-only for now)

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
entities with MobState named ".*Surname.*" | economy:status
pda:spritecheck
pda:programs
```
