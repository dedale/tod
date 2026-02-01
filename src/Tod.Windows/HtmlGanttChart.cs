using Serilog;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Forms.DataVisualization.Charting;
using System.Xml.Linq;
using Tod.Jenkins;

namespace Tod.Windows;

internal sealed record BuildChartData(string Name, DateTime StartTimeUtc, DateTime EndTimeUtc, bool IsDone)
{
    public static BuildChartData Done(BaseBuild build)
    {
        return new BuildChartData(build.JobName.Value.Split('/').Last(), build.StartTimeUtc, build.EndTimeUtc, true);
    }

    public static BuildChartData InProgress(JobName Name, DateTime StartTimeUtc)
    {
        return new BuildChartData(Name.Value.Split('/').Last(), StartTimeUtc, DateTime.UtcNow, false);
    }
}

internal sealed record BarColor(Color Text, Color Back)
{
    public static readonly BarColor Hidden = new(Color.White, Color.White);
    public static readonly BarColor Done = new(Color.White, Color.Green);
    public static readonly BarColor InProgress = new(Color.Black, Color.Orange);
}

internal static class HtmlGanttChart
{
    private static List<BuildChartData> GatherBuildData(RequestState requestState, OnDemandBuilds onDemandBuilds)
    {
        var builds = new List<BuildChartData>();
        foreach (var chainDiff in requestState.ChainDiffs)
        {
            DateTime testStart = requestState.Request.CreatedUtc;

            chainDiff.OnDemandRoot.Match(
                onQueued: (job, _) => builds.Add(BuildChartData.InProgress(job, requestState.Request.CreatedUtc)),
                onDone: buildRef =>
                {
                    var build = onDemandBuilds.GetRootBuild(buildRef);
                    builds.Add(BuildChartData.Done(build));
                    testStart = build.EndTimeUtc;
                });

            foreach (var diff in chainDiff.TestBuildDiffs)
            {
                diff.OnDemandBuild.Match(
                    onPending: job => { },
                    onQueued: job => builds.Add(BuildChartData.InProgress(job, testStart)),
                    onDone: buildRef => builds.Add(BuildChartData.Done(onDemandBuilds.GetTestBuild(buildRef))));
            }
        }
        builds.Reverse();
        return builds;
    }

    private static Form GetForm(List<BuildChartData> buildData, int totalMinutes, out Chart chart)
    {
        var form = new Form
        {
            Text = "Gantt chart",
            Height = 150 + 20 * buildData.Count,
            Width = 20 + 10 * buildData.Max(b => b.Name.Length) + 3 * totalMinutes,
        };
        chart = new Chart
        {
            Dock = DockStyle.Fill,
        };
        var area = new ChartArea
        {
            AxisX =
            {
                Title = "Builds",
                Interval = 1,
                Minimum = -1,
                Maximum = buildData.Count,
            },
            AxisY =
            {
                Title = "Duration (min)",
                Minimum = 0,
                Interval = totalMinutes >= 300 ? 60 : 10,
                Maximum = totalMinutes,
            },
        };
        chart.ChartAreas.Add(area);
        form.Controls.Add(chart);
        return form;
    }

    private sealed class ChartBuilder
    {
        private readonly Chart _chart;
        private readonly Series _series;
        private readonly DateTime _startTime;
        private readonly DateTime _endTime;

        public ChartBuilder(Chart chart, DateTime startTime, DateTime endTime)
        {
            _chart = chart;
            _series = new Series
            {
                ChartType = SeriesChartType.RangeBar,
            };
            chart.Series.Add(_series);
            _startTime = startTime;
            _endTime = endTime;
        }

        private DataPoint AddPoint(int buildIndex, DateTime fromTime, DateTime toTime, BarColor barColor)
        {
            var fromMin = (fromTime - _startTime).TotalMinutes;
            var toMin = (toTime - _startTime).TotalMinutes;
            var index = _series.Points.AddXY(buildIndex, fromMin, toMin);
            var point = _series.Points[index];
            point.Color = barColor.Back;
            point.LabelForeColor = barColor.Text;
            var delay = (int)Math.Round(toMin - fromMin, 0);
            if (delay >= 5)
            {
                point.Label = delay.ToString();
            }
            return point;
        }

        public XElement Build(List<BuildChartData> buildData)
        {
            AddPoint(-1, _startTime, _startTime, BarColor.Hidden).AxisLabel = " ";
            for (var buildIndex = 0; buildIndex < buildData.Count; buildIndex++)
            {
                var build = buildData[buildIndex];
                var barColor = build.IsDone ? BarColor.Done : BarColor.InProgress;
                AddPoint(buildIndex, build.StartTimeUtc, build.EndTimeUtc, barColor).AxisLabel = build.Name;
            }
            AddPoint(buildData.Count, _endTime, _endTime, BarColor.Hidden).AxisLabel = " ";

            using (var ms = new MemoryStream())
            {
                _chart.SaveImage(ms, ChartImageFormat.Png);
                var element = new XElement("img",
                    new XAttribute("src", $"data:image/png;base64,{Convert.ToBase64String(ms.ToArray())}")
                );
                return element;
            }
        }
    }

    public static XElement New(RequestState requestState, OnDemandBuilds onDemandBuilds, Action<Form>? postBuild)
    {
        var buildData = GatherBuildData(requestState, onDemandBuilds);

        var startTime = buildData.Min(b => b.StartTimeUtc);
        var endTime = buildData.Max(b => b.EndTimeUtc);
        var requestDuration = endTime - startTime;
        var totalMinutes = (int)Math.Round(requestDuration.TotalMinutes, 0);

        using var form = GetForm(buildData, totalMinutes, out var chart);
        var builder = new ChartBuilder(chart, startTime, endTime);
        var element = builder.Build(buildData);
        postBuild?.Invoke(form);
        return element;
    }
}

[ExcludeFromCodeCoverage]
internal static class HtmlGanttChartBuilder
{
    public static XElement Build(RequestState requestState, OnDemandBuilds onDemandBuilds)
    {
        Log.Debug("Building Gantt chart HTML element");
        using var resolver = new WindowsDesktopAssemblyResolver();
        return HtmlGanttChart.New(requestState, onDemandBuilds, null);
    }
}
