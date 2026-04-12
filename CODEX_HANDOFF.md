# CODEX HANDOFF

Last updated: `2026-04-12 16:56:03 +07:00`
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

The project is now in a better place than the previous handoff:

- master and slave both use JSON database profiles
- `default.json` remains the debug/simulation profile
- `ElectraNet.json` is now the first real FAT profile on both sides
- point database editing exists in both apps
- command-feedback pairing is now functionally working for the ElectraNet group-active use case
- slave command actuation is no longer generic only:
  - BOS 0 -> BI 21
  - BOS 1 -> BI 22
  - BOS 2 -> BI 23
  - BOS 3 -> BI 24
- master command correlation is already consuming explicit feedback metadata

Most recent progress in this session:

- slave startup timestamp seeding has now been upgraded from “model-only timestamp” to real outstation startup-event publication
- `Dnp3OutstationService` now uses `UpdateOptions.DetectEvent().WithEventMode(EventMode.Force)` for selected startup-seeded points
- local compile verification for `Dnp3SlaveSimulator` succeeded

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
- metadata enrichment is working well enough for real FAT iteration
- ElectraNet semantic state text is visible
- point database tab is now the real engineering workspace, not just a demo surface

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

- `Point Database` tab added and retained as the main metadata editor
- profile selection wired into the master UI
- metadata enrichment is applied to:
  - Value Viewer
  - SCADA Events
  - SOE Audit
  - Command Life Tracker
- command feedback metadata is now inline in the main point database grid, not stuck in a cramped sub-grid
- master still keeps structured feedback metadata internally:
  - `FeedbackIndex`
  - `FeedbackPointType`
  - `FeedbackDisplayName`
  - `DefaultCommandMode`
  - `TimeoutMs`
- `Dnp3MasterService.ExecuteBinaryControlAsync(...)` now accepts expected feedback point type/index and correlation window
- explicit ElectraNet mapping path is active for command feedback correlation
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
- `Point Database` now behaves like a real profile configurator
- inline feedback metadata added to the main signal grid
- separate cramped mapping sub-grid removed
- save/reload added
- enum JSON loading fixed by adding `JsonStringEnumConverter`
- command mapping is now functionally active:
  - slave runtime uses profile metadata to decide which feedback point to drive
  - BOS group-active commands now move their paired BI feedback points
- startup timestamp seeding is now materially improved:
  - `MainViewModel.SeedStartupTimestamps()` stamps the model before runtime start
  - `Dnp3OutstationService.Start()` now publishes startup-seeded points with forced event creation

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

6. Slave startup timestamp gap:

- previously startup seeding only updated the local model timestamp
- no real DNP3 event was guaranteed for unchanged startup values
- fixed by forcing startup event creation for timestamp-enabled event-capable points in `Dnp3OutstationService`

## Known gaps right now

### 1. Startup timestamp still needs runtime FAT validation

Implementation is now in place, but it still needs real validation from the user machine:

- start slave fresh
- connect master fresh
- perform startup integrity / event poll
- verify startup-seeded timestamped events are visible without waiting for manual point change

Important semantic note:

- GI/static responses may still legitimately show no timestamp depending on variation
- the expected proof is in event/scan behavior, not necessarily in every static row

### 2. Master and slave metadata model should stay synchronized

The direction is correct now:

- both have `default.json`
- both have `ElectraNet.json`
- both have inline command-feedback metadata

But future edits must keep the schema aligned across both apps.

### 3. Current FAT scope is intentionally narrow and explicit

What is already engineered for real validation:

- ElectraNet profile
- 4 group-active command/feedback pairs

What is not yet generalized:

- wider command families
- richer command-mode semantics per point
- advanced mismatch / uncertain verdict taxonomy

## Recommended next steps for Codex on home PC

### Immediate next step

Do runtime validation of the new startup timestamp behavior.

Suggested test:

1. run slave with `ELECTRANET PROFILE`
2. start slave from cold state
3. connect master with `ELECTRANET PROFILE`
4. perform startup integrity / event poll
5. verify whether startup-seeded BI/BOS rows now produce timestamped evidence immediately

If startup timestamp is still not visible enough, inspect:

- event variation used for the point
- whether the master UI is only showing static timestamp fields
- whether the first queued events are being cleared before the user looks at them

### Next step after timestamp validation

Recheck full ElectraNet command lifecycle end-to-end:

1. BO/BOS 0 command -> BI 21 feedback
2. BO/BOS 1 command -> BI 22 feedback
3. BO/BOS 2 command -> BI 23 feedback
4. BO/BOS 3 command -> BI 24 feedback

Confirm:

- slave BOS state changes
- paired BI feedback changes
- master life tracker correlates the explicit feedback point
- verdict remains stable after completion

### After that

Only then move into refinement:

- startup event origin labeling (`StartupSeed` vs real change)
- richer verdict wording
- broader ElectraNet engineering metadata

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
