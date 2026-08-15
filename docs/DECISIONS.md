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

## ADR-0011: Difese privacy, retention esatta e progetto derivato dalla commessa

- **Stato:** Accepted
- **Decisione privacy:** Private/incognito viene scartato prima di creare una `RawObservation`. Dominio, adapter SQLite e vincolo dello schema rifiutano inoltre ogni osservazione privata. La sola rappresentazione persistibile del periodo è un `ActivityGap` contenente esclusivamente intervallo e stato; un `ActivityInterval` identificativo non può essere private.
- **Decisione retention:** `DeleteActivityBeforeAsync(cutoff)` opera in una transazione, elimina intervalli/gap interamente antecedenti (`end <= cutoff`), tronca a cutoff quelli attraversanti e conserva quelli successivi. Le osservazioni antecedenti vengono eliminate dopo il trattamento degli intervalli; gli intervalli sopravvissuti perdono il riferimento all'evidenza tramite `ON DELETE SET NULL`. Le classificazioni di observation/interval eliminati vengono cancellate dai trigger.
- **Decisione tassonomia:** Una classificazione contiene `project_id` oppure `job_id`, mai entrambi. Con `job_id`, il progetto è derivato dalla relazione corrente della commessa. Lo spostamento futuro di una commessa tra progetti si riflette quindi sulle classificazioni senza coppie duplicate potenzialmente contraddittorie. Dominio e database rifiutano gli identificativi vuoti della tassonomia.
- **Perché:** Le difese in profondità impediscono persistenza accidentale di contenuti privati; la retention esatta non perde tempo successivo al cutoff; una sola fonte per la relazione job/progetto elimina stati incoerenti e definisce il comportamento dei successivi spostamenti.
- **Conseguenze:** Gli intervalli conservati dopo la rimozione dell'evidenza possono avere `ObservationId` nullo. I consumer devono trattarli come tempo valido senza metadati identificativi. Poiché lo schema Sprint 01 non è ancora confluito in `main`, queste correzioni sono incorporate nella migration v1 prima della sua pubblicazione anziché introdurre una migration correttiva v2.

## ADR-0012: Collector Windows event-driven e pipeline bounded

- **Stato:** Accepted
- **Decisione:** `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` alimenta una channel bounded tramite un callback minimale. La risoluzione dei metadata, le esclusioni, la state machine e SQLite restano fuori dal callback. Overflow incrementa una metrica non sensibile e forza una riconciliazione. `GetForegroundWindow` viene letto a start e dopo idle, unlock, resume, pause/private e perdita di segnali. Idle usa `GetLastInputInfo` soltanto per rilevare la soglia, mentre l'ordinamento runtime usa il timestamp monotonic.
- **Perché:** Una sorgente event-driven riduce polling e wakeup; il confine bounded impedisce crescita illimitata e separa i callback Win32 dalla latenza di persistenza.
- **Conseguenze:** Gli adapter Win32 rimangono in `PcActivityTracker.Windows`; il Core è testabile con segnali sintetici. I test interattivi e il profiling esteso richiedono un PC Windows fisico in Sprint 02B.

## ADR-0013: Nessun checkpoint runtime e periodi esclusi non materializzati

- **Stato:** Accepted
- **Decisione:** Sprint 02A non aggiunge checkpoint né migration v2. Le observation accettate sono persistite immediatamente; interval e gap sono persistiti alla chiusura. Dopo crash/restart il foreground viene riconciliato senza ricostruire retroattivamente la durata ignota. Un match di esclusione non crea `RawObservation`, interval o gap `Private`/`Excluded`: lascia intenzionalmente un periodo non identificato.
- **Perché:** Un checkpoint con metadata introdurrebbe scritture e rischio privacy sproporzionati; attribuire tempo dopo un crash non sarebbe attendibile. Riutilizzare `Private` per le esclusioni ne altererebbe la semantica, mentre una nuova categoria `Excluded` non è necessaria per 02A.
- **Conseguenze:** Un crash può perdere l'intervallo aperto e gli elementi non ancora consumati dalla coda. Il rischio viene dichiarato e sarà misurato nell'hardening 02B; qualunque recovery persistente futura richiederà migration e ADR dedicati.

## ADR-0014: Chiarimento di ADR-0012 — ingestion sequenziale, batch atomico e fault conservativo

- **Stato:** Accepted; chiarisce e supersede la forma implementativa iniziale di ADR-0012 senza cambiarne l'obiettivo event-driven/bounded.
- **Decisione pipeline:** Una sola lettura awaitable di `IAsyncEnumerable<TrackingSignal>` collega la channel bounded al coordinator. Non si usano eventi con handler asincroni nel data path. Ogni segnale conserva timestamp UTC, monotonic e sequence acquisiti dal producer. Overflow coalesce un solo `SignalLossDetected`; non invoca il coordinator dal callback. Reconciliation usa generation e sequence come barriera e attraversa lo stesso ordinamento logico.
- **Decisione condizioni:** La priorità unica è `Suspended > Locked/Disconnected > Idle > Paused > Private`. Le flag OS vengono aggiornate anche durante Pause/Private. Private avviato da Paused ritorna a Paused. Solo uno stato effettivamente osservabile richiede reconciliation foreground.
- **Decisione persistence:** Gli effetti observation/interval/gap prodotti da un segnale sono un `TrackingPersistenceBatch` atomico. SQLite applica il batch in una transazione. Un errore ripristina lo snapshot pre-segnale, porta il runtime in `Faulted`, arresta la raccolta e richiede Start esplicito; nessun intervallo successivo può riferire observation non confermate.
- **Decisione privacy:** Sprint 02A non acquisisce window title. Le exclusion WindowTitle e FilePath restano configurabili nel dominio ma inattive nel runtime fino ai resolver/eventi attendibili di 02B. Nessun titolo è loggato o persistito.
- **Conseguenze:** La capacità massima comprende la channel configurata e tre soli slot coalescenti per signal-loss, reconciliation e un controllo lifecycle/idle in overflow. Non è aggiunta alcuna tabella e migration v1 resta invariata. Session disconnect/reconnect, clock e time-zone sono mappati in segnali neutrali. Il comportamento interattivo Win32 resta da validare su Windows fisico.

## ADR-0015: Durata monotonic persistita e control lane convergente

- **Stato:** Accepted; integra ADR-0008 e aggiorna la conseguenza schema di ADR-0014.
- **Decisione temporale:** `ActivityInterval.Elapsed` e `ActivityGap.Elapsed` sono la durata autorevole e, per il runtime, derivano da `MonotonicTimestamp.Elapsed`. Gli estremi UTC restano la collocazione civile osservata e non vengono alterati per simulare la durata. Poiché i due concetti non sono rappresentabili correttamente con i soli estremi UTC, la migration v2 aggiunge `elapsed_ticks` nullable ed `elapsed_monotonic`. Le righe v1 sono legacy con elapsed nullo; il dominio espone il fallback civile marcato non-monotonic. Un truncation di retention azzera il marker e rende elapsed nullo invece di inventare una trasformazione monotonic/civile.
- **Decisione ordering:** Sequence e pubblicazione sono serializzate. I comandi utente usano una control lane bounded awaitable con garanzia di consegna. Il callback foreground non attende il gate: fallisce immediatamente verso `SignalLossDetected`. Le condizioni OS mantengono uno snapshot convergente di idle, locked/disconnected e suspended, consumato come `ConditionsChanged` dopo saturazione.
- **Decisione lifecycle:** Stopped non termina il consumer; soltanto Stop processato, Faulted o cancellation lo terminano. `WM_QUERYENDSESSION` risponde senza cleanup, `WM_ENDSESSION(TRUE)` ferma il runtime e una cancellazione continua il tracking. `WM_SETTINGCHANGE` emette TimeZoneChanged solo dopo una variazione effettiva di Id/offset.
- **Conseguenze:** Lo schema corrente è v2 e v1 resta immutata. Reporting futuro deve aggregare `Elapsed`, non `TimeRange.Duration`, quando `IsElapsedMonotonic` è vero. La control lane ha capacità fissa e introduce attesa esplicita nella UI anziché perdita silenziosa. La CI non richiede desktop interattivo; la validazione reale dei messaggi Windows resta manuale.

## ADR-0016: Resolver documentali espliciti, isolati e con provenance persistita

- **Stato:** Accepted per il primo incremento Sprint 02B.
- **Decisione:** Il modello neutralizzato distingue `FullPath`, `FileNameOnly` e `Unresolved` senza null o sentinel, conserva provenance e failure category, mentre registry, timeout e resolver applicativi vivono nel progetto Windows. Il dispatcher seleziona soltanto executable registrati, esegue fuori dal callback WinEvent e usa un gate singolo con timeout breve. Lo schema v3 additivo persiste precisione e provenance per i soli contesti file. Le exclusion `FilePath` sono applicate esclusivamente a `FullPath` prima della costruzione della observation.
- **Primo resolver:** Microsoft Word usa il Word Object Model via COM/Running Object Table e convalida `ActiveWindow.Hwnd`. Legge soltanto `FullName` e, se non è un percorso Windows completo, `Name`. Non usa window title. Sono state considerate UI Automation/accessibilità (più adatta a un fallback filename ma dipendente dall'albero UI), parsing del titolo (ambiguo e privacy-sensitive) e add-in Office (più robusto per eventi ma invasivo e non disponibile senza deployment); COM è stato scelto per questo incremento perché è l'API applicativa documentata più diretta e non richiede un add-in.
- **Affidabilità e privilegi:** Il risultato è `FullPath` quando Word espone un `FullName` assoluto, `FileNameOnly` quando espone soltanto un nome affidabile, altrimenti `Unresolved`. Servono stessa sessione desktop, Word registrato nella ROT e livelli di integrità compatibili; documento assente, finestra non corrispondente, RPC/COM unavailable, access denied, processo terminato, timeout ed eccezione hanno esiti distinti ma senza metadata nei log/contatori.
- **Conseguenze:** Un adapter COM che ignora cancellation può continuare in background dopo il timeout, ma il gate impedisce fan-out e il collector globale continua. Il profiling e i test con Word reale restano Windows-only. Almeno una seconda famiglia deve essere validata prima di chiudere Sprint 02B.


## ADR-0017: Isolamento per resolver e secondo adapter Excel conservativo

- **Stato:** Accepted per il secondo incremento Sprint 02B; corregge il gate globale descritto da ADR-0016.
- **Decisione isolamento:** Il registry assegna un `SemaphoreSlim(1,1)` a ciascuna istanza resolver registrata. Una chiamata acquisisce il gate prima di creare il work item: richieste accodate allo stesso resolver non producono task illimitati, mentre resolver differenti procedono indipendentemente. Timeout e cancellation includono l'attesa del gate e restano individuali. Un adapter sincrono bloccato può occupare soltanto il proprio gate.
- **Valutazione Tier 1:** AutoCAD (`MdiActiveDocument`/`Database.Filename`) e Revit (`ActiveUIDocument`/`Document.PathName`) offrono API ufficiali robuste soprattutto dentro add-in; l'associazione affidabile alla specifica finestra foreground da un processo esterno richiederebbe deployment/in-process state. Inventor espone `ActiveDocument.FullFileName` via Automation/COM, ma gestione multi-istanza e HWND è meno verificabile. Acrobat espone automazione interapplication limitata e non offre un percorso-documento/foreground affidabile senza integrazione più invasiva. Excel espone `Application.Hwnd`, `ActiveWorkbook.FullName` e `Name` via Object Model/COM, consentendo un controllo conservativo e una facade fake-testable.
- **Secondo resolver:** Excel viene scelto per stabilità e testabilità, nonostante il minor peso della preferenza di dominio rispetto ad Autodesk. L'adapter verifica che HWND appartenga al PID osservato e che `Application.Hwnd` coincida con la finestra foreground. Se la ROT restituisce un'altra istanza, il processo termina, l'accesso è negato, COM non è disponibile o l'associazione è ambigua, restituisce `Unresolved`; non enumera istanze e non inventa un path. Legge soltanto `FullName` e `Name`, mai contenuto o window title.
- **Conseguenze:** Nessuna migration v4: precisione e provenance attraversano lo schema v3. L'accesso ROT può non individuare l'istanza Excel foreground in scenari multi-istanza; il falso negativo è intenzionale. Word ed Excel reali, livelli di integrità, Protected View e multi-istanza restano da validare su hardware Windows.

## ADR-0018: Refresh documentale mirato, temporizzato e bounded

- **Stato:** Accepted per il terzo incremento Sprint 02B.
- **Decisione:** In assenza di un evento Word/Excel affidabile senza add-in, il collector riconcilia periodicamente soltanto la finestra foreground già riconosciuta come supportata dal registry. L'intervallo è configurabile (`DocumentRefreshInterval`, default 15 secondi), usa `TimeProvider` e un unico slot coalescente ordinato per sequence. Non enumera processi, non legge titoli e non esegue lavoro nei callback WinEvent.
- **Trade-off:** Quindici secondi limita il ritardo stale massimo atteso mantenendo quattro wakeup/minuto solo durante l'uso foreground di Word/Excel; è un valore iniziale da confermare con profiling Windows rispetto al budget CPU medio sotto 1%. Un evento applicativo richiederebbe un add-in e relativo deployment, sproporzionato per questo incremento.
- **Semantica:** Ogni risultato documentale diverso, inclusi cambi di precisione e passaggi resolved/unresolved, chiude l'intervallo precedente al timestamp osservato e apre una nuova observation senza attribuzione retroattiva. Un risultato identico non produce effetti. Timeout/fault mantengono il gate fino alla conclusione del lavoro reale; il refresh globale continua e gli altri resolver restano indipendenti.
- **Conseguenze:** Le metriche espongono solo conteggi aggregati di refresh cambiati/invariati oltre alla diagnostica resolver esistente. Nessuna migration v4 è necessaria; schema v3 resta autorevole. La frequenza e il consumo reale restano un gate di profiling Windows.
