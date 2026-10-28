# Eris Moebius medical port. Eris source: CEV-Eris (discordia-space/CEV-Eris).
# Access names live in _Oxyd/moebius/jobs.ftl.

## Surgical tools
oxyd-item-saw-circular-desc-line = For harder materials.

## Medical stacks
oxyd-stack-brutepack = bruise pack
oxyd-stack-ointment = ointment
oxyd-stack-traumakit = trauma kit
oxyd-stack-burnkit = burn kit
oxyd-stack-splint = splint
oxyd-stack-nanopaste = nanopaste


## Injectors

## Misc items

## Machines
oxyd-proxy-surgery = surgery interface
oxyd-proxy-scanner = scanner interface

## Surgery UI
oxyd-surgery-window-title = Moebius Surgery
oxyd-surgery-patient = Patient: {$patient}
oxyd-surgery-select-organ = Select an organ
oxyd-surgery-held-tools = Using: {$tool}
oxyd-surgery-no-tool = bare hands
oxyd-surgery-not-operable = You can't operate on {$patient} standing up - they need to be lying on an operating surface.
oxyd-surgery-flag-external = [external]
oxyd-surgery-flag-internal = [internal]
oxyd-surgery-flag-robotic = [robotic]
oxyd-surgery-step-diagnosewound = Diagnose wounds
oxyd-surgery-step-cutopen = Cut open
oxyd-surgery-step-retractskin = Retract skin
oxyd-surgery-step-fixbleeding = Clamp bleeders
oxyd-surgery-step-cauterize = Cauterise
oxyd-surgery-step-mendbone = Mend bone
oxyd-surgery-step-breakbone = Break bone
oxyd-surgery-step-fixbone = Set bone
oxyd-surgery-step-removeembedded = Remove embedded object
oxyd-surgery-step-insertitem = Insert item
oxyd-surgery-step-removeitem = Remove item
oxyd-surgery-step-attachorgan = Attach organ
oxyd-surgery-step-detachorgan = Detach organ
oxyd-surgery-step-amputate = Amputate
oxyd-surgery-step-roboopen = Unscrew panel
oxyd-surgery-step-robofixbrute = Repair brute damage
oxyd-surgery-step-robofixburn = Repair burn damage
oxyd-surgery-step-roboclose = Screw panel shut
oxyd-surgery-step-extractshrapnel = Extract shrapnel
oxyd-surgery-step-closewounds = Close surface wounds
oxyd-surgery-step-fixorgan = Treat organ damage
oxyd-surgery-mods-wounds = Mods/Wounds
oxyd-surgery-diagnostics = Diagnostics
oxyd-surgery-volume = Volume
oxyd-surgery-conditions = Conditions
oxyd-surgery-diagnose = Diagnose
oxyd-surgery-wounds = Wounds
oxyd-surgery-health = Health
oxyd-surgery-efficiency = Efficiency: {$pct}%
oxyd-surgery-occupied = Occupied space: {$cur} / {$max}
oxyd-surgery-occupied-label = Occupied space:
oxyd-surgery-remove-shrapnel = Remove shrapnel
oxyd-surgery-undiagnosed = Undiagnosed wounds - probe or scan
oxyd-surgery-cond-bleeding = Bleeding
oxyd-surgery-cond-fracture = Fracture
oxyd-surgery-cond-embedded = Embedded
oxyd-surgery-cond-damage = Organ damage
oxyd-surgery-cond-robo = Mechanical damage

# Eris organ card + internal view chrome
oxyd-surgery-wounds-count = Wounds:
oxyd-surgery-oxygen = Oxygen:
oxyd-surgery-blood = Blood:
oxyd-surgery-brute = Brute:
oxyd-surgery-burn = Burn:
oxyd-surgery-efficiency-label = Efficiency:
oxyd-surgery-organ-types = Organ types (efficiency):
oxyd-surgery-amputate = Amputate
oxyd-surgery-disconnect = Disconnect
oxyd-surgery-insert = Insert
oxyd-surgery-internal-return = Return to External Diagnostics
oxyd-surgery-internal-diagnostics = Diagnostics - {$organ}
oxyd-surgery-examine = Examine
oxyd-surgery-modifications = Modifications
oxyd-surgery-attach = Attach
oxyd-surgery-remove = Remove
oxyd-surgery-no-mods = no implants
oxyd-surgery-treat = Treat
oxyd-surgery-type = Type:
oxyd-surgery-severity = Severity:
oxyd-surgery-treatments = Treatments:
oxyd-surgery-no-wounds = No wounds detected.
oxyd-surgery-running = In progress: {$step}
oxyd-surgery-status-fractured = [fractured]
oxyd-surgery-status-splinted = [splinted]
oxyd-surgery-status-clamped = [clamped]
oxyd-surgery-status-open = [open]
oxyd-surgery-status-retracted = [retracted]

# Synthesised wound cards (Eris wounddatums)
oxyd-surgery-wound-incision = Open surgical incision
oxyd-surgery-wound-bleeding = Bleeding surface wound
oxyd-surgery-wound-fracture = Bone fracture
oxyd-surgery-wound-embedded = Embedded objects
oxyd-surgery-wound-brute = Internal bruising
oxyd-surgery-wound-burn = Burn tissue damage
oxyd-surgery-wound-mech = Mechanical damage
oxyd-surgery-wound-short = Burned circuits
oxyd-surgery-treat-incision = clamp bleeders (hemostat) or cauterise
oxyd-surgery-treat-cauterise = cauterise (cautery)
oxyd-surgery-treat-fracture = bone setter or bone gel
oxyd-surgery-treat-embedded = surgical extraction (hemostat)
oxyd-surgery-treat-trauma = advanced trauma kit
oxyd-surgery-treat-burn = advanced burn kit
oxyd-surgery-treat-robo = welder
oxyd-surgery-treat-coil = cable coil

oxyd-medical-surgery-start = You begin the procedure.
oxyd-medical-surgery-success = You complete the procedure.
oxyd-medical-surgery-fail = The procedure fails!
oxyd-medical-surgery-malpractice = Your hand slips, mangling {THE($organ)}!
oxyd-medical-diagnose-healthy = The organ appears healthy.
oxyd-medical-diagnose-damage = The organ shows {$amount} damage.
oxyd-medical-diagnose-fracture = Bones are fractured!
oxyd-medical-diagnose-bleeding = The incision is bleeding.
oxyd-medical-diagnose-embedded = Embedded objects detected: {$count}.
oxyd-medical-fracture = You feel a bone fracture in {THE($organ)}!
oxyd-medical-bone-mended = Your bone knits back together.
oxyd-medical-nsa-overload = Your nerves burn - neural system accumulation overload!

## Sleeper UI
oxyd-sleeper-window-title = Moebius Sleeper
oxyd-sleeper-occupant = Occupant: {$name}
oxyd-sleeper-empty = Empty
oxyd-sleeper-health = Health: {$health}
oxyd-sleeper-beaker = Beaker: {$vol} / {$max}
oxyd-sleeper-no-beaker = no beaker
oxyd-sleeper-inject-chem = Inject {$name} ({$dose}u, {$current}u in patient)
oxyd-sleeper-eject = Eject patient
oxyd-sleeper-eject-beaker = Eject beaker
oxyd-sleeper-chem-inaprovaline = Inaprovaline
oxyd-sleeper-chem-tramadol = Tramadol
oxyd-sleeper-chem-alkysine = Alkysine
oxyd-sleeper-chem-antitoxin = Anti-toxin
oxyd-sleeper-chem-dexalin = Dexalin
oxyd-medical-sleeper-verb-insert = Get inside
oxyd-medical-sleeper-verb-eject = Eject occupant
oxyd-medical-sleeper-verb-insert-beaker = Insert beaker
oxyd-medical-sleeper-chem-max = Patient is saturated with {$chem}.
oxyd-medical-sleeper-chem-saturated = Cannot inject: bloodstream saturated.

## Autodoc UI
oxyd-autodoc-window-title = Autodoc Surgeon
oxyd-autodoc-occupant = Occupant: {$name}
oxyd-autodoc-empty = Empty
oxyd-autodoc-idle = Idle
oxyd-autodoc-running = Running: {$step}
oxyd-autodoc-start = Start procedure
oxyd-autodoc-clear = Clear queue
oxyd-autodoc-eject = Eject occupant
oxyd-medical-autodoc-skip = Autodoc skips {$step} - no suitable organ.
oxyd-medical-autodoc-step-diagnosewound = Diagnose wounds
oxyd-medical-autodoc-step-cutopen = Cut open
oxyd-medical-autodoc-step-retractskin = Retract skin
oxyd-medical-autodoc-step-fixbleeding = Clamp bleeders
oxyd-medical-autodoc-step-cauterize = Cauterise
oxyd-medical-autodoc-step-mendbone = Mend bone
oxyd-medical-autodoc-step-breakbone = Break bone
oxyd-medical-autodoc-step-fixbone = Set bone
oxyd-medical-autodoc-step-removeembedded = Remove embedded object
oxyd-medical-autodoc-step-insertitem = Insert item
oxyd-medical-autodoc-step-removeitem = Remove item
oxyd-medical-autodoc-step-attachorgan = Attach organ
oxyd-medical-autodoc-step-detachorgan = Detach organ
oxyd-medical-autodoc-step-amputate = Amputate
oxyd-medical-autodoc-step-roboopen = Unscrew panel
oxyd-medical-autodoc-step-robofixbrute = Repair brute damage
oxyd-medical-autodoc-step-robofixburn = Repair burn damage
oxyd-medical-autodoc-step-roboclose = Screw panel shut

## Scanner UI
oxyd-scanner-window-title = Moebius Health Scanner
oxyd-scanner-patient = Patient: {$name}
oxyd-scanner-vitals = Vitals - Health {$health} | Brute {$brute} | Burn {$burn} | Toxin {$toxin} | Oxy {$oxy}
oxyd-scanner-extras = Pain {$pain} | NSA {$nsa} | Blood {$blood}/{$bloodmax}
oxyd-scanner-none = none detected

## Reagent guidebook
oxyd-medical-effect-nsa = Increases neural system accumulation by { $value }.
oxyd-medical-effect-nsa-tolerance = Raises the NSA threshold by { $value }.
oxyd-medical-effect-stim = Boosts { $skill } by { $amount } while metabolised.
oxyd-medical-effect-chem-sanity = Applies { $amount } sanity while metabolised.
oxyd-medical-effect-mend-bone = Mends a random fractured organ.
oxyd-medical-effect-heal-organ = Repairs damage to the most wounded organs.
oxyd-medical-effect-reduce-addiction = Advances addiction recovery.
oxyd-medical-effect-suppress-withdrawal = Suppresses withdrawal cravings.
oxyd-medical-effect-seal-wounds = Stops active bleeding.
oxyd-medical-scan-normal = normal
oxyd-medical-scan-robotic = robotic
oxyd-medical-scan-incision = incision
oxyd-medical-scan-bleeding = bleeding
oxyd-medical-scan-fractured = fractured
oxyd-medical-scan-embedded = embedded objects ({$count})

## Chem processor UI
oxyd-processor-window-title = Chemical Processor
oxyd-processor-mode-centrifuge = Centrifuge
oxyd-processor-mode-electrolyzer = Electrolyzer
oxyd-processor-no-beaker = Insert a beaker into the main slot.
oxyd-processor-beaker-status = Beaker {$n}: {$vol}u
oxyd-processor-beaker-empty = Beaker {$n}: empty
oxyd-processor-beaker-n = separation beaker {$n}
oxyd-processor-leave = leave in main beaker
oxyd-processor-start = Start
oxyd-processor-eject = Eject
oxyd-processor-eject-main = Eject main beaker
oxyd-medical-processor-insert-beaker = Insert beaker
oxyd-medical-processor-need-sep-beaker = The electrolyzer needs a separation beaker!

## IV drip
oxyd-medical-iv-verb-insert-beaker = Attach beaker
oxyd-medical-iv-verb-eject-beaker = Remove beaker
oxyd-medical-iv-verb-inject = Transfuse into patient
oxyd-medical-iv-verb-draw = Draw blood from patient
oxyd-medical-iv-verb-detach = Detach from patient
oxyd-medical-iv-attaching = You start attaching the drip to {THE($patient)}…
oxyd-medical-iv-attached = You attach the drip to {THE($patient)}.
oxyd-medical-iv-detached = You detach the drip.

## Stasis bag
oxyd-medical-stasis-seal = Seal inside
oxyd-medical-stasis-sealed = You seal {$patient} inside the stasis bag.
oxyd-medical-stasis-open = Open bag
oxyd-medical-stasis-discarded = The stasis bag is spent and discarded.

## Entity auto-localization (ent-<id>)
ent-OxydMedicalSleeper = sleeper
    .desc = A stasis pod that injects stabilising chemicals into its occupant.
ent-OxydMedicalAutodoc = autodoc
    .desc = An automated surgery pod that performs queued procedures on its occupant.
ent-OxydMedicalCentrifuge = centrifuge
    .desc = Separates a beaker's reagents across separation beakers.
ent-OxydMedicalElectrolyzer = electrolyzer
    .desc = Decomposes a reagent back into its constituent reactants.
ent-OxydMedicalIvDrip = IV drip
    .desc = A medical stand that transfuses fluids into or out of an attached patient.
ent-OxydMedicalOperatingTable = operating table
    .desc = A sterile table for performing surgery.
ent-OxydMedicalMorgueTray = morgue tray
    .desc = A refrigerated tray for long-term storage of cadavers.
ent-OxydMedicalScannerPod = body scanner
    .desc = A diagnostic pod for deep tissue scans.
ent-OxydMedicalSurgeryUiProxy = surgery interface
ent-OxydMedicalScannerUiProxy = scanner interface
ent-OxydSurgicalScalpel = scalpel
    .desc = A standard surgical scalpel. Sharp enough to cut through flesh.
ent-OxydSurgicalScalpelAdvanced = advanced scalpel
    .desc = A finer blade that completes incisions faster.
ent-OxydSurgicalScalpelLaser = laser scalpel
    .desc = A laser scalpel that cauterises as it cuts - doubles as a cautery.
ent-OxydSurgicalHemostat = hemostat
    .desc = Clamps bleeders shut and extracts embedded objects.
ent-OxydSurgicalRetractor = retractor
    .desc = Holds incisions open so you can work inside.
ent-OxydSurgicalCautery = cautery
    .desc = Seals wounds and attaches organs by burning tissue shut.
ent-OxydSurgicalBoneSetter = bone setter
    .desc = Sets fractured and dislocated bones back in place.
ent-OxydSurgicalSaw = surgical saw
    .desc = A large saw for amputations. Slowly.
ent-OxydSurgicalSawCircular = circular saw
    .desc = A powered surgical saw. Amputations go much faster.
ent-OxydSurgicalDrill = surgical drill
    .desc = Drills through bone for precision work.
ent-OxydSurgicalTray = surgical tray
    .desc = A sterile tray for holding organs and instruments.
ent-OxydErisHealthScanner = moebius health scanner
    .desc = A handheld scanner that reads vitals and organ status.
ent-OxydErisStasisBag = stasis bag
    .desc = A folded, one-use bag that puts a patient into stasis, halting deterioration.
ent-OxydErisBodyBag = body bag
    .desc = A plastic bag designed for the storage and transportation of cadavers.
ent-OxydErisMedkit = first aid kit
    .desc = An emergency medical kit for those serious boo-boos.
ent-OxydErisMedkitAdvanced = advanced first aid kit
    .desc = An advanced kit, for when a boo-boo becomes a catastrophe.
ent-OxydErisSurgeryKit = surgical kit
    .desc = A sterile case containing a full set of surgical tools.
ent-OxydErisOrganFreezer = organ freezer
    .desc = A refrigerated container for preserving harvested organs.
ent-OxydErisBruisePack = bruise pack
    .desc = A pack of trauma gel patches for bruises and cuts.
ent-OxydErisOintment = ointment
    .desc = A soothing burn ointment applicator.
ent-OxydErisTraumaKit = advanced trauma kit
    .desc = An advanced trauma kit for serious blunt and sharp trauma.
ent-OxydErisBurnKit = advanced burn kit
    .desc = An advanced burn kit for serious thermal injuries.
ent-OxydErisSplint = medical splint
    .desc = A rigid splint that stabilises broken bones while they knit.
ent-OxydErisNanopaste = nanopaste
    .desc = A tube of nanite paste that repairs synthetic and prosthetic parts.
ent-OxydErisAutoinjectorInaprovaline = autoinjector (inaprovaline)
    .desc = A rapid and safe way to administer small amounts of drugs by untrained or trained personnel.
ent-OxydErisAutoinjectorAntitoxin = autoinjector (anti-toxin)
    .desc = A rapid and safe way to administer small amounts of drugs by untrained or trained personnel.
ent-OxydErisAutoinjectorTricordrazine = autoinjector (tricordrazine)
    .desc = A rapid and safe way to administer small amounts of drugs by untrained or trained personnel.
ent-OxydErisAutoinjectorKelotane = autoinjector (kelotane)
    .desc = A rapid and safe way to administer small amounts of drugs by untrained or trained personnel.
ent-OxydErisAutoinjectorBicaridine = autoinjector (bicaridine)
    .desc = A rapid and safe way to administer small amounts of drugs by untrained or trained personnel.
ent-OxydErisAutoinjectorDexalin = autoinjector (dexalin)
    .desc = A rapid and safe way to administer small amounts of drugs by untrained or trained personnel.
ent-OxydErisAutoinjectorSpaceacillin = autoinjector (spaceacillin)
    .desc = A rapid and safe way to administer small amounts of drugs by untrained or trained personnel.
ent-OxydErisAutoinjectorTramadol = autoinjector (tramadol)
    .desc = A rapid and safe way to administer small amounts of drugs by untrained or trained personnel.
ent-OxydErisAutoinjectorOxycodone = autoinjector (oxycodone)
    .desc = A rapid and safe way to administer small amounts of drugs by untrained or trained personnel.
ent-OxydErisAutoinjectorPolystem = autoinjector (polystem)
    .desc = A rapid and safe way to administer small amounts of drugs by untrained or trained personnel.
ent-OxydErisAutoinjectorMeralyne = autoinjector (meralyne)
    .desc = A rapid and safe way to administer small amounts of drugs by untrained or trained personnel.
ent-OxydErisAutoinjectorRyetalyn = autoinjector (ryetalyn)
    .desc = A rapid and safe way to administer small amounts of drugs by untrained or trained personnel.
ent-OxydErisAutoinjectorBloodclot = autoinjector (blood clotting)
    .desc = A rapid and safe way to administer small amounts of drugs by untrained or trained personnel.
ent-OxydErisAutoinjectorHyperzine = autoinjector (hyperzine)
    .desc = A rapid and safe way to administer small amounts of drugs by untrained or trained personnel.
ent-OxydErisHypospray = moebius hypospray
    .desc = The Moebius Medical department hypospray is a sterile, air-needle autoinjector for rapid administration of drugs to patients.
ent-OxydErisNanopasteSingle = nanopaste
ent-OxydErisBruisePackSingle = bruise pack
ent-OxydErisOintmentSingle = ointment
ent-OxydErisTraumaKitSingle = advanced trauma kit
ent-OxydErisBurnKitSingle = advanced burn kit
ent-OxydErisSplintSingle = medical splint
oxyd-surgery-verb = Surgery

## Sleeper dialysis + Eris chem menu
oxyd-sleeper-dialysis = Dialysis
oxyd-sleeper-dialysis-on = Dialysis: ON
oxyd-sleeper-chem-soporific = Soporific
oxyd-sleeper-chem-paracetamol = Paracetamol
oxyd-sleeper-chem-tricordrazine = Tricordrazine

## Autodoc (Eris capitalist model)
oxyd-autodoc-balance = Balance: {$balance} cr
oxyd-autodoc-scan = Scan ({$cost} cr)
oxyd-autodoc-process-all = Process all: {$cost} cr
oxyd-autodoc-process-picked = Process picked: {$cost} cr
oxyd-autodoc-abort = Abort
oxyd-autodoc-eject-credits = Eject credits
oxyd-autodoc-in-progress = Procedure in progress...
oxyd-autodoc-no-scan = Occupant present — run a scan to diagnose.
oxyd-autodoc-overall = Overall status
oxyd-autodoc-brute = Brute
oxyd-autodoc-burn = Burn
oxyd-autodoc-toxin = Toxin
oxyd-autodoc-oxy = Suffocation
oxyd-autodoc-blood-level = Blood level
oxyd-autodoc-organ-damage = External — Brute: {$brute} Burn: {$burn}
oxyd-autodoc-inner-damage = Internal — damage: {$dmg}
oxyd-autodoc-op-cost = {$op} ({$cost} cr)
oxyd-autodoc-op-damage = Repair damage
oxyd-autodoc-op-openwounds = Close open wounds
oxyd-autodoc-op-internalwounds = Internal wounds
oxyd-autodoc-op-fracture = Set fracture
oxyd-autodoc-op-shrapnel = Remove embedded objects
oxyd-autodoc-op-toxin = Toxin chelation
oxyd-autodoc-op-dialysis = Dialysis
oxyd-autodoc-op-blood = Replenish blood
oxyd-medical-autodoc-insufficient = Insufficient credits inserted.
oxyd-medical-autodoc-locked = The pod is locked while a procedure is running.
oxyd-medical-autodoc-done = Procedure complete.
oxyd-medical-autodoc-nothing = Nothing to do.

## MIRC
oxyd-mirc-program-name = MIRC
oxyd-mirc-no-recipe = No known synthesis.

# Eris catalog flow (chemistry_catalog.tmpl)
oxyd-mirc-splash-title = Moebius reagent catalog
oxyd-mirc-splash-welcome = Welcome to Moebius Internal Reagent Database.
oxyd-mirc-splash-browse = Browse catalog
oxyd-mirc-search = Search
oxyd-mirc-entry-type = Type:
oxyd-mirc-entry-phase = Phase:
oxyd-mirc-entry-color = Color:
oxyd-mirc-entry-metabolism = Metabolism:
oxyd-mirc-entry-nsa = NSA:
oxyd-mirc-entry-addiction = Addiction:
oxyd-mirc-entry-overdose = Overdose:
oxyd-mirc-entry-taste = Taste:
oxyd-mirc-entry-used-in = Takes part in reactions
oxyd-mirc-entry-recipes = Synthesis reactions
oxyd-mirc-back = Back
oxyd-mirc-print = Print
oxyd-mirc-col-name = Name
oxyd-mirc-col-phase = Phase
oxyd-mirc-col-type = Type

# Eris get_pulse() classification (sleeper stat row + scanner pulse line).
oxyd-sleeper-pulse = Pulse:
oxyd-pulse-none = No pulse
oxyd-pulse-thready = Thready
oxyd-pulse-irregular = Irregular
oxyd-pulse-norm = Normal
oxyd-scanner-pulse-line = Pulse : {$pulse}

## Eris machine UI restyle
oxyd-sleeper-crit-health = Critical Health:
oxyd-sleeper-organ-health = Organ Health:
oxyd-sleeper-status-label = Status:
oxyd-sleeper-status-alive = Conscious
oxyd-sleeper-status-crit = Critical
oxyd-sleeper-status-dead = Deceased
oxyd-sleeper-bar-brute = Brute
oxyd-sleeper-bar-burn = Burn
oxyd-sleeper-bar-resp = Respiratory
oxyd-sleeper-bar-toxin = Toxin
oxyd-sleeper-dialysis-inactive = Dialysis inactive
oxyd-sleeper-dialysis-active = Dialysis active
oxyd-sleeper-eject-occupant = Eject occupant
oxyd-sleeper-in-patient = Occupant: {$units} units
oxyd-sleeper-inject-units = Inject {$units}
oxyd-sleeper-beaker-free = {$units} units of free space remaining

oxyd-processor-control = Control panel
oxyd-processor-status = Status: {$status}
oxyd-processor-status-idle = Idle
oxyd-processor-status-working = Working
oxyd-processor-start-spin = Start spin cycle
oxyd-processor-sep-rating = Supported up to {$count} separation beakers. Separation rating is 1u per second.
oxyd-processor-sep-beaker = Separation beaker
oxyd-processor-main-beaker = Beaker
oxyd-processor-on = On
oxyd-processor-off = Off

oxyd-cryo-status-title = Cryo Cell Status
oxyd-cryo-cell-temp = Cell Temperature:
oxyd-cryo-unconscious = Unconscious
oxyd-cryo-conscious = Conscious
oxyd-cryo-dead = DEAD
oxyd-cryo-eject-beaker = Eject beaker
oxyd-cryo-no-beaker = No beaker

oxyd-dispenser-energy = Energy
oxyd-dispenser-dispense = Dispense
oxyd-dispenser-beaker-contents = Beaker Contents
oxyd-dispenser-chemicals = Chemicals
oxyd-dispenser-amount = {$units}u
oxyd-dispenser-empty = empty

oxyd-scanner-analyzing = Analyzing Results for {$name}:
oxyd-scanner-overall = Overall Status: {$status}
oxyd-scanner-status-alive = alive
oxyd-scanner-status-crit = critical
oxyd-scanner-status-dead = deceased
oxyd-scanner-damage-specifics = Damage Specifics:
oxyd-scanner-body-temp = Body Temperature: {$celsius}°C ({$fahrenheit}°F)
oxyd-scanner-loc-damage = Localized Damage:
oxyd-scanner-limbs-ok = Limbs are OK.
oxyd-scanner-blood-level = Blood Level {$status}: {$pct}% {$volume}u
oxyd-scanner-blood-normal = Normal
oxyd-scanner-blood-low = Low
oxyd-scanner-blood-crit = Critically Low
oxyd-scanner-pulse = Subject's pulse: {$bpm} bpm
oxyd-scanner-print = Print Report
oxyd-scanner-clear = Clear data
oxyd-scanner-key = Key:

oxyd-medical-iv-verb-amount = Set IV transfer amount ({$amount}u)
oxyd-medical-iv-amount-set = Transfer rate set to {$amount} units.
oxyd-medical-iv-examine-tank = There is a {$tank} attached.
oxyd-medical-iv-examine-no-tank = There is no tank.
oxyd-medical-iv-examine-vessel = It's attached to {$vessel}.
oxyd-medical-iv-examine-no-vessel = There is no vessel.
oxyd-medical-iv-examine-mode-inject = It's set to inject mode, transferring {$amount}u per tick.
oxyd-medical-iv-examine-mode-draw = It's set to draw mode, transferring {$amount}u per tick.

## Organ decay / transplant
oxyd-organ-decayed = The decay has set in.
oxyd-surgery-organ-decayed = This organ is too decayed to transplant.
oxyd-surgery-organ-already-present = The patient already has {$organ}.

## Organ bioprinter
oxyd-bioprinter-window-title = Organ Bioprinter
oxyd-bioprinter-matter = Stored biomass
oxyd-bioprinter-sample-yes = Blood sample loaded.
oxyd-bioprinter-sample-no = No blood sample loaded.
oxyd-bioprinter-working = PRINTING ORGAN...
oxyd-bioprinter-print = Print
oxyd-bioprinter-printed = The bioprinter spits out a new organ.
oxyd-bioprinter-no-matter = There is not enough matter in the printer.
oxyd-bioprinter-biomass = The bioprinter processes the meat. Stored biomass: {$matter}
oxyd-bioprinter-full = The biomass tank is full ({$max}).
oxyd-bioprinter-sample-loaded = You inject the blood sample into the bioprinter.
oxyd-bioprinter-organ-heart = Heart
oxyd-bioprinter-organ-lungs = Lungs
oxyd-bioprinter-organ-kidney = Kidney
oxyd-bioprinter-organ-eyes = Eyes
oxyd-bioprinter-organ-liver = Liver
oxyd-bioprinter-organ-stomach = Stomach

## Resuscitator chem
oxyd-reagent-resuscitator = resuscitator
oxyd-reagent-resuscitator-desc = Incredibly rare cardiac stimulant.
oxyd-resuscitate-twitch = {$name} twitches a bit as their heart restarts!
entity-effect-guidebook-resuscitate =
    { $chance ->
        [1] Ravages the heart, restarting it on the recently deceased
        *[other] ravage the heart, restarting it on the recently deceased
    }.

## Medical side effects (Eris custom_pain tiers)
oxyd-sideeffect-headache-1 = You feel a light pain in your head.
oxyd-sideeffect-headache-2 = You feel a throbbing pain in your head!
oxyd-sideeffect-headache-3 = You feel an excruciating pain in your head!
oxyd-sideeffect-headache-cure = Your head stops throbbing...
oxyd-sideeffect-stomach-1 = You feel a bit light around the stomach.
oxyd-sideeffect-stomach-2 = Your stomach hurts.
oxyd-sideeffect-stomach-3 = You feel sick.
oxyd-sideeffect-stomach-cure = Your stomach feels a little better now...
oxyd-sideeffect-cramps-1 = The muscles in your body hurt a little.
oxyd-sideeffect-cramps-2 = The muscles in your body cramp up painfully.
oxyd-sideeffect-cramps-3 = There's pain all over your body.
oxyd-sideeffect-cramps-emote = flinches as all the muscles in their body cramp up.
oxyd-sideeffect-cramps-cure = The cramps let up...
oxyd-sideeffect-itch-1 = You feel a slight itch.
oxyd-sideeffect-itch-2 = You want to scratch your itch badly.
oxyd-sideeffect-itch-3 = This itch makes it really hard to concentrate.
oxyd-sideeffect-itch-emote = shivers slightly.
oxyd-sideeffect-itch-cure = The itching stops...

## Appendicitis (Eris)
oxyd-appendix-sting = You feel a stinging pain in your abdomen!
oxyd-appendix-wince = You wince painfully.
oxyd-appendix-gag = You gag as you want to throw up, but there's nothing in your stomach!
oxyd-appendix-rupture = Your abdomen is a world of pain!

## Forensics (Eris autopsy scanner + mass spectrometer)
oxyd-autopsy-not-dead = The subject is still alive - autopsy requires a cadaver.
oxyd-autopsy-scan-start = You begin scanning the body for wounds.
oxyd-autopsy-printed = The scanner rattles and prints out a sheet of paper.
oxyd-autopsy-paper-name = Autopsy Data ({$name})
oxyd-autopsy-report-title = AUTOPSY REPORT
oxyd-autopsy-report-subject = Subject: {$name}
oxyd-autopsy-report-tod = Time since death: ~{$time} minutes
oxyd-autopsy-report-damage = Damage by type:
oxyd-autopsy-report-damage-line =   {$type}: {$amount}
oxyd-autopsy-report-damage-none =   No residual damage detected.
oxyd-autopsy-report-wounds = Wound report (per limb):
oxyd-autopsy-report-wound-line =   {$organ}: {$notes}
oxyd-autopsy-report-wound-intact =   {$organ}: intact
oxyd-autopsy-report-chems = Trace chemicals in bloodstream:
oxyd-autopsy-report-chem-line =   {$reagent} ({$amount}u)
oxyd-autopsy-flag-damage = {$amount} trauma damage
oxyd-autopsy-flag-fracture = fractured
oxyd-autopsy-flag-incision = open incision
oxyd-autopsy-flag-bleeding = bleeding
oxyd-autopsy-flag-embedded = embedded objects ({$count})
oxyd-massspec-header = Chemicals found in {$target}:
oxyd-massspec-line-units = {$reagent} ({$units}u)
oxyd-massspec-none = No significant chemical agents found in {$target}.

## Operating computer
oxyd-opcomputer-window-title = Patient Monitoring Console
oxyd-opcomputer-patient-info = Patient Information:
oxyd-opcomputer-no-patient = No Patient Detected
oxyd-opcomputer-name = Name:
oxyd-opcomputer-status = Patient Status:
oxyd-opcomputer-status-stable = Stable
oxyd-opcomputer-status-crit = Non-Responsive
oxyd-opcomputer-status-dead = DECEASED
oxyd-opcomputer-crit-health = Critical Health:
oxyd-opcomputer-organ-health = Organ Health:
oxyd-opcomputer-blood = Blood Level:
oxyd-opcomputer-blood-value = {$pct}% ({$units}u)
oxyd-opcomputer-pulse = Heartbeat rate:
oxyd-opcomputer-bar-brute = Brute
oxyd-opcomputer-bar-burn = Burn
oxyd-opcomputer-bar-toxin = Toxin
oxyd-opcomputer-bar-asphyx = Asphyx

ent-OxydErisAutopsyScanner = autopsy scanner
    .desc = Extracts information on wounds from a cadaver and prints a report.
ent-OxydErisMassSpectrometer = mass spectrometer
    .desc = A hand-held mass spectrometer which identifies trace chemicals in a sample.
ent-OxydErisMassSpectrometerAdvanced = advanced mass spectrometer
    .desc = A hand-held mass spectrometer which identifies trace chemicals and their amounts.
ent-OxydMoebiusMedVend = Moebius MediVend
    .desc = A Moebius Medical dispenser stocked with Eris-grade supplies.
ent-OxydMedicalOperatingComputer = patient monitoring console
    .desc = A console displaying the vitals of the patient on the adjacent operating table.

## Cortical borer (Eris)
ent-OxydBorer = cortical borer
    .desc = A worm-like parasite with a thick, slimy carapace. It feeds on a host's chemicals and can cure or cripple them on a whim.

oxyd-borer-ghostrole-name = Cortical Borer
oxyd-borer-ghostrole-description = A worm-like parasite. Find a host, burrow into their ear canal, and survive.
oxyd-borer-ghostrole-rules = You are a parasite, not a killer. Burrow into a host to survive, secrete what you please into their bloodstream, and reproduce when you can. Sugar sedates you.

oxyd-borer-verb-category = Borer
oxyd-borer-verb-infest = Infest
oxyd-borer-verb-paralyze = Paralyze Victim
oxyd-borer-verb-secrete = Secrete {$reagent}
oxyd-borer-verb-reproduce = Reproduce
oxyd-borer-verb-release = Release Host

oxyd-borer-infest-start = You slither up {$host} and start burrowing into their ear canal...
oxyd-borer-infest-start-host = Something slimy wiggles in your ear!
oxyd-borer-infest-implant = A nanofiber mesh protects {$host}'s brainstem. It takes time to work around.
oxyd-borer-infest-protected = This one's head is sealed tight. You cannot find a way in.
oxyd-borer-infest-occupied = This host is already occupied.
oxyd-borer-infest-dead = A dead body would make a poor host.
oxyd-borer-infest-done = You wiggle into {$host}'s ear canal and press against their brainstem. Comfortable.
oxyd-borer-infest-done-host = A horrible, nauseating feeling trickles behind your eyes.

oxyd-borer-not-enough-chems = You don't have enough chemicals stored up.
oxyd-borer-docile = You are feeling too docile to do that.
oxyd-borer-docile-on = Something sweet in the host's bloodstream makes you feel docile...
oxyd-borer-docile-off = The sweet taste fades. You feel capable again.
oxyd-borer-secreted = You secrete {$reagent} into the host's bloodstream ({$amount}u in blood).
oxyd-borer-reproduce = You push a pulsing egg out through the host's mouth.
oxyd-borer-reproduce-poor = You need at least {$cost} chemicals stored to reproduce.
oxyd-borer-reproduce-host = Your stomach churns painfully and you retch something up.
oxyd-borer-release-start = You begin sliding out of your host...
oxyd-borer-release-start-host = Something stirs in your ear canal...
oxyd-borer-release-done = You detach and drop from {$host} to the floor.
oxyd-borer-release-done-host = Something slimy wiggles out of your ear and lands on the floor.
oxyd-borer-host-died = Your host's life signs have flatlined. You are trapped unless you release.

oxyd-borer-paralyze = You release a jolt of fear into {$victim}. Their limbs lock up.
oxyd-borer-paralyze-victim = Your limbs twitch horribly, like a puppet's.
oxyd-borer-paralyze-cooldown = Your toxin gland hasn't recharged yet.
oxyd-borer-paralyze-cooldown-in = Your toxin gland needs {$seconds} more seconds to recharge.


## Cortical borer — round 2 (assume control, comms, hide, evolution)
ent-OxydBorerCaptiveBrain = captive mind
    .desc = The displaced consciousness of a host body, held prisoner inside a cortical borer.

oxyd-borer-verb-assume-control = Assume Control
oxyd-borer-verb-release-control = Release Control
oxyd-borer-verb-talk-captive = Talk to Captive
oxyd-borer-verb-captive-whisper = Whisper to Host
oxyd-borer-verb-captive-resist = Resist
oxyd-borer-verb-psychic-whisper = Psychic Whisper
oxyd-borer-verb-commune = Commune
oxyd-borer-verb-read-mind = Read Mind
oxyd-borer-verb-write-mind = Write Mind
oxyd-borer-verb-speak-to-host = Speak to Host
oxyd-borer-verb-say-host = Say as Host
oxyd-borer-verb-whisper-host = Whisper as Host
oxyd-borer-verb-hide = Hide
oxyd-borer-verb-unhide = Stop Hiding

oxyd-borer-control-start = You begin delicately adjusting your connection to the host brain. This will take some time...
oxyd-borer-control-done = You plunge your probosci deep into the cortex of the host brain, interfacing directly with their nervous system.
oxyd-borer-control-done-host = You feel a strange shifting sensation behind your eyes as another consciousness displaces yours.
oxyd-borer-control-dead = You can't control a dead host.
oxyd-borer-control-no-mind = You have no mind to drive a body with.
oxyd-borer-control-stopped = You feel your control over your host suddenly stop.
oxyd-borer-release-control = You withdraw your probosci, releasing control of the host body.
oxyd-borer-release-control-captive = As though waking from a dream, you shake off the insidious mind control of the brain worm. Your thoughts are your own again.
oxyd-borer-resist-start = You begin doggedly resisting the parasite's control (this will take approximately thirty seconds).
oxyd-borer-resist-start-host = You feel the captive mind begin to resist your control.
oxyd-borer-resist-done = With an immense exertion of will, you regain control of your body!
oxyd-borer-resist-done-borer = You feel control of the host brain ripped from your grasp, and retract your probosci before the wild neural impulses can damage you.
oxyd-borer-docile-control = You are feeling far too docile to continue controlling your host...
oxyd-borer-no-mind = You need a mind to do that.
oxyd-borer-no-captive = The captive mind is gone.

oxyd-borer-dialog-speak-title = Speak to host
oxyd-borer-dialog-speak-prompt = Words to drop into the host's mind
oxyd-borer-dialog-say-host-title = Say as host
oxyd-borer-dialog-whisper-host-title = Whisper as host
oxyd-borer-dialog-say-host-prompt = Speech to force from the host
oxyd-borer-dialog-psychic-title = Psychic whisper
oxyd-borer-dialog-psychic-prompt = Message for their mind
oxyd-borer-dialog-commune-title = Commune
oxyd-borer-dialog-commune-prompt = Thoughts to project
oxyd-borer-dialog-captive-title = Talk to captive
oxyd-borer-dialog-captive-prompt = Message to your captive host
oxyd-borer-dialog-whisper-title = Whisper
oxyd-borer-dialog-whisper-prompt = Whisper to the body that was yours

oxyd-borer-speak-self = You drop words into {$host}'s mind: "{$text}"
oxyd-borer-speak-host = Your own thoughts speak: "{$text}"
oxyd-borer-psychic-target = You hear a strange, alien voice in your head... {$text}
oxyd-borer-psychic-self = You whispered to {$target}: "{$text}"
oxyd-borer-commune-target = Like lead slabs crashing into the ocean, alien thoughts drop into your mind: {$text}
oxyd-borer-commune-self = You communed to {$target}: "{$text}"
oxyd-borer-commune-nosebleed = Your nose begins to bleed...
oxyd-borer-captive-self = You say to your host: {$text}
oxyd-borer-captive-echo = YOU say to yourself: {$text}
oxyd-borer-whisper-self = You whisper silently, "{$text}"
oxyd-borer-whisper-host = The captive mind whispers, "{$text}"

oxyd-borer-read-self = You sift through the host's memories, extracting what you can.
oxyd-borer-read-host = Your head spins, your memories thrown in disarray!
oxyd-borer-write-self = You press fragments of your own mind into the host's.
oxyd-borer-write-host = Your head spins as new information fills your mind!

oxyd-borer-hide-on = You are now hiding under floor clutter.
oxyd-borer-hide-off = You have stopped hiding.
oxyd-borer-level-up = Congratulations! You've reached Evolution Level {$level} — new synthesis reagents and abilities are now available.
