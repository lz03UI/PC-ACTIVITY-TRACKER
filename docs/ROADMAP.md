# Roadmap

Le milestone descrivono lo scope della V1, non date rigide. Apprendimento sul prodotto e review privacy possono riordinare il lavoro senza rimuovere i risultati V1 definiti nella specifica.

## Sprint 00 — fondazione repository (completato)

- Definire confini di solution/progetti, impostazioni centrali, CI e documentazione.
- Fornire una shell WinUI minima e assembly neutrali verificabili su Linux.
- Aggiungere infrastruttura per unit test e test architetturali.
- Validare .NET 10 LTS e Windows App SDK stabile su pipeline Linux e Windows.

**Exit:** repository revisionabile e compilabile; entrambe le pipeline successive alla migrazione tecnologica sono verdi; nessuna funzionalità applicativa avanzata è considerata implementata.

## Sprint 01 — dominio, tempo e persistenza (completato)

- Modellare osservazioni, intervalli, provenance, classificazioni, progetti, commesse, categorie ed esclusioni.
- Definire semantica di clock/fuso orario, idle, foreground/focus time, lock, sleep e retention.
- Progettare schema SQLite versionato, migrazioni transazionali e backup/ripristino locale.
- Aggiungere unit test e integration test SQLite, inclusi casi di recovery.

## Sprint 02A — collector Windows runtime (completato)

- Implementare stato visibile start/pausa/private mode/stop.
- Raccogliere intervalli delle applicazioni/programmi in foreground tramite API Windows event-driven.
- Gestire idle, lock, sleep, shutdown, processi inaccessibili e buffering limitato.
- Introdurre metriche locali privacy-safe e demandare il profiling esteso a Sprint 02B.

**Exit:** runtime collector, ordering, durata monotonic, persistenza atomica e lifecycle Windows validati dalla CI Linux/Windows e confluiti in `main` tramite PR #3.

## Sprint 02B — documenti e hardening (corrente)

- Introdurre una matrice esplicita delle applicazioni supportate e resolver documentali separati per adapter.
- Rilevare file/documenti soltanto per applicazioni esplicitamente supportate, distinguendo `FullPath`, `FileNameOnly` e `Unresolved` senza inferire dati non osservati.
- Applicare minimizzazione ed esclusioni `FilePath` prima della costruzione/persistenza di `RawObservation` quando il dato è disponibile.
- Integrare il contesto documento nel runtime senza leggere contenuto, clipboard, testo digitato o screenshot.
- Rafforzare crash/restart, recovery, degrado e diagnostica privacy-safe senza introdurre checkpoint che inventino durata non osservata.
- Eseguire profiling esteso su sistemi Windows rappresentativi: CPU, working set, wakeup, code/drop/reconciliation, throughput SQLite e crescita DB/WAL.
- Validare lock/unlock, RDP disconnect/reconnect, suspend/resume, shutdown/logoff, cambi fuso/offset e applicazioni supportate su Windows reale.

**Exit:** i resolver documentali supportati producono contesto minimizzato e testabile, il runtime rimane local-first/offline e bounded, la CI completa è verde e i rischi Windows-only sono documentati con risultati di profiling riproducibili.

## Sprint 03 — browser e classificazione deterministica

- Integrare browser supportati con consenso opt-in, minimizzazione degli URL ed esclusione della navigazione privata.
- Implementare regole ordinate e spiegabili per progetto, commessa e categoria.
- Aggiungere correzioni manuali e coda delle attività non classificate preservando osservazioni e provenance.

## Sprint 04 — timeline, dashboard e ricerca

- Implementare timeline giornaliera e dashboard locale accessibile.
- Aggiungere ricerca globale per data, app, file, progetto/commessa e dominio.
- Aggiungere viste progetto/commessa con tempo, timeline, file e attività.
- Convalidare end-to-end offline, retention, esclusioni, eliminazione e salute database.

## Sprint 05 — report, work-log e continuità

- Generare report giornalieri, settimanali, mensili e per progetto/commessa.
- Proporre timesheet/work-log modificabili a supporto della ricostruzione del lavoro.
- Implementare “riprendi da dove avevi lasciato” nel rispetto di privacy ed esclusioni.
- Esportare CSV, XLSX, JSON e PDF oppure HTML secondo le decisioni implementative.
- Consentire facoltativamente l'invio email del solo report finale, senza rendere rete o cloud necessari.

## Dopo la V1

- Suggerimenti AI esclusivamente opzionali, dietro consenso esplicito e interfacce sostituibili.
- Eventuale sincronizzazione solo dopo un nuovo threat model e una decisione architetturale; Supabase resta escluso dalla V1.
