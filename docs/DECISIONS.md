# Architecture decision log

Decisions are append-only. Supersede an entry rather than silently rewriting its outcome.

## ADR-0001: Local-first modular monolith

- **Status:** Accepted
- **Decision:** Ship one Windows desktop application with SQLite as its local primary source of truth and no mandatory backend.
- **Why:** It satisfies offline operation, privacy, deployment simplicity, and low resource usage.
- **Consequences:** Sync is absent from V1. Schema migrations, backup, retention, and recovery are first-class desktop concerns.

## ADR-0002: .NET 8 and WinUI 3

- **Status:** Accepted
- **Decision:** Use C# on .NET 8 and Windows App SDK/WinUI 3 for the desktop UI.
- **Why:** This is the mandated native Windows technology and .NET 8 is an LTS baseline.
- **Consequences:** The desktop and Windows infrastructure projects require Windows to execute; neutral libraries remain cross-platform.

## ADR-0003: Project boundaries and dependency direction

- **Status:** Accepted
- **Decision:** Use Core, Data, Reporting, BrowserIntegration, Windows, and Desktop projects. Core is independent; Data, Reporting, and BrowserIntegration remain cross-platform; Windows and Desktop contain platform coupling.
- **Why:** These boundaries isolate volatile integrations and maximize testing in Codex Cloud without inventing deployable services.
- **Consequences:** Desktop is the composition root. An Application project is deferred until orchestration complexity justifies it.

## ADR-0004: Deterministic rules before optional AI

- **Status:** Accepted
- **Decision:** Classification begins with ordered, explainable local rules. AI is not present in the foundation and may later be an optional adapter only.
- **Why:** Determinism improves privacy, predictability, offline use, testability, and correction.
- **Consequences:** Classification output carries provenance. No domain behavior may require an AI or network provider.

## ADR-0005: Prohibited capture

- **Status:** Accepted
- **Decision:** Do not implement keylogging or continuous screenshots.
- **Why:** These techniques are disproportionate to workday reconstruction and violate product privacy boundaries.
- **Consequences:** Review collection changes for content creep. A materially different capture mechanism requires explicit product and privacy review.

## ADR-0006: Two solution validation surfaces

- **Status:** Accepted
- **Decision:** Maintain an authoritative complete solution and a cross-platform solution excluding the Windows-only projects.
- **Why:** Linux Codex environments can validate most logic while Windows CI validates WinUI and OS adapters.
- **Consequences:** Adding a neutral project requires adding it to both solutions. Architecture tests guard against Windows dependencies leaking inward.

## ADR-0007: Central package and compiler policy

- **Status:** Accepted
- **Decision:** Use `Directory.Build.props`, Central Package Management, nullable references, deterministic builds, and warnings as errors.
- **Why:** A single policy reduces drift and makes automated contributions reviewable.
- **Consequences:** Package versions change centrally and restore lock files will be committed after SDK bootstrap.

## Open decisions

- Observation/session model and time semantics.
- SQLite schema, migration tool, encryption expectations, and backup format.
- Supported browsers and native-messaging protocol.
- Dependency injection, logging, and configuration libraries.
- Packaging, signing, update, and release channels.

