# PROJECT_CONTEXT.md

## Purpose
`Dnp3MasterTester` is a WPF desktop DNP3 master-side protocol test and audit tool for communication to slave devices, especially protection relays.

The intended end-state is not merely a demo viewer. The application is being shaped into a credible FAT / interoperability / audit workspace that can help validate:
- TCP and Serial DNP3 communications
- master-to-outstation interoperability
- event delivery behavior
- SOE / time quality evidence
- operator-facing SCADA event interpretation
- protocol diagnostics for troubleshooting relay behavior

The product intent is:
1. Credible protocol behavior first
2. Interoperability across real relay implementations
3. Audit-friendly evidence surfaces
4. Professional operator UX

## Current reality
The project is in an active transition phase.

It already has:
- a working WPF shell
- integration with Step Function IO's official `.NET` DNP3 engine
- TCP and Serial master channels
- a main audit workspace with multiple tabs
- a separate connection setup window
- basic Value Viewer / SCADA Events / SOE Audit / Link Trace surfaces

It does **not** yet fully meet the target of a production-credible interoperability audit tool.

The main current gap is:
- the polling / event acquisition strategy is still too simplistic for broad relay interoperability and real SCADA-style behavior

## Tech stack
- UI: WPF
- Runtime: `.NET 8` (`net8.0-windows`)
- Protocol engine: Step Function IO DNP3 `.NET` binding/package `dnp3`
- Native runtime payload: `dnp3_ffi`

## Protocol/architecture constraints
- All active DNP3 communication must go through the official Step Function engine.
- Do not build a custom DNP3 stack or alternate wire parser to replace engine behavior.
- UI code must remain passive and operator-facing:
  - display values
  - summarize events
  - retain SOE evidence
  - show trace/diagnostics
- If engine context is incomplete, prefer explicit unknown/empty values over guessing.

## Primary source-of-truth references
- Project rules:
  - `C:\Git\DNPTester\Dnp3MasterTester\AGENTS.md`
- Step Function example master:
  - `C:\Git\DNPTester\dnp3-main\ffi\bindings\dotnet\examples\master\Program.cs`
- Step Function docs:
  - `C:\Git\DNPTester\dnp3-main\guide\docs\api\master\association_config.mdx`
  - `C:\Git\DNPTester\dnp3-main\guide\docs\api\master\association_polls.mdx`
  - `C:\Git\DNPTester\dnp3-main\guide\docs\api\master\read_handler.mdx`

These references matter because the current code is aligned to the official engine examples, but the examples are still only a baseline. The next work should move the tool from "sample-aligned" toward "interoperability-audit credible".

## High-level architecture
The project uses a lightweight MVVM-style structure:

- `Models`
  UI-facing records for connection settings, event rows, SOE rows, trace rows, and value rows
- `Services`
  DNP3 integration layer around the Step Function engine
- `ViewModels`
  UI orchestration, command handling, summary text, collection updates
- `Views`
  WPF windows and XAML layouts

## Project tree

```text
Dnp3MasterTester/
|-- AGENTS.md
|-- PROJECT_CONTEXT.md
|-- App.xaml
|-- App.xaml.cs
|-- AssemblyInfo.cs
|-- Dnp3MasterTester.csproj
|-- MainWindow.xaml
|-- MainWindow.xaml.cs
|-- ConnectionSettingsWindow.xaml
|-- ConnectionSettingsWindow.xaml.cs
|-- Models/
|   |-- ConnectionSettings.cs
|   |-- ConnectionStatusSnapshot.cs
|   |-- EventLogEntry.cs
|   |-- LinkTraceEntry.cs
|   |-- SoeEventRow.cs
|   `-- ValueViewerRow.cs
|-- Services/
|   |-- Dnp3MasterService.cs
|   `-- IDnp3MasterService.cs
`-- ViewModels/
    |-- MainViewModel.cs
    |-- RelayCommand.cs
    `-- ViewModelBase.cs
```

Generated folders:
- `bin/`
- `obj/`

Treat generated folders as build artifacts only.

## UI state as of now
The UI has recently been reset away from an over-styled rounded mockup look.

Current UI direction:
- neutral desktop baseline
- app background around `#F3F3F3`
- work surfaces around `#FFFFFF`
- small-radius panels instead of heavy rounding
- toolbar at top for main actions
- workspace tab model for audit surfaces

This reset was deliberate.

Reason:
- the previous styling had excessive rounding, weak separation, and non-professional visual language for an engineering/audit tool
- the current goal is to establish a calm desktop baseline first, then polish incrementally

## Main windows and responsibilities

### `MainWindow.xaml`
Current primary workspace.

Contains:
- header/toolbar
- session summary cards
- workspace tabs:
  - `Overview`
  - `Value Viewer`
  - `SCADA Events`
  - `SOE Audit`
  - `Link Trace`

### `ConnectionSettingsWindow.xaml`
Secondary window for editing current session settings:
- transport
- endpoint / serial port
- master address
- outstation address
- request timeout
- event poll interval

This split exists to keep the main workspace focused on audit/review instead of form entry.

## Model responsibilities

### `ConnectionSettings.cs`
Connection/session parameters.

Current fields:
- `Transport`
- `Endpoint`
- `SerialPort`
- `MasterAddress`
- `OutstationAddress`
- `RequestTimeoutSeconds`
- `EventPollSeconds`

It now implements property change notification so UI summary panels update live.

### `ValueViewerRow.cs`
Latest-state row per point key.

Purpose:
- represent the current state of points
- not a full event history

### `EventLogEntry.cs`
Operator-facing SCADA event row.

Current intent:
- binary state changes
- binary initialization observations
- command events

This model is no longer meant to be a generic dumping ground for engine trace or internal task chatter.

### `SoeEventRow.cs`
Forensic / callback-oriented audit row.

Current fields include:
- capture time
- source timestamp
- read type
- event class
- point type
- index
- value
- status
- flags
- time quality
- variation
- qualifier
- broadcast marker
- notes

This is an attempt to preserve more callback context so the tool can support forensic review and FAT evidence.

### `LinkTraceEntry.cs`
Runtime / protocol diagnostic row.

Current intent:
- logging from engine/runtime
- protocol debug messages
- internal state/task messages that are useful for engineering analysis

## Service responsibilities

### `IDnp3MasterService.cs`
UI-facing contract for the master service.

### `Dnp3MasterService.cs`
Main protocol integration seam.

Current responsibilities:
- configure Step Function logging
- create runtime
- create TCP or Serial channels
- add association
- add one periodic event poll
- expose connect/disconnect/poll/link-status operations
- translate engine callbacks into:
  - `ValueViewerRow`
  - `EventLogEntry`
  - `SoeEventRow`
  - `LinkTraceEntry`

Current implementation notes:
- connect/disconnect were moved off the UI thread to avoid freezes
- UI dispatch was changed away from blocking dispatcher invocation

## Current DNP3 behavior

### What the current service does
At connect time:
- configure logging once
- create runtime
- create master channel
- add association
- add one periodic poll for `Class 1/2/3`
- enable channel

The association config currently uses:
- disable unsolicited classes: `EventClasses.All()`
- enable unsolicited classes: `EventClasses.All()`
- startup integrity classes: `Classes.All()`
- automatic scan on asserted IIN event bits: `EventClasses.None()`
- LAN auto time sync
- keepalive timeout

### Why this matters
This setup is close to the official Step Function example, but it is not yet robust enough for broad interoperability expectations.

Observed concern:
- users reported that relay value changes are not delivered to the master until manual `Integrity Poll` is pressed

That means the current polling/event model is not yet credible enough for the intended use case.

## Polling / interoperability audit status
This is the single most important technical improvement area.

### Current issue
The current master polling strategy is too thin for a relay audit tool because:
- it only has one periodic `Class 1/2/3` poll
- it does not auto-scan event classes when corresponding IIN bits assert
- it has no slower periodic static/class 0 refresh
- it assumes relay event buffering/class assignment behavior is ideal

This may still work with some outstations, but it is not broad enough to be called reliably interoperable in the way operators expect from SCADA systems such as Survalent-like environments.

### Important guidance for future contributors
When reviewing or improving this project, treat the polling strategy as an audit-critical topic.

Do not assume the current behavior is sufficient.

Investigate and consider:
- auto scan when IIN Class 1/2/3 bits are asserted
- configurable unsolicited handling
- configurable startup integrity behavior
- dual-poll strategy:
  - fast periodic `Class 1/2/3`
  - slower periodic `Class 0` or broader refresh
- profile-based polling modes such as:
  - `Strict Event`
  - `Balanced SCADA`
  - `Relay FAT / Interoperability`

### Practical target behavior
For a credible relay interoperability tester, the likely target profile is:
- startup integrity scan `Class 0/1/2/3`
- periodic event poll `Class 1/2/3`
- optional slower static refresh
- optional auto scan when event IIN bits assert
- optional unsolicited enable/disable depending on relay support

## Event surface intent

### `Value Viewer`
Should be:
- latest point state
- quick operator review
- not raw event history

### `SCADA Events`
Should be:
- binary state changes
- command events
- other truly operator-meaningful state/event rows only

Should **not** become:
- a dump of protocol task start/success
- fragment begin/end chatter
- runtime trace noise

### `SOE Audit`
Should be:
- callback-derived evidence
- timestamp-sensitive forensic view
- variation/qualifier/time quality aware

### `Link Trace`
Should be:
- engine/protocol diagnostics
- internal troubleshooting data

## ViewModel responsibilities

### `MainViewModel.cs`
Current duties:
- own `ConnectionSettings`
- expose toolbar/workspace commands
- manage observable collections
- upsert latest values
- expose connection summary text:
  - connection profile
  - connection target
  - polling profile

The ViewModel should remain orchestration-focused and not absorb protocol logic that belongs in the service.

## Current strengths
- official engine only
- protocol work centralized in one service
- UI already split into operator-facing surfaces
- connection freeze issue has been addressed
- SCADA event and trace intent is now more clearly separated than before
- UI visual direction has been reset to a saner desktop baseline

## Current weaknesses / open risks
- polling behavior is not yet interoperability-credible
- SCADA event model is still heuristic and may need refinement
- SOE fields are improved but not yet proven against multiple relay vendors
- no persistence/profile system
- no explicit relay capability/profile concept
- no export/report evidence generator
- no operate/command test workflow yet
- no TLS support yet
- no test harness or automated validation for polling behavior

## Near-term roadmap
The highest-value next steps are:

1. Fix polling/interoperability behavior
- add more credible poll strategy
- add optional static refresh
- consider auto event scan on IIN bits
- expose polling mode in UI/settings

2. Improve audit credibility
- tighten SCADA event rules
- document exactly which SOE fields come from callbacks vs derived context
- show source reason for each row:
  - startup integrity
  - periodic poll
  - manual integrity
  - unsolicited
  - auto event scan

3. Add relay profile capability
- vendor/profile presets
- different polling strategies or unsolicited assumptions

4. Improve test usability
- add command/operate testing
- add clear health indicators
- add exportable audit report or CSV/JSON evidence dump

## Safe extension points

### Improve protocol behavior
Primarily:
- `Services/Dnp3MasterService.cs`
- `Services/IDnp3MasterService.cs`
- `Models/ConnectionSettings.cs`

### Improve operator workspace
Primarily:
- `MainWindow.xaml`
- `ConnectionSettingsWindow.xaml`
- `ViewModels/MainViewModel.cs`

### Add relay profiles
Likely add:
- `Models/RelayProfile.cs`
- `Services/Profiles/...`
- UI binding in `MainViewModel.cs`

### Add export/reporting
Likely add:
- `Services/Export/...`
- toolbar/menu actions in `MainWindow.xaml`

## Build and verification
Primary build command:

```powershell
dotnet build C:\Git\DNPTester\Dnp3MasterTester\Dnp3MasterTester.csproj
```

Note:
- builds inside the current sandbox may fail due to local SDK/user-profile access restrictions
- if a future agent cannot build in sandbox, do not assume the code is broken without checking the actual error source

## Recommended reading order for future contributors
1. `AGENTS.md`
2. `PROJECT_CONTEXT.md`
3. `App.xaml`
4. `MainWindow.xaml`
5. `ConnectionSettingsWindow.xaml`
6. `ViewModels/MainViewModel.cs`
7. `Services/IDnp3MasterService.cs`
8. `Services/Dnp3MasterService.cs`
9. `Models/*.cs`
10. Step Function docs/examples under `C:\Git\DNPTester\dnp3-main`

## One-line mental model
This project is becoming a DNP3 master-side relay audit workspace: a thin but increasingly structured WPF shell around the official Step Function engine, where protocol credibility and interoperability behavior matter more than decorative UI.
