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
- Active DNP3 master communication must go through this repository's native C# master stack.
- Do not add proprietary DNP3 protocol packages back to `Dnp3MasterTester` unless the user explicitly reverses the licensing direction.
- Analyzer/UI code must remain passive:
  - it may receive callbacks
  - normalize values
  - build operator logs
  - build SOE audit trails
  - build link trace views
- Analyzer/UI code must not:
  - fabricate unsolicited/event behavior not emitted by the native master stack
  - guess object values when decoding fails
  - silently treat malformed frames as valid data
- Prefer decoded protocol data and explicit unknown/error fields first.
- If engine data is incomplete, prefer `Unknown` or an empty field over guessing.

## Working rules for Codex
- Follow IEEE 1815/DNP-UG protocol behavior and the native stack boundaries first.
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
- `C:\Git\DNPTester\Dnp3MasterTester\Protocol`

## Protocol reference posture
Use IEEE 1815/DNP-UG materials as the source of truth for protocol behavior.
Permissive public stacks such as OpenDNP3 may inform architecture, but do not
copy proprietary DNP3 implementation code into this repository.

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
