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
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Avalonia;
using BugTape.Viewer.Models;

namespace BugTape.Viewer.Services;

public static class BugTapePackageReader
{
    private const double TimelineWidth = 1800.0;
    private const double MetricsGraphHeight = 84.0;
    private const double MinimumActionWidth = 3.0;
    private const int MaximumExcerptLineCount = 80;

    public static BugTapeSession Load(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("Enter a support package folder.", nameof(packagePath));

        var expandedPath = Environment.ExpandEnvironmentVariables(packagePath.Trim('"'));
        var file = new FileInfo(expandedPath);
        if (file.Exists && string.Equals(file.Extension, ".zip", StringComparison.OrdinalIgnoreCase))
            return LoadZip(file);

        var directory = new DirectoryInfo(expandedPath);
        if (!directory.Exists)
            throw new DirectoryNotFoundException(directory.FullName);

        return LoadDirectory(directory);
    }

    private static BugTapeSession LoadDirectory(DirectoryInfo directory)
    {
        var manifestFile = directory.GetFiles("bugtape-manifest.json", SearchOption.AllDirectories).FirstOrDefault();
        var timelineFile = directory.GetFiles("bugtape-timeline.jsonl", SearchOption.AllDirectories).FirstOrDefault();
        var metricsFile = directory.GetFiles("bugtape-metrics.jsonl", SearchOption.AllDirectories).FirstOrDefault();
        var stateFile = directory.GetFiles("bugtape-state.json", SearchOption.AllDirectories).FirstOrDefault();

        if (manifestFile == null)
            throw new FileNotFoundException("No bugtape-manifest.json file was found.", directory.FullName);
        if (timelineFile == null)
            throw new FileNotFoundException("No bugtape-timeline.jsonl file was found.", directory.FullName);

        var manifestJson = File.ReadAllText(manifestFile.FullName);
        var offset = GetPackageUtcOffset(manifestJson);
        var records = ReadTimeline(timelineFile).OrderBy(record => record.Sequence).ToList();
        var logs = ReadLogs(directory, offset);
        PopulateTimelineRegions(records);
        var metrics = metricsFile == null
            ? CreateMetricSeries(records, ExtractRecordMetrics(records))
            : CreateMetricSeries(records, ReadMetricSamples(File.ReadLines(metricsFile.FullName)));

        return new BugTapeSession
        {
            PackagePath = directory.FullName,
            ManifestSummary = CreateManifestSummary(manifestJson, records),
            StateSummary = stateFile == null ? "No bugtape-state.json file was found." : CreateStateSummary(stateFile),
            Records = records,
            Markers = CreateMarkers(records, offset),
            Ticks = CreateTicks(records, offset),
            MetricSeries = metrics,
            Tree = CreateTree(records, logs, offset)
        };
    }

    private static BugTapeSession LoadZip(FileInfo file)
    {
        using var archive = ZipFile.OpenRead(file.FullName);
        var manifestEntry = FindEntry(archive, "bugtape-manifest.json");
        var timelineEntry = FindEntry(archive, "bugtape-timeline.jsonl");
        var metricsEntry = FindEntry(archive, "bugtape-metrics.jsonl");
        var stateEntry = FindEntry(archive, "bugtape-state.json");

        if (manifestEntry == null)
            throw new FileNotFoundException("No bugtape-manifest.json file was found.", file.FullName);
        if (timelineEntry == null)
            throw new FileNotFoundException("No bugtape-timeline.jsonl file was found.", file.FullName);

        var manifestJson = ReadEntryText(manifestEntry);
        var offset = GetPackageUtcOffset(manifestJson);
        var records = ReadTimeline(ReadEntryText(timelineEntry)).OrderBy(record => record.Sequence).ToList();
        var logs = ReadLogs(archive, offset);
        PopulateTimelineRegions(records);
        var metrics = metricsEntry == null
            ? CreateMetricSeries(records, ExtractRecordMetrics(records))
            : CreateMetricSeries(records, ReadMetricSamples(ReadEntryText(metricsEntry).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)));

        return new BugTapeSession
        {
            PackagePath = file.FullName,
            ManifestSummary = CreateManifestSummary(manifestJson, records),
            StateSummary = stateEntry == null ? "No bugtape-state.json file was found." : CreateStateSummary(ReadEntryText(stateEntry)),
            Records = records,
            Markers = CreateMarkers(records, offset),
            Ticks = CreateTicks(records, offset),
            MetricSeries = metrics,
            Tree = CreateTree(records, logs, offset)
        };
    }

    private static ZipArchiveEntry FindEntry(ZipArchive archive, string fileName)
    {
        return archive.Entries.FirstOrDefault(entry =>
            string.Equals(Path.GetFileName(entry.FullName), fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static IEnumerable<BugTapeRecord> ReadTimeline(FileInfo timelineFile)
    {
        return ReadTimeline(File.ReadLines(timelineFile.FullName));
    }

    private static IEnumerable<BugTapeRecord> ReadTimeline(string timeline)
    {
        return ReadTimeline(
            timeline.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
    }

    private static IEnumerable<BugTapeRecord> ReadTimeline(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = GetString(root, "type");
            var name = GetString(root, "name");
            var level = GetString(root, "level");
            var outcome = GetString(root, "outcome");
            var message = GetString(root, "message");
            var timestampText = GetString(root, "timestampUtc");
            var displayName = CreateDisplayName(root, type, name);
            var metrics = root.TryGetProperty("metrics", out var metricsValue)
                ? ReadMetricValues(metricsValue)
                : new Dictionary<string, double>();

            yield return new BugTapeRecord
            {
                Sequence = GetInt64(root, "sequence"),
                Type = type,
                Name = name,
                DisplayName = displayName,
                Level = level,
                Outcome = outcome,
                ActionId = GetString(root, "actionId"),
                ParentActionId = GetString(root, "parentActionId"),
                TimestampUtc = DateTimeOffset.TryParse(timestampText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp)
                    ? timestamp
                    : DateTimeOffset.MinValue,
                DurationMilliseconds = GetDouble(root, "durationMilliseconds"),
                Summary = CreateSummary(type, displayName, level, outcome, message),
                Json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true }),
                MetricValues = metrics
            };
        }
    }

    private static IReadOnlyList<MetricReading> ReadMetricSamples(IEnumerable<string> lines)
    {
        var result = new List<MetricReading>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var timestampText = GetString(root, "timestampUtc");
            if (!DateTimeOffset.TryParse(timestampText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
                continue;

            result.Add(new MetricReading
            {
                TimestampUtc = timestamp,
                Values = ReadMetricValues(root)
            });
        }

        return result;
    }

    private static IReadOnlyList<MetricReading> ExtractRecordMetrics(IEnumerable<BugTapeRecord> records)
    {
        return records
            .Where(record => record.TimestampUtc > DateTimeOffset.MinValue && record.MetricValues.Count > 0)
            .Select(record => new MetricReading
            {
                TimestampUtc = record.TimestampUtc,
                Values = record.MetricValues
            })
            .ToList();
    }

    private static IReadOnlyDictionary<string, double> ReadMetricValues(JsonElement element)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (element.ValueKind != JsonValueKind.Object)
            return result;

        AddMetricValue(result, element, "cpuPercent");
        AddMetricValue(result, element, "averageCpuPercent");
        AddMetricValue(result, element, "managedMemoryBytes");
        AddMetricValue(result, element, "workingSetBytes");
        AddMetricValue(result, element, "privateMemoryBytes");
        return result;
    }

    private static void AddMetricValue(
        IDictionary<string, double> values,
        JsonElement element,
        string name)
    {
        if (element.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetDouble(out var value))
        {
            values[name] = value;
        }
    }

    private static string CreateDisplayName(JsonElement root, string type, string name)
    {
        var data = root.TryGetProperty("data", out var dataValue) ? dataValue : default;
        var baseName = string.IsNullOrWhiteSpace(name) ? type : name;

        if (type == "action-start" || type == "event")
        {
            var hint = FirstNonEmpty(
                GetString(data, "Name"),
                GetString(data, "Title"),
                GetString(data, "FileName"),
                GetString(data, "Path"));
            if (!string.IsNullOrWhiteSpace(hint) && !baseName.Contains(hint, StringComparison.OrdinalIgnoreCase))
                return AppendHint(baseName, hint);
        }

        return baseName;
    }

    private static string AppendHint(string value, string hint)
    {
        return string.IsNullOrWhiteSpace(hint) ? value : $"{value}: {hint}";
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static string CreateSummary(string type, string name, string level, string outcome, string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            return string.IsNullOrWhiteSpace(level) ? message : $"{level}: {message}";
        if (!string.IsNullOrWhiteSpace(outcome))
            return $"{name} ({outcome})";
        return string.IsNullOrWhiteSpace(name) ? type : name;
    }

    private static string CreateManifestSummary(FileInfo manifestFile, IReadOnlyCollection<BugTapeRecord> records)
    {
        return CreateManifestSummary(File.ReadAllText(manifestFile.FullName), records);
    }

    private static string CreateManifestSummary(string manifestJson, IReadOnlyCollection<BugTapeRecord> records)
    {
        using var document = JsonDocument.Parse(manifestJson);
        var root = document.RootElement;
        var app = root.TryGetProperty("application", out var appValue) ? appValue : default;
        var system = root.TryGetProperty("system", out var systemValue) ? systemValue : default;
        var failures = root.TryGetProperty("failures", out var failuresValue) && failuresValue.ValueKind == JsonValueKind.Array
            ? failuresValue.GetArrayLength()
            : 0;

        var duration = TimeSpan.Zero;
        if (records.Count > 0)
        {
            var first = records.Min(record => record.TimestampUtc);
            var last = records.Max(record => record.TimestampUtc);
            duration = last - first;
        }

        return string.Join(Environment.NewLine, new[]
        {
            $"{GetString(app, "name")} {GetString(app, "version")}".Trim(),
            $"{GetString(app, "company")} / {GetString(app, "environment")}".Trim(' ', '/'),
            $"Session: {GetString(root, "sessionId")}",
            $"Duration: {duration:g}   Records: {records.Count}   Export failures: {failures}",
            $"Machine: {GetString(system, "operatingSystem")}   Runtime: {GetString(system, "runtimeVersion")}"
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string CreateStateSummary(FileInfo stateFile)
    {
        return CreateStateSummary(File.ReadAllText(stateFile.FullName));
    }

    private static string CreateStateSummary(string stateJson)
    {
        using var document = JsonDocument.Parse(stateJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("providers", out var providers) || providers.ValueKind != JsonValueKind.Array)
            return "State file does not contain provider sections.";

        var summaries = providers.EnumerateArray()
            .Select(provider => $"{GetString(provider, "name")} ({GetString(provider, "status")})")
            .Where(value => !string.IsNullOrWhiteSpace(value));

        return "State providers: " + string.Join(", ", summaries);
    }

    private static TimeSpan GetPackageUtcOffset(string manifestJson)
    {
        try
        {
            using var document = JsonDocument.Parse(manifestJson);
            var root = document.RootElement;
            if (root.TryGetProperty("system", out var system) &&
                system.TryGetProperty("utcOffsetMinutes", out var offset) &&
                offset.TryGetInt32(out var minutes))
            {
                return TimeSpan.FromMinutes(minutes);
            }
        }
        catch (JsonException)
        {
        }

        return TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now);
    }

    private static IReadOnlyList<LogEntry> ReadLogs(DirectoryInfo directory, TimeSpan offset)
    {
        return directory
            .GetFiles("*", SearchOption.AllDirectories)
            .Where(IsSupportedLogFile)
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .SelectMany(file => ReadLogLines(file.Name, File.ReadLines(file.FullName), offset))
            .OrderBy(entry => entry.TimestampUtc)
            .ThenBy(entry => entry.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<LogEntry> ReadLogs(ZipArchive archive, TimeSpan offset)
    {
        return archive.Entries
            .Where(IsSupportedLogEntry)
            .OrderBy(entry => Path.GetFileName(entry.FullName), StringComparer.OrdinalIgnoreCase)
            .SelectMany(entry => ReadLogLines(Path.GetFileName(entry.FullName), ReadEntryText(entry).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None), offset))
            .OrderBy(entry => entry.TimestampUtc)
            .ThenBy(entry => entry.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsSupportedLogFile(FileInfo file)
    {
        return IsSupportedLogName(file.Name);
    }

    private static bool IsSupportedLogEntry(ZipArchiveEntry entry)
    {
        return !string.IsNullOrWhiteSpace(entry.Name) && IsSupportedLogName(entry.Name);
    }

    private static bool IsSupportedLogName(string name)
    {
        if (string.Equals(name, "log.txt", StringComparison.OrdinalIgnoreCase))
            return true;

        return name.EndsWith("_Log.txt", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<LogEntry> ReadLogLines(
        string source,
        IEnumerable<string> lines,
        TimeSpan offset)
    {
        DateTimeOffset lastTimestamp = default;
        foreach (var line in lines)
        {
            var text = line;
            if (TryParseLogTimestamp(line, offset, out var timestamp))
            {
                lastTimestamp = timestamp;
                text = StripLogTimestamp(line);
            }

            if (lastTimestamp == default)
                continue;

            yield return new LogEntry
            {
                TimestampUtc = lastTimestamp.ToUniversalTime(),
                Source = source,
                Text = text
            };
        }
    }

    private static string StripLogTimestamp(string line)
    {
        if (line.Length <= 20)
            return string.Empty;

        return line.Substring(20).TrimStart();
    }

    private static bool TryParseLogTimestamp(
        string line,
        TimeSpan offset,
        out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(line) || line.Length < 19)
            return false;

        var value = line.Substring(0, 19);
        if (!DateTime.TryParseExact(
                value,
                "yyyy-MM-dd'T'HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dateTime))
        {
            return false;
        }

        timestamp = new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified), offset);
        return true;
    }

    private static IReadOnlyList<TimelineMarker> CreateMarkers(
        IReadOnlyList<BugTapeRecord> records,
        TimeSpan offset)
    {
        if (records.Count == 0)
            return Array.Empty<TimelineMarker>();

        return records
            .Where(record => record.TimestampUtc > DateTimeOffset.MinValue)
            .Select(record =>
            {
                var isAction = record.Type == "action-start" || record.Type == "action-end";

                return new TimelineMarker
                {
                    Label = string.IsNullOrWhiteSpace(record.DisplayName) ? record.Type : record.DisplayName,
                    ToolTip = $"{FormatLogTime(record.TimestampUtc, offset)} {record.Type} {record.Summary}",
                    Left = record.TimelineLeft,
                    Top = GetMarkerTop(record),
                    Width = isAction ? Math.Max(MinimumActionWidth, record.TimelineWidth) : 2.0,
                    Height = isAction ? 18.0 : 28.0,
                    Brush = GetMarkerBrush(record)
                };
            })
            .ToList();
    }

    private static IReadOnlyList<TimelineTick> CreateTicks(
        IReadOnlyList<BugTapeRecord> records,
        TimeSpan offset)
    {
        var validRecords = records
            .Where(record => record.TimestampUtc > DateTimeOffset.MinValue)
            .OrderBy(record => record.TimestampUtc)
            .ToArray();
        if (validRecords.Length == 0)
            return Array.Empty<TimelineTick>();

        var first = validRecords.First().TimestampUtc;
        var last = validRecords.Last().TimestampUtc;
        var durationMs = Math.Max(1.0, (last - first).TotalMilliseconds);
        var tickCount = 6;
        var ticks = new List<TimelineTick>();
        for (var index = 0; index < tickCount; index++)
        {
            var fraction = tickCount == 1 ? 0.0 : (double)index / (tickCount - 1);
            var timestamp = first.AddMilliseconds(durationMs * fraction);
            ticks.Add(new TimelineTick
            {
                Label = FormatLogTime(timestamp, offset, false),
                Left = TimelineWidth * fraction
            });
        }

        return ticks;
    }

    private static IReadOnlyList<MetricSeries> CreateMetricSeries(
        IReadOnlyList<BugTapeRecord> records,
        IReadOnlyList<MetricReading> readings)
    {
        var validRecords = records
            .Where(record => record.TimestampUtc > DateTimeOffset.MinValue)
            .OrderBy(record => record.TimestampUtc)
            .ToArray();
        if (validRecords.Length == 0 || readings.Count == 0)
            return Array.Empty<MetricSeries>();

        var first = validRecords.First().TimestampUtc;
        var last = validRecords.Last().TimestampUtc;
        var durationMilliseconds = Math.Max(1.0, (last - first).TotalMilliseconds);

        return new[]
        {
            CreateMetricSeries(
                "cpu",
                "CPU %",
                "#f97316",
                readings,
                reading => FirstMetricValue(reading, "cpuPercent", "averageCpuPercent"),
                first,
                durationMilliseconds,
                value => value),
            CreateMetricSeries(
                "working",
                "Memory MB",
                "#16a34a",
                readings,
                reading => FirstMetricValue(reading, "workingSetBytes"),
                first,
                durationMilliseconds,
                BytesToMegabytes)
        }.Where(series => series.Points.Count > 0).ToList();
    }

    private static MetricSeries CreateMetricSeries(
        string key,
        string label,
        string brush,
        IEnumerable<MetricReading> readings,
        Func<MetricReading, double?> selectValue,
        DateTimeOffset first,
        double durationMilliseconds,
        Func<double, double> normalizeValue)
    {
        var known = readings
            .Select(reading => new MetricPoint
            {
                X = ((reading.TimestampUtc - first).TotalMilliseconds / durationMilliseconds) * TimelineWidth,
                Value = selectValue(reading)
            })
            .Where(point => point.Value.HasValue)
            .OrderBy(point => point.X)
            .ToList();
        if (known.Count == 0)
            return new MetricSeries { Key = key, Label = label };

        foreach (var point in known)
            point.Value = normalizeValue(point.Value.Value);

        var minimum = known.Min(point => point.Value.Value);
        var maximum = known.Max(point => point.Value.Value);
        var range = Math.Max(0.0001, maximum - minimum);
        var graphPoints = InterpolateMetricPoints(known);
        var points = graphPoints
            .Select(point =>
            {
                var y = MetricsGraphHeight - ((point.Value.Value - minimum) / range * (MetricsGraphHeight - 10.0)) - 5.0;
                return new Point(point.X, y);
            })
            .ToList();
        var segments = new List<MetricSegment>();
        for (var index = 1; index < points.Count; index++)
        {
            var previous = points[index - 1];
            var current = points[index];
            segments.Add(new MetricSegment
            {
                X1 = previous.X,
                Y1 = previous.Y,
                X2 = current.X,
                Y2 = current.Y
            });
        }

        return new MetricSeries
        {
            Key = key,
            Label = label,
            Brush = brush,
            Points = points,
            Segments = segments,
            Summary = FormattableString.Invariant($"{label}: {minimum:0.##} - {maximum:0.##}")
        };
    }

    private static IEnumerable<MetricPoint> InterpolateMetricPoints(IReadOnlyList<MetricPoint> known)
    {
        if (known.Count == 1)
        {
            yield return new MetricPoint { X = 0, Value = known[0].Value };
            yield return new MetricPoint { X = TimelineWidth, Value = known[0].Value };
            yield break;
        }

        var step = TimelineWidth / 200.0;
        for (var x = 0.0; x <= TimelineWidth; x += step)
            yield return InterpolateMetricPoint(known, x);

        yield return InterpolateMetricPoint(known, TimelineWidth);
    }

    private static MetricPoint InterpolateMetricPoint(IReadOnlyList<MetricPoint> known, double x)
    {
        if (x <= known[0].X)
            return new MetricPoint { X = x, Value = known[0].Value };
        if (x >= known[known.Count - 1].X)
            return new MetricPoint { X = x, Value = known[known.Count - 1].Value };

        for (var index = 1; index < known.Count; index++)
        {
            var right = known[index];
            if (x > right.X)
                continue;

            var left = known[index - 1];
            var width = Math.Max(0.0001, right.X - left.X);
            var fraction = (x - left.X) / width;
            return new MetricPoint
            {
                X = x,
                Value = left.Value.Value + (right.Value.Value - left.Value.Value) * fraction
            };
        }

        return new MetricPoint { X = x, Value = known[known.Count - 1].Value };
    }

    private static double? FirstMetricValue(MetricReading reading, params string[] names)
    {
        foreach (var name in names)
        {
            if (reading.Values.TryGetValue(name, out var value))
                return value;
        }

        return null;
    }

    private static double BytesToMegabytes(double value)
    {
        return value / 1024.0 / 1024.0;
    }

    private static IReadOnlyList<TimelineTreeNode> CreateTree(
        IReadOnlyList<BugTapeRecord> records,
        IReadOnlyList<LogEntry> logs,
        TimeSpan offset)
    {
        var roots = new List<TimelineTreeNode>();
        var actions = new Dictionary<string, TimelineTreeNode>();

        foreach (var record in records.OrderBy(record => record.Sequence))
        {
            if (record.Type == "action-start")
            {
                var actionNode = CreateNode(record, "Action", logs, offset);
                actions[record.ActionId] = actionNode;
                AddToParent(roots, actions, actionNode, record.ParentActionId);
                continue;
            }

            if (record.Type == "action-end")
            {
                if (!string.IsNullOrWhiteSpace(record.ActionId) &&
                    actions.TryGetValue(record.ActionId, out var actionNode))
                {
                    actionNode.Detail = CreateActionDetail(record);
                    actionNode.Brush = GetMarkerBrush(record);
                    actionNode.Json = MergeJson(actionNode.Json, record.Json);
                    actionNode.TimelineWidth = Math.Max(MinimumActionWidth, record.TimelineWidth);
                    actionNode.LogExcerpt = CreateLogExcerpt(logs, actionNode.Record.TimestampUtc, record.TimestampUtc, offset);
                }
                else
                {
                    AddToParent(roots, actions, CreateNode(record, "Action end", logs, offset), record.ParentActionId);
                }

                continue;
            }

            var node = CreateNode(record, GetNodeKind(record), logs, offset);
            AddToParent(roots, actions, node, record.ActionId);
        }

        return roots;
    }

    private static TimelineTreeNode CreateNode(
        BugTapeRecord record,
        string kind,
        IReadOnlyList<LogEntry> logs,
        TimeSpan offset)
    {
        var (left, width) = GetTimelineRegion(record);
        return new TimelineTreeNode
        {
            Kind = kind,
            Title = GetNodeTitle(record),
            Detail = GetNodeDetail(record),
            Timestamp = record.TimestampUtc == DateTimeOffset.MinValue ? string.Empty : FormatLogTime(record.TimestampUtc, offset),
            Brush = GetMarkerBrush(record),
            Json = record.Json,
            LogExcerpt = CreateLogExcerpt(logs, record.TimestampUtc, offset),
            Record = record,
            TimelineLeft = left,
            TimelineWidth = width
        };
    }

    private static string CreateLogExcerpt(
        IReadOnlyList<LogEntry> logs,
        DateTimeOffset timestamp,
        TimeSpan offset)
    {
        if (timestamp <= DateTimeOffset.MinValue)
            return "No timestamp is available for this timeline item.";

        return CreateLogExcerpt(
            logs,
            timestamp.AddSeconds(-3),
            timestamp.AddSeconds(3),
            offset);
    }

    private static string CreateLogExcerpt(
        IReadOnlyList<LogEntry> logs,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        TimeSpan offset)
    {
        if (logs.Count == 0)
            return "No supported log files were found. Expected Log.txt or *_Log.txt.";

        var expandedStart = FloorToSecond(startUtc).AddSeconds(-5);
        var expandedEnd = CeilingToSecond(endUtc).AddSeconds(3);
        var matches = logs
            .Where(entry => entry.TimestampUtc >= expandedStart && entry.TimestampUtc <= expandedEnd)
            .Take(MaximumExcerptLineCount + 1)
            .ToList();

        if (matches.Count == 0)
            return CreateLogExcerptHeader(startUtc, endUtc, expandedStart, expandedEnd, offset) +
                   Environment.NewLine +
                   $"No log lines were found near {FormatLogTime(startUtc, offset, false)}.";

        var truncated = matches.Count > MaximumExcerptLineCount;
        if (truncated)
            matches.RemoveAt(matches.Count - 1);

        var lines = new List<string>
        {
            CreateLogExcerptHeader(startUtc, endUtc, expandedStart, expandedEnd, offset),
            string.Empty
        };
        lines.AddRange(matches.Select(entry => $"[{entry.Source}] {entry.Text}"));
        if (truncated)
            lines.Add($"...showing first {MaximumExcerptLineCount} matching log lines...");

        return string.Join(Environment.NewLine, lines);
    }

    private static string CreateLogExcerptHeader(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        DateTimeOffset expandedStartUtc,
        DateTimeOffset expandedEndUtc,
        TimeSpan offset)
    {
        if (Math.Abs((endUtc - startUtc).TotalMilliseconds) < 1.0)
        {
            return $"Selected log time: {FormatLogTime(startUtc, offset)}; " +
                   $"showing {FormatLogTime(expandedStartUtc, offset, false)} to {FormatLogTime(expandedEndUtc, offset, false)}";
        }

        return $"Selected log time: {FormatLogTime(startUtc, offset)} to {FormatLogTime(endUtc, offset)}; " +
               $"showing {FormatLogTime(expandedStartUtc, offset, false)} to {FormatLogTime(expandedEndUtc, offset, false)}";
    }

    private static string FormatLogTime(
        DateTimeOffset timestampUtc,
        TimeSpan offset,
        bool includeMilliseconds = true)
    {
        var logTime = timestampUtc.ToOffset(offset);
        return includeMilliseconds
            ? logTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)
            : logTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset FloorToSecond(DateTimeOffset value)
    {
        var ticks = value.Ticks - value.Ticks % TimeSpan.TicksPerSecond;
        return new DateTimeOffset(ticks, value.Offset);
    }

    private static DateTimeOffset CeilingToSecond(DateTimeOffset value)
    {
        var floor = FloorToSecond(value);
        return floor == value ? floor : floor.AddSeconds(1);
    }

    private static (double left, double width) GetTimelineRegion(BugTapeRecord record)
    {
        var left = Math.Max(0.0, record.TimelineLeft);
        var width = record.DurationMilliseconds.HasValue
            ? Math.Max(MinimumActionWidth, record.TimelineWidth)
            : 8.0;
        return (left, width);
    }

    private static void PopulateTimelineRegions(IReadOnlyList<BugTapeRecord> records)
    {
        if (records.Count == 0)
            return;

        var validRecords = records.Where(record => record.TimestampUtc > DateTimeOffset.MinValue).ToArray();
        if (validRecords.Length == 0)
            return;

        var first = validRecords.Min(record => record.TimestampUtc);
        var last = validRecords.Max(record => record.TimestampUtc);
        var durationMs = Math.Max(1.0, (last - first).TotalMilliseconds);

        foreach (var record in validRecords)
        {
            record.TimelineLeft = ((record.TimestampUtc - first).TotalMilliseconds / durationMs) * TimelineWidth;
            record.TimelineWidth = record.DurationMilliseconds.HasValue
                ? Math.Max(MinimumActionWidth, record.DurationMilliseconds.Value / durationMs * TimelineWidth)
                : 2.0;
        }

        var actionStarts = validRecords
            .Where(record => record.Type == "action-start" && !string.IsNullOrWhiteSpace(record.ActionId))
            .ToDictionary(record => record.ActionId, record => record);
        foreach (var record in validRecords.Where(record => record.Type == "action-end"))
        {
            if (string.IsNullOrWhiteSpace(record.ActionId) ||
                !actionStarts.TryGetValue(record.ActionId, out var start))
            {
                continue;
            }

            var endLeft = record.TimelineLeft;
            record.TimelineLeft = start.TimelineLeft;
            if (!record.DurationMilliseconds.HasValue)
                record.TimelineWidth = Math.Max(MinimumActionWidth, endLeft - start.TimelineLeft);
        }
    }

    private static void AddToParent(
        ICollection<TimelineTreeNode> roots,
        IReadOnlyDictionary<string, TimelineTreeNode> actions,
        TimelineTreeNode node,
        string actionId)
    {
        if (!string.IsNullOrWhiteSpace(actionId) &&
            actions.TryGetValue(actionId, out var parent) &&
            !ReferenceEquals(parent, node))
        {
            parent.Children.Add(node);
            return;
        }

        roots.Add(node);
    }

    private static string GetNodeKind(BugTapeRecord record)
    {
        return record.Type switch
        {
            "event" => "Event",
            "log" => "Log",
            "action-data" => "Data",
            "session-start" => "Session",
            "session-end" => "Session",
            _ => record.Type
        };
    }

    private static string GetNodeTitle(BugTapeRecord record)
    {
        if (record.Type == "log")
            return string.IsNullOrWhiteSpace(record.Level) ? "Log" : record.Level;

        return string.IsNullOrWhiteSpace(record.DisplayName) ? record.Type : record.DisplayName;
    }

    private static string GetNodeDetail(BugTapeRecord record)
    {
        if (record.Type == "action-start")
            return "running";
        if (record.Type == "action-end")
            return CreateActionDetail(record);
        if (record.Type == "log")
            return record.Summary;
        if (record.Type == "action-data")
            return "action data";
        return record.Summary;
    }

    private static string CreateActionDetail(BugTapeRecord record)
    {
        var duration = record.DurationMilliseconds.HasValue
            ? $"{record.DurationMilliseconds.Value:N0} ms"
            : "duration unknown";
        return string.IsNullOrWhiteSpace(record.Outcome)
            ? duration
            : $"{record.Outcome}, {duration}";
    }

    private static string MergeJson(string startJson, string endJson)
    {
        return string.Join(
            $"{Environment.NewLine}{Environment.NewLine}",
            new[]
            {
                "Start:",
                startJson,
                "End:",
                endJson
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static double GetMarkerTop(BugTapeRecord record)
    {
        if (record.Type == "log")
            return 8.0;
        if (record.Type.StartsWith("action", StringComparison.OrdinalIgnoreCase))
            return 32.0;
        return 58.0;
    }

    private static string GetMarkerBrush(BugTapeRecord record)
    {
        if (record.Level == "error" || record.Outcome == "failed")
            return "#c2410c";
        if (record.Outcome == "cancelled" || record.Level == "warning")
            return "#b7791f";
        if (record.Type == "action-end")
            return "#2563eb";
        if (record.Type == "action-start")
            return "#38a169";
        if (record.Type == "log")
            return "#805ad5";
        return "#4a5568";
    }

    private static string GetString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var property) &&
               property.ValueKind != JsonValueKind.Null
            ? property.ToString()
            : string.Empty;
    }

    private static long GetInt64(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var property) &&
               property.TryGetInt64(out var value)
            ? value
            : 0;
    }

    private static double? GetDouble(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var property) &&
               property.TryGetDouble(out var value)
            ? value
            : null;
    }

    private sealed class MetricReading
    {
        public DateTimeOffset TimestampUtc { get; set; }

        public IReadOnlyDictionary<string, double> Values { get; set; } = new Dictionary<string, double>();
    }

    private sealed class MetricPoint
    {
        public double X { get; set; }

        public double? Value { get; set; }
    }
}
