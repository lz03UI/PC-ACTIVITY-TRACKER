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

SQLite connection, migrations, repositories, transactions, and retention implementation. It remains cross-platform so schema and repository behavior can be tested on Linux using temporary databases. Da Sprint 01 espone adapter orientati ai casi d'uso per osservazioni, intervalli, classificazioni, privacy e tassonomia; il Core contiene solo le relative porte e non conosce SQLite.

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

## Fondazione Sprint 01

Il dominio rappresenta istanti UTC, contesto locale (identificativo del fuso e offset osservato), intervalli semiaperti, stato active/idle/locked/suspended/paused/private e discontinuità di clock. Il timestamp monotonic è un valore distinto dal wall clock: i collector futuri lo useranno per misurare il tempo trascorso, senza persisterlo come istante civile.

Le `RawObservation` sono append-only e contengono solo contesti minimizzati di applicazione, file o browser. Lo stato private/incognito non è ammesso nelle osservazioni né negli intervalli identificativi: deve essere filtrato prima della creazione del dominio e la persistence applica una seconda difesa. Un periodo privato può essere conservato soltanto come `ActivityGap`, composto da intervallo e stato e privo di riferimenti ad applicazione, processo, file, browser o osservazione. Le classificazioni sono record separati e conservano target, provenance, regola opzionale, motivazione e timestamp. Le esclusioni sono leggibili prima dell'ingestione affinché gli adapter possano scartare dati sensibili prima della persistenza. Contenuti digitati, clipboard, form data e screenshot non hanno alcuna rappresentazione persistibile.

`PcActivityTracker.Data` usa un database SQLite locale con foreign key per connessione, WAL, synchronous NORMAL, busy timeout di 5 secondi e migrazioni SQL esplicite. Lo schema v1 comprende:

- `schema_info` per la versione;
- `observations`, `activity_intervals` e `activity_gaps`, indicizzati temporalmente;
- `classifications`, con indici su target, progetto, commessa e categoria;
- `projects`, `jobs`, `categories` ed `exclusions`.

La retention ha semantica temporale esatta: elimina intervalli e gap con `end <= cutoff`, tronca a `cutoff` quelli che lo attraversano e lascia invariati quelli successivi. Le osservazioni antecedenti vengono poi eliminate; un intervallo sopravvissuto viene scollegato dall'evidenza rimossa tramite `ON DELETE SET NULL`, senza perdere attività successiva al cutoff. I trigger eliminano le classificazioni dei target effettivamente cancellati. Il trigger `observations_immutable` impedisce l'aggiornamento delle osservazioni grezze; la cancellazione resta consentita per retention e diritto dell'utente alla rimozione.

Una classificazione associa direttamente un progetto oppure una commessa, mai entrambi. Quando è presente la commessa, il progetto è derivato dalla relazione corrente `jobs.project_id`; spostare una commessa aggiorna quindi coerentemente l'appartenenza delle classificazioni che la usano. Sono rinviati a sprint successivi batching misurato, backup/ripristino, motore di regole, sessionizzazione derivata, ricerca full-text ed eventuale cifratura at-rest: nessuno di questi rinvii introduce rete o cloud.
