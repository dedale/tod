using NUnit.Framework;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms.DataVisualization.Charting;
using Tod.Jenkins;
using Tod.Tests.Jenkins;

namespace Tod.Windows.Tests;

internal static class RequestEx
{
    public static Request MinutesAgo(this Request request, int minutes)
    {
        var json = JsonSerializer.Serialize(request);
        var node = JsonNode.Parse(json)!;
        var createdUtc = node["CreatedUtc"]!.GetValue<DateTime>();
        node["CreatedUtc"] = createdUtc.AddMinutes(-minutes);
        return JsonSerializer.Deserialize<Request>(node)!;
    }
}

[TestFixture]
internal sealed class HtmlGanttChartTests
{
    private static readonly string s_user = "user";
    private static readonly string s_userEmail = $"user@example.org";
    private static readonly BranchName _baseBranch = new("main");

    private static readonly JobName _baseCoreRoot = new("MAIN-Core-build");
    private static readonly JobName _testCoreRoot = new("CUSTOM-Core-build");
    private static readonly JobName _baseCoreDevTest = new("MAIN-Core-Dev-tests");
    private static readonly JobName _testCoreDevTest = new("CUSTOM-Core-Dev-tests");

    private static Request NewRequest() => Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), _baseBranch, [], s_user, s_userEmail);

    private sealed record BuildTimes(int StartMin, int? EndMin = null);

    private static RootBuild NewRootBuild(DateTime start, BuildReference build, BuildTimes times)
    {
        return new RootBuild(build.JobName, "id", build.BuildNumber, start.AddMinutes(times.StartMin), start.AddMinutes(times.EndMin!.Value), true, [], []);
    }

    private static TestBuild NewTestBuild(DateTime start, BuildReference build, BuildTimes times)
    {
        return new TestBuild(build.JobName, "id", build.BuildNumber, start.AddMinutes(times.StartMin), start.AddMinutes(times.EndMin!.Value), true, [], []);
    }

    private static async Task<RequestState> GetSimpleRequest(OnDemandBuilds onDemandBuilds, int startedMin, BuildTimes rootTimes, BuildTimes? testTimes = null)
    {
        var start = DateTime.UtcNow.Add(TimeSpan.FromMinutes(-startedMin));
        var request = NewRequest().MinutesAgo(startedMin);

        var chains = new[] { new RequestChain(
            new BuildReference(_baseCoreRoot, RandomData.NextBuildNumber),
            RequestRootBuildReference.Queue(_testCoreRoot, request.Commit),
            [ new RequestBuildDiff(_baseCoreDevTest, _testCoreDevTest), ]
        ),};
        Func<OnDemandJobKind, JobName, TriggerParameters, Task> triggerBuild = (_, _, _) => Task.CompletedTask;
        var requestState = await RequestState.New(request, chains, onDemandBuilds, triggerBuild).ConfigureAwait(false);

        if (rootTimes.EndMin.HasValue)
        {
            var testCoreRootRef = new BuildReference(_testCoreRoot, RandomData.NextBuildNumber);
            requestState = await requestState.TriggerTests(testCoreRootRef, job => Task.CompletedTask).ConfigureAwait(false);
            onDemandBuilds.TryAdd(NewRootBuild(start, testCoreRootRef, rootTimes));

            if (testTimes?.EndMin.HasValue == true)
            {
                var testCoreDevTestRef = new BuildReference(_testCoreDevTest, RandomData.NextBuildNumber);
                requestState = requestState
                    .DoneBaselineTestBuild(requestState.ChainDiffs[0].BaselineRoot, new BuildReference(_baseCoreDevTest, RandomData.NextBuildNumber))
                    .DoneOnDemandTestBuild(testCoreRootRef, testCoreDevTestRef);
                onDemandBuilds.TryAdd(NewTestBuild(start, testCoreDevTestRef, testTimes));
            }
        }

        return requestState;
    }

    private static async Task<RequestState> GetBigRequest(OnDemandBuilds onDemandBuilds, int startedMin, BuildTimes rootTimes, BuildTimes[] testTimes)
    {
        var start = DateTime.UtcNow.Add(TimeSpan.FromMinutes(-startedMin));
        var request = NewRequest().MinutesAgo(startedMin);

        var chains = new[] { new RequestChain(
            new BuildReference(_baseCoreRoot, RandomData.NextBuildNumber),
            RequestRootBuildReference.Queue(_testCoreRoot, request.Commit),
            [.. Enumerable.Range(1, testTimes.Length).Select(i => new RequestBuildDiff(RefJob(i), TestJob(i)))]
        ),};
        Func<OnDemandJobKind, JobName, TriggerParameters, Task> triggerBuild = (_, _, _) => Task.CompletedTask;
        var requestState = await RequestState.New(request, chains, onDemandBuilds, triggerBuild).ConfigureAwait(false);

        if (rootTimes.EndMin.HasValue)
        {
            var testCoreRootRef = new BuildReference(_testCoreRoot, RandomData.NextBuildNumber);
            requestState = await requestState.TriggerTests(testCoreRootRef, job => Task.CompletedTask).ConfigureAwait(false);
            onDemandBuilds.TryAdd(NewRootBuild(start, testCoreRootRef, rootTimes));

            for (var i = 1; i <= testTimes.Length; i++)
            {
                if (!testTimes[i - 1].EndMin.HasValue)
                {
                    continue;
                }
                var testCoreDevTestRef = new BuildReference(TestJob(i), RandomData.NextBuildNumber);
                requestState = requestState
                    .DoneBaselineTestBuild(requestState.ChainDiffs[0].BaselineRoot, new BuildReference(RefJob(i), RandomData.NextBuildNumber))
                    .DoneOnDemandTestBuild(testCoreRootRef, testCoreDevTestRef);
                onDemandBuilds.TryAdd(NewTestBuild(start, testCoreDevTestRef, testTimes[i - 1]));
            }
        }

        JobName RefJob(int i) => new($"MAIN-Core-Dev-tests-{i}");
        JobName TestJob(int i) => new($"CUSTOM-Core-Dev-tests-{i}");

        return requestState;
    }

    private sealed record Bar(int X, int Y, int Length, BarColor Color, string Label, string AxisLabel)
    {
        public static IEnumerable<Bar> Load(DataPointCollection points)
        {
            return points.Select(p => new Bar(
                (int)p.XValue, (int)p.YValues[0], (int)(p.YValues[1] - p.YValues[0]), new BarColor(p.LabelForeColor, p.Color), p.Label, p.AxisLabel));
        }
    }

    private sealed class BarXY
    {
        private readonly Dictionary<int, Dictionary<int, Bar>> _barByYByX;

        public BarXY(IEnumerable<Bar> bars)
        {
            _barByYByX = bars
                .GroupBy(b => b.X)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToDictionary(b => b.Y, b => b));
        }

        public Dictionary<int, Bar> this[int x] => _barByYByX[x];
    }

    [Test]
    public async Task CreateGanttChart_WithSimpleRequest_HasExpectedFormDefaults()
    {
        var onDemandBuilds = new OnDemandBuilds(new InMemoryOnDemandStore());
        var requestState = await GetSimpleRequest(onDemandBuilds, 5, new(0)).ConfigureAwait(false);
        Action<Form> assert = form =>
        {
            Assert.That(form.Controls, Has.Count.EqualTo(1));
            Assert.That(form.Text, Is.EqualTo("Gantt chart"));
            var chart = (Chart)form.Controls[0];
            Assert.That(chart.Dock, Is.EqualTo(DockStyle.Fill));
            var area = chart.ChartAreas.Single();
            Assert.That(area.AxisX.Title, Is.EqualTo("Builds"));
            Assert.That(area.AxisX.Interval, Is.EqualTo(1));
            Assert.That(area.AxisX.Minimum, Is.EqualTo(-1));
            Assert.That(area.AxisX.Maximum, Is.EqualTo(1));
            Assert.That(area.AxisY.Title, Is.EqualTo("Duration (min)"));
            Assert.That(area.AxisY.Interval, Is.EqualTo(10));
            Assert.That(area.AxisY.Minimum, Is.Zero);
            Assert.That(area.AxisY.Maximum, Is.EqualTo(5));
        };
        HtmlGanttChart.New(requestState, onDemandBuilds, assert);
    }

    private static void TestBars(RequestState requestState, OnDemandBuilds onDemandBuilds, Action<BarXY> assert)
    {
        Action<Form> assertForm = form =>
        {
            //form.ShowDialog();
            var chart = (Chart)form.Controls[0];
            Assert.That(chart.Series, Has.Count.EqualTo(1));
            var series = chart.Series[0];
            var barXY = new BarXY(Bar.Load(series.Points));
            assert(barXY);
        };
        HtmlGanttChart.New(requestState, onDemandBuilds, assertForm);
    }

    [Test]
    public async Task CreateGanttChart_WithSimpleDoneRequest_HasExpectedBars()
    {
        var onDemandBuilds = new OnDemandBuilds(new InMemoryOnDemandStore());
        var requestState = await GetSimpleRequest(onDemandBuilds, 12, new(0, 5), new(5, 12)).ConfigureAwait(false);
        Action<BarXY> assert = barXY =>
        {
            Assert.That(barXY[1][0], Is.EqualTo(new Bar(1, 0, 5, BarColor.Done, "5", _testCoreRoot.Value)));
            Assert.That(barXY[0][5], Is.EqualTo(new Bar(0, 5, 7, BarColor.Done, "7", _testCoreDevTest.Value)));
        };
        TestBars(requestState, onDemandBuilds, assert);
    }

    [Test]
    public async Task CreateGanttChart_WithSimpleRunningRequest_HasExpectedBars()
    {
        var onDemandBuilds = new OnDemandBuilds(new InMemoryOnDemandStore());
        var requestState = await GetSimpleRequest(onDemandBuilds, 12, new(0, 5), new(5)).ConfigureAwait(false);
        Action<BarXY> assert = barXY =>
        {
            Assert.That(barXY[1][0], Is.EqualTo(new Bar(1, 0, 5, BarColor.Done, "5", _testCoreRoot.Value)));
            Assert.That(barXY[0][5], Is.EqualTo(new Bar(0, 5, 7, BarColor.InProgress, "7", _testCoreDevTest.Value)));
        };
        TestBars(requestState, onDemandBuilds, assert);
    }

    [Test]
    public async Task CreateGanttChart_WithBigRequest_HasExpectedBars()
    {
        var onDemandBuilds = new OnDemandBuilds(new InMemoryOnDemandStore());
        var testTimes = Enumerable.Range(1, 20).Select(i => new BuildTimes(23, i < 18 ? 33 + i * 10 : null)).ToArray();
        var requestState = await GetBigRequest(onDemandBuilds, 33 + 17 * 10 + 5, new(0, 23), testTimes).ConfigureAwait(false);
        Action<BarXY> assert = barXY =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(barXY[20][0], Is.EqualTo(new Bar(20, 0, 23, BarColor.Done, "23", _testCoreRoot.Value)));
                for (var i = 0; i < 20; i++)
                {
                    if (i < 17)
                    {
                        Assert.That(barXY[19 - i][23], Is.EqualTo(new Bar(19 - i, 23, 20 + i * 10, BarColor.Done, (20 + i * 10).ToString(), $"CUSTOM-Core-Dev-tests-{i + 1}")));
                    }
                    else
                    {
                        Assert.That(barXY[19 - i][23], Is.EqualTo(new Bar(19 - i, 23, 18 * 10 + 5, BarColor.InProgress, (18 * 10 + 5).ToString(), $"CUSTOM-Core-Dev-tests-{i + 1}")));
                    }
                }
            }
        };
        TestBars(requestState, onDemandBuilds, assert);
    }
}
