using Serilog;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Tod.Net;

namespace Tod.Jenkins;

internal enum BuildStatus
{
    Pending,
    Triggered,
    Success,
    Failed
}

internal sealed record BuildReferenceResult(JobName JobName, int Number, BuildStatus Status)
{
    public static BuildReferenceResult Pending(JobName job) => new(job, 0, BuildStatus.Pending);

    public static BuildReferenceResult Queued(JobName job) => new(job, 0, BuildStatus.Triggered);

    public static BuildReferenceResult Done(BuildReference build, bool isSuccessful) => new(build.JobName, build.BuildNumber, isSuccessful ? BuildStatus.Success : BuildStatus.Failed);

    public static BuildReferenceResult Done(BaseBuild build) => Done(build.Reference, build.IsSuccessful);

    public string Id => Number > 0 ? $"{JobName} #{Number}" : JobName.Value;
}

internal abstract class BuildDiff
{
    public abstract void Match(Action<string> onNotComparable, Action<FailedTestDiff> onComparable);
    public abstract T Match<T>(Func<string, T> onNotComparable, Func<FailedTestDiff, T> onComparable);

    private sealed class NotComparable(string message) : BuildDiff
    {
        public override void Match(Action<string> onNotComparable, Action<FailedTestDiff> onComparable) => onNotComparable(message);
        public override T Match<T>(Func<string, T> onNotComparable, Func<FailedTestDiff, T> onComparable) => onNotComparable(message);
    }

    private sealed class Comparable(FailedTestDiff diff) : BuildDiff
    {
        public override void Match(Action<string> onNotComparable, Action<FailedTestDiff> onComparable) => onComparable(diff);
        public override T Match<T>(Func<string, T> onNotComparable, Func<FailedTestDiff, T> onComparable) => onComparable(diff);
    }

    public static readonly BuildDiff OnDemandPending = new NotComparable("Build not run");
    public static BuildDiff OnDemandTriggered(JobName job) => new NotComparable($"Build {job} not done");
    public static readonly BuildDiff ReferencePending = new NotComparable("No reference build");
    public static BuildDiff Diff(FailedTestDiff diff) => new Comparable(diff);
}

internal sealed class BuildDiffResult(BuildReferenceResult reference, BuildReferenceResult result, BuildDiff diff)
{
    public BuildReferenceResult Reference { get; } = reference;
    public BuildReferenceResult Result { get; } = result;
    public BuildDiff Diff { get; } = diff;
}

internal sealed class ChainReport(BuildReferenceResult rootResult, BuildDiffResult[] buildDiffs)
{
    public BuildReferenceResult RootResult { get; } = rootResult;
    public BuildDiffResult[] BuildDiffs { get; } = buildDiffs;
}

internal sealed class RequestReport(ChainReport[] chainReports, XElement? ganttChart = null)
{
    public ChainReport[] ChainReports { get; } = chainReports;
    public XElement? GanttChart { get; } = ganttChart;
}

internal interface IRequestReportBuilder
{
    RequestReport Build(RequestState requestState, IEnumerable<BranchReference> branchReferences, OnDemandBuilds onDemandBuilds, IFlakyTests flakyTests);
}

internal static class IRequestReportBuilderExtensions
{
    public static RequestReport Build(this IRequestReportBuilder builder, RequestState requestState, Workspace workspace)
    {
        return builder.Build(requestState, workspace.BranchReferences, workspace.OnDemandBuilds, workspace.FlakyTests);
    }
}

internal sealed class RequestReportBuilder : IRequestReportBuilder
{
    public static readonly RequestReportBuilder Instance = new();

    private RequestReportBuilder()
    {
    }

    [ExcludeFromCodeCoverage]
    private static XElement? GetGanttChart(RequestState requestState, OnDemandBuilds onDemandBuilds)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var fileName = "Tod.Windows.dll";
            var path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (File.Exists(path))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(path);
                    var type = assembly.GetType("Tod.Windows.HtmlGanttChartBuilder");
                    var method = type?.GetMethod("Build", BindingFlags.Public | BindingFlags.Static, [typeof(RequestState), typeof(OnDemandBuilds)]);
                    if (method?.Invoke(null, [requestState, onDemandBuilds]) is XElement element)
                    {
                        return element;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to build Gantt chart from '{WindowsLib}'", fileName);
                }
            }
        }
        return null;
    }

    private static RequestReport Build(RequestState requestState, BranchReference branchReference, OnDemandBuilds onDemandBuilds, IFlakyTests flakyTests)
    {
        var chainReports = new List<ChainReport>();
        foreach (var chainDiff in requestState.ChainDiffs)
        {
            var rootResult = chainDiff.OnDemandRoot.Match(
                onQueued: (job, _) => BuildReferenceResult.Queued(job),
                onDone: buildRef => BuildReferenceResult.Done(onDemandBuilds.RootBuilds.GetOrAdd(buildRef.JobName)[buildRef]));

            var buildDiffs = new List<BuildDiffResult>();
            foreach (var diff in chainDiff.TestBuildDiffs)
            {
                TestBuild? referenceTestBuild = null;
                var referenceResult = diff.ReferenceBuild.Match(
                    onPending: BuildReferenceResult.Pending,
                    onDone: referenceBuildRef =>
                    {
                        referenceTestBuild = branchReference.GetTestBuild(referenceBuildRef);
                        return BuildReferenceResult.Done(referenceTestBuild);
                    }
                );

                diff.OnDemandBuild.Match(
                    onPending: jobName => buildDiffs.Add(new(referenceResult, BuildReferenceResult.Pending(jobName), BuildDiff.OnDemandPending)),
                    onQueued: jobName => buildDiffs.Add(new(referenceResult, BuildReferenceResult.Queued(jobName), BuildDiff.OnDemandTriggered(jobName))),
                    onDone: onDemandBuildRef =>
                    {
                        var onDemandTestBuild = onDemandBuilds.GetTestBuild(onDemandBuildRef);
                        diff.ReferenceBuild.Match(
                            onPending: jobName => buildDiffs.Add(new(referenceResult, BuildReferenceResult.Done(onDemandBuildRef, onDemandTestBuild.IsSuccessful), BuildDiff.ReferencePending)),
                            onDone: referenceBuildRef =>
                            {
                                var failedTestsDiff = FailedTestDiffer.Diff(referenceTestBuild!.JobName, referenceTestBuild.FailedTests, onDemandTestBuild.FailedTests, flakyTests);
                                buildDiffs.Add(new(referenceResult, BuildReferenceResult.Done(onDemandTestBuild), BuildDiff.Diff(failedTestsDiff)));
                            }
                        );
                    }
                );
            }
            chainReports.Add(new ChainReport(rootResult, [.. buildDiffs]));
        }

        var ganttChart = GetGanttChart(requestState, onDemandBuilds);

        return new RequestReport([.. chainReports], ganttChart);
    }

    public RequestReport Build(RequestState request, IEnumerable<BranchReference> branchReferences, OnDemandBuilds onDemandBuilds, IFlakyTests flakyTests)
    {
        var branchReference = branchReferences.Single(r => r.BranchName == request.Request.GitReference.Branch);
        return Build(request, branchReference, onDemandBuilds, flakyTests);
    }
}

internal interface IRequestReportSender
{
    Task Send(RequestState request, RequestReport report);
    Task Send(RequestState request, Workspace workspace);
}

internal interface IJobLinker
{
    string GetUrl(JobName job);
    string GetUrl(JobName job, int buildNumber);
}

internal sealed class JenkinsJobLinker(JenkinsConfig config) : IJobLinker
{
    public string GetUrl(JobName job)
    {
        return $"{config.Url}/{job.UrlPath}/";
    }
    public string GetUrl(JobName job, int buildNumber)
    {
        return $"{config.Url}/{job.UrlPath}/{buildNumber}";
    }
}

internal static class IJobLinkerExtensions
{
    public static string GetUrl(this IJobLinker linker, BuildReference buildReference)
    {
        return linker.GetUrl(buildReference.JobName, buildReference.BuildNumber);
    }

    public static string GetUrl(this IJobLinker linker, BuildReferenceResult buildReferenceResult)
    {
        return buildReferenceResult.Number > 0
            ? linker.GetUrl(buildReferenceResult.JobName, buildReferenceResult.Number)
            : linker.GetUrl(buildReferenceResult.JobName);
    }
}

internal sealed class RequestReportSender(IJobLinker jobLinker, IMailSender mailSender) : IRequestReportSender
{
    private sealed record Counts(int Added, int Update, int Flaky)
    {
        public static readonly Counts Zero = new(0, 0, 0);

        public Counts Add(Counts other) => new(Added + other.Added, Update + other.Update, Flaky + other.Flaky);
    }

    private static Counts CountFailedTests(RequestReport report)
    {
        return report.ChainReports.SelectMany(chainReport =>
            chainReport.BuildDiffs.Select(buildDiffResult =>
                buildDiffResult.Diff.Match(
                    onNotComparable: _ => Counts.Zero,
                    onComparable: diff => new Counts(
                        diff.FailedTests.Count(t => t.Newness == Newness.New),
                        diff.FailedTests.Count(t => t.Newness == Newness.Updated),
                        diff.FailedTests.Count(t => t.IsFlaky))
                ))).Aggregate(Counts.Zero, (acc, counts) => acc.Add(counts));
    }

    private static IEnumerable<object?> GetElements(FailedTestDiff diff)
    {
        var statuses = new List<object>();
        int added = 0;
        int addedFlaky = 0;
        int updated = 0;
        int updatedFlaky = 0;
        foreach (var result in diff.FailedTests)
        {
            switch (result.Newness)
            {
                case Newness.New:
                    added++;
                    if (result.IsFlaky)
                    {
                        addedFlaky++;
                    }
                    break;
                case Newness.Updated:
                    updated++;
                    if (result.IsFlaky)
                    {
                        updatedFlaky++;
                    }
                    break;
            }
        }

        if (diff.Status.HasFlag(TestBuildDiffStatus.NewFailures))
        {
            statuses.Add($"🔴 { added} new failed test{(added > 1 ? "s" : "")}{(addedFlaky > 0 ? $" (incl. 🟠 {addedFlaky} flaky)" : "")}");
        }
        if (diff.Status.HasFlag(TestBuildDiffStatus.UpdatedFailures))
        {
            if (statuses.Count > 0)
            {
                statuses.Add(new XElement("br"));
            }
            statuses.Add($"🔴 {updated} updated failed test{(updated > 1 ? "s" : "")}{(updatedFlaky > 0 ? $" (incl. 🟠 {updatedFlaky} flaky)" : "")}");
        }
        if (diff.Status.HasFlag(TestBuildDiffStatus.SameFailures))
        {
            if (statuses.Count > 0)
            {
                statuses.Add(new XElement("br"));
            }
            statuses.Add("⚠ same failed tests (not included in report)");
        }
        if (diff.Status == TestBuildDiffStatus.OK)
        {
            statuses.Add("✅ OK");
        }

        yield return new XElement("tr",
            new XElement("td",
                new XAttribute("colspan", "2"),
                statuses));

        yield return diff.FailedTests.Length > 0 ? diff.FailedTests
            .Select(result => new XElement("tr",
                new XElement("td",
                    new XAttribute("class", "test-info"),
                    $"{(result.IsFlaky ? "🟠" : "🔴")}",
                    result.IsFlaky ? new XElement("span",
                        new XAttribute("class", "label unstable"),
                        "flaky") : null,
                    result.Newness == Newness.New ? new XElement("span",
                        new XAttribute("class", "label new"),
                        "new") : null),
                new XElement("td",
                    new XAttribute("class", "test-name"),
                    new XElement("span",
                        new XAttribute("style", "color:#2a5db0;"),
                        new XAttribute("title", $"{result.Test.ClassName} {result.Test.TestName}"),
                        $"{result.Test.ClassName} {result.Test.TestName}"),
                    new XElement("pre",
                        TodReports.Shorten(result.Test.ErrorDetails))))) : null;
    }

    private IEnumerable<object> GetLink(BuildReferenceResult result, string emoji)
    {
        yield return emoji;
        yield return new XElement("a",
            new XAttribute("href", jobLinker.GetUrl(result)),
            result.Id);
        yield return $": {result.Status}";
    }

    private IEnumerable<object> GetElement(BuildDiffResult buildDiffResult)
    {
        yield return new XElement("tr",
            new XElement("th",
                new XAttribute("colspan", "2"),
                new XAttribute("class", "build-header")),
            GetLink(buildDiffResult.Result, "🧪"),
            " vs ",
            GetLink(buildDiffResult.Reference, "📘"));

        yield return buildDiffResult.Diff.Match(
            onNotComparable: message => (object)new XElement("tr",
                new XElement("td",
                    new XAttribute("colspan", "2"),
                    $"Diff: {message}")),
            onComparable: GetElements);
    }

    private XElement GetElement(ChainReport chainReport)
    {
        return new XElement("table",
            new XAttribute("class", "tests"),
            new XElement("tr",
                new XElement("th",
                    new XAttribute("colspan", "2"),
                    new XAttribute("class", "build-header")),
                GetLink(chainReport.RootResult, "⚙")),
            chainReport.BuildDiffs.Select(GetElement));
    }

    private XElement GetLink(RequestRootBuildReference buildReference)
    {
        return buildReference.Match(
            onQueued: (job, _) => new XElement("a",
                new XAttribute("href", jobLinker.GetUrl(job)),
                job),
            onDone: buildRef => new XElement("a",
                new XAttribute("href", jobLinker.GetUrl(buildRef)),
                buildRef));
    }

    private XElement GetBody(RequestState request, RequestReport report, bool full)
    {
        var (newFailedTests, updatedFailedTests, flakyTests) = CountFailedTests(report);

        var failedTestsSummary = newFailedTests + updatedFailedTests == 0
            ? "💚 none"
            : $"🔴 {string.Join(", ", new string?[] {
                newFailedTests > 0 ? $"{newFailedTests} New" : null,
                updatedFailedTests > 0 ? $"{updatedFailedTests} Updated" : null,
                flakyTests > 0 ? $" (incl. 🟠 {flakyTests} Flaky)" : null
            }.Where(x => x != null))}";

        return new XElement("body",
            new XElement("h1",
                new XElement("span", new XAttribute("style", "color:red"), "T"),
                "est ",
                new XElement("span", new XAttribute("style", "color:red"), "O"),
                "n ",
                new XElement("span", new XAttribute("style", "color:red"), "D"),
                "emand Report"),
            new XElement("table",
                new XAttribute("class", "summary"),
                new XElement("tr",
                    new XElement("th", "📌 Request ID"),
                    new XElement("td", request.Request.Id)),
                new XElement("tr",
                    new XElement("th", "🗓 Created (UTC)"),
                    new XElement("td", request.Request.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss"))),
                new XElement("tr",
                    new XElement("th", "🔀 Tested Commit"),
                    new XElement("td", request.Request.Commit.Value)),
                new XElement("tr",
                    new XElement("th", "🌿 Reference branch"),
                    new XElement("td", $"{request.Request.GitReference.Branch} (commit {request.Request.GitReference.Commit})")),
                new XElement("tr",
                    new XElement("th", "🎯 Test Filters"),
                    new XElement("td", string.Join(" ", request.Request.GetTestFilters()))),
                newFailedTests + updatedFailedTests > 0 ? new XElement("tr",
                    new XElement("th", $"🧪 Failed test{(newFailedTests + updatedFailedTests > 1 ? "s" : "")}"),
                    new XElement("td", failedTestsSummary)) : null),
            new XElement("table",
                new XAttribute("class", "chains"),
                new XElement("tr",
                    new XElement("th", "Chain"),
                    new XElement("th", "Root Build"),
                    new XElement("th", "Test Builds")),
                request.ChainDiffs.Select(chainDiff => new XElement("tr",
                    new XElement("td", GetLink(chainDiff.OnDemandRoot)),
                    new XElement("td", $"{(chainDiff.Status == ChainStatus.RootTriggered ? "⏳" : "✅")} {chainDiff.Status}"),
                    new XElement("td", $"{(chainDiff.Status == ChainStatus.RootTriggered ? "" : chainDiff.Status == ChainStatus.TestsTriggered ? "⏳" : "🏁")} {chainDiff.Status}")))),
            full ? report.GanttChart : null,
            full ? report.ChainReports.Select(GetElement) : null);
    }

    private XDocument GetDoc(RequestState request, RequestReport report, bool full)
    {
        return new XDocument(new XElement("html",
            TodReports.GetHead(),
            GetBody(request, report, full)));
    }

    public Task Send(RequestState request, RequestReport report)
    {
        Log.Information("Sending report for request {RequestId} to {UserEmail}", request.Request.Id, request.Request.UserEmail);

        var body = GetDoc(request, report, false);
        var attachment = GetDoc(request, report, true);
        return mailSender.Send(request.Request.UserEmail, "On-Demand Report", body.ToString(), attachment.ToString());
    }

    public Task Send(RequestState requestState, Workspace workspace)
    {
        var report = RequestReportBuilder.Instance.Build(requestState, workspace);
        return Send(requestState, report);
    }
}
