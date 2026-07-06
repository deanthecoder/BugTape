// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System;

namespace BugTape;

/// <summary>
/// Configures the process-wide BugTape recorder.
/// </summary>
/// <remarks>
/// The options define application identity and hard resource limits. BugTape
/// copies the values during initialization, so later changes have no effect.
/// </remarks>
public sealed class BugTapeOptions
{
    /// <summary>
    /// Gets or sets the name of the host application.
    /// </summary>
    public string ApplicationName { get; set; }

    /// <summary>
    /// Gets or sets the version of the host application.
    /// </summary>
    public string ApplicationVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the company or publisher name.
    /// </summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional environment name such as Production or Test.
    /// </summary>
    public string EnvironmentName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the identifier for this application session.
    /// </summary>
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the maximum number of timeline records retained in memory.
    /// </summary>
    public int MaxRetainedEventCount { get; set; } = 2000;

    /// <summary>
    /// Gets or sets the approximate maximum number of serialized timeline bytes retained in memory.
    /// </summary>
    public long MaxRetainedPayloadBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum serialized size of data attached to one record.
    /// </summary>
    public int MaxEventPayloadBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// Gets or sets the maximum length of an individual captured string.
    /// </summary>
    public int MaxStringLength { get; set; } = 4096;

    /// <summary>
    /// Gets or sets the maximum number of items retained from a captured collection.
    /// </summary>
    public int MaxCollectionLength { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum depth of captured structured data.
    /// </summary>
    public int MaxObjectDepth { get; set; } = 8;

    /// <summary>
    /// Gets or sets the maximum size of an existing file copied into an export.
    /// </summary>
    public long MaxRegisteredFileBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>
    /// Gets or sets a callback for BugTape's own non-fatal diagnostic messages.
    /// </summary>
    public Action<string> DiagnosticMessageHandler { get; set; }

    internal BugTapeOptions Snapshot()
    {
        Validate();

        return new BugTapeOptions
        {
            ApplicationName = ApplicationName,
            ApplicationVersion = ApplicationVersion ?? string.Empty,
            CompanyName = CompanyName ?? string.Empty,
            EnvironmentName = EnvironmentName ?? string.Empty,
            SessionId = SessionId,
            MaxRetainedEventCount = MaxRetainedEventCount,
            MaxRetainedPayloadBytes = MaxRetainedPayloadBytes,
            MaxEventPayloadBytes = MaxEventPayloadBytes,
            MaxStringLength = MaxStringLength,
            MaxCollectionLength = MaxCollectionLength,
            MaxObjectDepth = MaxObjectDepth,
            MaxRegisteredFileBytes = MaxRegisteredFileBytes,
            DiagnosticMessageHandler = DiagnosticMessageHandler
        };
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApplicationName))
            throw new ArgumentException("An application name is required.", nameof(ApplicationName));
        if (string.IsNullOrWhiteSpace(SessionId))
            throw new ArgumentException("A session ID is required.", nameof(SessionId));
        if (MaxRetainedEventCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxRetainedEventCount));
        if (MaxRetainedPayloadBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxRetainedPayloadBytes));
        if (MaxEventPayloadBytes <= 0 || MaxEventPayloadBytes > MaxRetainedPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(MaxEventPayloadBytes));
        if (MaxStringLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxStringLength));
        if (MaxCollectionLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxCollectionLength));
        if (MaxObjectDepth <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxObjectDepth));
        if (MaxRegisteredFileBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxRegisteredFileBytes));
    }
}
