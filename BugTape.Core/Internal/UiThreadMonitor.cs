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
using System.Threading;

namespace BugTape.Core.Internal;

internal sealed class UiThreadMonitor : IDisposable
{
    private readonly object m_sync = new object();
    private readonly BugTapeRuntime m_runtime;
    private readonly Action<Action> m_postToUiThread;
    private readonly BugTapeUiThreadMonitorOptions m_options;
    private readonly Action<UiThreadMonitor> m_disposed;
    private readonly Timer m_timer;
    private bool m_disposeStarted;
    private bool m_hasPendingHeartbeat;

    public UiThreadMonitor(
        BugTapeRuntime runtime,
        Action<Action> postToUiThread,
        BugTapeUiThreadMonitorOptions options,
        Action<UiThreadMonitor> disposed)
    {
        m_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        m_postToUiThread = postToUiThread ?? throw new ArgumentNullException(nameof(postToUiThread));
        m_options = options ?? throw new ArgumentNullException(nameof(options));
        m_disposed = disposed ?? throw new ArgumentNullException(nameof(disposed));
        m_timer = new Timer(
            OnTimer,
            null,
            m_options.SampleInterval,
            m_options.SampleInterval);
    }

    public void Dispose()
    {
        lock (m_sync)
        {
            if (m_disposeStarted)
                return;

            m_disposeStarted = true;
        }

        m_timer.Dispose();
        m_disposed(this);
    }

    private void OnTimer(object state)
    {
        if (!TryBeginHeartbeat())
            return;

        var postedUtc = DateTimeOffset.UtcNow;
        try
        {
            m_postToUiThread(() => OnHeartbeat(postedUtc));
        }
        catch (Exception exception)
        {
            EndHeartbeat();
            m_runtime.ReportDiagnostic("UI thread monitor failed to post a heartbeat: " + exception.Message);
        }
    }

    private bool TryBeginHeartbeat()
    {
        lock (m_sync)
        {
            if (m_disposeStarted || m_hasPendingHeartbeat || !m_runtime.IsActive)
                return false;

            m_hasPendingHeartbeat = true;
            return true;
        }
    }

    private void EndHeartbeat()
    {
        lock (m_sync)
            m_hasPendingHeartbeat = false;
    }

    private void OnHeartbeat(DateTimeOffset postedUtc)
    {
        lock (m_sync)
        {
            if (m_disposeStarted)
                return;

            m_hasPendingHeartbeat = false;
        }

        var delay = DateTimeOffset.UtcNow - postedUtc;
        if (delay >= m_options.ErrorThreshold)
        {
            RecordDelay(BugTapeLogLevel.Error, delay);
            return;
        }

        if (delay >= m_options.WarningThreshold)
            RecordDelay(BugTapeLogLevel.Warning, delay);
    }

    private void RecordDelay(BugTapeLogLevel level, TimeSpan delay)
    {
        if (!m_runtime.IsActive)
            return;

        try
        {
            m_runtime.Log(
                level,
                "UI thread heartbeat delayed.",
                null,
                new
                {
                    DelayMilliseconds = delay.TotalMilliseconds,
                    SampleIntervalMilliseconds = m_options.SampleInterval.TotalMilliseconds,
                    WarningThresholdMilliseconds = m_options.WarningThreshold.TotalMilliseconds,
                    ErrorThresholdMilliseconds = m_options.ErrorThreshold.TotalMilliseconds
                });
        }
        catch (InvalidOperationException)
        {
            // Shutdown can race with a posted UI callback.
        }
        catch (Exception exception)
        {
            m_runtime.ReportDiagnostic("UI thread monitor failed to record a delayed heartbeat: " + exception.Message);
        }
    }
}
