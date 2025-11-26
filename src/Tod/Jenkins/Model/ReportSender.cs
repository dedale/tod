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

    public string Id => Number > 0 ? $"{JobName}#{Number}" : JobName.Value;
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
    RequestReport Build(RequestState requestState, BranchReference branchReference, OnDemandBuilds onDemandBuilds);
}

internal sealed class RequestReportBuilder : IRequestReportBuilder
{
    public RequestReport Build(RequestState requestState, BranchReference branchReference, OnDemandBuilds onDemandBuilds)
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
                                var failedTestsDiff = FailedTestDiffer.Diff(referenceTestBuild.FailedTests, onDemandTestBuild.FailedTests);
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
}

internal interface IReportSender
{
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

internal sealed class ReportSender(IRequestReportBuilder builder, IJobLinker jobLinker, IMailSender mailSender) : IReportSender
{
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
                    onComparable: diff => new Counts(diff.Added.Length, diff.Updated.Length)
                ))).Aggregate(Counts.Zero, (acc, counts) => acc.Add(counts));
    }

    private static XElement GetElement(FailedTestDiff diff)
    {
        var statuses = new List<string>();
        if (diff.Status.HasFlag(TestBuildDiffStatus.NewFailures))
        {
            statuses.Add($"{diff.Added.Length} New Failure{(diff.Added.Length > 1 ? "s" : "")} ❌");
        }
        if (diff.Status.HasFlag(TestBuildDiffStatus.UpdatedFailures))
        {
            statuses.Add($"{diff.Updated.Length} Updated Failure{(diff.Updated.Length > 1 ? "s" : "")} ❌");
        }
        if (diff.Status.HasFlag(TestBuildDiffStatus.SameFailures))
        {
            statuses.Add("Same Failures ⚠️");
        }
        if (diff.Status == TestBuildDiffStatus.OK)
        {
            statuses.Add("OK ✅");
        }
        return new XElement("div",
            new XElement("p", $"Diff Status: {string.Join(", ", statuses)}"),
            diff.Added.Length > 0 || diff.Updated.Length > 0 ? new XElement("ul",
                from test in diff.Added
                select new XElement("li", $"Added: {test.ClassName} {test.TestName}"),
                from test in diff.Updated
                select new XElement("li", $"Updated: {test.ClassName} {test.TestName}")
            ) : null
        );
    }

    private XElement GetElement(BuildDiffResult buildDiffResult)
    {
        return new XElement("li",
            new XElement("h4",
                new XElement("a",
                    new XAttribute("href", jobLinker.GetUrl(buildDiffResult.Result)),
                    buildDiffResult.Result.Id),
                $": {buildDiffResult.Result.Status}"),
            buildDiffResult.Diff.Match(
                onNotComparable: message => new XElement("p", $"Diff: {message}"),
                onComparable: GetElement));
    }

    private XElement GetElement(ChainReport chainReport)
    {
        return new XElement("li",
            new XElement("h3", $"{chainReport.RootResult.Id}: {chainReport.RootResult.Status}"),
            new XElement("ul", chainReport.BuildDiffs.Select(GetElement)));
    }

    private static string GetLabel(RequestRootBuildReference buildReference)
    {
        return buildReference.Match(
            onQueued: (job, _) => $"{job} (queued)",
            onDone: buildRef => $"{buildRef} (done)");
    }

    private XElement GetBody(RequestState request, RequestReport report)
    {
        var (newFailedTests, updatedFailedTests) = CountFailedTests(report);

        return new XElement("body",
            new XElement("h1", "Test On Demand Report"),
            new XElement("ul",
                new XElement("li", $"Request ID: {request.Request.Id}"),
                new XElement("li", $"Created (UTC): {request.Request.CreatedUtc}"),
                new XElement("li", $"Commit: {request.Request.Commit}"),
                new XElement("li", $"Ref Commit: {request.Request.GitReference.Commit} (on {request.Request.GitReference.Branch})"),
                new XElement("li", $"Test Filters: {string.Join(" ", request.Request.GetFilters())}")
            ),
            new XElement("h2", "Summary"),
            new XElement("ul",
                new XElement("li", $"{newFailedTests} New Failed Test{(newFailedTests > 1 ? "s" : "")}"),
                new XElement("li", $"{updatedFailedTests} Updated Failed Test{(updatedFailedTests > 1 ? "s" : "")}")),
            new XElement("h2", "Chain Reports"),
            new XElement("ul", request.ChainDiffs.Select(chainDiff => new XElement("li",
                new XElement("p", $"{GetLabel(chainDiff.OnDemandRoot)} chain status: {chainDiff.Status}")))),
            new XElement("ul", report.ChainReports.Select(GetElement)));
    }

    private Task Send(RequestState request, RequestReport report)
    {
        Log.Information("Sending report for request {RequestId} to {UserEmail}", request.Request.Id, request.Request.UserEmail);

        var doc = new XDocument(new XElement("html", GetBody(request, report)));
        return mailSender.Send(request.Request.UserEmail, "On-Demand Report", doc.ToString());
    }

    public Task Send(RequestState request, Workspace workspace)
    {
        var branchReference = workspace.BranchReferences.Single(r => r.BranchName == request.Request.GitReference.Branch);
        var report = builder.Build(request, branchReference, workspace.OnDemandBuilds);
        return Send(request, report);
    }
}
