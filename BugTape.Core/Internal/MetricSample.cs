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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BugTape.Core.Internal;

internal sealed class MetricSample
{
    public long Sequence { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }

    public JObject Metrics { get; set; }

    public JObject Serialize(Formatting formatting)
    {
        var result = new JObject
        {
            ["schemaVersion"] = 1,
            ["timestampUtc"] = TimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["sequence"] = Sequence
        };

        if (Metrics != null)
        {
            foreach (var property in Metrics.Properties())
                result[property.Name] = property.Value.DeepClone();
        }

        return formatting == Formatting.None ? result : JObject.Parse(result.ToString(formatting));
    }
}
