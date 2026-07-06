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
using BugTape.Core.Internal;

namespace BugTape;

/// <summary>
/// Records process-wide diagnostic actions, events, logs, and application state.
/// </summary>
/// <remarks>
/// BugTape is a singleton so instrumentation can be called from anywhere in a
/// desktop application without passing a recorder through the object graph.
/// Initialize it once during startup and shut it down during a clean exit.
/// </remarks>
public static class BugTape
{
    private static readonly object s_sync = new object();
    private static BugTapeRuntime s_runtime;
    private static bool s_hasInitialized;

    /// <summary>
    /// Gets a value indicating whether BugTape is initialized and active.
    /// </summary>
    public static bool IsInitialized
    {
        get
        {
            lock (s_sync)
                return s_runtime != null && s_runtime.IsActive;
        }
    }

    /// <summary>
    /// Initializes the process-wide BugTape recorder.
    /// </summary>
    /// <param name="options">The application identity and resource limits to use.</param>
    public static void Initialize(BugTapeOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        lock (s_sync)
        {
            if (s_hasInitialized)
                throw new InvalidOperationException("BugTape has already been initialized.");

            s_runtime = new BugTapeRuntime(options.Snapshot());
            s_hasInitialized = true;
        }
    }

    /// <summary>
    /// Marks the current BugTape session as cleanly shut down.
    /// </summary>
    public static void Shutdown()
    {
        BugTapeRuntime runtime;
        lock (s_sync)
            runtime = s_runtime;

        runtime?.Shutdown();
    }

    /// <summary>
    /// Records a structured point-in-time application event.
    /// </summary>
    /// <param name="name">The stable event name.</param>
    /// <param name="data">Optional structured event data.</param>
    public static void Record(string name, object data = null)
    {
        GetRuntime().Record(name, data);
    }

    /// <summary>
    /// Records a structured log message.
    /// </summary>
    /// <param name="level">The significance of the message.</param>
    /// <param name="message">The human-readable message.</param>
    /// <param name="data">Optional structured log data.</param>
    public static void Log(BugTapeLogLevel level, string message, object data = null)
    {
        GetRuntime().Log(level, message, null, data);
    }

    /// <summary>
    /// Records a structured log message and associated exception.
    /// </summary>
    /// <param name="level">The significance of the message.</param>
    /// <param name="message">The human-readable message.</param>
    /// <param name="exception">The exception associated with the message.</param>
    /// <param name="data">Optional structured log data.</param>
    public static void Log(
        BugTapeLogLevel level,
        string message,
        Exception exception,
        object data = null)
    {
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));
        GetRuntime().Log(level, message, exception, data);
    }

    /// <summary>
    /// Records an exception as an error log entry.
    /// </summary>
    /// <param name="exception">The exception to record.</param>
    /// <param name="message">An optional contextual message.</param>
    /// <param name="data">Optional structured error data.</param>
    public static void Log(
        Exception exception,
        string message = null,
        object data = null)
    {
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));

        Log(
            BugTapeLogLevel.Error,
            string.IsNullOrWhiteSpace(message) ? exception.Message : message,
            exception,
            data);
    }

    /// <summary>
    /// Starts a correlated action that succeeds when its scope is disposed.
    /// </summary>
    /// <param name="name">The stable action name.</param>
    /// <param name="data">Optional structured input data.</param>
    /// <returns>The action scope.</returns>
    public static IRecordedAction StartAction(string name, object data = null)
    {
        return GetRuntime().StartAction(name, data);
    }

    /// <summary>
    /// Runs and records a synchronous action.
    /// </summary>
    /// <param name="name">The stable action name.</param>
    /// <param name="action">The application work to execute.</param>
    /// <param name="data">Optional structured input data.</param>
    public static void Track(string name, Action action, object data = null)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        using var scope = StartAction(name, data);
        try
        {
            action();
        }
        catch (OperationCanceledException)
        {
            scope.Cancel();
            throw;
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    /// <summary>
    /// Runs and records a synchronous action that returns a value.
    /// </summary>
    /// <typeparam name="T">The type returned by the application action.</typeparam>
    /// <param name="name">The stable action name.</param>
    /// <param name="action">The application work to execute.</param>
    /// <param name="data">Optional structured input data.</param>
    /// <returns>The value returned by <paramref name="action"/>.</returns>
    public static T Track<T>(string name, Func<T> action, object data = null)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        using var scope = StartAction(name, data);
        try
        {
            return action();
        }
        catch (OperationCanceledException)
        {
            scope.Cancel();
            throw;
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    /// <summary>
    /// Runs and records an asynchronous action.
    /// </summary>
    /// <param name="name">The stable action name.</param>
    /// <param name="action">The asynchronous application work to execute.</param>
    /// <param name="data">Optional structured input data.</param>
    /// <returns>A task representing the tracked action.</returns>
    public static async Task TrackAsync(
        string name,
        Func<Task> action,
        object data = null)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        using var scope = StartAction(name, data);
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            scope.Cancel();
            throw;
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    /// <summary>
    /// Runs and records an asynchronous action that returns a value.
    /// </summary>
    /// <typeparam name="T">The type returned by the application action.</typeparam>
    /// <param name="name">The stable action name.</param>
    /// <param name="action">The asynchronous application work to execute.</param>
    /// <param name="data">Optional structured input data.</param>
    /// <returns>A task containing the value returned by <paramref name="action"/>.</returns>
    public static async Task<T> TrackAsync<T>(
        string name,
        Func<Task<T>> action,
        object data = null)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        using var scope = StartAction(name, data);
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            scope.Cancel();
            throw;
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    /// <summary>
    /// Registers application state to capture when a support pack is exported.
    /// </summary>
    /// <param name="name">The stable state name.</param>
    /// <param name="capture">The state-capture callback.</param>
    public static void RegisterStateProvider(string name, Func<object> capture)
    {
        if (capture == null)
            throw new ArgumentNullException(nameof(capture));

        GetRuntime().RegisterStateProvider(
            name,
            _ => Task.FromResult(capture()));
    }

    /// <summary>
    /// Registers asynchronous application state to capture during export.
    /// </summary>
    /// <param name="name">The stable state name.</param>
    /// <param name="captureAsync">The asynchronous state-capture callback.</param>
    public static void RegisterStateProvider(
        string name,
        Func<CancellationToken, Task<object>> captureAsync)
    {
        GetRuntime().RegisterStateProvider(name, captureAsync);
    }

    /// <summary>
    /// Registers an existing file for inclusion in future exports.
    /// </summary>
    /// <param name="file">The existing file to include at export time.</param>
    public static void RegisterFile(FileInfo file)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file));
        GetRuntime().RegisterFile(file);
    }

    /// <summary>
    /// Creates BugTape files for inclusion in an application-owned support pack.
    /// </summary>
    /// <param name="destinationDirectory">The export directory.</param>
    /// <param name="cancellationToken">A token that can cancel the export.</param>
    /// <returns>The files created by BugTape.</returns>
    public static Task<IReadOnlyCollection<FileInfo>> CreateSupportPackFilesAsync(
        DirectoryInfo destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        return BugTapeExporter.CreateFilesAsync(
            GetRuntime(),
            destinationDirectory,
            cancellationToken);
    }

    internal static void ResetForTests()
    {
        lock (s_sync)
        {
            s_runtime = null;
            s_hasInitialized = false;
        }
    }

    private static BugTapeRuntime GetRuntime()
    {
        lock (s_sync)
        {
            if (s_runtime == null)
                throw new InvalidOperationException("BugTape has not been initialized.");
            if (!s_runtime.IsActive)
                throw new InvalidOperationException("BugTape has been shut down.");
            return s_runtime;
        }
    }
}
