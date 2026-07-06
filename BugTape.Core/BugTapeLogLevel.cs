// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace BugTape;

/// <summary>
/// Identifies the significance of a log entry recorded by BugTape.
/// </summary>
/// <remarks>
/// The levels intentionally match the common levels exposed by desktop
/// application loggers so existing log events can be forwarded directly.
/// </remarks>
public enum BugTapeLogLevel
{
    /// <summary>
    /// Diagnostic information intended primarily for developers.
    /// </summary>
    Debug,

    /// <summary>
    /// Information describing normal application behavior.
    /// </summary>
    Information,

    /// <summary>
    /// A recoverable or potentially problematic condition.
    /// </summary>
    Warning,

    /// <summary>
    /// A failed operation or other error condition.
    /// </summary>
    Error
}
