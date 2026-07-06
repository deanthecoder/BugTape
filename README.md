# BugTape

**A black-box flight recorder and support-pack toolkit for desktop applications.**

BugTape helps an application explain what happened before something went wrong. It maintains a small, bounded timeline of useful diagnostic information and can package that evidence into a portable support bundle.

It is intended to integrate with applications that already produce support packs containing logs and configuration. BugTape adds structured user actions, screenshots, application state, environment details, and a timeline that makes the collected evidence much easier to understand.

## First Production Integration

For the first integration, point Codex at this README and the target desktop
application with the following brief:

> Read BugTape's README, then inspect this application's logging,
> startup/shutdown, exception handling, WPF/Avalonia setup, document import
> flow, and support-pack implementation. Do not change anything yet. Recommend
> the smallest useful BugTape integration points, noting privacy, threading,
> performance, and .NET Framework compatibility concerns.

After reviewing the recommendations, implement one thin vertical slice:

1. Create a minimal `BugTape.Core` support-pack builder.
2. Register application state providers and existing-file contributors.
3. Forward the existing logger's logged event at its corresponding error,
   warning, information, or debug level.
4. Wrap one useful workflow with `StartAction` or `Track`, such as document
   import.
5. Include BugTape's generated manifest, state, and timeline files in the
   application's existing support pack.

This should validate the public API against production application code before
adding crash recovery, screenshots, broader automatic instrumentation, or
viewer features.

### First Integration Notes

The first target application already has a support-package command, so BugTape
should not replace that mechanism at first. It should produce additional files
that the host application includes in its existing package.

Relevant host-application integration points to look for:

- an existing hook for adding extra files to a support package;
- existing application settings, logs, exported configuration, and crash-event
  files that BugTape can reference or complement;
- a live logger event that can be mirrored into BugTape's timeline;
- application startup, shutdown, and unhandled-exception handlers;
- a user-visible import, export, processing, or save operation that already
  logs success and failure context;
- background processing tasks that can become later action boundaries once the
  first support-pack slice works.

Useful application state providers for the first pass:

- application/product version, build configuration, culture, UI culture, and
  command-line mode;
- active branding/product name, without copying style files into BugTape;
- connected service count, service names or redacted identifiers, and
  connection state;
- work queue counts and current queue state;
- active profile, workspace, or document summary, avoiding full user paths;
- BugTape self-diagnostics such as accepted records, dropped records, provider
  failures, and last export time.

Avoid capturing licence keys, complete document paths, document contents, user
file names unless explicitly redacted, passwords, tokens, connection strings,
or arbitrary text-field contents.

### Relationship to Microsoft Diagnostics

Microsoft already provides several useful diagnostics building blocks, but not
the whole local support-pack workflow BugTape is aiming for.

- `System.Diagnostics.Activity` is a strong fit for operation identity,
  parent/child relationships, tags, durations, and ambient async context. The
  BugTape action model should either wrap it internally or be easy to bridge to
  it later.
- `Microsoft.Extensions.Logging` is useful for modern logging integrations, but
  many target desktop products still use existing in-house loggers. BugTape
  should provide simple logger-event adapters rather than require a logging
  framework migration.
- `EventSource`, ETW, and EventPipe are useful low-level event streams and
  profiler/tooling hooks. They do not by themselves provide a user-visible,
  privacy-reviewed support bundle with application state and existing product
  files.
- Windows Error Reporting and crash dumps can help with hard crashes, but they
  are too large and sensitive to be BugTape's default evidence format.

BugTape's value is the product-owned policy and package: local export,
application-specific contributors, privacy filtering, user inspectability,
timeline correlation, and resilience when individual diagnostics fail.

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

## SDK API Checklist

The first SDK pass should make the support-pack builder usable without requiring
the flight recorder to be complete.

Core setup:

```csharp
BugTape.Initialize(new BugTapeOptions
{
    ApplicationName = "Example Desktop App",
    ApplicationVersion = version,
    DataDirectory = appDataDirectory,
    SessionId = Guid.NewGuid().ToString("N")
});

BugTape.ShutdownCleanly();
```

Minimum public API:

- `BugTape.Initialize(BugTapeOptions options)`
- `BugTape.ShutdownCleanly()`
- `BugTape.Record(string name, object data = null)`
- `BugTape.Log(BugTapeLogLevel level, string message, object data = null)`
- `BugTape.Log(BugTapeLogLevel level, string message, Exception exception,
  object data = null)`
- `BugTape.StartAction(string name, object data = null)`
- `BugTape.Track(string name, Action action, object data = null)`
- `BugTape.TrackAsync(string name, Func<Task> action, object data = null)`
- `BugTape.RegisterStateProvider(string name, Func<object> capture)`
- `BugTape.RegisterContributor(IBugTapeContributor contributor)`
- `BugTape.CreateSupportPackAsync(FileInfo destination)`
- `BugTape.CreateSupportPackFilesAsync(DirectoryInfo destinationDirectory)`

`CreateSupportPackFilesAsync` is important for applications whose existing
support-package command already owns the final zip. The method should return
the files BugTape created, for example `bugtape-manifest.json`,
`bugtape-timeline.jsonl`, `bugtape-state-application.json`, and
`bugtape-redaction-report.json`, so the host application can include them via
its existing extra-file hook.

Suggested first interfaces:

```csharp
public interface IBugTapeContributor
{
    string Name { get; }
    Task<IReadOnlyCollection<FileInfo>> CollectAsync(
        DirectoryInfo destinationDirectory,
        BugTapeExportContext context,
        CancellationToken cancellationToken);
}

public interface IBugTapeAction : IDisposable
{
    string Id { get; }
    void Add(string name, object value);
    void Fail(Exception exception);
    void Cancel(string reason = null);
}
```

First built-in contributors:

- `ExistingFileContributor`
- `ExistingDirectoryContributor`
- `JsonStateContributor`
- `TimelineContributor`
- `ManifestContributor`
- `RedactionReportContributor`

First options:

- application name, version, company, environment, and session ID;
- data directory for any temporary or later recovery files;
- maximum retained event count and approximate payload bytes;
- maximum string length, collection length, object depth, and event payload
  size;
- before-record and before-export hooks for redaction or suppression;
- clock abstraction for tests;
- logger callback for BugTape self-diagnostics that must not feed back into
  the host application's log adapter.

The first implementation can keep the timeline in memory and write JSON Lines
only during export. A bounded crash-recovery journal can be added after a real
host application integration proves the basic API shape.

## Initial API Decisions

The first intended integration is a production desktop application built with
WPF or Avalonia. The API must remain application-neutral, but should make it
easy to describe operations such as importing a document into a work queue.

### Actions

An action is a named, correlated unit of work. Starting an action returns a
disposable scope. Disposing the scope records a successful completion unless
`Fail` was called:

```csharp
var sourceFile = new FileInfo(path);

using (var action = BugTape.StartAction("Document.Import", new
{
    FileName = sourceFile.Name,
    FileSizeBytes = sourceFile.Length
}))
{
    try
    {
        var document = await queue.ImportAsync(sourceFile.FullName);

        action.Add("DocumentId", document.Id);
        action.Add("PageCount", document.PageCount);
    }
    catch (Exception exception)
    {
        action.Fail(exception);
        throw;
    }
}
```

Action names such as `Document.Import` should be stable, application-defined
identifiers. Human-readable messages and arbitrary structured data are stored
separately. Events, logs, screenshots, metrics, and nested actions recorded
inside the scope inherit its action/correlation ID.

`Track` and `TrackAsync` convenience methods should run a delegate, mark the
action as failed if the delegate throws, and rethrow the exception. A plain
disposable scope cannot reliably detect an exception escaping from a `using`
block, so callers using the scope API must call `Fail` themselves.

Caught exceptions can be recorded explicitly. Platform integrations can also
register global handlers for unhandled exceptions.

### Logging

Logging is a first-class structured event source with levels for `Error`,
`Warning`, `Information`, and `Debug`:

```csharp
BugTape.Log(BugTapeLogLevel.Information, "Import started");
BugTape.Log(BugTapeLogLevel.Warning, "Validation warning", data);
BugTape.Log(BugTapeLogLevel.Error, "Import failed", exception);
BugTape.Log(BugTapeLogLevel.Debug, "Parser selected", debugData);
```

Applications with an existing logger can subscribe to its logged event and map
each entry to `BugTape.Log`. The underlying log file can also be registered as
a support-pack contributor. This preserves the authoritative log while allowing
important live entries to be correlated with actions. Adapters must guard
against logging feedback loops.

### Structured Export

The rolling timeline is stored as JSON Lines (`.jsonl`): one complete JSON
object per line. This makes the file appendable, streamable, and more tolerant
of a process ending during a write than a single large JSON array.

```json
{"timestampUtc":"2026-07-05T14:32:10.1234567Z","type":"action-start","actionId":"42","name":"Document.Import"}
{"timestampUtc":"2026-07-05T14:32:11.4560000Z","type":"log","actionId":"42","level":"warning","message":"Validation warning"}
{"timestampUtc":"2026-07-05T14:32:13.7890000Z","type":"action-end","actionId":"42","outcome":"success"}
```

Exports should support:

- JSON Lines for efficient processing and larger timelines.
- A self-contained JSON document for a selected incident.
- A ZIP support pack containing the timeline, logs, screenshots, state,
  metrics, and manifest.

Selection should be composable and include:

- The most recently started top-level action, including correlated child data.
- A particular action or correlation ID.
- Everything since a timestamp.
- An explicit time range.
- A recent duration, such as the last 15 minutes.
- Everything currently retained.

Action-based exports may include a configurable period immediately before and
after the action so that relevant context is not lost.

### Locale-Independent Data

Serialized diagnostic data must not depend on the machine's locale:

- Timestamps use `DateTimeOffset`, are normalized to UTC, and are written in
  ISO 8601 round-trip form, for example `2026-07-05T14:32:10.1234567Z`.
- Durations are stored as numeric milliseconds or ticks.
- Numbers are JSON numbers rather than localized strings.
- Levels, outcomes, event types, and other schema values use stable identifiers.
- The manifest records the originating time-zone ID, UTC offset, culture, and
  UI culture so a viewer can render times appropriately.

### File and Directory Values

BugTape's primary APIs and internal implementation should use `FileInfo` and
`DirectoryInfo` for filesystem values. String overloads should remain available
for convenience and compatibility, but should convert to `FileInfo` or
`DirectoryInfo` at the API boundary:

```csharp
Task CreateSupportPackAsync(FileInfo destination);
Task CreateSupportPackAsync(string destination);

void RegisterLogFile(FileInfo file);
void RegisterLogFile(string file);
```

The same rule applies to contributors, attachments, screenshot destinations,
and export locations.

### Reliability, Retention, and Host Isolation

BugTape must not destabilise, noticeably pause, or consume unbounded resources
in the host application.

- The primary rolling buffer is memory-first and uses compact immutable event
  records with strict limits on event count, age, and estimated byte size.
- Recording is fast and non-blocking. Serialization, compression, screenshots,
  and filesystem work happen away from the UI thread.
- When buffers are full, configurable overload policy discards lower-priority
  events such as `Debug` records before warnings, errors, or action boundaries.
  Dropped-event counts and reasons are themselves reported.
- Retention is action-aware where possible. If an action boundary has been
  evicted, exports mark the action or time range as truncated.
- All collection and serialization failures are contained within BugTape and
  cannot be allowed to fail the host application's operation.
- Shutdown flushing is explicitly bounded and must never cause the application
  to hang indefinitely.

Although normal operation is memory-first, recovery after a process crash
requires a minimal form of persistence. BugTape therefore maintains a small,
bounded, append-only recovery journal asynchronously. Critical records such as
action starts, warnings, and errors may be flushed more aggressively than
lower-priority data. On the next initialization, BugTape detects a journal left
by an unclean shutdown and creates a recovery support pack from the last
recorded information before starting a fresh session. Recovery-pack creation
must tolerate an incomplete final record.

The recovery journal lives in an application-controlled per-user data directory,
not a shared temporary directory, and uses the most restrictive practical file
permissions on the host platform. Journal, screenshot, temporary, and recovered
pack files all have explicit age, count, and byte limits. Recovery consumes or
quarantines a journal atomically so a corrupt journal cannot cause a package to
be recreated on every application start. Optional support-pack encryption may
be added later; it is not required for the first usable slice.

Support packs are assembled into a temporary file and atomically moved to their
destination only after successful completion. A failed export must not leave a
file that appears to be a valid finished pack.

### Action Outcomes and Correlation

- Disposing an action records success unless `Fail` or `Cancel` was called.
- Cancellation is distinct from failure.
- If recording ends without an action-end record, such as after a process
  crash, the action is presented as `Incomplete`.
- Ambient action context flows across asynchronous calls, for example using
  `AsyncLocal`, so nested events and logs are correlated automatically.
- Parallel actions remain independent and receive distinct IDs.
- Each event has a strictly increasing session sequence number in addition to
  its UTC timestamp.
- Durations use a monotonic clock rather than subtracting wall-clock timestamps,
  because the system clock may change during an action.

### Safe Structured Data

Arbitrary action and event data must be snapshotted defensively. The serializer
enforces limits for object depth, string length, collection length, and total
payload size; handles cycles and throwing property getters; and reports
truncation or serialization failure without affecting the host application.

Paths, document names, workspace names, and other metadata may contain personal
or commercially sensitive information. The API should provide explicit privacy
transformations such as `Sensitive`, `Redact`, `Hash`, and `FileNameOnly`. File
contents and complete paths are not captured by default. Receiving a `FileInfo`
or `DirectoryInfo` does not itself cause BugTape to open, hash, enumerate, or
test the filesystem object.

Repeated log messages and error storms must not exhaust the buffer. Logging can
apply configurable rate limiting or deduplication while preserving severity,
first and last occurrence times, and an occurrence count.

### Schema and Compatibility

Every event record and support-pack manifest carries a schema version. Readers
should ignore unknown fields where safe, allowing newer BugTape producers and
older viewers to interoperate. Format changes should be additive where
possible, and test fixtures should preserve representative packs from older
schema versions.

### Relationship to Existing SDK Patterns

BugTape combines established ideas rather than introducing an unusual telemetry
model:

- Its structured timeline resembles
  [Sentry breadcrumbs](https://docs.sentry.io/platforms/javascript/guides/svelte/enriching-events/breadcrumbs/),
  including category, message, level, timestamp, arbitrary data, and a hook
  that can transform or discard a record.
- Its actions resemble
  [OpenTelemetry spans](https://opentelemetry.io/docs/concepts/signals/traces/),
  which have names, parent/child relationships, timestamps, attributes, events,
  and status. Correlating logs and metrics through ambient context follows the
  same model.
- .NET's
  [`System.Diagnostics.Activity`](https://learn.microsoft.com/dotnet/api/system.diagnostics.activity)
  already represents a disposable operation with IDs, tags, duration, and an
  ambient current value that flows across asynchronous calls.
- Its recovery journal and explicit local export resemble
  [Crashpad's pending/completed report database](https://chromium.googlesource.com/crashpad/crashpad/+/main/tools/crashpad_database_util.md),
  while deliberately omitting automatic upload.
- Privacy-first screenshots and configurable capture policies are consistent
  with established session-replay SDKs, which mask content by default and
  sample expensive capture separately from ordinary telemetry.

BugTape's distinguishing purpose is to keep this evidence local, combine it
with application-defined support-pack material, and remain useful without a
hosted telemetry service.

Before-record and before-export processing hooks should allow applications to
redact, transform, or discard records. Actions should eventually support links
as well as parent/child relationships: for example, an import action can link
to a later background-processing action. BugTape should also interoperate with
`System.Diagnostics.Activity` when it is already in use, without requiring
applications to instrument the same operation twice.

Each run has a session ID plus explicit clean-start and clean-shutdown markers.
These distinguish a crash from a machine restart or an export interrupted for
another reason. BugTape also exposes small self-diagnostic counters such as
records accepted, records dropped, serialization failures, journal failures,
and last successful flush time.

### Recommended First Usable Slice

The complete design is intentionally broader than the first release. The first
production integration should concentrate on:

- Initialization, options, session identity, and clean-shutdown detection.
- `StartAction`, `Fail`, `Cancel`, `Track`, and `TrackAsync`.
- Structured `Record`, `Log`, and exception capture.
- Ambient correlation and stable sequence IDs.
- A bounded memory buffer plus a small crash-recovery journal.
- One resilient ZIP support-pack format containing a versioned JSON Lines
  timeline, manifest, existing log files, and application-provided state.
- UTC/locale-safe serialization, privacy processing hooks, and hard size limits.
- `FileInfo` and `DirectoryInfo` primary APIs with string overloads.
- Manual window-only screenshot capture for WPF and Avalonia, with explicit
  sensitive regions.

Automatic UI instrumentation, sophisticated screenshot masking, process
metrics, log-storm deduplication, Activity/OpenTelemetry adapters, multiple
standalone export formats, and the graphical viewer can follow after the core
format and recording semantics have been exercised in a real application. This
keeps the public API small without closing off those capabilities.

## Proposed Projects

### BugTape.Core

The platform-neutral foundation:

- Timeline and ring buffer.
- Event schema and serialization.
- State-provider and contributor APIs.
- Support-pack assembly.
- Size and retention policies.
- Redaction primitives.

The core API should target `netstandard2.0` so it can be consumed by .NET
Framework 4.7 and .NET 7 or later.

### BugTape.Wpf

Optional integration for WPF applications:

- Window and application lifecycle integration.
- Window-only screenshot capture and sensitive-region masking.
- UI-thread-safe state collection.
- Dispatcher and unhandled-exception integration.

### BugTape.Avalonia

Optional integration for Avalonia applications:

- Window and navigation breadcrumbs.
- Routed pointer and control interactions.
- Window-only screenshot capture.
- Sensitive-control masking.
- UI-thread-safe state collection.
- Exception and dispatcher integration.

UI events should be recorded semantically, using control names or application-provided descriptions, rather than as raw input.

Screenshot capture is deliberately limited to the application's window for
privacy. BugTape does not capture the desktop or unrelated application windows.
Implementations must account for DPI scaling and capture failures, execute
UI-sensitive work on the correct dispatcher, and apply sensitive-region masks
before screenshot bytes enter the rolling buffer or recovery journal.

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

Optional process metrics should be sampled at a low configurable frequency.
Cross-platform measurements can include process CPU usage, working set, and
managed-memory usage. CPU can be derived from `Process.TotalProcessorTime`,
elapsed wall time, and processor count on both Windows and macOS. Metrics use
the same bounded retention policy as other timeline data.

For document-processing applications, useful application-defined fields include
operation IDs, queue transitions, document metadata, processing phases, service
state changes, durations, and result codes. Document contents, complete paths,
and personally identifying data remain excluded by default.

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
