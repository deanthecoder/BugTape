// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace BugTape.Core.Tests;

internal sealed class TestDirectory : IDisposable
{
    public TestDirectory()
    {
        Directory = new DirectoryInfo(
            Path.Combine(
                Path.GetTempPath(),
                "BugTape.Tests",
                Guid.NewGuid().ToString("N")));
        Directory.Create();
    }

    public DirectoryInfo Directory { get; }

    public FileInfo GetFile(string name)
    {
        return new FileInfo(Path.Combine(Directory.FullName, name));
    }

    public DirectoryInfo GetDirectory(string name)
    {
        return new DirectoryInfo(Path.Combine(Directory.FullName, name));
    }

    public void Dispose()
    {
        Directory.Refresh();
        if (Directory.Exists)
            Directory.Delete(true);
    }
}
