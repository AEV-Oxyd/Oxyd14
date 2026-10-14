# Eris Economy + PDA Port (execution slice)

Implements the first vertical slice of the full Eris economy + PDA plan:

- **P0/P1:** `OxydEconomySystem` personal accounts, PIN, transfers, transaction logs
- **P2:** `oxydJobEconomy` prototypes + spawn seeding (Club WAGE_NONE, Vagabond no-account)
- **P3:** Payday ticker + `economy:payday force`
- **PD1:** Eris `modular_pda.dmi` → `Resources/Textures/_Oxyd/Devices/eris_pda.rsi` + job PDA protos
- **PD3/PD4:** TaxQuickly 2565 cartridge UI + Club drink/cook catalog cartridges
- **Tools:** `economy:*` and `pda:*` Toolshed debug commands

## Not yet in this PR

- ATM / EFTPOS / cash register machines (P4)
- Accounts DB HoP terminal
- Vendor earnings wiring (P6)
- Full trade stack unify (P7)
- Artist bench monetization (P8)
- Remaining NTOS cartridges (email/chat/records/bounty/trade_orders)
- Optional modular hardware (PD6)

## QA

```
economy:create Alice 1000
economy:transfer <from> <to> 100
economy:dept Service
economy:payday force
entities with MobState named ".*Surname.*" | economy:status
pda:spritecheck
pda:programs
```
