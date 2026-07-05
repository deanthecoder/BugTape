# BugTape

**A black-box flight recorder and support-pack toolkit for desktop applications.**

BugTape helps an application explain what happened before something went wrong. It maintains a small, bounded timeline of useful diagnostic information and can package that evidence into a portable support bundle.

It is intended to integrate with applications that already produce support packs containing logs and configuration. BugTape adds structured user actions, screenshots, application state, environment details, and a timeline that makes the collected evidence much easier to understand.

## The Idea

When a user encounters a problem, asking for log files often leaves important questions unanswered:

- What was the user trying to do?
- Which screen or document was active?
- What state was the application in?
- What happened immediately before the error?
- Did the UI show something that was never written to the log?

BugTape acts as a rolling flight recorder. Applications can record semantic breadcrumbs such as:

- Opened a project.
- Changed operating mode.
- Attempted to connect to a server.
- Selected an item.
- Started an import.
- Displayed a warning.
- Encountered an exception.

When requested, BugTape combines its recent history with the application's existing diagnostic files and creates a `.zip` support pack.

## Example Support Pack

```text
support-pack.zip
├── manifest.json
├── timeline/
│   └── events.jsonl
├── logs/
│   ├── application.log
│   └── previous.log
├── screenshots/
│   ├── 2026-07-05T14-32-10Z.png
│   └── 2026-07-05T14-33-02Z.png
├── state/
│   ├── application.json
│   ├── active-project.json
│   └── open-documents.json
├── system/
│   └── environment.json
├── attachments/
└── redaction-report.json
```

The manifest describes the application, build, pack format, capture period, included components, and any collection failures.

## Core Features

- A bounded rolling event timeline with configurable memory and disk limits.
- Semantic application breadcrumbs with structured metadata.
- Integration with existing application logs and support-pack files.
- Periodic or event-triggered screenshots.
- Application-defined state snapshots.
- Build, operating-system, locale, runtime, and environment details.
- Automatic exception and warning capture.
- A contributor model so each application can add its own diagnostics.
- Redaction and screenshot masking for sensitive information.
- A preview showing users what will be included.
- A standalone viewer for navigating events, screenshots, logs, and state.

## Example API

```csharp
BugTape.RegisterStateProvider("active-project", () => new
{
    Project.Current?.Name,
    Project.Current?.Mode,
    Project.Current?.IsModified
});

BugTape.Record("Connection attempted", new
{
    Server = Redact.Host(connection.Server),
    connection.Protocol
});

BugTape.Record("Import completed", new
{
    ImportedItems = result.Count,
    DurationMs = result.Duration.TotalMilliseconds
});

await BugTape.CreateSupportPackAsync("support-pack.zip");
```

Applications should be able to register arbitrary support-pack contributors:

```csharp
BugTape.RegisterContributor(new ExistingLogContributor(logDirectory));
BugTape.RegisterContributor(new ConfigurationContributor(settings));
BugTape.RegisterContributor(new DatabaseSummaryContributor(database));
```

One broken contributor should not prevent the remainder of the support pack from being created. Collection failures should instead be recorded in the manifest.

## Proposed Projects

### BugTape.Core

The platform-neutral foundation:

- Timeline and ring buffer.
- Event schema and serialization.
- State-provider and contributor APIs.
- Support-pack assembly.
- Size and retention policies.
- Redaction primitives.

### BugTape.Avalonia

Optional integration for Avalonia applications:

- Window and navigation breadcrumbs.
- Routed pointer and control interactions.
- Screenshot capture.
- Sensitive-control masking.
- UI-thread-safe state collection.
- Exception and dispatcher integration.

UI events should be recorded semantically, using control names or application-provided descriptions, rather than as raw input.

### BugTape.Logging

Adapters for common .NET logging systems, allowing existing logs and selected structured events to appear on the BugTape timeline.

### BugTape.Viewer

A standalone desktop viewer for support packs:

- Chronological event timeline.
- Screenshot preview synchronized with events.
- Log filtering and search.
- Application-state inspection and comparison.
- Exception and warning markers.
- Exportable summaries for support tickets.

## Privacy and Security

BugTape must never become an accidental keylogger or secret collector.

Its default behaviour should be conservative:

- Do not record arbitrary keystrokes.
- Do not capture text-field contents by default.
- Do not collect passwords, tokens, connection strings, or personal data.
- Record semantic actions instead of raw input wherever possible.
- Allow controls and screen regions to be marked as sensitive.
- Mask sensitive regions before screenshots are written.
- Apply configurable redaction to structured values and logs.
- Let the user preview the pack before sharing it.
- Include a redaction report describing removed or transformed data.
- Enforce retention, file-count, and total-size limits.

Support packs should be created locally. Uploading or transmitting them should remain a separate, explicit application action.

## Event Design

Events should be structured and versioned rather than stored only as prose:

```json
{
  "timestampUtc": "2026-07-05T14:32:10.123Z",
  "sequence": 1842,
  "category": "Connection",
  "name": "ConnectionAttempted",
  "message": "Connection attempted",
  "severity": "Information",
  "window": "ServerConfiguration",
  "correlationId": "operation-42",
  "data": {
    "server": "[redacted-host]",
    "protocol": "OPC UA"
  }
}
```

JSON Lines (`.jsonl`) would allow events to be appended safely and processed without loading the entire timeline into memory.

## Capture Strategy

BugTape should use a bounded rolling buffer so that long-running applications do not consume unbounded storage.

Possible defaults:

- Keep the most recent 30 minutes of events.
- Keep a maximum number of screenshots.
- Capture screenshots only after significant actions, warnings, or errors.
- Retain a small amount of history on disk so crashes do not erase everything.
- Flush critical events immediately.
- Allow each application to override limits.

Screenshots and large attachments should be governed separately from lightweight timeline events.

## Roadmap

### Phase 1: Support-Pack Builder

- Contributor API.
- Manifest and versioned pack format.
- Existing log and attachment collection.
- Application-state snapshots.
- Redaction pipeline.
- Resilient `.zip` creation.

This phase is immediately useful even without automatic recording.

### Phase 2: Flight Recorder

- Bounded structured-event timeline.
- Logging integration.
- Exception capture.
- Screenshot capture and masking.
- Crash-safe persistence.

### Phase 3: Support-Pack Viewer

- Timeline, logs, screenshots, and state in one UI.
- Search and filtering.
- Correlation of screenshots and errors with preceding actions.
- Support-ticket summary export.

### Phase 4: Optional Replay

- Record selected deterministic interactions.
- Restore application-provided state.
- Replay supported actions in a test environment.

Replay should remain optional. Useful, privacy-conscious support packs provide substantial value without attempting automatic reproduction of every application interaction.

## Design Principles

- **Useful by stages:** support-pack assembly should be valuable before advanced recording exists.
- **Safe by default:** applications must opt into sensitive or detailed capture.
- **Application-aware:** semantic breadcrumbs are more useful than raw mouse and keyboard events.
- **Resilient:** diagnostic collection should continue when an individual contributor fails.
- **Bounded:** recording must have predictable storage and performance costs.
- **Inspectable:** users and support engineers should be able to see exactly what was captured.
- **Extensible:** work applications can contribute their own state and existing diagnostic material.
- **Local first:** creation and inspection should not require a hosted service.

## Potential Future Ideas

- Compare state snapshots before and after an operation.
- Attach BugTape packs directly to issue trackers.
- Generate a concise Markdown incident summary.
- Add automated health checks to support packs.
- Capture network request metadata without sensitive payloads.
- Allow QA automation to add step names and expected outcomes.
- Produce CI artifacts from failed UI tests.
- Use application-specific viewers for specialised state.

## Initial Goal

The first milestone should be deliberately small:

> Allow a .NET desktop application to register diagnostic contributors and structured state providers, then reliably create a privacy-conscious, versioned support-pack `.zip`.

Once that foundation works inside a real application, the rolling timeline, screenshots, viewer, and possible replay features can grow around it.
