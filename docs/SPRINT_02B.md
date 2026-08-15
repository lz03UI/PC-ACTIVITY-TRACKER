# Sprint 02B — documenti e hardening

## Obiettivo

Portare il runtime di Sprint 02A da collector affidabile di applicazioni in foreground a collector affidabile di applicazioni + documento pertinente, mantenendo privacy, local-first, offline-first, bounded ingestion e persistenza atomica. Lo sprint include inoltre hardening crash/restart e profiling prolungato su Windows reale.

## Vincoli non negoziabili

- Nessun keylogging, clipboard capture, screenshot continuo o lettura del contenuto dei documenti.
- Nessun cloud, Supabase o rete richiesta.
- Il resolver documentale vive nel progetto Windows; policy e modelli restano platform-neutral nel Core.
- Nessun dato documento viene inventato. Ogni risultato è `FullPath`, `FileNameOnly` o `Unresolved` con provenance esplicita.
- Le esclusioni `FilePath` sono valutate prima della costruzione/persistenza di `RawObservation` quando il percorso è noto.
- Nessun `WindowTitle` general-purpose viene introdotto come scorciatoia indiscriminata.

## Matrice iniziale resolver

| Famiglia applicazione | Strategia preferita | Fallback | Risultato ammesso |
|---|---|---|---|
| Microsoft Office (Word/Excel/PowerPoint) | API/accessibilità o integrazione documentata che esponga il documento attivo senza leggerne il contenuto | titolo strutturato solo se validato per quella app | FullPath / FileNameOnly / Unresolved |
| AutoCAD | adapter specifico per processo/applicazione supportata | titolo strutturato validato, senza parsing generico globale | FullPath / FileNameOnly / Unresolved |
| Autodesk Inventor | adapter specifico | titolo strutturato validato | FullPath / FileNameOnly / Unresolved |
| Revit | adapter specifico | titolo strutturato validato | FullPath / FileNameOnly / Unresolved |
| Adobe Acrobat/Reader | adapter specifico | titolo strutturato validato | FullPath / FileNameOnly / Unresolved |
| VS Code / editor supportati | adapter specifico quando il file attivo è esposto in modo affidabile | FileNameOnly se non esiste un path affidabile | FullPath / FileNameOnly / Unresolved |
| Applicazioni non supportate | nessuna euristica invasiva | nessuno | Unresolved |

La matrice è intenzionalmente conservativa: un'app entra tra le supportate solo dopo test reali e documentazione del meccanismo usato.

## Work package A — contratti e modello

- Definire `IDocumentContextResolver` e risultato tipizzato con stato di risoluzione, applicazione supportata, valore minimizzato e provenance.
- Collegare il risultato alla snapshot foreground senza introdurre dipendenze Win32 nel Core.
- Mantenere immutabilità delle osservazioni grezze e compatibilità con schema/persistence esistenti.
- Aggiungere test unitari per FullPath, FileNameOnly, Unresolved, errori e timeout.

## Work package B — resolver Windows

- Registry/dispatcher di resolver esplicitamente supportati per process executable/path normalizzato.
- Timeout e cancellation per ogni resolver; nessun blocco del callback WinEvent.
- Errori di accesso o API non disponibili convergono su `Unresolved`, mai su dato inventato.
- Normalizzazione path case-insensitive Windows e minimizzazione configurabile.
- Test adapter con facade fake e test Windows per almeno due famiglie applicative prima di dichiarare il framework stabile.

## Work package C — privacy ed esclusioni FilePath

- Riattivare `FilePath` exclusion solo nel punto in cui il resolver ha un path reale.
- Match di esclusione prima di `RawObservation`.
- Nessun log con path completo per default.
- Test che dimostrano che un file escluso non compare in observation, interval, diagnostica o metriche identificative.

## Work package D — hardening e recovery

- Verificare crash/restart durante intervallo aperto, write batch, queue saturation e shutdown.
- Nessuna ricostruzione retroattiva della durata non osservata.
- Verificare Faulted → restart esplicito → reconciliation.
- Controllare comportamento con processi che terminano durante la risoluzione documento.
- Aggiungere diagnostica locale privacy-safe per resolver timeout/failure/unresolved.

## Work package E — profiling Windows reale

Misure minime su sessione prolungata:

- CPU media e picchi;
- working set medio/p95;
- wakeup/event rate;
- depth/drop delle lane e reconciliation;
- latenza di risoluzione documento p50/p95/p99;
- throughput e latenza SQLite;
- crescita DB + WAL;
- comportamento dopo lock/unlock, RDP disconnect/reconnect, suspend/resume, timezone change e shutdown/logoff.

I risultati vanno registrati senza includere nomi file o percorsi reali.

## Ordine di implementazione

1. Contratti e risultato tipizzato.
2. Dispatcher resolver + fake resolver testabile.
3. Primo resolver reale supportato e test Windows.
4. Integrazione runtime/persistence.
5. FilePath exclusions.
6. Secondo/terzo resolver reale.
7. Hardening crash/restart.
8. Profiling e budget di risorse.
9. Aggiornamento ADR/documentazione e PR.

## Primo incremento implementato

- Aggiunti modello tipizzato e provenance platform-neutral, registry/dispatcher Windows e resolver fake/contract test.
- Integrato il dispatcher nel consumer foreground, fuori dal callback WinEvent, con timeout predefinito di 750 ms, cancellation e gate di isolamento singolo.
- Aggiunto il primo resolver reale per Microsoft Word tramite Word Object Model/COM. Alternative: UI Automation (filename più fragile e dipendente dalla UI), titolo strutturato (scartato come fonte raw/ambigua) e add-in (affidabile ma invasivo). Il resolver COM richiede Word nella stessa sessione e un livello di integrità compatibile; produce `FullPath` da `ActiveDocument.FullName`, `FileNameOnly` da `Name`, oppure `Unresolved`.
- Attivate exclusion `FilePath` soltanto per percorsi completi osservati, prima della `RawObservation`; nessun raw window title viene acquisito o persistito.
- Aggiunta migration v3 per precisione/provenance documentale e metriche locali prive di filename/path: attempts, FullPath, FileNameOnly, unresolved, timeout, error e latenza cumulativa.
- Restano aperti resolver Tier 1 ulteriori, validazione Word reale, recovery/profiling prolungato e lifecycle su hardware Windows: lo Sprint 02B non è completato.

## Criteri di accettazione

- Nessuna regressione dei 110 test esistenti.
- Nuovi test deterministici per contratti, minimizzazione, esclusioni e failure mode.
- Build cross-platform e Windows completamente verdi.
- Almeno due famiglie applicative validate su Windows reale prima di chiudere lo sprint.
- Nessun browser tracking, classificazione, dashboard, report o AI introdotto in 02B.
- `docs/PROJECT_STATE.md`, `docs/ARCHITECTURE.md` e `docs/DECISIONS.md` aggiornati per riflettere il comportamento effettivamente implementato.
