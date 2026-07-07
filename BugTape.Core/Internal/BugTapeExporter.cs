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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BugTape.Core.Internal;

internal static class BugTapeExporter
{
    private static readonly Encoding s_utf8WithoutBom = new UTF8Encoding(false);

    public static async Task<IReadOnlyCollection<FileInfo>> CreateFilesAsync(
        BugTapeRuntime runtime,
        DirectoryInfo destinationDirectory,
        CancellationToken cancellationToken)
    {
        if (runtime == null)
            throw new ArgumentNullException(nameof(runtime));
        if (destinationDirectory == null)
            throw new ArgumentNullException(nameof(destinationDirectory));

        cancellationToken.ThrowIfCancellationRequested();
        destinationDirectory.Create();

        var exportStartedUtc = DateTimeOffset.UtcNow;
        var createdFiles = new List<FileInfo>();
        var failures = new JArray();

        var timelineFile = new FileInfo(
            Path.Combine(destinationDirectory.FullName, "bugtape-timeline.jsonl"));
        await WriteTimelineAsync(
                timelineFile,
                runtime.SnapshotTimeline(),
                cancellationToken)
            .ConfigureAwait(false);
        createdFiles.Add(timelineFile);

        var metricSamples = runtime.SnapshotMetricSamples();
        if (metricSamples.Count > 0)
        {
            var metricsFile = new FileInfo(
                Path.Combine(destinationDirectory.FullName, "bugtape-metrics.jsonl"));
            await WriteMetricSamplesAsync(
                    metricsFile,
                    metricSamples,
                    cancellationToken)
                .ConfigureAwait(false);
            createdFiles.Add(metricsFile);
        }

        var stateFile = new FileInfo(
            Path.Combine(destinationDirectory.FullName, "bugtape-state.json"));
        var state = await CaptureStateProvidersAsync(
                runtime,
                failures,
                exportStartedUtc,
                cancellationToken)
            .ConfigureAwait(false);
        await WriteJsonAsync(stateFile, state, cancellationToken).ConfigureAwait(false);
        createdFiles.Add(stateFile);

        var filesDirectory = new DirectoryInfo(
            Path.Combine(destinationDirectory.FullName, "bugtape-files"));
        foreach (var registeredFile in runtime.SnapshotRegisteredFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                registeredFile.Refresh();
                if (!registeredFile.Exists)
                    throw new FileNotFoundException("The registered file does not exist.", registeredFile.FullName);
                if (registeredFile.Length > runtime.Options.MaxRegisteredFileBytes)
                {
                    throw new IOException(
                        $"The registered file exceeds the {runtime.Options.MaxRegisteredFileBytes} byte limit.");
                }

                filesDirectory.Create();
                var destination = GetUniqueFile(filesDirectory, registeredFile.Name);
                var originalLength = registeredFile.Length;
                await CopyFileAsync(registeredFile, destination, cancellationToken)
                    .ConfigureAwait(false);
                registeredFile.Refresh();
                if (registeredFile.Exists && registeredFile.Length != originalLength)
                {
                    failures.Add(new JObject
                    {
                        ["source"] = "registered-file",
                        ["name"] = registeredFile.Name,
                        ["errorType"] = "FileChangedDuringExport"
                    });
                }

                createdFiles.Add(destination);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(CreateFailure(
                    "registered-file",
                    registeredFile.Name,
                    exception));
            }
        }

        var manifestFile = new FileInfo(
            Path.Combine(destinationDirectory.FullName, "bugtape-manifest.json"));
        createdFiles.Add(manifestFile);

        var manifest = CreateManifest(
            runtime,
            destinationDirectory,
            createdFiles,
            failures,
            exportStartedUtc);
        await WriteJsonAsync(manifestFile, manifest, cancellationToken)
            .ConfigureAwait(false);

        return createdFiles.AsReadOnly();
    }

    private static async Task WriteTimelineAsync(
        FileInfo file,
        IReadOnlyCollection<TimelineRecord> timeline,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            file.FullName,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        using var writer = new StreamWriter(stream, s_utf8WithoutBom);
        foreach (var record in timeline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(record.Serialize(Formatting.None))
                .ConfigureAwait(false);
        }
    }

    private static async Task WriteMetricSamplesAsync(
        FileInfo file,
        IReadOnlyCollection<MetricSample> samples,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            file.FullName,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        using var writer = new StreamWriter(stream, s_utf8WithoutBom);
        foreach (var sample in samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(sample.Serialize(Formatting.None).ToString(Formatting.None))
                .ConfigureAwait(false);
        }
    }

    private static async Task WriteJsonAsync(
        FileInfo file,
        JToken data,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(
            file.FullName,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        using var writer = new StreamWriter(stream, s_utf8WithoutBom);
        var value = data ?? JValue.CreateNull();
        await writer.WriteAsync(value.ToString(Formatting.Indented))
            .ConfigureAwait(false);
    }

    private static async Task CopyFileAsync(
        FileInfo source,
        FileInfo destination,
        CancellationToken cancellationToken)
    {
        using var input = source.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var output = destination.Open(FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await input.CopyToAsync(output, 81920, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JObject> CaptureStateProvidersAsync(
        BugTapeRuntime runtime,
        JArray failures,
        DateTimeOffset exportStartedUtc,
        CancellationToken cancellationToken)
    {
        var providers = new JArray();

        foreach (var provider in runtime.SnapshotStateProviders())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var state = await provider.CaptureAsync(cancellationToken).ConfigureAwait(false);
                providers.Add(new JObject
                {
                    ["name"] = provider.Name,
                    ["status"] = "ok",
                    ["data"] = runtime.CaptureState(state)
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var failure = CreateFailure("state-provider", provider.Name, exception);
                failures.Add(failure);
                providers.Add(new JObject
                {
                    ["name"] = provider.Name,
                    ["status"] = "failed",
                    ["failure"] = new JObject(failure)
                });
            }
        }

        return new JObject
        {
            ["schemaVersion"] = 1,
            ["format"] = "BugTape.State",
            ["sessionId"] = runtime.Options.SessionId,
            ["capturedUtc"] = ToIso(exportStartedUtc),
            ["providers"] = providers
        };
    }

    private static JObject CreateManifest(
        BugTapeRuntime runtime,
        DirectoryInfo destinationDirectory,
        IEnumerable<FileInfo> files,
        JArray failures,
        DateTimeOffset exportStartedUtc)
    {
        var utcOffset = TimeZoneInfo.Local.GetUtcOffset(exportStartedUtc);
        return new JObject
        {
            ["schemaVersion"] = 1,
            ["format"] = "BugTape.SupportPack",
            ["sessionId"] = runtime.Options.SessionId,
            ["sessionStartedUtc"] = ToIso(runtime.StartedUtc),
            ["exportStartedUtc"] = ToIso(exportStartedUtc),
            ["createdUtc"] = ToIso(DateTimeOffset.UtcNow),
            ["application"] = new JObject
            {
                ["name"] = runtime.Options.ApplicationName,
                ["version"] = runtime.Options.ApplicationVersion,
                ["company"] = runtime.Options.CompanyName,
                ["environment"] = runtime.Options.EnvironmentName
            },
            ["system"] = new JObject
            {
                ["timeZoneId"] = TimeZoneInfo.Local.Id,
                ["utcOffsetMinutes"] = (int)utcOffset.TotalMinutes,
                ["culture"] = CultureInfo.CurrentCulture.Name,
                ["uiCulture"] = CultureInfo.CurrentUICulture.Name,
                ["operatingSystem"] = Environment.OSVersion.ToString(),
                ["runtimeVersion"] = Environment.Version.ToString(),
                ["processorCount"] = Environment.ProcessorCount
            },
            ["files"] = new JArray(
                files.Select(file => GetRelativePath(destinationDirectory, file))),
            ["failures"] = failures
        };
    }

    private static JObject CreateFailure(
        string source,
        string name,
        Exception exception)
    {
        return new JObject
        {
            ["source"] = source,
            ["name"] = name,
            ["errorType"] = exception.GetType().FullName
        };
    }

    private static FileInfo GetUniqueFile(
        DirectoryInfo directory,
        string requestedName)
    {
        var safeName = SanitizeFileName(requestedName);
        var candidate = new FileInfo(Path.Combine(directory.FullName, safeName));
        var index = 2;

        while (candidate.Exists)
        {
            var extension = Path.GetExtension(safeName);
            var stem = Path.GetFileNameWithoutExtension(safeName);
            candidate = new FileInfo(
                Path.Combine(directory.FullName, $"{stem}-{index}{extension}"));
            index++;
        }

        return candidate;
    }

    private static string SanitizeName(string value)
    {
        var sanitized = new string(
            value.Select(character =>
                    char.IsLetterOrDigit(character) || character == '-' || character == '_'
                        ? char.ToLowerInvariant(character)
                        : '-')
                .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "state" : sanitized;
    }

    private static string SanitizeFileName(string value)
    {
        var sanitized = new string(
            value.Select(character =>
                    char.IsLetterOrDigit(character) ||
                    character == '-' ||
                    character == '_' ||
                    character == '.'
                        ? character
                        : '-')
                .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "file" : sanitized;
    }

    private static string GetRelativePath(
        DirectoryInfo root,
        FileInfo file)
    {
        var rootPath = root.FullName.TrimEnd(
                           Path.DirectorySeparatorChar,
                           Path.AltDirectorySeparatorChar) +
                       Path.DirectorySeparatorChar;
        if (!file.FullName.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            return file.Name;

        return file.FullName.Substring(rootPath.Length)
            .Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string ToIso(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
