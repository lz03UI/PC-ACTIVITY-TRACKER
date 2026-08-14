# PC Activity Tracker

PC Activity Tracker è una singola applicazione desktop Windows, local-first e attenta alla privacy, pensata per ricostruire la giornata digitale dell'utente da attività di applicazioni, file pertinenti e browser, organizzandola per progetto, commessa e categoria.

> **Stato:** fondazione Sprint 00. Tracking, classificazione, persistenza, integrazione browser e dashboard non sono ancora implementati.

## Principi di prodotto

- Desktop Windows con C#/.NET e WinUI 3.
- SQLite è la fonte primaria di verità locale.
- Tracking e dashboard funzionano completamente offline, senza backend obbligatorio.
- La V1 non usa Supabase.
- Nessun keylogging e nessuno screenshot continuo.
- Privacy e basso consumo di risorse sono requisiti di progetto.
- Le regole deterministiche precedono qualsiasi assistenza AI opzionale.

## Mappa del repository

| Percorso | Responsabilità | Piattaforma |
| --- | --- | --- |
| `src/PcActivityTracker.Core` | Tipi di dominio, policy e astrazioni | Cross-platform |
| `src/PcActivityTracker.Data` | Adapter di persistenza SQLite | Cross-platform |
| `src/PcActivityTracker.Reporting` | Query e composizione dei report | Cross-platform |
| `src/PcActivityTracker.BrowserIntegration` | Contratti browser-neutral e normalizzazione | Cross-platform |
| `src/PcActivityTracker.Windows` | Raccolta attività e adapter OS Windows | Windows |
| `src/PcActivityTracker.Desktop` | Composition root e presentazione WinUI 3 | Windows |
| `tests/PcActivityTracker.Core.UnitTests` | Unit test della logica di business | Cross-platform |
| `tests/PcActivityTracker.ArchitectureTests` | Test automatici dei confini di dipendenza | Cross-platform |

Consultare [la guida architetturale](docs/ARCHITECTURE.md) per le regole sulle dipendenze.

## Prerequisiti

- .NET 10 SDK (10.0.400 o feature band compatibile).
- Una versione di Visual Studio compatibile con .NET 10 e workload **WinUI application development**, per build, esecuzione e packaging desktop.
- Windows 10 versione 1809 (build 17763) o successiva per eseguire l'applicazione desktop.

Le librerie e i test cross-platform sono progettati per compilare anche negli ambienti Linux di Codex. La solution completa, incluso il progetto WinUI, è validata dalla CI Windows.

## Build e test

Validazione cross-platform:

```bash
dotnet restore PcActivityTracker.CrossPlatform.sln --locked-mode
dotnet build PcActivityTracker.CrossPlatform.sln --no-restore --configuration Release
dotnet test PcActivityTracker.CrossPlatform.sln --no-build --configuration Release
dotnet format PcActivityTracker.CrossPlatform.sln --verify-no-changes --no-restore
```

Validazione completa su Windows, da un Developer PowerShell con Visual Studio MSBuild nel `PATH`:

```powershell
msbuild PcActivityTracker.sln -target:Restore -property:RestoreLockedMode=true -property:Configuration=Release -property:Platform="Any CPU"
msbuild PcActivityTracker.sln -target:Build -property:Configuration=Release -property:Platform="Any CPU" -property:RestorePackages=false -maxCpuCount
dotnet test PcActivityTracker.sln --configuration Release --no-build
dotnet format PcActivityTracker.CrossPlatform.sln --verify-no-changes --no-restore
```

Visual Studio MSBuild è necessario per risolvere i task PRI/AppX importati da WinUI; `dotnet build` non è il comando supportato per la build completa in questa configurazione. I lock file NuGet sono versionati per restore riproducibili. Usare `dotnet restore --use-lock-file --force-evaluate` solo per aggiornare intenzionalmente le dipendenze, poi revisionare e committare i lock file.

## Contribuire

Leggere [`AGENTS.md`](AGENTS.md) prima di apportare modifiche. Scope, decisioni, roadmap e stato corrente si trovano in [`docs/`](docs/). Usare un branch dedicato, mantenere i commit focalizzati e dichiarare nella pull request ogni lacuna di validazione Windows-only.

## Licenza

Non è stata ancora scelta una licenza. Tutti i diritti restano riservati finché il proprietario del progetto non ne aggiunge una.
