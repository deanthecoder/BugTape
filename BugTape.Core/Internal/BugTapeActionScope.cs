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
using System.Diagnostics;
using System.Threading;

namespace BugTape.Core.Internal;

internal sealed class BugTapeActionScope : IRecordedAction
{
    private readonly BugTapeRuntime m_runtime;
    private readonly Stopwatch m_stopwatch;
    private int m_disposed;
    private string m_outcome = "success";
    private Exception m_exception;
    private string m_cancellationReason;

    public BugTapeActionScope(
        BugTapeRuntime runtime,
        string id,
        string name,
        BugTapeActionScope parent)
    {
        m_runtime = runtime;
        Id = id;
        Name = name;
        Parent = parent;
        StartMetrics = ProcessMetricsSnapshot.Capture();
        m_stopwatch = Stopwatch.StartNew();
    }

    public string Id { get; }

    internal string Name { get; }

    internal BugTapeActionScope Parent { get; }

    internal ProcessMetricsSnapshot StartMetrics { get; }

    public void Add(string name, object value)
    {
        EnsureNotDisposed();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("An action-data name is required.", nameof(name));

        m_runtime.AddActionData(this, name, value);
    }

    public void Fail(Exception exception)
    {
        EnsureNotDisposed();
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));

        m_outcome = "failed";
        m_exception = exception;
        m_cancellationReason = null;
    }

    public void Cancel(string reason = null)
    {
        EnsureNotDisposed();
        m_outcome = "canceled";
        m_exception = null;
        m_cancellationReason = reason;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            return;

        m_stopwatch.Stop();
        m_runtime.EndAction(
            this,
            m_outcome,
            m_stopwatch.Elapsed.TotalMilliseconds,
            m_exception,
            m_cancellationReason);
    }

    private void EnsureNotDisposed()
    {
        if (Volatile.Read(ref m_disposed) != 0)
            throw new ObjectDisposedException(nameof(BugTapeActionScope));
    }
}
