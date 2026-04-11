# Dnp3MasterTester

## Overview
`Dnp3MasterTester` is a WPF desktop DNP3 master-side test workspace for communicating with slave devices, especially protection relays over TCP and Serial.

It is built on top of the official Step Function IO `.NET` DNP3 package and is intended to evolve into a credible FAT / interoperability / audit tool.

Current focus areas:
- latest point state review
- SCADA-style event journal
- SOE / forensic callback evidence
- protocol/runtime diagnostics

## Status
The project is functional but still under active refinement.

What already exists:
- WPF operator shell
- TCP and Serial DNP3 master channels
- connection setup dialog
- workspace tabs for value/event/audit/trace review
- service-based integration with official Step Function DNP3 engine

What still needs major work:
- more credible polling/interoperability strategy
- better relay-profile behavior
- export/report output
- command/operate testing workflow

## Technology
- `.NET 8`
- WPF
- Step Function IO `dnp3` package
- native `dnp3_ffi` payload

## Main surfaces

### `Value Viewer`
Shows last known state per point.

### `SCADA Events`
Intended for operator-facing event journal rows such as:
- binary state changes
- command events

### `SOE Audit`
Shows more callback-derived detail for forensic review.

### `Link Trace`
Shows protocol/runtime diagnostics and engine trace information.

## Project structure

Important files:
- [AGENTS.md](/C:/Git/DNPTester/Dnp3MasterTester/AGENTS.md)
- [PROJECT_CONTEXT.md](/C:/Git/DNPTester/Dnp3MasterTester/PROJECT_CONTEXT.md)
- [project_tree.md](/C:/Git/DNPTester/Dnp3MasterTester/project_tree.md)
- [project_data_flow.md](/C:/Git/DNPTester/Dnp3MasterTester/project_data_flow.md)
- [project_summary.md](/C:/Git/DNPTester/Dnp3MasterTester/project_summary.md)
- [MainWindow.xaml](/C:/Git/DNPTester/Dnp3MasterTester/MainWindow.xaml)
- [ConnectionSettingsWindow.xaml](/C:/Git/DNPTester/Dnp3MasterTester/ConnectionSettingsWindow.xaml)
- [ViewModels/MainViewModel.cs](/C:/Git/DNPTester/Dnp3MasterTester/ViewModels/MainViewModel.cs)
- [Services/Dnp3MasterService.cs](/C:/Git/DNPTester/Dnp3MasterTester/Services/Dnp3MasterService.cs)

## Build

```powershell
dotnet build C:\Git\DNPTester\Dnp3MasterTester\Dnp3MasterTester.csproj
```

## Development notes

### Use the official engine only
Do not replace the Step Function engine with a custom protocol stack.

### UI should stay passive
The UI layer should display and organize evidence, not invent protocol behavior.

### Polling is currently the highest-value improvement area
If relay changes only appear after manual integrity, treat that as a protocol/interoperability issue first, not merely a UI issue.

## Recommended reading order
1. `AGENTS.md`
2. `PROJECT_CONTEXT.md`
3. `project_tree.md`
4. `project_data_flow.md`
5. `project_summary.md`
6. `Services/Dnp3MasterService.cs`
7. `MainWindow.xaml`

## Goal
The goal is to turn this repository into a DNP3 master-side relay audit tool that is:
- credible
- readable
- interoperable
- useful in real FAT and troubleshooting workflows
