using Serilog.Core;
using Serilog.Events;
using System.Diagnostics.CodeAnalysis;
using Tod.Jenkins;

namespace Tod.Core;

internal sealed class TimeSpanEnricher : ILogEventEnricher
{
    internal static string Pretty(TimeSpan t)
    {
        if (t.TotalMilliseconds < 950)
        {
            return $"{t.TotalMilliseconds:0.#} ms";
        }
        if (t.TotalSeconds < 59.5)
        {
            return $"{t.TotalSeconds:0.#} s";
        }
        if (t.TotalMinutes < 59.5)
        {
            var min = Math.Round(t.TotalMinutes, 0);
            if (min > t.TotalMinutes || t.Seconds == 0)
            {
                return $"{min:0} min";
            }
            return $"{t.TotalMinutes:0} min {t.Seconds:0.} s";
        }
        var h = Math.Round(t.TotalHours, 0);
        if (h > t.TotalHours || t.Minutes == 0)
        {
            return $"{h:0} h";
        }
        return $"{t.TotalHours:0} h {t.Minutes:0.#} min";
    }

    internal static string ColoredPretty(TimeSpan t) => $"\x1b[38,5,0079m{Pretty(t)}\x1b[0m";

    [ExcludeFromCodeCoverage]
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var properties = logEvent.Properties
            .Where(p => p.Value is ScalarValue scalar && scalar.Value is TimeSpan)
            .ToList();

        foreach (var property in properties)
        {
            var timeSpan = (TimeSpan)((ScalarValue)property.Value).Value!;
            var pretty = ColoredPretty(timeSpan);
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(property.Key, pretty));
        }
    }
}

[ExcludeFromCodeCoverage]
internal static class JobNameFormatter
{
    public static string Format(JobName job)
    {
        var name = job.Value;
        var index = name.LastIndexOf('/');
        if (index >= 0)
        {
            index++;
            name = $"\x1b[90m{name[..index]}\x1b[38,5,0045m{name[index..]}";
        }
        return name;
    }
}

[ExcludeFromCodeCoverage]
internal sealed class JobNameDestructuringPolicy : IDestructuringPolicy
{
    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        if (value is JobName job)
        {
            result = new ScalarValue(JobNameFormatter.Format(job));
            return true;
        }
        result = null;
        return false;
    }
}