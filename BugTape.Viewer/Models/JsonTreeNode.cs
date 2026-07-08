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

namespace BugTape.Viewer.Models;

public sealed class JsonTreeNode
{
    public string Name { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public bool IsExpanded { get; init; }

    public string FullText => string.IsNullOrWhiteSpace(Value)
        ? Name
        : $"{Name}: {Value}";

    public ObservableCollection<JsonTreeNode> Children { get; } = new();
}
