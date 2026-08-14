# AGENTS.md

## Mission

Keep PC Activity Tracker local-first, private, resource-efficient, and useful without a network connection. This repository is designed for Codex-assisted development: make small, reviewable changes and leave the project state clearer than you found it.

## Lingua e comunicazione

- Comunica con l'utente in italiano.
- Scrivi in italiano spiegazioni, riepiloghi, review, documentazione e messaggi di stato.
- Identificatori di codice, classi, metodi, variabili, namespace, nomi tecnici e nomi di file possono rimanere in inglese quando è la convenzione corretta.
- I messaggi di commit possono rimanere in inglese se sono coerenti con le convenzioni del repository.

## Non-negotiable constraints

- Use C#/.NET and WinUI 3 for the Windows desktop application.
- SQLite is the local primary source of truth. Do not add a required cloud service or Supabase in V1.
- Tracking and reporting must work fully offline.
- Never implement keylogging or continuous screenshots.
- Prefer deterministic classification rules; AI integrations are optional adapters and must never be required for core workflows.
- Keep platform-independent policy and business logic out of Windows-specific projects.

## Repository workflow

1. Read `docs/PRODUCT_SPEC.md`, `docs/ARCHITECTURE.md`, `docs/DECISIONS.md`, and `docs/PROJECT_STATE.md` before changing architecture or scope.
2. Work on a dedicated branch; do not commit directly to the default branch.
3. Update tests and relevant documentation with behavior or architecture changes.
4. Valida la solution cross-platform con `dotnet restore --locked-mode`, `dotnet build --no-restore`, `dotnet test --no-build`, and `dotnet format --verify-no-changes`.
5. Su Windows esegui restore e build della solution completa (incluso WinUI) con Visual Studio MSBuild, perché i task PRI/AppX sono forniti da Visual Studio; usa `dotnet test --no-build` per i test.
6. Registra nella pull request le validazioni Windows-only che non è stato possibile eseguire.
7. Aggiorna `docs/PROJECT_STATE.md` con progressi significativi, limitazioni e il prossimo task sicuro.

## Code conventions

- Nullable reference types and implicit usings remain enabled.
- Treat warnings as errors. Prefer immutable domain values and explicit dependencies.
- Production code belongs under `src/`; tests mirror it under `tests/`.
- Dependency direction is inward: UI and infrastructure may reference application-neutral libraries, never the reverse.
- Do not put business logic in WinUI code-behind or persistence classes.
- Do not hide I/O, time, OS, or network access behind static global state; inject interfaces at boundaries.
- Add packages centrally in `Directory.Packages.props` and commit `packages.lock.json` files once generated.

## Definition of done

- The change builds and relevant tests pass on supported runners.
- Architecture boundaries and privacy constraints remain enforced.
- No secret, personal activity data, database, generated build output, or IDE state is committed.
- Documentation and project state accurately describe the repository after the change.
