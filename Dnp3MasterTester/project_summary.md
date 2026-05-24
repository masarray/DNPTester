# project_summary.md

## What this project is
`Dnp3MasterTester` is a WPF desktop application that acts as a DNP3 master-side test and audit workspace for communicating with slave devices, especially protection relays over TCP and Serial.

It now carries a native C# DNP3 master stack for active master communication. The WPF shell remains an operator/audit workspace over `IDnp3MasterService`, without a proprietary DNP3 runtime dependency.

## What it is trying to become
The long-term goal is a tool that is:
- credible for FAT
- useful for interoperability testing
- readable for operators and engineers
- defensible for audit and troubleshooting

That means the app must do more than show raw values. It must help users answer:
- did the relay communicate correctly?
- did the master receive events when it should?
- did the timestamps and qualities make sense?
- is the event journal suitable for SCADA/operator interpretation?
- what protocol/runtime evidence exists if behavior looks wrong?

## Current architecture in one paragraph
The app uses a simple MVVM structure. WPF windows bind to `MainViewModel`, which owns UI collections and commands. `MainViewModel` delegates all protocol work to `Dnp3MasterService`, which uses the native C# protocol layer, manages transport/polls/commands, and translates decoded responses into rows for `Value Viewer`, `SCADA Events`, `SOE Audit`, and `Link Trace`.

## Current user-facing surfaces

### `Overview`
High-level summary panel for current session/audit scope.

### `Value Viewer`
Latest known point state.

### `SCADA Events`
Operator-facing journal intended for meaningful event changes rather than raw trace.

### `SOE Audit`
Forensic event/callback view with more callback context.

### `Link Trace`
Engine/runtime/protocol diagnostics.

## Current strengths
- Uses the official DNP3 engine rather than a custom stack
- Clear separation between UI layer and protocol service
- Separate audit surfaces instead of one mixed grid
- Connect/disconnect no longer block the UI thread
- Visual theme has been reset toward a calmer desktop baseline

## Current weaknesses
- Polling and event acquisition are not yet interoperability-credible
- Some relays appear to require manual integrity before changes become visible
- SCADA event model is still heuristic
- SOE model is improved but still needs validation against real devices
- No persistence/profile system
- No export/report output yet
- No command operate workflow yet

## The most important current technical problem
The most important current problem is not UI polish. It is master polling behavior.

At the moment, the tool:
- performs startup integrity through association init
- creates one periodic class event poll
- supports manual integrity

This is too thin for a broad relay interoperability tool because different outstations handle:
- event buffering
- class assignment
- unsolicited behavior
- static refresh expectations

in different ways.

## The most important current UX principle
UI improvements should support protocol credibility, not distract from it.

The project recently moved away from an over-rounded, overly white mockup style. The current visual direction should stay:
- desktop-like
- readable
- calm
- subordinate to the data

Data tables and audit evidence should remain the visual center of gravity.

## Recommended next development priorities

1. Improve polling strategy
- fast periodic event polling
- optional slower static refresh
- optional auto scan on IIN event bits
- configurable interoperability profiles

2. Make event provenance clearer
- identify whether a row came from startup integrity, periodic poll, manual poll, unsolicited, or future auto scan

3. Add relay/test profiles
- so users can switch between practical strategies without code edits

4. Add export/reporting
- CSV, JSON, or audit summary output

5. Add command testing
- especially useful for relay FAT workflows

## How to think about the project
This is best viewed as a master-side relay audit workspace with four jobs:
- show latest state
- show operator-meaningful events
- preserve forensic evidence
- expose protocol diagnostics

Any future improvement should make one or more of those jobs stronger.
