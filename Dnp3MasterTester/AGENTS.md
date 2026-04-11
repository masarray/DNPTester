# AGENTS.md

## Purpose
This repository is a lightweight WPF `net8.0-windows` DNP3 Master Tester for FAT, troubleshooting, and operator-facing diagnostics.

The product priority is:
1. Protocol correctness on the wire
2. Stable operator workflow
3. Clear observability for SCADA/event analysis
4. UI polish

Do not trade protocol correctness for architecture experiments or decorative UI changes.

## Hard rules for Codex
- All active DNP3 communication must go through Step Function IO's DNP3 engine only.
- The active engine for this repository is the official `.NET` DNP3 binding/package (`dnp3`) and its native `dnp3_ffi` runtime payload.
- Analyzer/UI code must remain passive:
  - it may receive callbacks
  - normalize values
  - build operator logs
  - build SOE audit trails
  - build link trace views
- Analyzer/UI code must not:
  - create a custom DNP3 protocol stack
  - invent link/application framing
  - parse DNP3 packets with custom wire logic as a replacement for the engine
  - fabricate unsolicited/event behavior not emitted by the engine
  - run an alternate communication routine outside the Step Function engine
- Prefer official callback data and engine status first.
- If engine data is incomplete, prefer `Unknown` or an empty field over guessing.

## Working rules for Codex
- Follow the Step Function DNP3 `.NET` API and example behavior first.
- Keep the application operator-facing:
  - `Value Viewer` is the latest point state view
  - `Event Logs SCADA` is operational journaling
  - `SOE Event Audit` is event/timestamp evidence
  - `Link Trace` is protocol/runtime diagnostics
- Do not reintroduce IEC-101-specific assumptions, naming, or lib60870 code paths into the active DNP3 path.
- Do not add redundancy-link features unless explicitly requested.
- Do not add link timeline monitor features unless explicitly requested.
- Keep transport support practical:
  - `TCP` and `Serial` are the current primary transports
  - add `TLS` only if requested or if the task clearly requires it

## Current protocol principles
- `Integrity Poll` is operator-triggered or startup-policy-driven, not spammed continuously.
- `Event Poll` is for background event retrieval.
- `Link Status` should come from official engine operations, not inferred UI state.
- `SOE` should prefer engine timestamps/qualities when available.
- `Value Viewer` should be last-known state per point key (`PointType + Index`), not a raw event stream dump.
- `Event Logs SCADA` should stay readable for operators and testers, not become a full debug console.
- `Link Trace` may include decoded runtime/protocol log lines, but it should remain clearly labeled as trace data.

## Important files
- `C:\Git\DNPTester\Dnp3MasterTester\Services\Dnp3MasterService.cs`
- `C:\Git\DNPTester\Dnp3MasterTester\Services\IDnp3MasterService.cs`
- `C:\Git\DNPTester\Dnp3MasterTester\ViewModels\MainViewModel.cs`
- `C:\Git\DNPTester\Dnp3MasterTester\Models\ConnectionSettings.cs`
- `C:\Git\DNPTester\Dnp3MasterTester\MainWindow.xaml`
- `C:\Git\DNPTester\Dnp3MasterTester\MainWindow.xaml.cs`
- `C:\Git\DNPTester\dnp3-main\ffi\bindings\dotnet\examples\master\Program.cs`

## External engine reference
Local engine reference tree:
- `C:\Git\DNPTester\dnp3-main`

Use this folder as the source-of-truth reference when:
- aligning API usage with official examples
- checking Step Function behavior
- later migrating from NuGet package consumption to local-source consumption

Important note:
- the current application uses the official `dnp3` package at build time
- it is aligned to the Step Function local source tree, but not yet directly built from that tree

## Build command
Use:
`dotnet build C:\Git\DNPTester\Dnp3MasterTester\Dnp3MasterTester.csproj`

## Documentation baseline
Before making large changes, inspect:
- `AGENTS.md`
- `PROJECT_CONTEXT.md`

If protocol/architecture intent is unclear:
- preserve the current event flow
- preserve operator-focused outputs
- prefer minimal invasive changes

