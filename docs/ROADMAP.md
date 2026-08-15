# Roadmap

Le milestone descrivono lo scope della V1, non date rigide. Apprendimento sul prodotto e review privacy possono riordinare il lavoro senza rimuovere i risultati V1 definiti nella specifica.

## Sprint 00 — fondazione repository (completato)

- Definire confini di solution/progetti, impostazioni centrali, CI e documentazione.
- Fornire una shell WinUI minima e assembly neutri verificabili su Linux.
- Aggiungere infrastruttura per unit test e test architetturali.
- Validare .NET 10 LTS e Windows App SDK stabile su pipeline Linux e Windows.

**Exit:** repository revisionabile e compilabile; entrambe le pipeline successive alla migrazione tecnologica sono verdi; nessuna funzionalità applicativa avanzata è considerata implementata.

## Sprint 01 — dominio, tempo e persistenza (completato)

- Modellare osservazioni, intervalli, provenance, classificazioni, progetti, commesse, categorie ed esclusioni.
- Definire semantica di clock/fuso orario, idle, foreground/focus time, lock, sleep e retention.
- Progettare schema SQLite versionato, migrazioni transazionali e backup/ripristino locale.
- Aggiungere unit test e integration test SQLite, inclusi casi di recovery.

## Sprint 02A — collector Windows runtime (corrente)

- Implementare stato visibile start/pausa/private mode/stop.
- Raccogliere intervalli delle applicazioni/programmi in foreground tramite API Windows event-driven.
- Gestire idle, lock, sleep, shutdown, processi inaccessibili e buffering limitato.
- Introdurre metriche locali privacy-safe e demandare il profiling esteso a Sprint 02B.

## Sprint 02B — documenti e hardening

- Rilevare file/documenti per le applicazioni esplicitamente supportate, distinguendo percorso completo, solo nome e unresolved.
- Eseguire hardening, recovery e profiling esteso su sistemi Windows rappresentativi.

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
