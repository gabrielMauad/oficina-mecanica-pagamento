using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Api.Logging;

/// <summary>
/// Console formatter que emite uma linha JSON por entrada de log, com trace_id/span_id
/// obtidos de <see cref="Activity.Current"/> (ADR-004). Construído sobre a mesma
/// extensibilidade do ConsoleFormatter nativo (Microsoft.Extensions.Logging.Console),
/// sem dependências externas.
/// </summary>
public sealed class TraceJsonConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "trace-json";

    public TraceJsonConsoleFormatter() : base(FormatterName)
    {
    }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
            return;

        var activity = Activity.Current;

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            writer.WriteString("timestamp", DateTimeOffset.UtcNow.ToString("O"));
            writer.WriteString("level", GetLevelName(logEntry.LogLevel));
            writer.WriteString("category", logEntry.Category);
            writer.WriteString("message", message);

            if (activity is not null)
            {
                writer.WriteString("trace_id", activity.TraceId.ToHexString());
                writer.WriteString("span_id", activity.SpanId.ToHexString());
            }
            else
            {
                writer.WriteNull("trace_id");
                writer.WriteNull("span_id");
            }

            if (logEntry.State is IEnumerable<KeyValuePair<string, object>> stateProperties)
            {
                foreach (var property in stateProperties)
                {
                    if (property.Key == "{OriginalFormat}")
                        continue;

                    WriteProperty(writer, property.Key, property.Value);
                }
            }

            scopeProvider?.ForEachScope(static (scope, writer) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> scopeProperties)
                {
                    foreach (var property in scopeProperties)
                        WriteProperty(writer, property.Key, property.Value);
                }
            }, writer);

            if (logEntry.Exception is { } exception)
            {
                writer.WriteStartObject("exception");
                writer.WriteString("type", exception.GetType().FullName);
                writer.WriteString("message", exception.Message);
                writer.WriteString("stack_trace", exception.StackTrace);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        textWriter.Write(Encoding.UTF8.GetString(buffer.ToArray()));
        textWriter.Write(Environment.NewLine);
    }

    private static void WriteProperty(Utf8JsonWriter writer, string key, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNull(key);
                break;
            case string s:
                writer.WriteString(key, s);
                break;
            case bool b:
                writer.WriteBoolean(key, b);
                break;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                writer.WriteNumber(key, Convert.ToInt64(value));
                break;
            case float or double or decimal:
                writer.WriteNumber(key, Convert.ToDouble(value));
                break;
            case DateTime dt:
                writer.WriteString(key, dt.ToString("O"));
                break;
            case DateTimeOffset dto:
                writer.WriteString(key, dto.ToString("O"));
                break;
            default:
                writer.WriteString(key, value.ToString());
                break;
        }
    }

    private static string GetLevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "Trace",
        LogLevel.Debug => "Debug",
        LogLevel.Information => "Information",
        LogLevel.Warning => "Warning",
        LogLevel.Error => "Error",
        LogLevel.Critical => "Critical",
        _ => "None"
    };
}
