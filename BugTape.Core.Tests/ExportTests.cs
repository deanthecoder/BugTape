// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Newtonsoft.Json.Linq;
using Recorder = global::BugTape.BugTape;

namespace BugTape.Core.Tests;

[NonParallelizable]
public class ExportTests
{
    private TestDirectory m_testDirectory;

    [SetUp]
    public void SetUp()
    {
        Recorder.ResetForTests();
        m_testDirectory = new TestDirectory();
        Recorder.Initialize(new BugTapeOptions
        {
            ApplicationName = "BugTape Tests",
            ApplicationVersion = "1.2.3",
            CompanyName = "Example Company"
        });
    }

    [TearDown]
    public void TearDown()
    {
        Recorder.ResetForTests();
        m_testDirectory.Dispose();
    }

    [Test]
    public async Task ExportCapturesCurrentFileAndStateAtExportTime()
    {
        var sourceFile = m_testDirectory.GetFile("application.log");
        await File.WriteAllTextAsync(sourceFile.FullName, "Before registration");

        Recorder.RegisterFile(sourceFile);
        Recorder.RegisterStateProvider("application", () => new
        {
            Mode = "Ready",
            ItemCount = 3
        });

        await File.WriteAllTextAsync(sourceFile.FullName, "At export");

        var outputDirectory = m_testDirectory.GetDirectory("output");
        var files = await Recorder.CreateSupportPackFilesAsync(outputDirectory);

        var copiedFile = new FileInfo(
            Path.Combine(outputDirectory.FullName, "bugtape-files", "application.log"));
        var stateFile = new FileInfo(
            Path.Combine(outputDirectory.FullName, "bugtape-state.json"));

        Assert.That(files.Select(file => file.FullName), Does.Contain(copiedFile.FullName));
        Assert.That(files.Select(file => file.FullName), Does.Contain(stateFile.FullName));
        Assert.That(await File.ReadAllTextAsync(copiedFile.FullName), Is.EqualTo("At export"));

        var state = JObject.Parse(await File.ReadAllTextAsync(stateFile.FullName));
        Assert.That(state.Value<string>("format"), Is.EqualTo("BugTape.State"));
        Assert.That(
            state.SelectToken("providers[0].name")?.Value<string>(),
            Is.EqualTo("application"));
        Assert.That(
            state.SelectToken("providers[0].data.Mode")?.Value<string>(),
            Is.EqualTo("Ready"));
        Assert.That(
            state.SelectToken("providers[0].data.ItemCount")?.Value<int>(),
            Is.EqualTo(3));
    }

    [Test]
    public async Task ProviderFailureIsRecordedWithoutAbortingExport()
    {
        Recorder.RegisterStateProvider(
            "broken",
            () => throw new InvalidOperationException("Expected failure."));

        var outputDirectory = m_testDirectory.GetDirectory("output");
        await Recorder.CreateSupportPackFilesAsync(outputDirectory);

        var manifestFile = new FileInfo(
            Path.Combine(outputDirectory.FullName, "bugtape-manifest.json"));
        var manifest = JObject.Parse(await File.ReadAllTextAsync(manifestFile.FullName));

        Assert.That(manifest.SelectToken("failures[0].source")?.Value<string>(), Is.EqualTo("state-provider"));
        Assert.That(manifest.SelectToken("failures[0].name")?.Value<string>(), Is.EqualTo("broken"));
        Assert.That(
            manifest.SelectToken("failures[0].errorType")?.Value<string>(),
            Is.EqualTo(typeof(InvalidOperationException).FullName));

        var stateFile = new FileInfo(
            Path.Combine(outputDirectory.FullName, "bugtape-state.json"));
        var state = JObject.Parse(await File.ReadAllTextAsync(stateFile.FullName));
        Assert.That(state.SelectToken("providers[0].status")?.Value<string>(), Is.EqualTo("failed"));
        Assert.That(state.SelectToken("providers[0].failure.name")?.Value<string>(), Is.EqualTo("broken"));
    }

    [Test]
    public async Task NullStateIsExportedAsJsonNull()
    {
        Recorder.RegisterStateProvider("empty", () => null);
        var outputDirectory = m_testDirectory.GetDirectory("output");

        await Recorder.CreateSupportPackFilesAsync(outputDirectory);

        var stateFile = new FileInfo(
            Path.Combine(outputDirectory.FullName, "bugtape-state.json"));
        Assert.That(
            JObject.Parse(await File.ReadAllTextAsync(stateFile.FullName))
                .SelectToken("providers[0].data")?.Type,
            Is.EqualTo(JTokenType.Null));
    }

    [Test]
    public async Task MissingRegisteredFileIsReportedWithoutAbortingExport()
    {
        Recorder.RegisterFile(m_testDirectory.GetFile("missing.log"));
        var outputDirectory = m_testDirectory.GetDirectory("output");

        await Recorder.CreateSupportPackFilesAsync(outputDirectory);

        var manifestFile = new FileInfo(
            Path.Combine(outputDirectory.FullName, "bugtape-manifest.json"));
        var manifest = JObject.Parse(await File.ReadAllTextAsync(manifestFile.FullName));

        Assert.That(
            manifest.SelectToken("failures[0].source")?.Value<string>(),
            Is.EqualTo("registered-file"));
        Assert.That(
            manifest.SelectToken("failures[0].errorType")?.Value<string>(),
            Is.EqualTo(typeof(FileNotFoundException).FullName));
    }

    [Test]
    public async Task ExportIncludesPeriodicMetricSamples()
    {
        Recorder.ResetForTests();
        Recorder.Initialize(new BugTapeOptions
        {
            ApplicationName = "BugTape Tests",
            MetricsSampleInterval = TimeSpan.FromMilliseconds(250),
            MaxRetainedMetricSampleCount = 4
        });
        await Task.Delay(350);

        var outputDirectory = m_testDirectory.GetDirectory("output");
        var files = await Recorder.CreateSupportPackFilesAsync(outputDirectory);

        var metricsFile = new FileInfo(
            Path.Combine(outputDirectory.FullName, "bugtape-metrics.jsonl"));
        Assert.That(files.Select(file => file.FullName), Does.Contain(metricsFile.FullName));

        var firstLine = (await File.ReadAllLinesAsync(metricsFile.FullName)).First();
        var sample = JObject.Parse(firstLine);
        Assert.That(sample.Value<string>("timestampUtc"), Is.Not.Empty);
        Assert.That(sample.Value<double?>("processCpuMilliseconds"), Is.Not.Null);
        Assert.That(sample.Value<long?>("workingSetBytes"), Is.Not.Null);
        Assert.That(sample["managedMemoryBytes"], Is.Null);
        Assert.That(sample["privateMemoryBytes"], Is.Null);
    }

    [Test]
    public void RegisterFileDoesNotRequireFileToExistImmediately()
    {
        var futureFile = m_testDirectory.GetFile("future.log");

        Assert.That(() => Recorder.RegisterFile(futureFile), Throws.Nothing);
    }

}
