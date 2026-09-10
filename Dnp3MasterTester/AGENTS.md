# AGENTS.md — DNP3 Master Tester Application Contract

The repository root `AGENTS.md` is authoritative. This file adds stricter application rules for `Dnp3MasterTester/**`.

## Purpose and priority

Dnp3MasterTester is a WPF `net8.0-windows` DNP3 master tester for FAT, commissioning, troubleshooting, command verification, and operator-facing evidence.

Priority:
1. protocol correctness on the wire;
2. command and evidence integrity;
3. stable operator workflow and recoverability;
4. clear SCADA/SOE observability;
5. bounded performance;
6. UI polish.

Do not trade protocol truth for architecture experiments, screenshots, or decorative UI changes.

## Authoritative boundaries

- Active DNP3 communication goes through this repository's native C# master stack.
- Do not add proprietary DNP3 packages unless the user explicitly reverses that direction.
- Analyzer/UI/reporting code may consume callbacks, normalize decoded values, build operator evidence, SOE, traces, and reports.
- Analyzer/UI/reporting code must not fabricate events/unsolicited behavior, guess values when decoding fails, or silently promote malformed data to valid state.
- Prefer explicit `Unknown`, unavailable, invalid-quality, or typed failure state over invented values.
- Do not reintroduce IEC-101/lib60870 assumptions into the active DNP3 path.

## Root-cause-first work

Before changing protocol/runtime behavior identify:
- transport/session owner;
- request/task owner;
- parser/object decoder;
- authoritative point cache;
- event/SOE timestamp provenance;
- command lifecycle owner;
- UI/report consumers.

Use REPRODUCE -> TRACE -> ROOT CAUSE -> FIX -> REGRESSION TEST. If three symptom patches fail in the same subsystem, stop before patch four and re-audit ownership and architecture.

## Protocol principles

- Integrity Poll is operator/startup-policy driven, not spammed continuously.
- Event Poll is background event retrieval.
- Link Status comes from engine operations/evidence, not inferred UI state.
- SOE prefers source/IED timestamps and quality when available.
- Value Viewer is last-known state keyed by `PointType + Index`, not a raw event stream.
- SCADA Events remains operator-readable; Link Trace remains explicitly forensic/diagnostic.
- TCP and Serial are current primary transports. TLS/redundancy/link-timeline expansion requires explicit product scope.

## Exception-free protocol hot paths

Expected/recoverable failures must not use exceptions as normal control flow inside frame/object decoding, poll/event processing, point normalization, command state advancement, transport receive loops, or high-frequency evidence handling.

Prefer `TryXxx`, a coherent typed Result/status record, compact enum/status, or nullable output only where failure detail is unnecessary.

Expected conditions include timeout/no data, malformed/truncated frame, unsupported group/variation, invalid qualifier/count, CRC/length rejection, missing timestamp, disconnect, DFC/busy/rejection, command feedback timeout, and bounded queue saturation.

Exceptions from `Socket`, serial drivers, filesystem, WPF, PDF/report/export or other infrastructure may still happen. Catch at the nearest meaningful adapter/application boundary and convert to structured status/diagnostics. Never use broad catch/retry loops as a substitute for explicit state transitions.

## Defensive parsing

Before indexing based on received metadata validate all link/transport/application lengths, offsets, counts, qualifier/range semantics, object sizes, numeric conversions, and timestamp bounds.

Malformed traffic must not crash the master session or UI and must not partially mutate authoritative point/event state. Preserve raw evidence sufficient for diagnosis.

## Bounded diagnostics

Transport/protocol hot paths may emit only compact structured diagnostic events/counters. No per-frame file logging, JSON serialization, stack-trace formatting, or synchronous WPF notification.

Diagnostic queues must be bounded and non-blocking for high-rate producers. Aggregate/deduplicate/rate-limit repeated failures. Preserve occurrence/drop counts and severity according to explicit overload policy.

Formatting and persistence belong to a background/lower-rate consumer. Diagnostic failure must never stall polling, commands, reconnect, Stop, or rendering.

## Point/event state ownership

Build decoded updates as validated candidates. Commit to Value Viewer/event/SOE state only after the complete object and provenance are valid enough for the intended view.

Do not let a partial parse overwrite last-known-good point state. If quality/timestamp is invalid, represent that explicitly rather than silently reusing an unrelated timestamp/value.

Value Viewer order/state should remain stable during live updates; do not rebuild or reorder the whole collection for each callback.

## Command lifecycle safety

A command is not successful merely because bytes were sent or a request received a transport response.

Preserve distinct evidence for prepared -> requested -> protocol accepted/rejected -> feedback observed/not observed -> final verdict.

Do not retry an operate automatically after ambiguous timeout unless the workflow has an explicitly reviewed bounded/idempotent retry contract. Unknown command outcome is safer than a duplicate operate.

## WPF responsiveness

No blocking TCP/serial I/O, heavy protocol parsing, report generation, file I/O, or long synchronization waits on the UI thread.

Batch/coalesce point, event, trace, and diagnostics updates. Use virtualization/recycling for large grids and bound retained live evidence. Never create one Dispatcher invocation/render per incoming DNP object/frame under sustained traffic.

UI status must reflect authoritative engine state rather than requested button state.

## Stop/reconnect lifecycle

Workers, sockets/serial ports, timers, subscriptions, cancellation sources, and callbacks need explicit owners. Stop must prevent new work, cancel/close transport as required to release blocking I/O, retire workers, then restore a coherent operator state.

Do not detach workers or use arbitrary sleeps to hide shutdown races. Reconnect loops must be bounded/cancellable and must not create overlapping sessions.

## Reporting integrity

Report preview/export consume the same authoritative evidence model. Distinguish not-executed, passed, warning, and failed. Do not fabricate missing evidence to make a report look complete.

Source timestamp vs captured-time fallback must remain visible where relevant. Raw trace evidence can support forensic review but should not replace operator meaning.

## Performance evidence

For hot-path/runtime changes measure where applicable: sustained poll/event throughput, allocations, queue depth/drop count, memory growth, UI latency, reconnect/Stop latency, and report generation time. Do not claim performance improvement without before/after evidence.

## Build and validation

Build from repository-relative paths; do not hardcode `C:\Git\...` assumptions in production scripts or new documentation.

A protocol bug fix should add a deterministic regression fixture when practical, including the exact malformed frame, timeout/state transition, qualifier/variation, timestamp, command, or lifecycle failure.

Definition of done as applicable:
BUILD + REGRESSION TEST + MALFORMED/FAILURE TEST + TRANSPORT LIFECYCLE + UI BATCHING/RESPONSIVENESS + REPORT EVIDENCE + AUTHORIZED DEVICE/SIMULATOR VALIDATION.

## Final rule

The native master engine owns protocol truth. UI and reports explain that truth; they never repair or invent it. Make expected failures explicit, keep queues/work bounded, and treat ambiguous command outcomes conservatively.
