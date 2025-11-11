using System.Diagnostics.Metrics;

namespace Tod.Core;

internal static class Telemetry
{
    public static readonly Meter Meter = new("Tod.Telemetry");
    public static readonly Counter<int> BuildsLoaded = Meter.CreateCounter<int>("builds_loaded");
    public static readonly Counter<int> BuildsSaved = Meter.CreateCounter<int>("builds_saved");
}