# Product specification

## Purpose

PC Activity Tracker helps a single user reconstruct a digital workday without surrendering a detailed activity history to a cloud service. It observes coarse application focus, relevant document context, and explicitly supported browser activity; groups observations into sessions; applies user-controlled classification; and presents a local dashboard.

## V1 outcomes

A successful V1 will let a user:

1. Start and stop tracking, with an obvious persistent status.
2. Review a local timeline of applications and meaningful file/browser context.
3. Assign and correct project, job/commessa, and category labels.
4. Define deterministic classification rules and understand why a rule matched.
5. View daily and period summaries while offline.
6. Export or delete their data and configure retention.
7. Pause tracking and exclude applications, titles, paths, domains, or private browser contexts.

## Users and operating context

The initial product serves one knowledge worker on one Windows device. Multi-user administration, organizational surveillance, employee scoring, and remote monitoring are explicitly outside the product intent.

## Functional scope

### Activity collection

- Capture foreground application identity and time intervals with event-driven APIs where practical.
- Capture a sanitized reference to an open file only for supported applications and only when configured.
- Accept browser navigation metadata from an opt-in browser integration, excluding private/incognito sessions.
- Recover conservatively from sleep, lock, process exit, and application shutdown so idle time is not misreported as work.

### Classification

- Evaluate deterministic, ordered rules over normalized activity metadata.
- Preserve the rule and reason behind each automatic classification.
- Allow manual corrections without rewriting raw observations.
- Keep any future AI suggestion local or explicitly opt-in; the product remains complete without it.

### Reporting and controls

- Provide timeline, totals, filters, and correction workflows.
- Expose pause, exclusions, retention, export, and deletion controls.
- Clearly distinguish observed, inferred, manually assigned, and unclassified data.

## Privacy and security requirements

- Never capture keystrokes or clipboard contents.
- Never take continuous screenshots. Any future user-initiated capture requires a separate decision and consent design.
- Minimize captured strings and prefer normalized application/path/domain identifiers over full content.
- Keep the database local by default and do not require authentication or internet access.
- Do not introduce Supabase in V1.
- Avoid collecting URL query strings and fragments by default; provide domain/path filtering before persistence.
- Store configuration and activity data in the user's application-data boundary with least-privilege file access.
- Make tracking state and deletion outcomes visible and auditable.

## Non-functional requirements

- **Offline:** collection, classification, correction, and reporting work with no network.
- **Efficiency:** collectors favor OS events, batching, bounded queues, and configurable sampling over tight polling.
- **Reliability:** writes are transactional; shutdown and migration are recoverable; raw events are not silently reclassified.
- **Explainability:** deterministic decisions carry a rule identifier and human-readable rationale.
- **Testability:** domain, reporting, browser normalization, and data behavior are testable without Windows.
- **Accessibility:** the WinUI interface follows Windows accessibility, keyboard, contrast, and scaling guidance.

## Explicitly out of scope for Sprint 00

Sprint 00 delivers repository, build, dependency, documentation, and test foundations only. It does not implement collection, a production database schema, classification, reporting UI, browser extensions, sync, telemetry, installers, AI, or automatic updates.

## V1 exclusions

- Mandatory accounts, cloud database, or cloud sync.
- Supabase.
- Keylogging, content inspection, continuous screenshots, webcam, or microphone collection.
- macOS/Linux desktop clients.
- Team dashboards, manager surveillance, billing, and mobile clients.
- AI-required classification.

## Acceptance principles

Each product increment must state what data it reads, transforms, persists, displays, exports, and deletes; prove offline behavior for its scope; provide deterministic tests where possible; and identify Windows-runtime validation separately.

