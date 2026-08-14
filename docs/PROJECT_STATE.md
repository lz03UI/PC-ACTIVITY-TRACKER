# Project state

**Updated:** 2026-08-14  
**Phase:** Sprint 00 — repository foundation (validazione Windows in corso)

## Completed in this phase

- Product constraints, architectural boundaries, roadmap, and decision log are documented.
- Complete and cross-platform .NET solution structures are defined.
- Six production project shells and two test projects are present.
- A minimal WinUI 3 shell and neutral assembly markers establish compile-time boundaries.
- Architecture tests prohibit platform and persistence dependencies from leaking into Core.
- GitHub Actions defines Linux cross-platform and Windows full-solution validation.

## Intentionally not implemented

No activity collection, classification rules, production SQLite schema, reports, browser extension, application telemetry, cloud service, AI, packaging, or update mechanism exists yet.

## Validation status

- Static repository and XML structure can be inspected in the current environment.
- A local .NET 8 SDK bootstrap was used to restore, build, test, and format-check the cross-platform solution successfully; NuGet lock files are committed.
- WinUI application launch, OS adapters, MSIX behavior, accessibility, and Windows resource profiling require a Windows runtime and remain unvalidated.
- Il primo tentativo della CI Windows ha confermato restore e build dei progetti neutrali e di `PcActivityTracker.Windows`, ma `dotnet build` non ha trovato i task PRI/AppX installati con Visual Studio (`Microsoft.Build.Packaging.Pri.Tasks.dll`).
- Il job Windows ora configura Visual Studio MSBuild tramite `microsoft/setup-msbuild` e lo usa per restore e build della soluzione completa; la nuova esecuzione della CI deve ancora confermare la correzione. Lo Sprint 00 non è considerato completato finché questo job non è verde.

## Next safe task

Confermare su CI Windows che Visual Studio MSBuild compili la soluzione completa e che tutti i test passino. Solo dopo procedere alla progettazione, guidata dai test, della semantica temporale/delle osservazioni e della persistenza SQLite.

## Known risks

- The Windows App SDK project has not been compiled or launched on Windows in this environment.
- La correzione del workflow dipende dalla disponibilità dei workload WinUI/AppX nell'immagine GitHub Actions `windows-latest` e deve essere verificata dalla nuova esecuzione della PR #1.
- The empty Windows adapter project proves layering only, not Windows API feasibility or resource consumption.
- Privacy requirements need a formal threat/data-flow review before collectors or browser integration persist data.
