// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Collections.Generic;
using Avalonia;

namespace BugTape.Viewer.Models;

public sealed class MetricSeries
{
    public string Key { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string Brush { get; init; } = "#0ea5e9";

    public IReadOnlyList<Point> Points { get; init; } = new List<Point>();

    public IReadOnlyList<MetricSegment> Segments { get; init; } = new List<MetricSegment>();

    public string Summary { get; init; } = string.Empty;
}
