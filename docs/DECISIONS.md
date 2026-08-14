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

> Le prime due decisioni aperte sopra sono risolte dagli ADR-0008, ADR-0009 e ADR-0010. La cronologia è mantenuta senza riscrivere le voci precedenti.

## ADR-0008: Semantica temporale UTC e intervalli semiaperti

- **Stato:** Accepted
- **Decisione:** Persistiamo ogni istante come `DateTimeOffset` a offset zero e ogni intervallo come `[start, end)`, con `end >= start`. Conserviamo accanto all'osservazione l'identificativo del fuso e l'offset effettivamente osservato. Wall clock e timestamp monotonic sono tipi distinti; `TimeProvider` è il confine testabile per l'ora corrente. Lock, sleep, cambi del clock/fuso e restart chiudono un intervallo con una causa esplicita.
- **Perché:** UTC rende ordinamento e durata non ambigui; zona più offset ricostruiscono correttamente la vista locale anche attraverso DST o cambio fuso. Il clock monotonic protegge il calcolo futuro da salti dell'orologio civile.
- **Conseguenze:** I collector futuri non devono calcolare durate da due letture wall-clock quando è disponibile il clock monotonic. Gli intervalli adiacenti non si sovrappongono e non esistono durate negative. Il monotonic non è convertibile in un istante UTC e non viene usato come tale.

## ADR-0009: Evidenza grezza immutabile e classificazione separata

- **Stato:** Accepted
- **Decisione:** `RawObservation` è un valore immutabile append-only; in SQLite un trigger ne vieta gli update. Correzioni, inferenze e suggerimenti sono nuove righe `classifications`, con target, timestamp, motivazione, rule id opzionale e provenance `manual`, `deterministic rule`, `system/inferred` o `AI suggestion`. Un suggerimento AI non è autorevole.
- **Perché:** Separare evidenza e interpretazione rende correzioni verificabili, impedisce riclassificazioni silenziose e mantiene il core offline e deterministico.
- **Conseguenze:** Più classificazioni possono riferirsi allo stesso target e una vista futura dovrà scegliere quella efficace secondo policy esplicita. La cancellazione retention dell'evidenza non equivale a una sua modifica. I collector devono applicare esclusioni e minimizzazione prima di costruire/persistire l'osservazione; incognito e query/fragment non sono accettati dal modello browser.

## ADR-0010: SQLite con migrazioni SQL esplicite

- **Stato:** Accepted
- **Decisione:** Usiamo `Microsoft.Data.Sqlite` e una sequenza ordinata di script SQL numerati, ciascuno applicato insieme all'aggiornamento di `schema_info` in una singola transazione. Non introduciamo EF Core o un migration framework. Ogni connessione abilita foreign key, WAL, synchronous NORMAL e busy timeout.
- **Perché:** SQL esplicito ha overhead e superficie dipendenze minimi, rende schema e vincoli revisionabili e consente integration test reali su Linux.
- **Conseguenze:** Le migrazioni devono essere additive, deterministiche, senza buchi e testate sia nel percorso da una versione precedente sia nel rollback. Le modifiche future aggiungono una nuova migrazione invece di alterare quella pubblicata. Backup/ripristino, recovery da corruzione, compattazione e benchmark dei batch saranno decisi prima dell'uso di produzione dei collector.
