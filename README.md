# PC Activity Tracker

PC Activity Tracker is a privacy-first Windows desktop application intended to reconstruct a user's digital workday from application, relevant-file, and browser activity, then organize that activity by project, job/commessa, and category.

> **Status:** Sprint 00 repository foundation. Tracking, classification, persistence, browser collection, and dashboard features are intentionally not implemented yet.

## Product principles

- Windows desktop, built with C#/.NET and WinUI 3.
- SQLite is the local primary source of truth.
- Tracking and the dashboard operate fully offline; there is no mandatory backend.
- V1 does not use Supabase.
- No keylogging and no continuous screenshots.
- Privacy and low resource use are design requirements, not follow-up work.
- Deterministic rules precede optional AI assistance.

## Repository map

| Path | Responsibility | Platform |
| --- | --- | --- |
| `src/PcActivityTracker.Core` | Domain types, policies, and abstractions | Cross-platform |
| `src/PcActivityTracker.Data` | SQLite persistence adapters | Cross-platform |
| `src/PcActivityTracker.Reporting` | Queries and report composition | Cross-platform |
| `src/PcActivityTracker.BrowserIntegration` | Browser-neutral contracts and normalization | Cross-platform |
| `src/PcActivityTracker.Windows` | Windows activity collection and OS adapters | Windows |
| `src/PcActivityTracker.Desktop` | WinUI 3 composition root and presentation | Windows |
| `tests/PcActivityTracker.Core.UnitTests` | Fast business-logic tests | Cross-platform |
| `tests/PcActivityTracker.ArchitectureTests` | Automated dependency-boundary checks | Cross-platform |

See [the architecture guide](docs/ARCHITECTURE.md) for dependency rules.

## Prerequisites

- .NET 8 SDK
- Visual Studio 2022 17.8+ with the **WinUI application development** workload for desktop execution and packaging
- Windows 10 version 1809 (build 17763) or later to run the desktop application

The cross-platform libraries and tests are designed to build in Linux-based Codex environments. The complete solution, including the WinUI project, is validated on Windows CI.

## Build and test

Cross-platform validation:

```bash
dotnet restore PcActivityTracker.CrossPlatform.sln --locked-mode
dotnet build PcActivityTracker.CrossPlatform.sln --no-restore --configuration Release
dotnet test PcActivityTracker.CrossPlatform.sln --no-build --configuration Release
dotnet format PcActivityTracker.CrossPlatform.sln --verify-no-changes --no-restore
```

Complete Windows validation:

```powershell
dotnet restore PcActivityTracker.sln --locked-mode
dotnet build PcActivityTracker.sln --no-restore --configuration Release
dotnet test PcActivityTracker.sln --no-build --configuration Release
dotnet format PcActivityTracker.sln --verify-no-changes --no-restore
```

NuGet lock files are committed for reproducible restores. Use `dotnet restore --use-lock-file --force-evaluate` only when intentionally updating dependencies, then review and commit the lock-file changes.

## Contributing

Read [`AGENTS.md`](AGENTS.md) before making changes. Product scope, decisions, roadmap, and current status live in [`docs/`](docs/). Use a dedicated branch, keep commits focused, and explain any Windows-only validation gap in the pull request.

## License

No license has been selected. All rights are reserved until the project owner adds one.
