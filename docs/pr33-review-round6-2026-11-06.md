# PR #33 round 6: MLGTASTICa partial review (2026-10-10) — dispositions

- **PR:** https://github.com/AEV-Oxyd/Oxyd14/pull/33
- **Review:** `pullrequestreview-5792795164` ("Partial review, adress issues please.") — 27 inline comments.
- **Earlier rounds** (`docs/`): `pr33-code-review-2026-10-10.md`, `pr33-review-followup-2026-10-11.md`, `pr33-review-round3-2026-10-11.md`, `pr33-review-round4-2026-10-11.md`, `pr33-review-round5-2026-10-11.md`.
- **Reference implementation:** CEV-Eris at `/Users/russellrozario/Desktop/SS13-14/CEV-Eris` (BYOND/DM source) for the gameplay ports.

**Summary:** all 27 comments are addressed in code. The recurring themes — data on components/prototypes, stock `DeviceLinkSystem` over hand-rolled links, active-marker components over broad tick queries, declaration ordering, and moving antag/xenobio content out of the research system — are all applied. Gameplay items were ported against the Eris DM source rather than approximated where the fork's systems allowed it.

---

## 1. Hygiene (empty functions, ordering, unused vars, per-tick scans)

| Comment | Disposition |
|---|---|
| `OxydMedicalMachineSystems.cs:163` empty `Initialize()` | Deleted. Same defect fixed in `OxydOrganDecaySystem` and `OxydOperatingComputerSystem` — an override without `base.Initialize()` breaks `[MustCallBase]` init. |
| `OxydResearchSystem.Devices.cs:33` empty `InitializeDevices()` | Method and its call site deleted. |
| `OxydResearchSystem.Devices.cs:181` mid-file `[Dependency]` + declaration order | `OxydResearchSystem.Devices.cs` and `OxydResearchSystem.cs` reordered to the maintainer's 6-section order: dependencies → prototype constants → other constants → runtime/tick vars → Update/Initialize overrides → everything else. Saved as a project rule at `.devin/rules/code-style-pass.md`. |
| `OxydResearchSystem.Devices.cs:386` unused `tree` | Now `foreach (var (_, lvl) ...)`. |
| `OxydResearchSystem.Devices.cs:111` per-tick query over all analyzers | New `OxydActiveAnalyzerComponent` marker: `EnsureComp` when deconstruction starts, `RemComp` in `ClearPending` (covers both abort and finish paths). `Update` queries only analyzers carrying the marker. The dead `TerminatingOrDeleted` check on query results was dropped (queries never return terminating entities). |

## 2. Data on components / prototypes

| Comment | Disposition |
|---|---|
| `OxydWoundSystem.cs:45` hardcoded `ExternalCategories` | `External` flag added to `OrganCategoryPrototype`; the 10 external categories are flagged in `organ_categories.yml`. `IsExternal` is now an instance prototype lookup (replay-safe). All call sites updated (`OxydAutodocSystem`, `OxydForensicsSystem`, operating computer, surgery). |
| `OxydResearchSystem.cs:39` `TreePointValues` dict | `PointValue` DataField on `OxydTechTreePrototype` (`research_trees.yml`); system dict deleted. |
| `OxydResearchSystem.cs:39` console selection state | `SelectedTree`/`SelectedDevice` moved onto `OxydRdConsoleComponent` (was system-level dictionaries that leaked on console deletion). |
| `OxydMedicalMachineSystems.cs:400` IV drip scan | New `OxydActiveIvDripComponent` (networked) holds `AttachedTo`/`AttachedBy`; `Update` queries only active drips. The integration test was updated for the new component. |
| `ClientOxydEconomySystem.cs:23` balance visible to all | `[AutoNetworkedField]` removed from `OxydPersonalAccountComponent.Balance` (same treatment `DepartmentPin` already had). Balance still reaches the owner via ATM/AccountsDB BUI states; the client-side character-panel label that leaked it was removed. |

## 3. DeviceLinking migration

All four comments (`OxydResearchSystem.cs:46/90`, `OxydResearchSystem.Devices.cs:503`, `OxydOperatingComputerSystem.cs:72`) now run on stock `Content.Shared.DeviceLinking`:

- New port prototypes: `OxydRdConsole` (source) / `OxydRdDevice` (sink) for research; `OxydOpComputer` (source) / `OxydOpTable` (sink) for the operating computer, with FTL names in `machine-linking/*_ports.ftl`.
- R&D console YAML carries `DeviceLinkSourceComponent` (range 6); analyzers, protolathes, circuit imprinters and mechfabs carry `DeviceLinkSinkComponent` with the `OxydRdDevice` port.
- `RescanDevices` calls `DeviceLinkSystem.SaveLinks`; `GetLinkedDevices` reads `GetLinkedSinks`; `FindConsolesLinkedTo` resolves through the source's linked sinks (the sink's `LinkedSources` is `[Access]`-restricted to `DeviceLinkSystem`, so the reverse lookup goes through the public API). Deleted/moved entities are cleaned up by the stock system.
- `OxydRdConsoleComponent.LinkedDevices` was removed entirely — no compat shim needed since nothing serialized it.
- Operating computer ↔ table links are created by `SaveLinks` on table/computer `MapInit` and on `StrappedEvent`; `FindPatient` reads `GetLinkedSinks` and prunes links that drifted out of range via `RemoveSinkFromSource`.
- The `LinkRange` constant is gone — range lives on `DeviceLinkSourceComponent.Range` in YAML.

## 4. Gameplay ports (Eris DM-verified)

| Comment | Disposition |
|---|---|
| `OxydNsaSystem.cs:60` missing vomiting/stun/sleep/heart damage | `ApplyOverloadEffects` fires on overload onset: `_vomit.Vomit(force: true)`, `TryAddStunDuration`, `StatusEffectForcedSleeping`, and `AddOrganDamage` on the heart organ — all tunable on `OxydNsaComponent` (`OverloadStunSeconds`, `OverloadSleepSeconds`, `OverloadHeartDamage`, `OverloadToxinPerSecond`). |
| `OxydStasisBagSystem.cs:106` bag deleted on use | `Used` flag on the component: one-use bags stay as an opened empty bag, re-sealing is blocked, drag/drop guards respect `Used`. |
| `OxydSleeperSystem.cs:190` injectability + chem storage | Dead occupants are refused (`MobState` check), `TargetBeforeInjectEvent` is raised so clothing/implants can veto (popup shows the veto reason), and each injection drains `dose * ChemPowerCost` from the new battery/`ApcPowerReceiverBattery` pair — unpowered injections are refused. Eris `Sleeper.dm` synthesizes chems for `use_power(amount * CHEM_SYNTH_ENERGY)` rather than storing them, so power-cost-per-dose is the faithful port of "not out of thin air". |
| `OxydWoundSystem.cs:113` random organ attribution | **Limb targeting ported.** New `OxydBodyTargetingComponent` (networked `TargetedZone` = external organ category) is ensured on every mob at MapInit; self-verbs (`Target: Head/Torso/Arm…`) switch the zone — the SS14 equivalent of Eris's `zone_sel` HUD. `OnBodyDamaged` resolves `DamageDealtEvent.Origin`'s targeted zone to an organ in the victim's body; untargeted attackers keep the random fallback. Eris's pixel→zone sprite map has no SS14 analog; the verb menu is the selection UI. |
| `OxydResuscitateEntityEffectSystem.cs:49` true death | **Organ-death model implemented** in new `OxydOrganDeathSystem`: while blood stops circulating oxygen (blood level < 50% of reference, or heart organ destroyed), every organ accrues anoxic damage each tick; a destroyed brain organ → `MobState.Dead` + `OxydTrueDeathComponent`, which blocks resuscitation. Freezing (cryo pod / stasis / <0°C up the container chain) pauses accrual — Eris's "freezing slows organ death". Oxygenated blood prevents the accrual entirely — Eris's "oxygenation from chems" maps to blood-volume restoration (transfusion/blood chems). |
| `OxydResuscitateEntityEffectSystem.cs:65` heart-stop question | Verified: nothing declared death on heart stop — `MobThresholdSystem` damage thresholds were the only death path. Under the new model heart damage feeds `IsOxygenated` (destroyed heart = blood not pumping = anoxia), while brain death is the true-death trigger. |
| `OxydOrganDecaySystem.cs:96` organ temperature | `IsFrozen` check in the decay tick: cryo-pod/stasis/below-0°C organs skip metabolic decay and take cold damage instead (`FreezeDamage`/`FreezeDamageChance` consts). |
| `OxydOperatingComputerSystem.cs:72` radius table search | See §3 — device links replace the range scan. |
| `OxydSurgerySystem.cs:421` flat speed | Eris scaling ported: duration/success/agy scale with the surgeon's `Bio` skill (`SurgerySkill`, `SkillExpertLevel`), patient `PainComponent.CurrentPain`, and tool `Quality` (held tool, best-held fallback for non-tool items). Tool validation at start and completion retained; one-operation-per-organ and malpractice behavior unchanged. |
| `OxydMedicalMachineSystems.cs:437` IV edge cases | `OxydActiveIvDripComponent` drives the tick; `TargetBeforeInjectEvent` is re-raised every tick and a veto force-detaches with the `oxyd-medical-iv-forced-out` popup; range is measured against the patient's buckle/mount entity so strapped patients on wheeled beds keep the line while the mount stays in range; deleted targets and invalid beaker/bloodstream solutions detach safely without losing solution. |

## 5. File reorganization

| Comment | Disposition |
|---|---|
| `MoebiusScienceDebugCommand.cs:25` generic names | Verified safe (toolshed subcommands are namespaced), and renamed anyway for clarity: `MoebiusResearchDebugCommand` → `moebiusresearch:*`. `docs/moebius-research-port-gaps.md` and code comments updated. |
| `OxydResearchSystem.Devices.cs:409` mental imprinter | Moved to `Content.Server/_Oxyd/Skill/OxydMentalImprinterSystem.cs`; component moved to `_Oxyd/Skills/SkillComponents.cs`. **Eris skill-selection ported**: verb menu lists all `SkillPrototype`s (the `input()` picker equivalent); selecting one grants +5, or +10 when the base stat ≤ `LowSkillThreshold` (Eris `STAT_LEVEL_BASIC` = 15) at half the sanity toll (30/15 — Eris values), then consumes the device. Gates preserved: `MobSkillComponent`, alive `MobState`, empty `eyes` inventory slot. |
| `OxydResearchSystem.Devices.cs:427` drone content | Moved to new `Content.Server/_Oxyd/Silicons/OxydDroneSystem.cs` + `Content.Shared/_Oxyd/Silicons/OxydSiliconComponents.cs` (fabricator + shell components). |
| `OxydResearchSystem.Devices.cs:449` slime extractor | Moved to new `Content.Server/_Oxyd/Xenobiology/OxydSlimeExtractorSystem.cs` + `Content.Shared/_Oxyd/Xenobiology/OxydXenobioComponents.cs`. Kept generic over `ToolRefinableComponent` (any refinable specimen yields a core) — noted as an intentional adaptation, not slime-gated. |

## 6. Verification

- `Content.Server` DebugOpt: **0 errors** (267 warnings — all pre-existing obsolete-API/NuGet noise).
- `Content.IntegrationTests` DebugOpt: **0 errors**.
- `dotnet test --filter "FullyQualifiedName~_Oxyd"`: **320 passed, 4 skipped, 0 failed** (324 total).
- `dotnet test --filter "FullyQualifiedName~MoebiusMedicalTest|FullyQualifiedName~ResearchTest|FullyQualifiedName~LitanyEffectsSkillTest"`: **10 passed, 1 skipped, 0 failed**.
- Pre-existing fix included: `NeoTheologyFeatureGapTest.DeactivationRevokesTheUplinkAndBanksItsBalance` asserted `IsQueuedForDeletion(store)`, but `NtUplinkSystem.BankBalance` now hosts the store on the cruciform and strips `StoreComponent` instead of deleting the implant — the assertion was updated to match (unrelated to this review round; the test was stale on the branch).

## 7. Known adaptations / follow-ups

- Eris `zone_sel` is a clickable sprite overlay; SS14 has no per-pixel mob hitboxes, so targeting is a verb menu on self. Same selection semantics, different presentation.
- Eris oxygenation is per-reagent blood chemistry; SS14 blood has no O₂ reagent, so "oxygenated" = blood level ≥ 50% of reference AND heart organ viable. Chems that restore blood (transfusion, blood bags) restore oxygenation; direct organ-targeted chem application remains future scope.
- Anoxia accrues to all organs at `AnoxiaDamage`/2s; brain at `OrganMaxDamage` or `Decayed` = true death. Mobs with no brain organ are unaffected (brain-removal lethality is unchanged from before).
- Operating-computer auto-link timing (MapInit/Strap events) is covered by code review but not yet by a dedicated integration test — flagged for the next round if the maintainer wants one.
