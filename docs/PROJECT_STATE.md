# Stato del progetto

**Aggiornato:** 2026-08-15
**Fase:** Sprint 02B — documenti e hardening (avviato)

## Baseline completata

- Sprint 00, Sprint 01 e Sprint 02A sono completati e confluiti in `main`.
- Sprint 01 è stato integrato con commit `c717ffb291d465888c6ae057d7c7c3c762e93702`.
- Sprint 02A è confluito tramite PR #3; la CI finale è verde sia nel job cross-platform sia nel job Windows completo con Visual Studio MSBuild.
- La baseline autorevole post-PR #5 è `main` al commit `9be15990dd7e1316ebdca9ac6c36cb36c13c23cd`, con 136 test: 63 Core, 26 Data integration, 8 architecture e 39 adapter Windows.

## Completato in Sprint 02A

- Implementata la state machine deterministica platform-neutral per start, foreground, idle, lock/disconnect, suspend, pause, private, resume, stop, signal-loss, restart, discontinuità, duplicati e segnali stale, con priorità effettiva centralizzata.
- Implementato il collector foreground Windows event-driven con ingestion realmente bounded e un solo consumer awaitable, timestamp producer-side, reconciliation barrier, risoluzione processo tollerante agli accessi negati e idle configurabile.
- Integrati messaggi WTS, power e shutdown/logoff best-effort e controlli minimi WinUI con stato/degrado sempre visibile.
- Applicate esclusioni Application prima della creazione di `RawObservation`; Private persiste esclusivamente gap anonimi. Nessun titolo finestra general-purpose viene acquisito.
- Gli effetti di ogni segnale sono persistiti atomicamente tramite `ITrackingBatchStore`; un errore porta il runtime in `Faulted`, arresta l'ingestione e impedisce riferimenti a observation non confermate.
- Aggiunte metriche locali privacy-safe per segnali, drop, riconciliazioni, scritture, CPU, working set e crescita effettiva DB + WAL.
- Aggiunta migration v2 additiva per conservare la durata monotonic-derived separatamente dagli estremi UTC civili.
- Comandi user awaitable, ordering atomico dei producer, condizioni OS convergenti, segnali pre-Start non terminali e shutdown/time-zone filtering sono coperti dal runtime corretto.

## Sprint 02B avviato

Il terzo incremento è sviluppato sul branch dedicato `sprint-02b/runtime-hardening-recovery`; `main` resta la baseline autorevole.

Obiettivi approvati:

- introdurre resolver documentali per applicazioni esplicitamente supportate;
- distinguere `FullPath`, `FileNameOnly` e `Unresolved` con provenance esplicita;
- attivare esclusioni `FilePath` solo quando il percorso osservato è realmente disponibile e prima della persistenza;
- mantenere esclusi keylogging, clipboard capture, screenshot continui, content inspection e parsing indiscriminato dei titoli finestra;
- hardening crash/restart, fault/recovery e process termination durante la risoluzione;
- profiling prolungato su Windows reale per CPU, working set, wakeup, queue/drop/reconciliation, latenza resolver, SQLite e crescita DB/WAL;
- validazione Windows reale di lock/unlock, RDP, suspend/resume, shutdown/logoff e cambi fuso/offset.

La specifica esecutiva dello sprint è in `docs/SPRINT_02B.md`.

### Primo incremento document resolver

- Implementati risultato documentale tipizzato/provenance nel Core, registry/dispatcher isolato nel progetto Windows e fake resolver testabili.
- Integrato il resolver nel consumer foreground senza lavoro aggiuntivo nel callback WinEvent. Timeout, cancellation, access denied, processo terminato, COM unavailable ed eccezioni convergono a diagnostica categorizzata senza filename/path.
- Implementato Microsoft Word tramite Word Object Model/COM, senza window title: `FullName` assoluto produce `FullPath`, `Name` affidabile produce `FileNameOnly`, gli altri casi `Unresolved`.
- Le exclusion `FilePath` sono ora attive esclusivamente su `FullPath` e precedono la costruzione/persistenza della observation. Precisione e provenance sono conservate dalla migration SQLite v3.
- Aggiunte metriche resolver locali non identificative e 16 test (3 Core, 1 Data integration, 12 Windows), portando la suite a 126 test: 63 Core, 26 Data integration, 8 architecture e 29 Windows adapter.

### Secondo incremento document resolver

- Corretto l'isolamento da gate globale a gate per resolver: al massimo una chiamata attiva per adapter, nessun blocco reciproco e nessuna creazione illimitata di task dietro un resolver bloccato.
- Valutati AutoCAD, Inventor, Revit, Excel e Acrobat/Reader. È stato scelto Excel perché l'Object Model espone workbook attivo, path/nome e HWND senza add-in, UI Automation o parsing del titolo; gli adapter Autodesk più affidabili richiederebbero integrazione in-process/add-in.
- Implementato Excel COM con facade fake-testable e controlli conservativi PID/HWND/istanza. Mismatch, istanza ambigua, API unavailable, access denied, terminazione, timeout e cancellation attraversano la failure taxonomy condivisa; non vengono letti contenuto o window title.
- Nessuna migration v4: Excel riusa modello, provenance e schema v3.
- Aggiunti 10 test Windows per isolamento/concurrency bounded, timeout/cancellation e contratto Excel, portando la suite a 136 test: 63 Core, 26 Data integration, 8 architecture e 39 Windows adapter.
- Automated validation: **PASS**. Real Windows application validation: **PENDING**. Sprint 02B resta aperto.

### Terzo incremento runtime/recovery/document refresh

- Aggiunto refresh documentale mirato e configurabile (default 15 secondi), attivo soltanto per il foreground con resolver registrato e coalescente in un unico slot bounded ordinato.
- Formalizzati e testati i confini documento nella stessa finestra: path/nome, resolved/unresolved e Save As chiudono il precedente intervallo al tempo monotonic osservato; refresh invariato non duplica observation.
- Verificato il recovery del gate dopo fault e timeout: il gate è rilasciato dal lavoro reale nel `finally`, non dal caller scaduto, senza fan-out o blocco tra resolver.
- Aggiunti contatori locali aggregati per refresh tentati, cambiati e invariati; nessun campo stringa o dato documento è incluso.
- Nessuna migration v4: lo schema resta v3. La suite automatizzata è di 145 test (69 Core, 26 Data integration, 8 architecture, 42 Windows adapter). Sprint 02B resta **APERTO** per profiling e validazione Word/Excel reali.

### Hotfix P1 startup WinUI

- Durante la validation su hardware Windows reale è stato rilevato un crash sistematico allo startup con firma `0xC000027B` / `0x802B000A`.
- Una diagnosi differenziale esterna con controlli WinUI minimali ha isolato la causa nell'assenza di `XamlControlsResources`, necessaria per la theme resource usata dalla finestra principale.
- `App.xaml` ora carica `XamlControlsResources` nei merged resource dictionaries; un test di configurazione protegge il requisito. Non sono cambiati Windows App SDK, runtime deployment o schema SQLite.
- La validation automatizzata è separata dal gate post-fix su hardware Windows reale, che resta **PENDING**. Il P1 non è dichiarato definitivamente risolto e Sprint 02B resta **APERTO**.


## Ordine di implementazione Sprint 02B

1. Contratti neutralizzati per document context resolver e risultato tipizzato.
2. Dispatcher/registry Windows e facade fake testabile.
3. Primo resolver reale supportato con test Windows.
4. Integrazione nel runtime e nella persistence esistente.
5. Esclusioni `FilePath` pre-persistenza.
6. Ulteriori resolver applicativi validati.
7. Hardening crash/restart e failure mode.
8. Profiling Windows e definizione budget risorse.
9. Aggiornamento ADR/documentazione e PR.

## Intenzionalmente non implementato in Sprint 02B

- Browser tracking: Sprint 03.
- Classificazione deterministica progetto/commessa/categoria: Sprint 03.
- Timeline, dashboard e ricerca: Sprint 04.
- Report, work-log, export e invio email: Sprint 05.
- AI e cloud: fuori dalla V1 core e comunque opzionali.

## Stato della validazione

- Baseline post-Sprint 02A: CI verde su Linux e Windows.
- Build Windows completa WinUI validata in CI con Visual Studio MSBuild.
- Restano da produrre in 02B misure su hardware Windows reale e test applicativi dei resolver documentali.
- Ogni incremento 02B deve mantenere verdi i 110 test esistenti e aggiungere test per i nuovi failure mode senza indebolire i confini architetturali.
- Il primo incremento è verde localmente su Linux: 97 test cross-platform e 29 test Windows adapter con facade fake. La solution WinUI completa e il resolver COM contro Word reale richiedono Visual Studio MSBuild/desktop Windows e non sono stati validati in questo ambiente.

## Prossimo task sicuro

Eseguire prima il gate post-fix su hardware Windows reale: full Release build, avvio stabile senza `0xC000027B` / `0x802B000A` e inizializzazione DB. Solo dopo, validare Word ed Excel COM e il refresh a 15 secondi, quindi eseguire profiling prolungato senza iniziare Sprint 03.

## Rischi noti

- Le applicazioni espongono il documento attivo con meccanismi diversi e non sempre affidabili; i resolver devono quindi essere espliciti, isolati e conservativi.
- Un titolo finestra può essere ambiguo o contenere dati non destinati al tracking; non deve diventare una fonte general-purpose.
- Il percorso completo è più sensibile del solo nome file: minimizzazione ed esclusioni devono essere applicate prima possibile.
- Il profiling su runner CI non sostituisce la validazione di consumo e lifecycle su hardware Windows fisico.
