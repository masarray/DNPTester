# CODEX HANDOFF

Last updated: `2026-04-12 10:08:10 +07:00`
Workspace: `D:\Git\DNPTester`

## Current intent

This project is no longer in "demo/debug-only" mode.

The user wants the DNP3 tools to become FAT-capable with:

- one analyzer core for TCP and Serial
- real point-database profiles in JSON
- `default.json` kept for debugging/simulation
- `ElectraNet.json` used as the first real FAT metadata profile
- master and slave both moving toward synchronized profile-driven operation

## Executive summary

The current position is materially ahead of the old `task_handoff.md`.

What is already in place:

- master command tracker exists and has already been hardened against:
  - duplicate feedback mutation after terminal state
  - duplicate completion rows
  - late idle/status rematch on a completed transaction
- master now has point-catalog/profile infrastructure
- master has `MetadataProfiles\default.json` and `MetadataProfiles\ElectraNet.json`
- master now has a real `Point Database` tab, not just a profile label
- slave now also has profile JSON infrastructure:
  - `MetadataProfiles\default.json`
  - `MetadataProfiles\ElectraNet.json`
  - load-on-startup
  - profile selection in `Point Database`
  - save/reload flow

## What the latest user screenshot proves

The screenshot from the user shows the master is running and loading the ElectraNet profile successfully.

Observed from screenshot:

- master is connected
- analyzer profile is `ELECTRANET PROFILE`
- current setup shows `Point Profile: ELECTRANET PROFILE`
- `Point Database` tab exists
- `Value Viewer` now resolves semantic values for ElectraNet DI points:
  - `Binary Input 0 = CLEAR`
  - `Binary Input 21..24 = OFF`
- BOS rows still display generic point labels like:
  - `Binary Output Status 0`
  - `Binary Output Status 1`
  - etc.

This means:

- profile selection is working
- metadata enrichment is partially working
- DI naming/state-text mapping is active
- BOS naming is not yet resolved the way the user wants

## Most likely reason BOS labels are still generic

The current master enrichment key is based on:

- `PointType`
- `Index`

The runtime `PointType` text coming from the service appears to differ from the string stored in JSON for BOS rows.

Likely mismatch examples:

- runtime emits `Binary Output Status`
- metadata may be keyed differently or value path may use another point-type label

Action for next Codex:

1. inspect the exact `PointType` string emitted into:
   - `ValueViewerRow.PointType`
   - `EventLogEntry.PointType`
   - `SoeEventRow.PointType`
2. compare against values stored in:
   - `Dnp3MasterTester\MetadataProfiles\ElectraNet.json`
3. normalize point type keys in one place only
4. avoid spreading string heuristics everywhere

Best fix direction:

- create a canonical point-type normalization helper in master ViewModel/metadata layer
- map all equivalent labels to one normalized key before metadata lookup

Do not move this into `Dnp3MasterService`.

## Important code changes already made in this session

### Master

Files materially changed:

- `Dnp3MasterTester\MainWindow.xaml`
- `Dnp3MasterTester\ViewModels\MainViewModel.cs`
- `Dnp3MasterTester\Services\Dnp3MasterService.cs`
- `Dnp3MasterTester\Models\CommandTransaction.cs`
- `Dnp3MasterTester\Models\EventLogEntry.cs`
- `Dnp3MasterTester\Models\SoeEventRow.cs`
- `Dnp3MasterTester\Models\ValueViewerRow.cs`
- `Dnp3MasterTester\Models\PointCatalogEntry.cs`
- `Dnp3MasterTester\Models\PointCatalogProfile.cs`
- `Dnp3MasterTester\MetadataProfiles\default.json`
- `Dnp3MasterTester\MetadataProfiles\ElectraNet.json`

Master functional changes:

- `Point Database` tab added
- point catalog profile load/save/reload implemented
- profile selection wired into the master UI
- event/value/SOE/command tracker enrichment uses metadata layer
- semantic state text now maps from metadata where available
- command tracker still remains transport-agnostic
- `PointCatalogProfile.FilePath` marked `[JsonIgnore]`
- constructor order fixed so profile setter does not hit `NullReferenceException`

### Slave

Files materially changed:

- `Dnp3SlaveSimulator\MainWindow.xaml`
- `Dnp3SlaveSimulator\ViewModels\MainViewModel.cs`
- `Dnp3SlaveSimulator\Models\SignalDatabaseProfile.cs`
- `Dnp3SlaveSimulator\MetadataProfiles\default.json`
- `Dnp3SlaveSimulator\MetadataProfiles\ElectraNet.json`
- `Dnp3SlaveSimulator\Dnp3SlaveSimulator.csproj`

Slave functional changes:

- profile JSON system added
- startup now loads profiles from `MetadataProfiles`
- selected profile populates `Signals`
- `Point Database` now behaves like a profile configurator
- save/reload added
- enum JSON loading fixed by adding `JsonStringEnumConverter`

## Errors encountered and already fixed

### Fixed

1. Master WPF binding crash:

- `LatestTransactionPoint` was read-only but bound as two-way
- fixed by using one-way binding in XAML

2. Master WPF binding crash:

- `ConnectionTarget` was read-only but bound as two-way
- fixed by using one-way binding

3. Master WPF binding crash:

- `PointCatalogProfileName` was read-only but bound as two-way
- fixed by using one-way binding

4. Master startup `NullReferenceException`:

- `SelectedPointCatalogProfile` was assigned before point-catalog commands existed
- fixed by moving initial profile assignment after command creation
- `RaisePointCatalogCommandState()` also guarded with null-conditional calls

5. Slave startup `JsonException`:

- enum strings in JSON (`BinaryInput`, `Class1`, etc.) were not deserializing
- fixed by adding `JsonStringEnumConverter` to slave profile serializer options

## Known gaps right now

### 1. Master metadata enrichment is not yet fully normalized

Visible symptom:

- DI rows map properly
- BOS rows still show generic labels

Priority:

- high

Reason:

- this affects FAT readability directly

### 2. Master still has both:

- top-level profile combo in summary card
- full `Point Database` tab

This is not broken, but UX should be reviewed later.

Possible direction:

- keep summary card as active-profile indicator only
- keep edit actions only inside `Point Database`

Do this later, not before fixing BOS metadata resolution.

### 3. Slave profile model is functional but not yet engineering-rich

Current slave `ElectraNet.json` is enough to support:

- profile-driven runtime
- group-active command lifecycle validation

But it is not yet a full relay engineering database in the same richness as master metadata.

That is acceptable for now.

### 4. Build verification from Codex sandbox was incomplete

`dotnet build` from sandbox hit local SDK path permission issues:

- `C:\Users\me\AppData\Local\Microsoft SDKs`

So this session relied on user local builds/runs for final confirmation.

## Recommended next steps for Codex on home PC

### Immediate next step

Fix master metadata lookup so BOS rows resolve ElectraNet labels correctly.

Suggested plan:

1. inspect the exact `PointType` values reaching master UI models
2. add a canonical point-type normalization helper in `MainViewModel`
3. normalize both:
   - metadata profile point types
   - runtime point types from service rows
4. verify the following rows display proper names:
   - BOS 0 -> `SET 2 GROUP 1 ACTIVE`
   - BOS 1 -> `SET 2 GROUP 2 ACTIVE`
   - BOS 2 -> `SET 2 GROUP 3 ACTIVE`
   - BOS 3 -> `SET 2 GROUP 4 ACTIVE`

### After that

Validate end-to-end command lifecycle with ElectraNet profile:

1. master profile = `ELECTRANET PROFILE`
2. slave profile = `ELECTRANET PROFILE`
3. issue command on BOS 0..3
4. confirm:
   - requested
   - accepted
   - feedback matched
   - final verdict stable
   - no late rematch mutation

### Next architectural step after BOS naming is clean

Add explicit `CommandDefinition` mapping for ElectraNet group-active pairs only:

- DO/BOS 0 -> DI 21
- DO/BOS 1 -> DI 22
- DO/BOS 2 -> DI 23
- DO/BOS 3 -> DI 24

Important constraints:

- keep mapping logic out of `Dnp3MasterService`
- keep service thin and transport-agnostic
- implement mapping in metadata/ViewModel layer
- no heuristic-only correlation for these 4 points once explicit mapping exists

## Cautions for next Codex

- do not delete `default.json`; the user explicitly wants it preserved for debugging and all-signal simulation
- do not fork separate TCP/Serial analyzer logic
- do not move metadata intelligence into the protocol service
- do not prioritize UI polish over metadata correctness
- do not refine verdict wording before evidence correctness

## Repository notes

There are `.dotnet-cli` telemetry artifacts in workspace status due to sandbox build attempts.

These are not product changes and should not drive implementation decisions.

## If resuming from this file

Start here:

`Dnp3MasterTester\ViewModels\MainViewModel.cs`

Focus on:

- metadata key normalization
- BOS name resolution in Value Viewer / SOE / SCADA Events / Command Tracker

Only after that, continue to explicit ElectraNet command-feedback mapping.
