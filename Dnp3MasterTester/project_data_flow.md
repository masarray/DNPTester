# project_data_flow.md

## Purpose
This file explains how data and commands move through `Dnp3MasterTester`, and which files/methods are responsible at each stage.

## High-level flow

The app can be understood as four connected layers:

1. WPF Views
2. ViewModel orchestration
3. DNP3 service integration
4. Step Function engine callbacks

In shorthand:

`Window -> MainViewModel -> Dnp3MasterService -> dnp3 engine -> callbacks -> Dnp3MasterService -> MainViewModel -> UI collections`

## Startup flow

### Files involved
- `App.xaml`
- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- `ViewModels/MainViewModel.cs`
- `Services/Dnp3MasterService.cs`

### Sequence
1. WPF starts the app using `StartupUri="MainWindow.xaml"` from `App.xaml`.
2. `MainWindow` is created.
3. `MainWindow.xaml.cs` creates a `MainViewModel`.
4. `MainWindow.xaml.cs` assigns that view model to `DataContext`.
5. `MainViewModel` creates a `Dnp3MasterService`.
6. `MainViewModel` subscribes to service events.
7. UI bindings start reflecting the view model state.

## Connection settings flow

### Files involved
- `Models/ConnectionSettings.cs`
- `ViewModels/MainViewModel.cs`
- `ConnectionSettingsWindow.xaml`
- `ConnectionSettingsWindow.xaml.cs`
- `MainWindow.xaml.cs`

### Sequence
1. `MainViewModel` owns one shared `ConnectionSettings` instance.
2. `MainWindow` shows summary text derived from `Settings`.
3. User clicks toolbar action to open connection setup.
4. `MainWindow.xaml.cs` opens `ConnectionSettingsWindow` and passes the same `MainViewModel`.
5. `ConnectionSettingsWindow` edits `MainViewModel.Settings` directly.
6. `ConnectionSettings` raises property changed notifications.
7. `MainViewModel` updates summary properties via `RaiseConnectionSummaryChanged()`.
8. The main window reflects updated transport/profile/polling text.

## Command flow

### Toolbar/button to ViewModel

#### Files involved
- `MainWindow.xaml`
- `ViewModels/MainViewModel.cs`
- `ViewModels/RelayCommand.cs`

#### Pattern
Buttons bind to commands exposed by `MainViewModel`.

Examples:
- `ConnectCommand`
- `DisconnectCommand`
- `IntegrityPollCommand`
- `EventPollCommand`
- `LinkStatusCommand`

When a button is pressed:
1. WPF invokes `RelayCommand.Execute`.
2. `RelayCommand` runs the bound delegate.
3. The command eventually calls a ViewModel async method.

### ViewModel command handling

Key methods in `MainViewModel.cs`:
- `ConnectAsync`
- `DisconnectAsync`
- `IntegrityPollAsync`
- `EventPollAsync`
- `CheckLinkAsync`

All of them route through:
- `RunBusyAsync(Func<Task> action)`

Purpose of `RunBusyAsync`:
- set `IsBusy`
- prevent overlapping commands
- catch exceptions
- write UI error rows when something fails
- restore command state afterward

## Connection flow

### Files involved
- `MainWindow.xaml`
- `ViewModels/MainViewModel.cs`
- `Services/IDnp3MasterService.cs`
- `Services/Dnp3MasterService.cs`

### Sequence
1. User clicks `Connect`.
2. `ConnectCommand` calls `MainViewModel.ConnectAsync()`.
3. `MainViewModel.ConnectAsync()` calls `_service.ConnectAsync(Settings)`.
4. `Dnp3MasterService.ConnectAsync()` runs on a background thread.
5. Service:
   - configures logging
   - creates runtime
   - creates master channel
   - adds association
   - adds event poll
   - enables channel
6. Service emits `ConnectionStateChanged`.
7. `MainViewModel` receives the event and updates:
   - `ConnectionState`
   - `ConnectionDetail`
8. UI summary cards update.

## Disconnect flow

### Files involved
- `ViewModels/MainViewModel.cs`
- `Services/Dnp3MasterService.cs`

### Sequence
1. User clicks `Disconnect`.
2. ViewModel calls `_service.DisconnectAsync()`.
3. Service disables/shuts down channel and runtime.
4. Service clears internal references.
5. Service emits new connection state.
6. ViewModel updates UI state.

## Polling flow

### Current implementation
The current service creates:
- one periodic `Class 1/2/3` poll using `AddPoll(...)`

It also supports manual actions:
- `DemandEventPollAsync()`
- `RunIntegrityPollAsync()`

### Files/methods involved
- `Services/Dnp3MasterService.cs`
  - `ConnectAsync`
  - `DemandEventPollAsync`
  - `RunIntegrityPollAsync`
  - `GetAssociationConfig`

### Important reality
This polling/data acquisition flow is currently the biggest technical gap in the project.

Why:
- users observed that some relay value changes do not appear until `Integrity Poll` is pressed
- that indicates current event acquisition is not robust enough for real interoperability testing

Future contributors should treat polling strategy as protocol-critical, not as a minor enhancement.

## Engine callback flow

### Files involved
- `Services/Dnp3MasterService.cs`
  - nested `ReadHandler`
  - nested `AssociationInformation`
  - nested `ClientStateListener`
  - nested `PortStateListener`

### Main callback categories

#### Channel state callbacks
- `ClientStateListener.OnChange`
- `PortStateListener.OnChange`

Current effect:
- publish connection-related messages
- update connection status
- route general state chatter into trace/event handling

#### Association/task callbacks
- `AssociationInformation.TaskStart`
- `AssociationInformation.TaskSuccess`
- `AssociationInformation.TaskFail`
- `AssociationInformation.UnsolicitedResponse`

Current effect:
- mostly routed into general operational trace/event messages

#### Read callbacks
- `ReadHandler.BeginFragment`
- `ReadHandler.EndFragment`
- `ReadHandler.HandleBinaryInput`
- `HandleDoubleBitBinaryInput`
- `HandleBinaryOutputStatus`
- `HandleCounter`
- `HandleFrozenCounter`
- `HandleAnalogInput`
- `HandleFrozenAnalogInput`
- `HandleAnalogOutputStatus`
- `HandleBinaryOutputCommandEvent`
- `HandleAnalogOutputCommandEvent`
- `HandleUnsignedInteger`
- `HandleOctetString`

These methods are where most useful DNP3 data enters the application.

## Value translation flow

### Core method
- `Dnp3MasterService.PublishValue(...)`

### What it does
For each callback value:
1. determine fragment/read context
2. build a `ValueViewerRow`
3. update `_latestValues`
4. emit `ValueReceived`
5. build and emit `SoeEventRow`
6. sometimes generate `EventLogEntry` if the value qualifies as a SCADA event

### Resulting surfaces
- `Value Viewer`
- `SOE Audit`
- sometimes `SCADA Events`

## SCADA event flow

### Current intent
`SCADA Events` should contain operator-meaningful state/event journal rows, not raw engine chatter.

### Current generation logic
Inside `PublishValue(...)`:
- if point is a binary state point and value changed:
  - emit `Binary State Change`
- if point is a binary state point and first observation:
  - emit `Binary State Initialize`
- if point is a command event:
  - emit `Command Event`

### Files involved
- `Services/Dnp3MasterService.cs`
- `Models/EventLogEntry.cs`
- `ViewModels/MainViewModel.cs`
- `MainWindow.xaml`

### Caveat
This logic is still heuristic and should be considered a work-in-progress until validated against multiple relay devices.

## SOE audit flow

### Source
Read callbacks in `Dnp3MasterService.ReadHandler`

### Transformation
Callbacks are routed through `PublishValue(...)`, which packages callback data plus fragment context into `SoeEventRow`.

### Destination
`MainViewModel` receives `SoeEventReceived` and inserts the row into `SoeAudit`.

### Display
`SOE Audit` tab in `MainWindow.xaml`

## Trace flow

### Source
- logging callbacks
- connection state listeners
- association/task information
- explicit trace writes

### Transformation
- `WriteTrace(...)` creates `LinkTraceEntry`

### Destination
`MainViewModel` inserts the row into `LinkTrace`

### Display
`Link Trace` tab

## UI collection update flow

### Files involved
- `ViewModels/MainViewModel.cs`
- `MainWindow.xaml`

### Collections
- `ValueViewer`
- `EventLogs`
- `SoeAudit`
- `LinkTrace`

### Update behavior
- `ValueViewer`
  updated by `UpsertValue(...)` so latest state replaces older state for same point key

- `EventLogs`
  inserted at top

- `SoeAudit`
  inserted at top

- `LinkTrace`
  inserted at top

### Row cap
`MainViewModel.InsertTop<T>(...)` trims each collection to `MaxRows`

## Dispatcher flow

### Files involved
- `ViewModels/MainViewModel.cs`

### Method
- `Dispatch(Action action)`

### Purpose
Ensures UI collection/property updates happen safely on the WPF dispatcher thread.

Current behavior:
- executes directly if already on UI thread
- otherwise uses non-blocking dispatcher begin invoke

This is important because a previous version could freeze when connection logic and callback dispatching blocked the UI thread.

## Summary of major method responsibilities

### `MainWindow.xaml.cs`
- `OpenConnectionSetup_Click`
  Opens the modal configuration window

### `ConnectionSettingsWindow.xaml.cs`
- `DoneButton_Click`
  Closes the settings dialog

### `MainViewModel.cs`
- `RunBusyAsync`
  wraps user actions with busy/error handling
- `UpsertValue`
  keeps the value viewer as latest-state data
- `RaiseConnectionSummaryChanged`
  keeps header summary text synchronized with settings

### `Dnp3MasterService.cs`
- `ConnectAsync`
  protocol startup
- `DisconnectAsync`
  protocol shutdown
- `DemandEventPollAsync`
  manual event poll trigger
- `RunIntegrityPollAsync`
  manual integrity request
- `CheckLinkStatusAsync`
  official link status request
- `PublishValue`
  central data normalization point
- `PublishScadaEvent`
  central SCADA event emission point
- `GetAssociationConfig`
  key interoperability policy point

## Most important future data flow improvement
The next major improvement should be in the polling path:

Current:
- startup integrity
- one periodic event poll
- manual integrity fallback

Desired direction:
- better event acquisition
- optional auto scan on IIN event bits
- optional slower static refresh
- clearer source reason tagging for each row

That change should be centered in:
- `Models/ConnectionSettings.cs`
- `Services/Dnp3MasterService.cs`
- `ViewModels/MainViewModel.cs`
- `MainWindow.xaml`
