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

## Resolver documentali Sprint 02B

Il Core rappresenta la risoluzione con stati tipizzati `FullPath`, `FileNameOnly` e `Unresolved`, provenance `DirectlyObserved`, `Derived` o `Unresolved` e una causa degradata non identificativa. Il contratto operativo `IDocumentContextResolver`, il registry per executable normalizzato, i timeout e gli adapter applicativi restano in `PcActivityTracker.Windows`; nessun resolver conosce SQLite o WinUI.

La risoluzione avviene nel consumer dopo la lettura dei metadata foreground, mai nel callback WinEvent. Un gate limita a uno il lavoro isolato: allo scadere del timeout il collector prosegue, mentre ulteriori tentativi non possono creare lavoro illimitato dietro a un adapter bloccato. I contatori registrano soltanto tentativi, precisione, timeout, errori e latenza aggregata. Un `FullPath` attraversa la valutazione `FilePath` prima di creare la `RawObservation`; `FileNameOnly` non viene trasformato in un percorso inventato. La migration v3 conserva precisione e provenance insieme al riferimento file.

Il primo adapter reale è Microsoft Word via Word Object Model/COM. Legge esclusivamente `ActiveWindow.Hwnd`, `ActiveDocument.FullName` e `ActiveDocument.Name`; verifica che la finestra Word attiva coincida con quella foreground. Non legge contenuto né usa il titolo finestra. Richiede la stessa sessione desktop e un livello di integrità compatibile con Word; COM/RPC indisponibile, documento assente, mismatch finestra, access denied o processo terminato convergono a un risultato degradato privacy-safe.

## Runtime Sprint 02A

L'orchestrazione resta nel Core perché è ancora una singola state machine coesa, senza giustificare un progetto `Application`. `TrackingStateMachine` riduce segnali sintetici ordinati dal timestamp monotonic e produce effetti di persistenza; `TrackingCoordinator` materializza tali effetti esclusivamente attraverso `IObservationStore`. Gli stati operativi sono `Stopped`, `Running`, `Paused` e `Private`; idle, lock e suspend sono condizioni sovrapposte che chiudono l'attività identificativa e aprono gap anonimi. Ogni uscita da una condizione di soppressione richiede una nuova snapshot foreground.

Il collector Windows usa `SetWinEventHook` come sorgente primaria, una channel bounded non bloccante nel callback e `GetForegroundWindow` per la riconciliazione. Un worker separato risolve PID, nome e percorso processo; `GetLastInputInfo` genera soltanto transizioni idle con soglia predefinita di cinque minuti. I messaggi WTS, power ed end-session sono tradotti in segnali neutrali dal composition root. SQLite non è visibile al progetto Windows.

Le esclusioni vengono caricate prima dell'avvio del collector e valutate prima della costruzione di `RawObservation`. Un match non crea né observation né gap semantico; il periodo resta intenzionalmente non identificato. Private produce soltanto `ActivityGap(Private)`. Il runtime non introduce checkpoint o migration v2: una observation accettata è subito persistita, mentre un crash può perdere soltanto la coda bounded e l'intervallo ancora aperto. Al riavvio si effettua una riconciliazione conservativa e non si inventa la durata perduta.

`RuntimeMetrics` e `LocalResourceSnapshot` forniscono contatori locali non identificativi (segnali, drop, riconciliazioni, scritture, CPU cumulativa, working set e dimensione DB). Nessuna metrica viene trasmessa in rete.

### Correzioni runtime post-review

La sorgente espone ora un unico `IAsyncEnumerable<TrackingSignal>` consumato sequenzialmente dal coordinator: non esistono subscriber `async void` nel data path. Foreground, idle, lifecycle, perdita segnali e reconciliation condividono una sequenza producer-side e una capacità bounded; reconciliation, signal-loss e un controllo lifecycle/idle in overflow hanno ciascuno un solo slot coalescente aggiuntivo. Il callback foreground acquisisce soltanto HWND, UTC, monotonic, generation e sequence. Se la coda è piena incrementa il drop counter, imposta atomicamente `SignalLossDetected` e ritorna; la risoluzione processo avviene durante la lettura awaitable.

La reconciliation è una barriera ordinata: incrementa la generation sotto lo stesso lock minimale usato dal callback, cattura HWND e timestamp e viene consumata secondo sequence. Gli eventi foreground antecedenti vengono invalidati; lifecycle antecedenti conservano il proprio ordine. La priorità effettiva centralizzata è `Suspended > Locked/Disconnected > Idle > Paused > Private`. Entrare in Private da Paused è consentito, ma ExitPrivate ripristina Paused.

`ITrackingBatchStore` è la porta Core orientata al caso d'uso. Data persiste tutti gli effetti di un segnale in una singola transazione SQLite. Il coordinator conserva uno snapshot della macchina: se il batch fallisce, ripristina lo snapshot, passa a `Faulted`, arresta la sorgente e non consuma altri segnali. Un nuovo Start esplicito effettua restart e reconciliation. Le exclusion `WindowTitle` e `FilePath` sono disabilitate in 02A: il collector non acquisisce titoli e i resolver documento restano in 02B.

### Durata monotonic e schema v2

Activity e gap aperti conservano timestamp monotonic e frequenza del producer. Alla chiusura, `Elapsed` è derivato esclusivamente dalla differenza monotonic; `start_utc` e `end_utc` restano coordinate della timeline civile e possono quindi divergere dalla durata in presenza di cambi del wall clock. La migration v2, additiva, aggiunge `elapsed_ticks` ed `elapsed_monotonic` a interval e gap senza modificare v1. Le righe legacy mantengono `NULL` e usano esplicitamente la durata civile come fallback non-monotonic; la retention che tronca una riga invalida il dato elapsed perché non esiste una mappatura corretta del cutoff civile sul clock monotonic originale.

I comandi utente attraversano una control lane bounded e awaitable: vengono confermati dalla scrittura in coda e non sono scartati. Foreground e segnali OS usano un gate di pubblicazione che rende atomici sequence ed enqueue; il WinEvent callback usa soltanto `Wait(0)` e, se il gate non è immediatamente disponibile, converge su signal-loss. Idle, lock/disconnect e suspend mantengono inoltre uno snapshot atomico dell'ultimo stato noto: dopo saturazione il consumer applica `ConditionsChanged` e converge al valore corrente anziché perdere una metà della transizione.

`RunAsync` rimane vivo mentre la macchina è `Stopped`: segnali OS pre-Start vengono ignorati, quindi Start richiede una reconciliation che include sia foreground sia condizioni OS correnti. La terminazione avviene soltanto per Stop processato, Faulted o cancellation/dispose. `WM_QUERYENDSESSION` non avvia cleanup; solo `WM_ENDSESSION(TRUE)` pubblica Stop. `WM_SETTINGCHANGE` viene confrontato con Id/offset effettivi tramite `TimeZoneChangeDetector`, mentre `WM_TIMECHANGE` resta dedicato al cambiamento dell'ora di sistema.

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
