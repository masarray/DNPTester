# Serial Hardening Checklist

## Goal

Use the same analyzer core for `TCP` and `Serial`, with transport differences isolated to channel creation, serial port configuration, retry/open-state handling, and observability.

## Principle

- one analyzer
- one polling/profile model
- one SOE/event/audit model
- one command tracker
- one verdict engine
- no duplicated TCP-vs-Serial command logic

## Transport creation

- verify `MasterChannel.CreateSerialChannel(...)` is the only serial master channel creation path
- ensure serial settings map directly from user config:
  - baud rate
  - data bits
  - stop bits
  - parity
  - flow control
- ensure serial open retry delay is explicit and user-configurable

## Validation

- reject empty COM port name
- reject baud rate `0`
- reject invalid retry/open delay
- show active serial summary in operator-visible surfaces

## Observability

- show clear serial port state changes in `Session Status`
- route `PortState` transitions into `Link Trace`
- make these conditions explicit:
  - port opened
  - port unavailable
  - retrying open
  - port closed
  - channel enabled but port not available

## Connect / disconnect behavior

- verify connect does not freeze UI when port is missing
- verify disconnect is safe during:
  - idle
  - active polling
  - delayed command feedback scenarios
- verify reconnect after disconnect works without stale state

## Failure scenarios

- COM port does not exist
- COM port already in use
- USB-to-serial adapter unplugged while running
- adapter returns later and port is reopened
- bad parity / data bits / stop bits mismatch against slave

## Command lifecycle on Serial

- validate the same command tracker on Serial with no special-case verdict logic
- test at least:
  - accepted + matching feedback
  - accepted + no feedback
  - accepted + mismatch
  - rejected
  - delayed feedback

## Polling / event acquisition

- verify event poll, integrity poll, and static refresh behave the same as TCP from analyzer perspective
- confirm `SourceReason`, SOE timestamps, and SCADA event generation remain transport-agnostic

## Exit criteria

- Serial can connect/disconnect cleanly
- Serial errors are visible and operator-readable
- command lifecycle evidence matches TCP semantics
- no duplicate analyzer logic is introduced for Serial
