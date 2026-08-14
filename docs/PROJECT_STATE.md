# Project state

**Updated:** 2026-08-14  
**Phase:** Sprint 00 — repository foundation

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
- The complete solution restore/build and WinUI runtime behavior still require separate validation; Windows CI is the authoritative validation point.

## Next safe task

Confirm the complete solution on Windows CI, then design domain time/observation semantics and SQLite persistence through tests before implementing collection.

## Known risks

- The Windows App SDK project has not been compiled or launched on Windows in this environment.
- The empty Windows adapter project proves layering only, not Windows API feasibility or resource consumption.
- Privacy requirements need a formal threat/data-flow review before collectors or browser integration persist data.
