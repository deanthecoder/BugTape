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
/// Represents a named unit of application work recorded by BugTape.
/// </summary>
/// <remarks>
/// Disposing an action records success unless the caller first marks it as
/// failed or canceled. Events recorded inside the scope are correlated with it.
/// </remarks>
public interface IRecordedAction : IDisposable
{
    /// <summary>
    /// Gets the unique identifier assigned to the action.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Adds structured information to the action timeline.
    /// </summary>
    /// <param name="name">The stable name of the value.</param>
    /// <param name="value">The value to capture.</param>
    void Add(string name, object value);

    /// <summary>
    /// Marks the action as failed.
    /// </summary>
    /// <param name="exception">The exception associated with the failure.</param>
    void Fail(Exception exception);

    /// <summary>
    /// Marks the action as canceled.
    /// </summary>
    /// <param name="reason">An optional non-sensitive cancellation reason.</param>
    void Cancel(string reason = null);
}
