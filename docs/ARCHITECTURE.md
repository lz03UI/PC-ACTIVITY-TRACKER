# Architecture

## Context

PC Activity Tracker is a modular monolith: one Windows desktop product, one local SQLite database, and no required server. Process boundaries may be introduced for browser extensions or reliability only when a concrete feature requires them.

## Dependency model

```text
Desktop (WinUI composition root) ---> Windows Infrastructure ---> Core
            |                              |
            +--> Data ---------------------+
            +--> Reporting ---------------> Core
            +--> Browser Integration -----> Core

Data -------------------------------> Core
```

Dependencies point toward platform-neutral policy. `Core` references no other production project. `Reporting` and `BrowserIntegration` are platform-neutral. `Data` owns SQLite implementation details behind Core-facing ports. `Windows` owns Win32/Windows App SDK integration. `Desktop` wires dependencies and renders state; it contains no domain rules.

## Projects

### Core

Domain values, deterministic policies, use-case-neutral interfaces, and time abstractions. It must not depend on SQLite, WinUI, Win32, browser SDKs, networking, or optional AI providers.

### Data

SQLite connection, migrations, repositories, transactions, and retention implementation. It remains cross-platform so schema and repository behavior can be tested on Linux using temporary databases. A schema will be designed in a later sprint.

### Reporting

Read models, aggregation, and report composition over Core contracts. Reports must use explicit time zones and avoid coupling to WinUI.

### Browser Integration

Browser-neutral messages, validation, URL minimization, and ingestion contracts. Browser-specific extensions/native messaging hosts will be separate adapters when selected. Raw query strings and fragments are excluded by default.

### Windows Infrastructure

Foreground-window, process, idle/session, power, and supported-document adapters. This project targets Windows and translates OS signals into neutral observations. It must not classify or persist directly.

### Desktop

WinUI 3 views, view models, user interaction, lifecycle, and the dependency-composition root. It is the only executable in Sprint 00 and is initially unpackaged to keep the development loop simple.

## Data flow (target, not yet implemented)

1. Windows and opt-in browser adapters emit minimized observations.
2. An application orchestration layer validates, normalizes, and batches them.
3. Data repositories transactionally append observations to SQLite.
4. Ordered deterministic rules produce separately stored classifications with provenance.
5. Reporting queries create read models for WinUI.
6. Manual corrections and exclusions are explicit domain actions with auditable provenance.

Core will initially host orchestration contracts. If use cases become substantial, an `Application` project may be extracted by an architectural decision; an empty layer is avoided in Sprint 00.

## Storage direction

SQLite is authoritative. WAL mode, bounded write batches, migration transactions, indexes, retention, backup/export, and corruption recovery must be designed before production persistence. UI caches and future AI suggestions are derived state, never the only copy. No production schema is claimed in Sprint 00.

## Privacy boundaries

- Collection adapters minimize before crossing into Core.
- Exclusion checks occur before persistence whenever feasible.
- Browser messages are authenticated to the local native host and sanitized.
- Network access is absent from the core runtime path. Future outbound integrations require explicit consent and an ADR.
- Logs must never contain raw activity metadata by default.

## Resource strategy

Use OS event hooks rather than rapid polling; bounded channels and backpressure; batched SQLite transactions; cancellation-aware background work; and measured, configurable retention. Resource budgets will be established through Windows profiling before the collector milestone is accepted.

## Test strategy

- Unit tests validate deterministic policies and values.
- Data integration tests use temporary SQLite files and run cross-platform.
- Architecture tests enforce forbidden namespace/project dependencies.
- Contract tests validate browser message minimization.
- Windows integration tests exercise OS adapters on a Windows runner.
- UI smoke/accessibility and packaged-install tests require Windows and are reported separately.

`PcActivityTracker.CrossPlatform.sln` is the Codex/Linux validation surface. `PcActivityTracker.sln` is authoritative and is built on Windows CI.

## Failure handling

Collectors must tolerate inaccessible processes and malformed external data. Failures are contained at adapters, queues are bounded, persistence is transactional, and cancellation is honored. Do not use broad exception swallowing; surface privacy-safe diagnostics and degraded state to the user.

