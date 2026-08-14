# Stato del progetto

**Aggiornato:** 2026-08-14
**Fase:** Sprint 00 — fondazione repository (completato)

## Completato in questa fase

- Vincoli di prodotto, confini architetturali, scope V1, roadmap e decision log sono documentati.
- Sono presenti solution completa e cross-platform, sei progetti di produzione e due progetti di test.
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

Avviare lo Sprint 01 con la progettazione test-first della semantica temporale, delle osservazioni e della persistenza SQLite, mantenendo i confini architetturali e i vincoli privacy definiti nello Sprint 00.

## Rischi noti

- Le prove strutturali non dimostrano ancora fattibilità, affidabilità o consumo di risorse degli adapter Windows.
- I requisiti privacy richiedono threat model e data-flow review prima che collector o integrazione browser persistano dati reali.
- Formati export, modalità email e applicazioni/browser supportati richiedono decisioni implementative dedicate, senza modificare i vincoli local-first e offline del core.
