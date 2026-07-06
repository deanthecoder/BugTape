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
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BugTape.Core.Internal;

internal sealed class TimelineRecord
{
    public long Sequence { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }

    public string Type { get; set; }

    public string Name { get; set; }

    public string Message { get; set; }

    public string Level { get; set; }

    public string ActionId { get; set; }

    public string ParentActionId { get; set; }

    public string Outcome { get; set; }

    public double? DurationMilliseconds { get; set; }

    public JToken Data { get; set; }

    public JObject Exception { get; set; }

    public JObject Metrics { get; set; }

    public int SerializedByteCount { get; private set; }

    public string Serialize(Formatting formatting)
    {
        return ToJObject().ToString(formatting);
    }

    public void Measure()
    {
        SerializedByteCount = Encoding.UTF8.GetByteCount(Serialize(Formatting.None));
    }

    private JObject ToJObject()
    {
        var result = new JObject
        {
            ["schemaVersion"] = 1,
            ["timestampUtc"] = TimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["sequence"] = Sequence,
            ["type"] = Type
        };

        AddIfPresent(result, "name", Name);
        AddIfPresent(result, "message", Message);
        AddIfPresent(result, "level", Level);
        AddIfPresent(result, "actionId", ActionId);
        AddIfPresent(result, "parentActionId", ParentActionId);
        AddIfPresent(result, "outcome", Outcome);

        if (DurationMilliseconds.HasValue)
            result["durationMilliseconds"] = DurationMilliseconds.Value;
        if (Data != null)
            result["data"] = Data.DeepClone();
        if (Exception != null)
            result["exception"] = Exception.DeepClone();
        if (Metrics != null)
            result["metrics"] = Metrics.DeepClone();

        return result;
    }

    private static void AddIfPresent(JObject target, string name, string value)
    {
        if (!string.IsNullOrEmpty(value))
            target[name] = value;
    }
}
