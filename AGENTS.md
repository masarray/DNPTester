# AGENTS.md — DNP3 Interoperability Tester Engineering Contract

This is the repository-level operating contract for AI/code agents and maintainers. The application-specific rules in `Dnp3MasterTester/AGENTS.md` are also authoritative for files under that directory and may impose stricter requirements.

DNPTester is commissioning/test software. Protocol correctness, evidence integrity, command safety, failure containment, responsiveness, and reproducible release behavior are product requirements.

## Prime directive

Do not begin with a disposable, intentionally naive, or demo-only implementation when the production architecture is knowable. Prefer the smallest coherent production-quality change that preserves existing evidence and operator workflows.

Priority order:
1. wire/protocol correctness and command safety;
2. evidence/timestamp/data integrity;
3. failure containment and recoverability;
4. regression compatibility;
5. responsive bounded runtime behavior;
6. maintainability;
7. convenience.

## Mandatory workflow

For non-trivial changes:

RECONNAISSANCE -> REPRODUCE/BASELINE -> ROOT CAUSE -> INVARIANTS -> ARCHITECTURE IMPACT -> IMPLEMENT -> REGRESSION TEST -> FAILURE TEST -> PERFORMANCE/LIFECYCLE CHECK -> BUILD/CI -> DEVICE/SIMULATOR VALIDATION AS APPLICABLE.

Before editing, locate the authoritative protocol/master state, point cache, command lifecycle, timestamp source, evidence/report pipeline, and UI consumer. Do not create parallel truth merely to make a screen update.

If three successive patches in the same subsystem still treat symptoms, STOP. Patch four requires a fresh root-cause/state-ownership/architecture audit.

## Repository boundaries

- The native C# DNP3 master stack is the authoritative active protocol engine.
- UI/reporting/installer/docs must not invent protocol state or device evidence.
- `Value Viewer` represents latest-known point state, not an append-only event dump.
- SCADA Events/SOE preserve source timestamp quality and provenance.
- Command Testing must preserve explicit prepared/requested/accepted/feedback/final-verdict lifecycle semantics.
- Report Preview and export must use the same authoritative evidence model/render path.
- Installer/release changes must preserve portable user operation and legal/third-party notices.

## Result-oriented failure architecture

Expected or recoverable outcomes must not use exceptions as normal control flow in protocol decode, poll/task processing, point normalization, command lifecycle handling, serial/TCP loops, or high-frequency evidence processing.

Prefer `TryXxx`, one coherent typed Result/status model per subsystem, compact enums/records, or nullable results only when failure detail is unnecessary.

Normal runtime conditions such as timeout, disconnect, malformed/truncated response, unsupported object/variation, invalid qualifier, missing source timestamp, command rejection, feedback timeout, queue saturation, or unavailable optional metadata must produce explicit status/evidence rather than repeated throw/catch behavior.

Exceptions from sockets, serial drivers, filesystem, PDF/reporting, WPF, or other infrastructure may still occur. Catch them at the nearest meaningful boundary and convert them into structured application failure/diagnostic state. Do not silently swallow them and do not scatter broad catch-all blocks through inner protocol loops.

## Bounded asynchronous diagnostics

High-rate protocol/transport paths must not perform expensive string formatting, synchronous file logging, stack-trace generation, JSON serialization, or one UI update per event.

Emit compact structured diagnostic events/counters into a bounded non-blocking pipeline. Aggregate/deduplicate/rate-limit repeated failures and retain occurrence/drop counters. Queue saturation must have an explicit policy.

Formatting, persistence, and UI presentation belong on lower-rate/background consumers. Diagnostic failure must never block transport processing, polling, commands, shutdown, or UI responsiveness.

## Zero UI blocking and backpressure

Do not perform blocking serial/TCP I/O, large report generation, heavy parsing, file I/O, or long protocol operations on the WPF UI thread.

Polling/event acquisition rate and rendering rate are independent. Batch/coalesce display updates and virtualize/bound large evidence grids. Do not create unbounded ObservableCollections or one render per DNP3 frame/object.

## Transactional state changes

Connection settings, mapping/profile state, command candidates, report configuration, and other multi-field state changes must be validated before activation where practical.

Use candidate -> validate -> commit. On failure retain last-known-good state or transition explicitly to a safe stopped/faulted state. Never leave partially applied command/session/mapping state.

## Command safety

Do not infer command success from transport send alone. Preserve command mode/operate response, protocol status, feedback matching, timeout, and final verdict as separate evidence.

Never auto-repeat an operate solely because a timeout/exception occurred unless an explicitly approved bounded retry policy proves the operation is safe and idempotent for that workflow.

## Large data and evidence

Keep live point state bounded and keyed. Evidence retention must have explicit bounds/export policy. Reports must not fabricate missing evidence or convert `not executed` into `failed`.

Raw protocol evidence remains available for forensic use, but operator-facing views should remain meaning-first.

## Performance/resource discipline

For relevant changes measure where practical:
- poll/event throughput;
- UI update latency;
- queue high-water marks/drop counts;
- allocation rate and memory growth;
- reconnect/stop latency;
- report generation time;
- long-session resource stability.

Do not add workers, caches, queues, pools, or retries without a demonstrated need and explicit lifecycle owner.

## Release discipline

A user-facing release must be reproducible and must preserve version, installer/package identity, license/notices, and checksum/provenance expectations. Do not publish from an unvalidated working tree or replace a requested binary artifact with an unrelated fallback.

## Definition of done

A change is not complete because it compiles. Validate as applicable:
BUILD + STATIC ANALYSIS + UNIT/DETERMINISTIC TESTS + REGRESSION TEST + MALFORMED/FAILURE PATH + TRANSPORT/STOP LIFECYCLE + PERFORMANCE CHECK + REPORT/PACKAGE CHECK + SIMULATOR/AUTHORIZED DEVICE VALIDATION.

Never claim a validation step was run when it was not.

## Completion report

Report: changed, demonstrated root cause, architecture/state owner, invariants preserved, Result/failure contract, regression protection, performance/lifecycle impact, exact validation, and genuine remaining limitations.

## Final rule

Treat every connection, point, timestamp, command, and report row as engineering evidence. Fix root causes, keep runtime work bounded, make failures explicit, and never let UI convenience redefine protocol truth.
