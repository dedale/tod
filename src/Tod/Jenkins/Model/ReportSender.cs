using Serilog;

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

    public static BuildReferenceResult Triggered(BuildReference build) => new(build.JobName, build.BuildNumber, BuildStatus.Triggered);

    public static BuildReferenceResult Done(BuildReference build, bool isSuccessful) => new(build.JobName, build.BuildNumber, isSuccessful ? BuildStatus.Success : BuildStatus.Failed);

    public static BuildReferenceResult Done(BaseBuild build) => Done(build.Reference, build.IsSuccessful);
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
    public static BuildDiff OnDemandTriggered(int buildNumber) => new NotComparable($"Build #{buildNumber} not done");
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
                onPending: BuildReferenceResult.Pending, // BUG
                onTriggered: BuildReferenceResult.Triggered,
                onDone: buildRef => BuildReferenceResult.Done(onDemandBuilds.RootBuilds.GetOrAdd(buildRef.JobName)[buildRef]));

            var buildDiffs = new List<BuildDiffResult>();
            foreach (var diff in chainDiff.TestBuildDiffs)
            {
                diff.OnDemandBuild.Match(
                    onPending: jobName => buildDiffs.Add(new(BuildReferenceResult.Pending(jobName), BuildDiff.OnDemandPending)),
                    onTriggered: buildRef => buildDiffs.Add(new(BuildReferenceResult.Triggered(buildRef), BuildDiff.OnDemandTriggered(buildRef.BuildNumber))),
                    onDone: onDemandBuildRef =>
                    {
                        var onDemandTestBuild = onDemandBuilds.GetTestBuild(onDemandBuildRef);
                        diff.ReferenceBuild.Match(
                            onPending: jobName => buildDiffs.Add(new(BuildReferenceResult.Done(onDemandBuildRef, onDemandTestBuild.IsSuccessful), BuildDiff.ReferencePending)),
                            onTriggered: buildRef => throw new InvalidOperationException("Ref builds should be pending or done"),
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
    void Send(RequestState request, Workspace workspace);
}

internal sealed class ReportSender(IRequestReportBuilder builder) : IReportSender
{
    private static void Send(RequestState request, RequestReport report)
    {
        Log.Information("📧 Sending report for request {RequestId}", request.Request.Id);
        foreach (var chainDiff in request.ChainDiffs)
        {
            Log.Information("   Chain Status: {Status}", chainDiff.Status);
            Log.Information("   Reference Root: {ReferenceRoot}", chainDiff.ReferenceRoot);
            Log.Information("   On-Demand Root: {OnDemandRoot}", chainDiff.OnDemandRoot);
        }

        foreach (var chainReport in report.ChainReports)
        {
            if (chainReport.RootResult.Number > 0)
            {
                Log.Information($"{chainReport.RootResult.JobName} #{chainReport.RootResult.Number} {chainReport.RootResult.Status}");
            }
            else
            {
                Log.Information($"{chainReport.RootResult.JobName} {chainReport.RootResult.Status}");
            }
            foreach (var buildDiffResult in chainReport.BuildDiffs)
            {
                if (buildDiffResult.Result.Number > 0)
                {
                    Log.Information($"{buildDiffResult.Result.JobName} #{buildDiffResult.Result.Number} {buildDiffResult.Result.Status}");
                }
                else
                {
                    Log.Information($"{buildDiffResult.Result.JobName} {buildDiffResult.Result.Status}");
                }
                buildDiffResult.Diff.Match(
                    onNotComparable: message => Log.Information("      Diff: {Message}", message),
                    onComparable: diff =>
                    {
                        var statuses = new List<string>();
                        if (diff.Status.HasFlag(TestBuildDiffStatus.NewFailures))
                        {
                            statuses.Add("New Failures");
                        }
                        if (diff.Status.HasFlag(TestBuildDiffStatus.UpdatedFailures))
                        {
                            statuses.Add("Updated Failures");
                        }
                        if (diff.Status.HasFlag(TestBuildDiffStatus.SameFailures))
                        {
                            statuses.Add("Same Failures");
                        }
                        Log.Information("      Diff Status: {Statuses}", string.Join(", ", statuses));
                        diff.Added.ToList().ForEach(test => Log.Information("      Added: {TestName}", test.TestName));
                        diff.Updated.ToList().ForEach(test => Log.Information("      Updated: {TestName}", test.TestName));
                    });
            }
        }   
    }

    public void Send(RequestState request, Workspace workspace)
    {
        var branchReference = workspace.BranchReferences.Single(r => r.BranchName == request.Request.ReferenceBranchName);
        var report = builder.Build(request, branchReference, workspace.OnDemandBuilds);
        Send(request, report);
    }
}
