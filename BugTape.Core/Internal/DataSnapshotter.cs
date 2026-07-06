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
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BugTape.Core.Internal;

internal sealed class DataSnapshotter
{
    [ThreadStatic]
    private static bool s_reportingDiagnostic;

    private readonly BugTapeOptions m_options;

    public DataSnapshotter(BugTapeOptions options)
    {
        m_options = options;
    }

    public JToken Capture(object value)
    {
        if (value == null)
            return null;

        try
        {
            var serializer = JsonSerializer.Create(CreateSettings());
            var token = JToken.FromObject(value, serializer);
            token = ApplyLimits(token, 0);

            var byteCount = Encoding.UTF8.GetByteCount(token.ToString(Formatting.None));
            if (byteCount <= m_options.MaxEventPayloadBytes)
                return token;

            return new JObject
            {
                ["truncated"] = true,
                ["reason"] = "Maximum event payload size exceeded.",
                ["originalByteCount"] = byteCount
            };
        }
        catch (Exception exception)
        {
            ReportDiagnostic("Structured data could not be captured.", exception);
            return new JObject
            {
                ["captureError"] = exception.GetType().FullName
            };
        }
    }

    public JObject CaptureException(Exception exception)
    {
        if (exception == null)
            return null;

        return CaptureException(exception, 0);
    }

    public string CaptureText(string value)
    {
        return LimitString(value);
    }

    private JObject CaptureException(Exception exception, int depth)
    {
        var result = new JObject
        {
            ["type"] = exception.GetType().FullName,
            ["message"] = LimitString(exception.Message),
            ["stackTrace"] = LimitString(exception.StackTrace)
        };

        if (exception.InnerException != null && depth < m_options.MaxObjectDepth)
            result["innerException"] = CaptureException(exception.InnerException, depth + 1);

        return result;
    }

    private JsonSerializerSettings CreateSettings()
    {
        return new JsonSerializerSettings
        {
            Culture = CultureInfo.InvariantCulture,
            DateFormatString = "O",
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            MaxDepth = m_options.MaxObjectDepth,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Error = (_, args) =>
            {
                ReportDiagnostic("A structured-data member could not be captured.", args.ErrorContext.Error);
                args.ErrorContext.Handled = true;
            }
        };
    }

    private JToken ApplyLimits(JToken token, int depth)
    {
        if (token == null)
            return JValue.CreateNull();
        if (depth >= m_options.MaxObjectDepth &&
            (token.Type == JTokenType.Array || token.Type == JTokenType.Object))
        {
            return new JValue("[truncated: maximum object depth]");
        }

        if (token is JObject sourceObject)
        {
            var result = new JObject();
            foreach (var property in sourceObject.Properties().Take(m_options.MaxCollectionLength))
                result[property.Name] = ApplyLimits(property.Value, depth + 1);
            if (sourceObject.Count > m_options.MaxCollectionLength)
                result["_bugTapeTruncated"] = true;
            return result;
        }

        if (token is JArray sourceArray)
        {
            var result = new JArray(
                sourceArray.Take(m_options.MaxCollectionLength)
                    .Select(item => ApplyLimits(item, depth + 1)));
            if (sourceArray.Count > m_options.MaxCollectionLength)
                result.Add("[truncated: maximum collection length]");
            return result;
        }

        if (token.Type == JTokenType.String)
            return new JValue(LimitString(token.Value<string>()));

        return token.DeepClone();
    }

    private string LimitString(string value)
    {
        if (value == null || value.Length <= m_options.MaxStringLength)
            return value;
        return value.Substring(0, m_options.MaxStringLength) + "[truncated]";
    }

    private void ReportDiagnostic(string message, Exception exception)
    {
        if (s_reportingDiagnostic)
            return;

        try
        {
            s_reportingDiagnostic = true;
            m_options.DiagnosticMessageHandler?.Invoke(
                $"{message} {exception.GetType().FullName}");
        }
        catch
        {
            // Host diagnostics must never interfere with recording.
        }
        finally
        {
            s_reportingDiagnostic = false;
        }
    }
}
