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
/// Configures UI-thread responsiveness monitoring.
/// </summary>
/// <remarks>
/// The monitor periodically posts a heartbeat callback to the host
/// application's UI dispatcher and records a BugTape log event when the
/// callback is delayed beyond the configured thresholds.
/// </remarks>
public sealed class BugTapeUiThreadMonitorOptions
{
    /// <summary>
    /// Gets or sets how often BugTape posts a UI-thread heartbeat.
    /// </summary>
    /// <remarks>
    /// A shorter interval detects stalls sooner but wakes a background timer
    /// more often. The default is one second.
    /// </remarks>
    public TimeSpan SampleInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the delay that records a warning log event.
    /// </summary>
    /// <remarks>
    /// Delays below this threshold are treated as normal UI scheduling noise.
    /// The default is 500 milliseconds.
    /// </remarks>
    public TimeSpan WarningThreshold { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets or sets the delay that records an error log event.
    /// </summary>
    /// <remarks>
    /// Delays at or above this threshold indicate a more serious UI stall. The
    /// default is two seconds.
    /// </remarks>
    public TimeSpan ErrorThreshold { get; set; } = TimeSpan.FromSeconds(2);

    internal BugTapeUiThreadMonitorOptions Snapshot()
    {
        Validate();
        return new BugTapeUiThreadMonitorOptions
        {
            SampleInterval = SampleInterval,
            WarningThreshold = WarningThreshold,
            ErrorThreshold = ErrorThreshold
        };
    }

    private void Validate()
    {
        if (SampleInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SampleInterval));
        if (WarningThreshold <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(WarningThreshold));
        if (ErrorThreshold < WarningThreshold)
            throw new ArgumentOutOfRangeException(nameof(ErrorThreshold));
    }
}
