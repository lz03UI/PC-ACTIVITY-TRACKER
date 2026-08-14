# Specifica di prodotto

## Scopo

PC Activity Tracker è un progetto autonomo e una singola applicazione desktop Windows che aiuta un utente a ricostruire la propria giornata digitale senza trasferire a un servizio cloud la cronologia dettagliata delle attività. Rileva applicazioni in primo piano, documenti pertinenti e attività browser esplicitamente supportata; organizza le osservazioni in sessioni; applica classificazioni controllabili dall'utente; presenta dashboard e report locali.

## Risultati attesi dalla V1

La V1 deve consentire all'utente di:

1. avviare, mettere in pausa e fermare il tracking, con stato sempre evidente e una modalità privata;
2. ricostruire il tempo effettivo di foreground/focus distinguendolo da inattività, blocco, sospensione e cambi di applicazione;
3. consultare una timeline giornaliera di applicazioni, file/documenti pertinenti e siti browser supportati;
4. organizzare e correggere le attività per progetto, commessa e categoria;
5. applicare regole deterministiche e spiegabili, con una coda dedicata alle attività non classificate;
6. usare una dashboard locale, una ricerca operativa globale e viste di progetto/commessa;
7. produrre report e proposte di timesheet/work-log, esportarli e riprendere il lavoro dal contesto precedente;
8. gestire esclusioni, retention, eliminazione e backup locale;
9. svolgere tutti i flussi core offline, senza account, cloud o AI obbligatori.

## Utenti e contesto operativo

Il prodotto iniziale serve un singolo knowledge worker su un dispositivo Windows. Amministrazione multiutente, sorveglianza organizzativa, scoring dei dipendenti e monitoraggio remoto sono estranei allo scopo.

## Scope funzionale V1

### Raccolta e tempo attendibile

- Tracking delle applicazioni/programmi in foreground con API event-driven quando possibile.
- Tracking dei file/documenti utilizzati nelle applicazioni esplicitamente supportate, con riferimenti minimizzati e configurabili.
- Tracking di browser/siti tramite integrazione opt-in, senza sessioni private/incognito e senza query string o fragment per impostazione predefinita.
- Idle detection e calcolo del vero tempo di foreground/focus, con gestione conservativa di sleep, lock, arresto, processi terminati e dati inaccessibili.
- Comandi di pausa, private mode ed esclusione per applicazioni, titoli, percorsi e domini.

### Organizzazione e classificazione

- Progetti, commesse e categorie configurabili.
- Regole deterministiche ordinate, con regola e motivazione conservate per ogni classificazione automatica.
- Correzioni manuali che non riscrivono le osservazioni grezze.
- Coda operativa delle attività non classificate.
- Distinzione visibile tra dati osservati, inferiti, assegnati manualmente e non classificati.

### Consultazione, ripresa e report

- Timeline giornaliera e dashboard locale con totali e filtri.
- Ricerca operativa globale per data, applicazione, file, progetto/commessa e dominio.
- Viste progetto/commessa con tempo, timeline, file e attività correlate.
- Report giornalieri, settimanali, mensili e per progetto/commessa.
- Export CSV, XLSX, JSON e PDF oppure HTML; i formati esatti e le librerie saranno scelti con decisioni tecniche future.
- Supporto alla ricostruzione del lavoro e generazione di una proposta modificabile di timesheet/work-log.
- Funzione “riprendi da dove avevi lasciato” basata esclusivamente sul contesto locale consentito.
- Invio email opzionale del solo report finale; non è richiesto per generare o consultare il report e non introduce un backend obbligatorio.

### Gestione dei dati

- SQLite locale come fonte primaria di verità.
- Retention configurabile, eliminazione verificabile ed esclusioni applicate prima possibile nel flusso dati.
- Backup e ripristino locali avviati dall'utente.

### AI opzionale

Eventuali suggerimenti AI sono adapter opzionali, esplicitamente attivabili e sostituibili. Raccolta, classificazione deterministica, correzione, ricerca, dashboard e report devono restare completi senza AI e senza rete.

## Requisiti di privacy e sicurezza

- Non acquisire mai tasti premuti, clipboard, webcam o microfono.
- Non eseguire screenshot continui.
- Minimizzare le stringhe raccolte e preferire identificatori normalizzati di applicazione, percorso e dominio ai contenuti completi.
- Conservare database e configurazione nel perimetro dati dell'utente con privilegi minimi.
- Non richiedere autenticazione, internet, servizio cloud o Supabase in V1.
- Rendere visibili e verificabili stato del tracking, provenienza delle classificazioni ed esito delle eliminazioni.

## Requisiti non funzionali

- **Offline:** raccolta, classificazione, correzione, ricerca, dashboard e reporting funzionano senza rete.
- **Efficienza:** collector event-driven, batch, code limitate e campionamento configurabile prevalgono sul polling stretto.
- **Affidabilità:** scritture transazionali, migrazioni recuperabili e nessuna riclassificazione silenziosa dei dati grezzi.
- **Spiegabilità:** ogni decisione deterministica conserva identificativo e motivazione leggibile.
- **Testabilità:** dominio, reporting, normalizzazione browser e persistenza sono testabili senza Windows.
- **Accessibilità:** la UI WinUI segue le linee guida Windows per tastiera, contrasto, scaling e tecnologie assistive.

## Esplicitamente fuori scope per Sprint 00

Sprint 00 consegna soltanto fondazione del repository, build, confini delle dipendenze, documentazione e infrastruttura di test. Non implementa le funzionalità V1 elencate sopra, schema SQLite di produzione, estensioni browser, packaging, telemetria, AI o aggiornamenti automatici.

## Esclusioni V1

- Account obbligatori, database cloud, sincronizzazione cloud e Supabase.
- Keylogging, ispezione del contenuto, screenshot continui, webcam o microfono.
- Client desktop macOS/Linux.
- Dashboard di team, sorveglianza manageriale, billing e client mobili.
- AI necessaria alla classificazione o a qualsiasi workflow core.

## Principi di accettazione

Ogni incremento deve dichiarare quali dati legge, trasforma, persiste, mostra, esporta ed elimina; dimostrare il comportamento offline; includere test deterministici dove possibile; e separare chiaramente la validazione che richiede Windows.
