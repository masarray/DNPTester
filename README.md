# DNP3 Interoperability Tester

Professional DNP3 master-side test workspace for IED commissioning, SCADA
signal troubleshooting, FAT evidence collection, and interoperability testing.

![Mission Control](Dnp3MasterTester/Assets/screenshot/web/mission-control.png)

## Why this exists

DNP3 testing often starts simple: open a TCP socket, poll a few points, operate a
binary output, and inspect logs. That is not enough for real commissioning.

In a relay FAT or SCADA integration session, engineers need answers that can be
defended:

- Is the device only reachable, or did it actually respond as a DNP3 outstation?
- Which binary and analog values were decoded?
- Were events received with source timestamp quality from the IED?
- Was the command accepted, and did feedback or final state confirm the action?
- Which evidence can be exported into a report for the customer?

DNP3 Interoperability Tester is built around those questions. It is not just a
raw protocol console. It is an evidence workspace for engineers who need to
test, explain, troubleshoot, and report what happened.

## What you can use it for

- **IED and protection relay testing**  
  Connect to a DNP3 outstation, run integrity/event polling, inspect live point
  values, and verify whether mapped binary/analog signals make sense.

- **SCADA signal troubleshooting**  
  Use live values, SCADA Events, SOE Audit, and Link Trace together to understand
  why a signal is missing, stale, incorrectly mapped, timestamp-invalid, or only
  visible after a specific poll.

- **Factory Acceptance Test evidence**  
  Capture report identity, point evidence, timestamp quality, command lifecycle,
  non-operation guard evidence, and recovery evidence into a structured FAT
  report snapshot.

- **DNP3 interoperability checks**  
  Validate how an IED behaves under polling, command, timestamp, class/event,
  and recovery workflows before connecting it to a production SCADA system.

- **Command lifecycle review**  
  Track command prepared, requested, accepted, feedback matched, and final
  verdict stages instead of treating a command as a single button click.

## Current product surfaces

| Workspace | Purpose |
| --- | --- |
| Mission Control | Operational dashboard for connection state, point cache, event count, SOE count, and command lifecycle. |
| Value Viewer | Latest decoded point values with flags, source reason, and mapping context. |
| SCADA Events | Operator-facing event journal using IED/source timestamp when available, with captured-time fallback. |
| SOE Audit | Forensic event table with source timestamp status, class, variation, qualifier, broadcast flag, and source reason. |
| Link Trace | Protocol and task trace view for troubleshooting poll, response, and engine behavior. |
| Command Testing | Binary output test workflow with lifecycle evidence and feedback matching. |
| Report Workspace | Guided FAT evidence flow and QuestPDF preview/export path. |
| About | License, maintainer, and third-party DNP3 engine compliance notes. |

## Evidence-first reporting

The report workspace is designed around evidence, not screenshots pasted into a
document. The app builds a structured FAT snapshot from the evidence it has
actually captured:

- real device response evidence, not just open socket state
- point value evidence from decoded DNP3 objects
- binary and analog mapping assessment with operator verdict and remarks
- SOE/event evidence with source timestamp quality
- command lifecycle evidence and final command verdict
- guarded non-operation and communication recovery evidence
- protocol trace extracts for troubleshooting context
- report identity fields and logo support

Preview and export use the same QuestPDF render path, so the PDF preview is
intended to match the exported report instead of being a separate mock screen.

## Screenshots

| Report Workspace | SCADA Events |
| --- | --- |
| ![Report preview](Dnp3MasterTester/Assets/screenshot/web/report-preview.png) | ![SCADA events](Dnp3MasterTester/Assets/screenshot/web/scada-events.png) |

| SOE Audit | Mission Control |
| --- | --- |
| ![SOE audit](Dnp3MasterTester/Assets/screenshot/web/soe-audit.png) | ![Mission Control](Dnp3MasterTester/Assets/screenshot/web/mission-control.png) |

## Important design choices

- **Connected means evidence-backed**  
  The app distinguishes transport-open state from real DNP3 device response.
  This matters because an open socket is not the same thing as a responding
  outstation.

- **Timestamp basis is visible**  
  SCADA Events prefer IED/source timestamp when valid and show captured-time
  fallback when source timestamp is invalid or unavailable.

- **Report verdicts separate completion from failure**  
  A partially executed FAT should not look like a failed FAT. The report engine
  tracks executed, open, failed, warning, and passed items separately.

- **UI stays evidence-oriented**  
  The interface is designed for engineers who need fast scanning during live
  communication, not for dumping every internal object into the screen.

## Build

```powershell
dotnet build DNPTester.slnx
```

## GitHub Pages

The static product landing page lives in [docs](docs). Use GitHub Pages with
`Source: GitHub Actions`, then run the `Deploy GitHub Pages` workflow.

## Compliance note

This repository can publish the application source under its own license, but
third-party dependencies remain governed by their own terms. In particular, the
Step Function I/O DNP3 package must be reviewed separately before commercial or
production distribution.

Read:

- [LICENSE](LICENSE)
- [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
- [Dnp3MasterTester README](Dnp3MasterTester/README.md)
