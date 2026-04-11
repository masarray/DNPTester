# project_tree.md

## Purpose
This file describes the source tree for `Dnp3MasterTester`, the role of each file, and where future contributors should make changes.

## Top-level layout

```text
Dnp3MasterTester/
|-- AGENTS.md
|-- PROJECT_CONTEXT.md
|-- project_tree.md
|-- project_data_flow.md
|-- project_summary.md
|-- README.md
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
|   |-- IDnp3MasterService.cs
|   `-- Dnp3MasterService.cs
`-- ViewModels/
    |-- ViewModelBase.cs
    |-- RelayCommand.cs
    `-- MainViewModel.cs
```

## Root files

### `AGENTS.md`
Repository-specific coding and architecture rules.

Use it to understand:
- protocol constraints
- UI/architecture boundaries
- build guidance
- what not to do

### `PROJECT_CONTEXT.md`
Current project status, architecture intent, interoperability gaps, and near-term roadmap.

This is the best starting point for a new agent who needs to suggest next steps.

### `project_tree.md`
This file.

### `project_data_flow.md`
Explains how commands, callbacks, and UI updates move through the app.

### `project_summary.md`
Shorter narrative summary of the system, intended use, strengths, and current risks.

### `README.md`
Human-facing quick onboarding document for the repository.

### `Dnp3MasterTester.csproj`
Project definition.

Main role:
- target framework
- WPF enablement
- package dependencies

Current critical dependency:
- official `dnp3` package

### `App.xaml`
Application-level resource dictionary and baseline styling.

Current role:
- neutral desktop baseline theme
- surface/background brushes
- card styling
- toolbar button styling
- tab styling
- data grid styling

This is the main place to tune visual language without changing workflow behavior.

### `App.xaml.cs`
Minimal WPF application bootstrap code-behind.

Currently contains:
- `App : Application`

No major logic lives here yet.

### `AssemblyInfo.cs`
Assembly metadata if needed by the project/build system.

### `MainWindow.xaml`
Primary operator workspace.

Current major sections:
- top toolbar
- session summary cards
- main workspace tabs

Current tabs:
- `Overview`
- `Value Viewer`
- `SCADA Events`
- `SOE Audit`
- `Link Trace`

This file is the main UI shell for the tester.

### `MainWindow.xaml.cs`
Code-behind for the main window.

Current responsibilities:
- create `MainViewModel`
- assign `DataContext`
- open `ConnectionSettingsWindow`

Important method:
- `OpenConnectionSetup_Click`
  Opens the connection settings dialog using the same shared `MainViewModel`

### `ConnectionSettingsWindow.xaml`
Secondary modal window for editing connection/session settings.

Contains:
- transport selection
- endpoint / serial fields
- master/outstation addressing
- timeout
- event poll interval

### `ConnectionSettingsWindow.xaml.cs`
Code-behind for connection dialog.

Important members:
- constructor taking `MainViewModel`
  reuses the existing view model instead of creating a disconnected config object
- `DoneButton_Click`
  closes the dialog

## Models folder

### `Models/ConnectionSettings.cs`
Stores the editable session configuration.

Current major properties:
- `Transport`
- `Endpoint`
- `SerialPort`
- `MasterAddress`
- `OutstationAddress`
- `RequestTimeoutSeconds`
- `EventPollSeconds`

Important note:
- now implements property change notification so summary text in the main window can update live

### `Models/ConnectionStatusSnapshot.cs`
Represents current connection state published by the service.

Fields:
- `State`
- `Detail`
- `TimestampUtc`

### `Models/ValueViewerRow.cs`
Represents latest known state for a point.

Fields:
- `PointType`
- `Index`
- `Value`
- `Flags`
- `Quality`
- `TimestampLocal`
- `Source`

Used by:
- `Value Viewer` tab

### `Models/EventLogEntry.cs`
Operator-facing SCADA event row.

Fields:
- `TimestampLocal`
- `EventType`
- `Source`
- `PointType`
- `Index`
- `Value`
- `Status`
- `Quality`
- `Detail`

Intent:
- represent SCADA-relevant event journal rows
- not a generic trace dump

### `Models/SoeEventRow.cs`
Forensic / audit row derived from read callbacks.

Fields include:
- `ReceivedAtLocal`
- `TimestampLocal`
- `ReadType`
- `EventClass`
- `PointType`
- `Index`
- `Value`
- `Status`
- `Flags`
- `Quality`
- `Variation`
- `Qualifier`
- `IsBroadcast`
- `Notes`

Used by:
- `SOE Audit` tab

### `Models/LinkTraceEntry.cs`
Protocol/runtime diagnostic row.

Fields:
- `TimestampLocal`
- `Level`
- `Direction`
- `Summary`

Used by:
- `Link Trace` tab

## Services folder

### `Services/IDnp3MasterService.cs`
Service contract used by the view model.

Exposes:
- connection state event
- value event
- SCADA event log event
- SOE event
- trace event
- command methods for connect/disconnect/polls

Why it matters:
- keeps the UI decoupled from the concrete Step Function integration

### `Services/Dnp3MasterService.cs`
Main protocol integration file.

This is the most important non-UI file in the project.

Primary responsibilities:
- initialize logging
- create runtime
- create TCP/Serial master channel
- create association
- configure polling
- translate callbacks into UI-facing rows

Key public methods:

- `ConnectAsync(ConnectionSettings, CancellationToken)`
  Creates runtime/channel/association, adds polling, enables the channel, and publishes connection status.

- `DisconnectAsync()`
  Disables/shuts down channel and runtime, clears state, publishes disconnect status.

- `DemandEventPollAsync()`
  Manually requests the periodic event poll.

- `RunIntegrityPollAsync()`
  Sends a class read containing static + event classes.

- `CheckLinkStatusAsync()`
  Executes the official link status check and logs the result.

Key internal methods:

- `ConfigureLoggingOnce()`
  Calls `Logging.Configure(...)` only once for the app lifetime.

- `GetMasterChannelConfig(ConnectionSettings)`
  Creates the engine master channel configuration.

- `GetAssociationConfig(ConnectionSettings)`
  Creates the association config.
  This method is currently audit-critical because it controls unsolicited behavior, startup integrity, and automatic event scanning strategy.

- `RaiseConnection(...)`
  Publishes `ConnectionStatusSnapshot`.

- `WriteEvent(...)`
  Currently routes general operational chatter into `LinkTrace` rather than `SCADA Events`.

- `WriteTrace(...)`
  Publishes `LinkTraceEntry`.

- `PublishValue(...)`
  Central translation point from engine callback values into:
  - `ValueViewerRow`
  - `SoeEventRow`
  - sometimes `EventLogEntry` for SCADA event journal rows

- `PublishScadaEvent(...)`
  Emits operator-facing event log rows.

Nested helper classes:
- `Logger`
- `ClientStateListener`
- `PortStateListener`
- `AssociationHandler`
- `AssociationInformation`
- `ReadHandler`

`ReadHandler` is especially important because it is where most callback-based data translation happens.

## ViewModels folder

### `ViewModels/ViewModelBase.cs`
Minimal property notification base class.

Key methods:
- `SetProperty`
- `RaisePropertyChanged`

### `ViewModels/RelayCommand.cs`
Simple `ICommand` implementation for button actions.

Supports:
- synchronous action
- asynchronous action

Important method:
- `Execute`
  Runs the attached action or async delegate

### `ViewModels/MainViewModel.cs`
Main UI orchestration layer.

Primary responsibilities:
- own the live `ConnectionSettings`
- expose user commands
- subscribe to service events
- manage observable collections
- build summary text for the main window

Key public members:
- `Settings`
- `TransportTypes`
- `ValueViewer`
- `EventLogs`
- `SoeAudit`
- `LinkTrace`
- `ConnectCommand`
- `DisconnectCommand`
- `IntegrityPollCommand`
- `EventPollCommand`
- `LinkStatusCommand`
- `ConnectionProfile`
- `ConnectionTarget`
- `PollingProfile`

Key private methods:
- `ConnectAsync`
- `DisconnectAsync`
- `IntegrityPollAsync`
- `EventPollAsync`
- `CheckLinkAsync`
- `RunBusyAsync`
- `UpsertValue`
- `InsertTop`
- `Dispatch`
- `RaiseCommandState`
- `RaiseConnectionSummaryChanged`

## Most important files for future contributors
If someone needs to improve protocol credibility, interoperability, or audit behavior, start here:

1. `AGENTS.md`
2. `PROJECT_CONTEXT.md`
3. `Services/Dnp3MasterService.cs`
4. `Models/ConnectionSettings.cs`
5. `ViewModels/MainViewModel.cs`
6. `MainWindow.xaml`

If someone needs to improve only visuals:

1. `App.xaml`
2. `MainWindow.xaml`
3. `ConnectionSettingsWindow.xaml`

## Current hotspot
The most important technical hotspot is still:
- `Services/Dnp3MasterService.cs`

Reason:
- the current polling/event acquisition strategy is not yet robust enough for broad relay interoperability
- this is the main blocker to calling the tool production-credible for DNP3 audit/FAT work
