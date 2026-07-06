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
using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace BugTape.Core.Internal;

internal sealed class ProcessMetricsSnapshot
{
    private ProcessMetricsSnapshot()
    {
    }

    public double? ProcessCpuMilliseconds { get; private set; }

    public long? WorkingSetBytes { get; private set; }

    public long? PrivateMemoryBytes { get; private set; }

    public long ManagedMemoryBytes { get; private set; }

    public static ProcessMetricsSnapshot Capture()
    {
        var snapshot = new ProcessMetricsSnapshot
        {
            ManagedMemoryBytes = GC.GetTotalMemory(false)
        };

        try
        {
            using (var process = Process.GetCurrentProcess())
            {
                process.Refresh();
                snapshot.ProcessCpuMilliseconds = TryRead(
                    () => process.TotalProcessorTime.TotalMilliseconds);
                snapshot.WorkingSetBytes = TryRead(() => process.WorkingSet64);
                snapshot.PrivateMemoryBytes = TryRead(() => process.PrivateMemorySize64);
            }
        }
        catch
        {
            // Process metrics are useful context, never a reason to fail an action.
        }

        return snapshot;
    }

    public JObject ToStartJson()
    {
        var result = new JObject
        {
            ["managedMemoryBytes"] = ManagedMemoryBytes
        };

        AddIfPresent(result, "processCpuMilliseconds", ProcessCpuMilliseconds);
        AddIfPresent(result, "workingSetBytes", WorkingSetBytes);
        AddIfPresent(result, "privateMemoryBytes", PrivateMemoryBytes);
        return result;
    }

    public JObject ToEndJson(ProcessMetricsSnapshot start, double durationMilliseconds)
    {
        var result = ToStartJson();
        result["managedMemoryDeltaBytes"] = ManagedMemoryBytes - start.ManagedMemoryBytes;

        AddDelta(
            result,
            "workingSetDeltaBytes",
            start.WorkingSetBytes,
            WorkingSetBytes);
        AddDelta(
            result,
            "privateMemoryDeltaBytes",
            start.PrivateMemoryBytes,
            PrivateMemoryBytes);

        if (start.ProcessCpuMilliseconds.HasValue && ProcessCpuMilliseconds.HasValue)
        {
            var cpuMilliseconds = Math.Max(
                0,
                ProcessCpuMilliseconds.Value - start.ProcessCpuMilliseconds.Value);
            result["cpuMilliseconds"] = cpuMilliseconds;

            if (durationMilliseconds > 0)
            {
                result["averageCpuPercent"] =
                    cpuMilliseconds /
                    durationMilliseconds /
                    Math.Max(1, Environment.ProcessorCount) *
                    100.0;
            }
        }

        return result;
    }

    private static double? TryRead(Func<double> capture)
    {
        try
        {
            return capture();
        }
        catch
        {
            return null;
        }
    }

    private static long? TryRead(Func<long> capture)
    {
        try
        {
            return capture();
        }
        catch
        {
            return null;
        }
    }

    private static void AddIfPresent(JObject result, string name, double? value)
    {
        if (value.HasValue)
            result[name] = value.Value;
    }

    private static void AddIfPresent(JObject result, string name, long? value)
    {
        if (value.HasValue)
            result[name] = value.Value;
    }

    private static void AddDelta(
        JObject result,
        string name,
        long? start,
        long? end)
    {
        if (start.HasValue && end.HasValue)
            result[name] = end.Value - start.Value;
    }
}
