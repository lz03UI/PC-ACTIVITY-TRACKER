# Roadmap

Milestones describe intent, not dates. Product learning and privacy review may reorder work.

## Sprint 00 — repository foundation (current)

- Establish solution/project boundaries, central build settings, CI, and documentation.
- Provide a minimal WinUI shell and cross-platform library markers.
- Add unit and architecture-test infrastructure.
- Confirm cross-platform and Windows validation paths.

Exit: repository is reviewable and structurally buildable; Windows CI is defined; no advanced feature is implied complete.

## Sprint 01 — domain and persistence design

- Model observations, activity intervals, classifications, provenance, and exclusions.
- Specify lifecycle, clock/time-zone, and retention semantics.
- Design versioned SQLite schema and transactional migration strategy.
- Add unit and SQLite integration tests, including crash-oriented cases.

## Sprint 02 — minimal Windows collection

- Implement visible start/pause/stop state.
- Collect foreground-application intervals using event-driven Windows APIs.
- Handle lock, sleep, idle, shutdown, inaccessible processes, and bounded buffering.
- Measure CPU, memory, wakeups, and database growth on representative Windows systems.

## Sprint 03 — deterministic classification

- Implement ordered, explainable rules and manual correction.
- Add project, job/commessa, and category configuration.
- Preserve raw observation and classification provenance.

## Sprint 04 — local dashboard and controls

- Implement timeline and summary read models and accessible WinUI views.
- Add exclusions, retention, export, deletion, and database-health controls.
- Validate fully offline end-to-end use.

## Sprint 05 — opt-in browser integration

- Select supported browsers and document extension/native-host trust boundaries.
- Minimize URLs before persistence and exclude private browsing.
- Add consent, health, disconnect, and contract-test flows.

## Later candidates (not V1 commitments)

- Supported-application file context.
- User-initiated local backup/restore.
- Optional AI suggestions behind explicit consent and replaceable interfaces.
- Optional sync only after a new privacy threat model and architecture decision; no Supabase in V1.

