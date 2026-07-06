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
public class RecordingTests
{
    private TestDirectory m_testDirectory;

    [SetUp]
    public void SetUp()
    {
        Recorder.ResetForTests();
        m_testDirectory = new TestDirectory();
    }

    [TearDown]
    public void TearDown()
    {
        Recorder.ResetForTests();
        m_testDirectory.Dispose();
    }

    [Test]
    public void InitializeCreatesOneProcessWideSession()
    {
        Assert.That(Recorder.IsInitialized, Is.False);

        Initialize();

        Assert.That(Recorder.IsInitialized, Is.True);
        Assert.That(
            () => Initialize(),
            Throws.TypeOf<InvalidOperationException>());

        Recorder.Shutdown();
        Recorder.Shutdown();

        Assert.That(Recorder.IsInitialized, Is.False);
        Assert.That(
            () => Recorder.Record("AfterShutdown"),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task TrackAsyncReturnsValueAndCorrelatesNestedRecords()
    {
        Initialize();

        var result = await Recorder.TrackAsync(
            "Example.Calculate",
            async () =>
            {
                Recorder.Log(BugTapeLogLevel.Information, "Calculation started.");
                await Task.Yield();
                Recorder.Record("Calculation.Resumed");
                return 42;
            },
            new { Input = 21 });

        var records = await ExportTimelineAsync();
        var actionStart = records.Single(record =>
            record.Value<string>("type") == "action-start");
        var actionEnd = records.Single(record =>
            record.Value<string>("type") == "action-end");
        var log = records.Single(record => record.Value<string>("type") == "log");
        var resumed = records.Single(record =>
            record.Value<string>("name") == "Calculation.Resumed");

        Assert.That(result, Is.EqualTo(42));
        Assert.That(actionEnd.Value<string>("outcome"), Is.EqualTo("success"));
        Assert.That(log.Value<string>("actionId"), Is.EqualTo(actionStart.Value<string>("actionId")));
        Assert.That(resumed.Value<string>("actionId"), Is.EqualTo(actionStart.Value<string>("actionId")));
        Assert.That(actionEnd.Value<string>("actionId"), Is.EqualTo(actionStart.Value<string>("actionId")));
        Assert.That(actionEnd.Value<double>("durationMilliseconds"), Is.GreaterThanOrEqualTo(0));
        Assert.That(
            actionStart.SelectToken("metrics.managedMemoryBytes")?.Value<long>(),
            Is.GreaterThan(0));
        Assert.That(
            actionEnd.SelectToken("metrics.managedMemoryDeltaBytes"),
            Is.Not.Null);
        Assert.That(
            actionEnd.SelectToken("metrics.cpuMilliseconds")?.Value<double>(),
            Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void TrackRecordsFailureAndRethrows()
    {
        Initialize();

        Assert.That(
            () => Recorder.Track(
                "Example.Fail",
                () => throw new InvalidOperationException("Expected failure.")),
            Throws.TypeOf<InvalidOperationException>());

        var records = ExportTimelineAsync().GetAwaiter().GetResult();
        var actionEnd = records.Single(record =>
            record.Value<string>("type") == "action-end");

        Assert.That(actionEnd.Value<string>("outcome"), Is.EqualTo("failed"));
        Assert.That(
            actionEnd.SelectToken("exception.type")?.Value<string>(),
            Is.EqualTo(typeof(InvalidOperationException).FullName));
    }

    [Test]
    public async Task LogExceptionDefaultsToErrorLevel()
    {
        Initialize();

        Recorder.Log(new InvalidOperationException("Expected failure."));

        var records = await ExportTimelineAsync();
        var log = records.Single(record => record.Value<string>("type") == "log");

        Assert.That(log.Value<string>("level"), Is.EqualTo("error"));
        Assert.That(log.Value<string>("message"), Is.EqualTo("Expected failure."));
        Assert.That(
            log.SelectToken("exception.type")?.Value<string>(),
            Is.EqualTo(typeof(InvalidOperationException).FullName));
    }

    [Test]
    public async Task ManualActionCanAddDataAndBeCanceled()
    {
        Initialize();

        using (var action = Recorder.StartAction("Example.Cancel"))
        {
            action.Add("ItemCount", 4);
            action.Cancel("User requested cancellation.");
        }

        var records = await ExportTimelineAsync();
        var actionData = records.Single(record =>
            record.Value<string>("type") == "action-data");
        var actionEnd = records.Single(record =>
            record.Value<string>("type") == "action-end");

        Assert.That(actionData.Value<string>("name"), Is.EqualTo("ItemCount"));
        Assert.That(actionData.Value<int>("data"), Is.EqualTo(4));
        Assert.That(actionEnd.Value<string>("outcome"), Is.EqualTo("canceled"));
        Assert.That(
            actionEnd.SelectToken("data.Reason")?.Value<string>(),
            Is.EqualTo("User requested cancellation."));
    }

    [Test]
    public async Task ThrowingDataPropertyDoesNotEscapeIntoHostApplication()
    {
        Initialize();

        Assert.That(
            () => Recorder.Record("Example.UnsafeData", new ThrowingData()),
            Throws.Nothing);

        var records = await ExportTimelineAsync();
        var capturedEvent = records.Single(record =>
            record.Value<string>("name") == "Example.UnsafeData");

        Assert.That(capturedEvent.SelectToken("data.Safe")?.Value<string>(), Is.EqualTo("Captured"));
    }

    [Test]
    public async Task ConcurrentCallersCanRecordSafely()
    {
        Initialize(maxEventCount: 200);

        await Task.WhenAll(
            Enumerable.Range(0, 100)
                .Select(index => Task.Run(() =>
                    Recorder.Record("Example.Concurrent", new { Index = index }))));

        var records = await ExportTimelineAsync();

        Assert.That(
            records.Count(record => record.Value<string>("name") == "Example.Concurrent"),
            Is.EqualTo(100));
        Assert.That(
            records.Select(record => record.Value<long>("sequence")).Distinct().ToList(),
            Has.Count.EqualTo(records.Count));
    }

    [Test]
    public async Task TimelineDiscardsOldestRecordsAtConfiguredLimit()
    {
        Initialize(maxEventCount: 3);

        for (var i = 0; i < 5; i++)
            Recorder.Record($"Example.Event{i}");

        var records = await ExportTimelineAsync();

        Assert.That(records, Has.Count.EqualTo(3));
        Assert.That(
            records.Select(record => record.Value<string>("name")),
            Is.EqualTo(new[]
            {
                "Example.Event2",
                "Example.Event3",
                "Example.Event4"
            }));
    }

    [Test]
    public void RecordBeforeInitializationIsRejected()
    {
        Assert.That(
            () => Recorder.Record("Example.Event"),
            Throws.TypeOf<InvalidOperationException>());
    }

    private void Initialize(int maxEventCount = 100)
    {
        Recorder.Initialize(new BugTapeOptions
        {
            ApplicationName = "BugTape Tests",
            ApplicationVersion = "1.0.0",
            MaxRetainedEventCount = maxEventCount,
            MaxRetainedPayloadBytes = 1024 * 1024,
            MaxEventPayloadBytes = 16 * 1024
        });
    }

    private async Task<List<JObject>> ExportTimelineAsync()
    {
        var outputDirectory = m_testDirectory.GetDirectory("export");
        await Recorder.CreateSupportPackFilesAsync(outputDirectory);

        var timelineFile = new FileInfo(
            Path.Combine(outputDirectory.FullName, "bugtape-timeline.jsonl"));
        return File.ReadAllLines(timelineFile.FullName)
            .Select(JObject.Parse)
            .ToList();
    }

    private sealed class ThrowingData
    {
        public string Safe => "Captured";

        public string Broken => throw new InvalidOperationException("Expected failure.");
    }
}
