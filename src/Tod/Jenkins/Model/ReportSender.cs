using Serilog;
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

internal sealed class BuildDiffResult(BuildReferenceResult result, BuildDiff diff)
{
    public BuildReferenceResult Result { get; } = result;
    public BuildDiff Diff { get; } = diff;
}

internal sealed class ChainReport(BuildReferenceResult rootResult, BuildDiffResult[] buildDiffs)
{
    public BuildReferenceResult RootResult { get; } = rootResult;
    public BuildDiffResult[] BuildDiffs { get; } = buildDiffs;
}

internal sealed class RequestReport(ChainReport[] chainReports)
{
    public ChainReport[] ChainReports { get; } = chainReports;
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
                diff.OnDemandBuild.Match(
                    onPending: jobName => buildDiffs.Add(new(BuildReferenceResult.Pending(jobName), BuildDiff.OnDemandPending)),
                    onQueued: jobName => buildDiffs.Add(new(BuildReferenceResult.Queued(jobName), BuildDiff.OnDemandTriggered(jobName))),
                    onDone: onDemandBuildRef =>
                    {
                        var onDemandTestBuild = onDemandBuilds.GetTestBuild(onDemandBuildRef);
                        diff.ReferenceBuild.Match(
                            onPending: jobName => buildDiffs.Add(new(BuildReferenceResult.Done(onDemandBuildRef, onDemandTestBuild.IsSuccessful), BuildDiff.ReferencePending)),
                            onDone: referenceBuildRef =>
                            {
                                var referenceTestBuild = branchReference.GetTestBuild(referenceBuildRef);
                                var failedTestsDiff = FailedTestDiffer.Diff(referenceTestBuild.JobName, referenceTestBuild.FailedTests, onDemandTestBuild.FailedTests, flakyTests);
                                buildDiffs.Add(new(BuildReferenceResult.Done(onDemandTestBuild), BuildDiff.Diff(failedTestsDiff)));
                            }
                        );
                    }
                );
            }
            chainReports.Add(new ChainReport(rootResult, [.. buildDiffs]));
        }
        return new RequestReport([.. chainReports]);
    }

    public RequestReport Build(RequestState request, IEnumerable<BranchReference> branchReferences, OnDemandBuilds onDemandBuilds, IFlakyTests flakyTests)
    {
        var branchReference = branchReferences.Single(r => r.BranchName == request.Request.GitReference.Branch);
        return Build(request, branchReference, onDemandBuilds, flakyTests);
    }
}

internal interface IReportSender
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
    public static string GetUrl(this IJobLinker linker, BuildReferenceResult buildReferenceResult)
    {
        return buildReferenceResult.Number > 0
            ? linker.GetUrl(buildReferenceResult.JobName, buildReferenceResult.Number)
            : linker.GetUrl(buildReferenceResult.JobName);
    }
}

internal sealed class ReportSender(IJobLinker jobLinker, IMailSender mailSender) : IReportSender
{
    private static XElement GetHead()
    {
        return new XElement("head",
            new XElement("meta",
                new XAttribute("charset", "UTF-8")),
            /*new XElement("meta",
                new XAttribute("name", "viewport"),
                new XAttribute("content", "width=device-width, initial-scale=1.0"))*/
            new XElement("style", @"
body {
  font-family: ""Segoe UI"", Roboto, sans-serif;
  background: #f9f9fb;
  color: #333;
  margin: 20px;
}

table {
  border-collapse: collapse;
  width: 100%;
  margin-bottom: 20px;
  background: #fff;
  box-shadow: 0 2px 4px rgba(0,0,0,0.05);
}

th, td {
  padding: 8px 12px;
  text-align: left;
  border-bottom: 1px solid #eee;
}

th {
  background: #f0f2f5;
  font-weight: 600;
}

tr:hover {
  background: #f9f9f9;
}

a {
  color: #0078d4;
  text-decoration: none;
}

a:hover {
  text-decoration: underline;
}

table.tests {
  border-collapse: collapse;
  width: 100%;
  font-family: ""Segoe UI"", Roboto, sans-serif;
  font-size: 13px;
}

table.tests th, table.tests td {
  padding: 6px 10px;
  text-align: left;
  border-bottom: 1px solid #eee;
}

/*
table.tests th {
  background: #f0f2f5;
  font-weight: 600;
}

table.tests tr:nth-child(even) {
  background-color: #f9f9fb;
}

table.tests tr:nth-child(odd) {
  background-color: #ffffff;
}
*/


table.tests th {
  background: #e8f0fe;
  font-weight: 600;
}

table.tests tr:nth-child(even) {
  background-color: #f0f8ff;
}

table.tests tr:nth-child(odd) {
  background-color: #ffffff;
}

.test-name {
  font-family: monospace;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 600px;
}
.test-info {
  font-size: 0.9em;
  color: #555;
}

.label {
  display: inline-block;
  padding: 2px 6px;
  margin: 2px 4px 2px 0;
  font-size: 0.85em;
  font-weight: 500;
  border-radius: 8px;
  color: #fff;
}

.label.new {
  background-color: #e74c3c;
}

.label.unstable {
  background-color: #f39c12;
}")
        );
    }

    private sealed record Counts(int Added, int Update)
    {
        public static readonly Counts Zero = new(0, 0);

        public Counts Add(Counts other) => new(Added + other.Added, Update + other.Update);
    }

    private static Counts CountFailedTests(RequestReport report)
    {
        return report.ChainReports.SelectMany(chainReport =>
            chainReport.BuildDiffs.Select(buildDiffResult =>
                buildDiffResult.Diff.Match(
                    onNotComparable: _ => Counts.Zero,
                    onComparable: diff => new Counts(diff.FailedTests.Count(t => t.Newness == Newness.New), diff.FailedTests.Count(t => t.Newness == Newness.Updated))
                ))).Aggregate(Counts.Zero, (acc, counts) => acc.Add(counts));
    }

    private static IEnumerable<object> GetElements(FailedTestDiff diff)
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
            statuses.Add("⚠️ same failed tests (not included in report)");
        }
        if (diff.Status == TestBuildDiffStatus.OK)
        {
            statuses.Add("✅ OK");
        }

        yield return new XElement("tr",
            new XAttribute("colspan", "2"),
            statuses);

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
                // TODO: title="full name"
                new XElement("td",
                    new XAttribute("class", "test-name"),
                    $"{result.Test.ClassName} {result.Test.TestName}"))) : new XElement("tr",
                new XElement("td"),
                new XElement("td"));
    }

    private IEnumerable<object> GetLink(BuildReferenceResult result, string emoji)
    {
        yield return new XElement("a",
            emoji,
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
            GetLink(buildDiffResult.Result, "⚙"));

        yield return buildDiffResult.Diff.Match(
            onNotComparable: message => (object)new XElement("tr",
                new XElement("td",
                    new XAttribute("colspan", "2"),
                    $"Diff: {message}")),
            onComparable: GetElements);

        //return new XElement("li",
        //    new XElement("h4",
        //        GetLink(buildDiffResult.Result, "⚙")),
        //    buildDiffResult.Diff.Match(
        //        onNotComparable: message => (object)new XElement("p", $"Diff: {message}"),
        //        onComparable: GetElements));
    }

    private XElement GetElement(ChainReport chainReport)
    {
        return new XElement("table",
            new XAttribute("class", "tests"),
            new XElement("tr",
                new XElement("th",
                    new XAttribute("colspan", "2"),
                    new XAttribute("class", "build-header")),
                GetLink(chainReport.RootResult, "🧪")),
            chainReport.BuildDiffs.Select(GetElement));
    }

    private static string GetLabel(RequestRootBuildReference buildReference)
    {
        return buildReference.Match(
            onQueued: (job, _) => $"{job} (⏳ queued)",
            onDone: buildRef => $"{buildRef} (✅ done)");
    }

    private XElement GetBody(RequestState request, RequestReport report)
    {
        var (newFailedTests, updatedFailedTests) = CountFailedTests(report);

        return new XElement("body",
            new XElement("h1", "Test On Demand Report"),
            new XElement("table",
                new XAttribute("class", "summary"),
                new XElement("tr",
                    new XElement("th", "📌 Request ID"),
                    new XElement("td", request.Request.Id)),
                new XElement("tr",
                    new XElement("th", "🗓 Created (UTC)"),
                    new XElement("td", request.Request.CreatedUtc.ToString())),
                new XElement("tr",
                    new XElement("th", "🔀 Tested Commit"),
                    new XElement("td", request.Request.Commit.Value)),
                new XElement("tr",
                    new XElement("th", "🌿 Reference branch"),
                    new XElement("td", $"{request.Request.GitReference.Branch} (commit {request.Request.GitReference.Commit})")),
                new XElement("tr",
                    new XElement("th", "🎯 Test Filters"),
                    new XElement("td", string.Join(" ", request.Request.GetFilters()))),
                newFailedTests + updatedFailedTests > 0 ? new XElement("tr",
                    new XElement("th", $"🧪 Failed test{(newFailedTests + updatedFailedTests > 1 ? "s" : "")}"),
                    new XElement("td", $"{(newFailedTests > 0 ? $"🔴 {newFailedTests} New" : "")}{(updatedFailedTests > 0 ? $"🟠 {updatedFailedTests} Updated" : "")}")) : null),
            new XElement("table",
                new XAttribute("class", "chains"),
                new XElement("tr",
                    new XElement("th", "Chain"),
                    new XElement("th", "Root Build"),
                    new XElement("th", "Test Builds")),
                request.ChainDiffs.Select(chainDiff => new XElement("tr",
                    new XElement("td", GetLabel(chainDiff.OnDemandRoot)),
                    new XElement("td", $"{(chainDiff.Status == ChainStatus.RootTriggered ? "⏳" : "✅")} {chainDiff.Status}"),
                    new XElement("td", $"{(chainDiff.Status == ChainStatus.RootTriggered ? "" : chainDiff.Status == ChainStatus.TestsTriggered ? "⏳" : "🏁")} {chainDiff.Status}")))),
            report.ChainReports.Select(GetElement));
    }

    public Task Send(RequestState request, RequestReport report)
    {
        Log.Information("Sending report for request {RequestId} to {UserEmail}", request.Request.Id, request.Request.UserEmail);

        var doc = new XDocument(new XElement("html",
            GetHead(),
            GetBody(request, report)));
        return mailSender.Send(request.Request.UserEmail, "On-Demand Report", doc.ToString());
    }

    public Task Send(RequestState requestState, Workspace workspace)
    {
        var report = RequestReportBuilder.Instance.Build(requestState, workspace);
        return Send(requestState, report);
    }
}
