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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BugTape.Core.Internal;

internal sealed class BugTapeRuntime
{
    private readonly object m_sync = new object();
    private readonly LinkedList<TimelineRecord> m_timeline = new LinkedList<TimelineRecord>();
    private readonly LinkedList<MetricSample> m_metricSamples = new LinkedList<MetricSample>();
    private readonly List<StateProviderRegistration> m_stateProviders = new List<StateProviderRegistration>();
    private readonly List<FileInfo> m_registeredFiles = new List<FileInfo>();
    private readonly List<UiThreadMonitor> m_uiThreadMonitors = new List<UiThreadMonitor>();
    private readonly AsyncLocal<BugTapeActionScope> m_currentAction = new AsyncLocal<BugTapeActionScope>();
    private readonly DataSnapshotter m_snapshotter;
    private readonly Timer m_metricsTimer;
    private long m_sequence;
    private long m_metricSequence;
    private long m_retainedBytes;
    private ProcessMetricsSnapshot m_lastMetricSnapshot;
    private DateTimeOffset m_lastMetricSnapshotUtc;
    private bool m_isSamplingMetrics;
    private bool m_shutdown;

    public BugTapeRuntime(BugTapeOptions options)
    {
        Options = options;
        m_snapshotter = new DataSnapshotter(options);
        StartedUtc = DateTimeOffset.UtcNow;

        AddRecord(new TimelineRecord
        {
            Type = "session-start",
            Name = "Session.Start",
            Data = m_snapshotter.Capture(new
            {
                options.ApplicationName,
                options.ApplicationVersion,
                options.SessionId
            })
        });

        if (options.MetricsSampleInterval != TimeSpan.Zero)
        {
            m_metricsTimer = new Timer(
                CaptureMetricSample,
                null,
                TimeSpan.Zero,
                options.MetricsSampleInterval);
        }
    }

    public BugTapeOptions Options { get; }

    public DateTimeOffset StartedUtc { get; }

    public bool IsActive
    {
        get
        {
            lock (m_sync)
                return !m_shutdown;
        }
    }

    public void Record(string name, object data)
    {
        EnsureActive();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("An event name is required.", nameof(name));

        AddRecord(new TimelineRecord
        {
            Type = "event",
            Name = m_snapshotter.CaptureText(name),
            ActionId = m_currentAction.Value?.Id,
            Data = m_snapshotter.Capture(data)
        });
    }

    public void Log(BugTapeLogLevel level, string message, Exception exception, object data)
    {
        EnsureActive();
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("A log message is required.", nameof(message));

        AddRecord(new TimelineRecord
        {
            Type = "log",
            Message = m_snapshotter.CaptureText(message),
            Level = ToSerializedLevel(level),
            ActionId = m_currentAction.Value?.Id,
            Data = m_snapshotter.Capture(data),
            Exception = m_snapshotter.CaptureException(exception)
        });
    }

    public BugTapeActionScope StartAction(string name, object data)
    {
        EnsureActive();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("An action name is required.", nameof(name));

        var parent = m_currentAction.Value;
        var action = new BugTapeActionScope(
            this,
            Guid.NewGuid().ToString("N"),
            m_snapshotter.CaptureText(name),
            parent);

        AddRecord(new TimelineRecord
        {
            Type = "action-start",
            Name = action.Name,
            ActionId = action.Id,
            ParentActionId = parent?.Id,
            Data = m_snapshotter.Capture(data),
            Metrics = action.StartMetrics.ToStartJson()
        });
        m_currentAction.Value = action;

        return action;
    }

    public void AddActionData(BugTapeActionScope action, string name, object value)
    {
        EnsureActive();
        AddRecord(new TimelineRecord
        {
            Type = "action-data",
            Name = m_snapshotter.CaptureText(name),
            ActionId = action.Id,
            Data = m_snapshotter.Capture(value)
        });
    }

    public void EndAction(
        BugTapeActionScope action,
        string outcome,
        double durationMilliseconds,
        Exception exception,
        string cancellationReason)
    {
        EnsureActive();

        JToken data = null;
        if (!string.IsNullOrWhiteSpace(cancellationReason))
            data = m_snapshotter.Capture(new { Reason = cancellationReason });
        var endMetrics = ProcessMetricsSnapshot.Capture();

        AddRecord(new TimelineRecord
        {
            Type = "action-end",
            Name = action.Name,
            ActionId = action.Id,
            ParentActionId = action.Parent?.Id,
            Outcome = outcome,
            DurationMilliseconds = durationMilliseconds,
            Data = data,
            Exception = m_snapshotter.CaptureException(exception),
            Metrics = endMetrics.ToEndJson(action.StartMetrics, durationMilliseconds)
        });

        if (ReferenceEquals(m_currentAction.Value, action))
            m_currentAction.Value = action.Parent;
        else
            ReportDiagnostic("An action scope was disposed out of order.");
    }

    public void RegisterStateProvider(
        string name,
        Func<CancellationToken, Task<object>> captureAsync)
    {
        EnsureActive();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A state-provider name is required.", nameof(name));
        if (captureAsync == null)
            throw new ArgumentNullException(nameof(captureAsync));

        lock (m_sync)
        {
            if (m_stateProviders.Exists(provider =>
                    string.Equals(provider.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"A state provider named '{name}' is already registered.");
            }

            m_stateProviders.Add(new StateProviderRegistration
            {
                Name = name,
                CaptureAsync = captureAsync
            });
        }
    }

    public void RegisterFile(FileInfo file)
    {
        EnsureActive();
        if (file == null)
            throw new ArgumentNullException(nameof(file));

        lock (m_sync)
            m_registeredFiles.Add(new FileInfo(file.FullName));
    }

    public IDisposable MonitorUiThread(
        Action<Action> postToUiThread,
        BugTapeUiThreadMonitorOptions options)
    {
        EnsureActive();
        if (postToUiThread == null)
            throw new ArgumentNullException(nameof(postToUiThread));
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        var monitor = new UiThreadMonitor(
            this,
            postToUiThread,
            options,
            RemoveUiThreadMonitor);

        lock (m_sync)
        {
            if (m_shutdown)
            {
                monitor.Dispose();
                throw new InvalidOperationException("BugTape has been shut down.");
            }

            m_uiThreadMonitors.Add(monitor);
        }

        return monitor;
    }

    public void Shutdown()
    {
        List<UiThreadMonitor> monitors;
        lock (m_sync)
        {
            if (m_shutdown)
                return;

            AddRecord(new TimelineRecord
            {
                Type = "session-end",
                Name = "Session.End",
                Data = m_snapshotter.Capture(new { Clean = true })
            });

            m_shutdown = true;
            m_metricsTimer?.Dispose();
            monitors = new List<UiThreadMonitor>(m_uiThreadMonitors);
            m_uiThreadMonitors.Clear();
        }

        foreach (var monitor in monitors)
            monitor.Dispose();
    }

    public IReadOnlyCollection<TimelineRecord> SnapshotTimeline()
    {
        EnsureActive();
        lock (m_sync)
            return new List<TimelineRecord>(m_timeline);
    }

    public IReadOnlyCollection<StateProviderRegistration> SnapshotStateProviders()
    {
        EnsureActive();
        lock (m_sync)
            return new List<StateProviderRegistration>(m_stateProviders);
    }

    public IReadOnlyCollection<FileInfo> SnapshotRegisteredFiles()
    {
        EnsureActive();
        lock (m_sync)
            return new List<FileInfo>(m_registeredFiles);
    }

    public IReadOnlyCollection<MetricSample> SnapshotMetricSamples()
    {
        EnsureActive();
        lock (m_sync)
            return new List<MetricSample>(m_metricSamples);
    }

    public JToken CaptureState(object value)
    {
        return m_snapshotter.Capture(value);
    }

    private void AddRecord(TimelineRecord record)
    {
        record.Sequence = Interlocked.Increment(ref m_sequence);
        record.TimestampUtc = DateTimeOffset.UtcNow;
        record.Measure();

        lock (m_sync)
        {
            if (m_shutdown)
                throw new InvalidOperationException("BugTape has been shut down.");

            m_timeline.AddLast(record);
            m_retainedBytes += record.SerializedByteCount;

            while (m_timeline.Count > Options.MaxRetainedEventCount ||
                   m_retainedBytes > Options.MaxRetainedPayloadBytes)
            {
                var first = m_timeline.First;
                if (first == null)
                    break;

                m_timeline.RemoveFirst();
                m_retainedBytes -= first.Value.SerializedByteCount;
            }
        }
    }

    private void CaptureMetricSample(object state)
    {
        try
        {
            DateTimeOffset now;
            ProcessMetricsSnapshot previousSnapshot;
            DateTimeOffset previousSnapshotUtc;
            lock (m_sync)
            {
                if (m_shutdown || m_isSamplingMetrics)
                    return;

                m_isSamplingMetrics = true;
                previousSnapshot = m_lastMetricSnapshot;
                previousSnapshotUtc = m_lastMetricSnapshotUtc;
            }

            ProcessMetricsSnapshot snapshot;
            try
            {
                now = DateTimeOffset.UtcNow;
                snapshot = ProcessMetricsSnapshot.Capture();
            }
            finally
            {
                lock (m_sync)
                    m_isSamplingMetrics = false;
            }

            var durationMilliseconds = previousSnapshot == null
                ? 0
                : Math.Max(0, (now - previousSnapshotUtc).TotalMilliseconds);
            var sample = new MetricSample
            {
                Sequence = Interlocked.Increment(ref m_metricSequence),
                TimestampUtc = now,
                Metrics = snapshot.ToSampleJson(previousSnapshot, durationMilliseconds)
            };

            lock (m_sync)
            {
                if (m_shutdown)
                    return;

                m_lastMetricSnapshot = snapshot;
                m_lastMetricSnapshotUtc = now;
                m_metricSamples.AddLast(sample);
                while (m_metricSamples.Count > Options.MaxRetainedMetricSampleCount)
                    m_metricSamples.RemoveFirst();
            }
        }
        catch (Exception exception)
        {
            ReportDiagnostic("Failed to capture process metrics: " + exception.Message);
        }
    }

    private void EnsureActive()
    {
        lock (m_sync)
        {
            if (m_shutdown)
                throw new InvalidOperationException("BugTape has been shut down.");
        }
    }

    public void ReportDiagnostic(string message)
    {
        try
        {
            Options.DiagnosticMessageHandler?.Invoke(message);
        }
        catch
        {
            // Host diagnostics must never interfere with recording.
        }
    }

    private void RemoveUiThreadMonitor(UiThreadMonitor monitor)
    {
        lock (m_sync)
            m_uiThreadMonitors.Remove(monitor);
    }

    private static string ToSerializedLevel(BugTapeLogLevel level)
    {
        switch (level)
        {
            case BugTapeLogLevel.Debug:
                return "debug";
            case BugTapeLogLevel.Information:
                return "information";
            case BugTapeLogLevel.Warning:
                return "warning";
            case BugTapeLogLevel.Error:
                return "error";
            default:
                throw new ArgumentOutOfRangeException(nameof(level), level, null);
        }
    }
}
