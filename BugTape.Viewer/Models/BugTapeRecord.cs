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

namespace BugTape.Viewer.Models;

public sealed class BugTapeRecord
{
    public long Sequence { get; init; }

    public string Type { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Level { get; init; } = string.Empty;

    public string Outcome { get; init; } = string.Empty;

    public string ActionId { get; init; } = string.Empty;

    public string ParentActionId { get; init; } = string.Empty;

    public DateTimeOffset TimestampUtc { get; init; }

    public double? DurationMilliseconds { get; init; }

    public double TimelineLeft { get; set; }

    public double TimelineWidth { get; set; }

    public string Summary { get; init; } = string.Empty;

    public string Json { get; init; } = string.Empty;
}
