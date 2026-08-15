# Stato del progetto

**Aggiornato:** 2026-08-14
**Fase:** Sprint 01 — dominio, tempo e persistenza locale (pronto per review)

## Completato in questa fase

- Implementato un dominio platform-neutral fortemente tipizzato per osservazioni, contesti applicazione/file/browser, stati, intervalli, discontinuità, classificazioni/provenance, esclusioni e tassonomia progetto/commessa/categoria.
- Formalizzata la semantica UTC, `[start, end)`, fuso/offset osservato, wall clock rispetto a monotonic e accesso testabile al tempo con `TimeProvider`.
- Definite nel Core porte di persistenza orientate ai casi d'uso; il Core non dipende da SQLite.
- Implementato schema SQLite v1 locale con migrazioni SQL sequenziali atomiche, versione leggibile, foreign key, trigger d'immutabilità, indici, WAL, busy timeout e retention temporale transazionale.
- Rafforzata la privacy: private/incognito non può diventare raw activity; un periodo privato è rappresentabile solo come gap temporale privo di contenuto identificativo.
- La retention elimina i periodi precedenti, tronca quelli attraversanti il cutoff e conserva quelli successivi senza mantenere evidenza identificativa scaduta.
- Una classificazione usa progetto o commessa; con la commessa, il progetto è derivato dalla sua relazione corrente. Gli ID della tassonomia sono validati nel dominio e nello schema.
- Aggiunti 21 unit test di dominio e 21 integration test SQLite su file temporanei reali; restano inoltre verdi gli 8 test architetturali, per un totale di 50 test.

## Fuori scope e rinvii Sprint 01

- Nessun collector Windows, tracking reale, browser extension, UI applicativa, motore completo di classificazione, report, AI, rete o cloud è stato introdotto.
- Sessionizzazione derivata, batching profilato, backup/ripristino, recovery da corruzione e policy di scelta della classificazione efficace sono rinviati prima dei rispettivi flussi di produzione.

- Vincoli di prodotto, confini architetturali, scope V1, roadmap e decision log sono documentati.
- Sono presenti solution completa e cross-platform, sei progetti di produzione e tre progetti di test.
- Una shell WinUI 3 minima e marker assembly neutrali stabiliscono i confini a compile time.
- I test architetturali impediscono che dipendenze di piattaforma e persistenza entrino in Core.
- La CI definisce validazione Linux cross-platform e build/test della solution completa su Windows.
- La precedente CI Windows è diventata verde dopo il passaggio da `dotnet build` a Visual Studio MSBuild, che risolve correttamente i task PRI/AppX di WinUI.
- La baseline è stata aggiornata a .NET 10 LTS e Microsoft.WindowsAppSDK 2.3.1 stabile, mantenendo Windows 10 1809 come versione minima di esecuzione e 19041 come Target Platform Version.
- La CI post-upgrade della PR #1 è risultata verde sia nel job Linux cross-platform sia nel job Windows della solution completa; Sprint 00 è completato.

## Intenzionalmente non implementato

Sprint 00 non implementa raccolta attività/file/browser, classificazione, schema SQLite di produzione, timeline, dashboard, ricerca, report, export, backup, AI o altre funzionalità applicative V1.

## Stato della validazione

- I lock file NuGet sono rigenerati per .NET 10 e le dipendenze aggiornate.
- La validazione cross-platform locale comprende restore bloccato, build, test e verifica del formato.
- Launch WinUI, adapter OS, MSIX, accessibilità e profiling delle risorse richiedono Windows e non sono convalidabili nell'ambiente Linux locale.
- La CI della baseline finale .NET 10 LTS / Windows App SDK 2.3.1 è verde su entrambi i job della PR #1: restore, formattazione, build e test cross-platform su Linux, oltre a restore, build della solution completa e test su Windows.

## Prossimo task sicuro

Sottoporre lo Sprint 01 a CI Windows (restore e build completa con Visual Studio MSBuild, test con `dotnet test --no-build`); dopo l'approvazione, pianificare Sprint 02 partendo dai contratti neutrali senza anticipare UI o integrazioni browser.

## Rischi noti

- Le prove strutturali non dimostrano ancora fattibilità, affidabilità o consumo di risorse degli adapter Windows.
- I requisiti privacy richiedono threat model e data-flow review prima che collector o integrazione browser persistano dati reali.
- Formati export, modalità email e applicazioni/browser supportati richiedono decisioni implementative dedicate, senza modificare i vincoli local-first e offline del core.
