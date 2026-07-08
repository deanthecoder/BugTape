// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BugTape.Viewer.Models;

public partial class TimelineTreeNode : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    public string Kind { get; init; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public string FullText => string.IsNullOrWhiteSpace(Detail)
        ? Title
        : $"{Title} - {Detail}";

    public string Timestamp { get; init; } = string.Empty;

    public string Brush { get; set; } = "#4a5568";

    public string Json { get; set; } = string.Empty;

    public string LogExcerpt { get; set; } = string.Empty;

    public BugTapeRecord Record { get; init; }

    public double TimelineLeft { get; set; }

    public double TimelineWidth { get; set; }

    public ObservableCollection<TimelineTreeNode> Children { get; } = new();
}
