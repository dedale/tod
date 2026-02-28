namespace Tod.Jenkins;

internal sealed record BaselineChainReport(
    BranchName BranchName,
    string ChainName,
    RootBuild[] RootBuilds,
    Dictionary<JobName, BuildDiffResult> TestDiffs
);

internal sealed class BaselineReportBuilder
{
    public static BaselineChainReport? Build(
        BaselineChain[] baselineChains,
        string chainName,
        BaselineBranch baselineBranch,
        IFlakyTests flakyTests)
    {
        if (baselineChains.Length == 0)
        {
            return null;
        }

        var testDiffs = new Dictionary<JobName, BuildDiffResult>();
        var currentRootRef = baselineChains[^1].RootBuild;
        var currentRoot = baselineBranch.GetRootBuild(currentRootRef);

        foreach (var (testJob, testBuildRef) in baselineChains[^1].TestBuilds)
        {
            var currentTestBuildRef = testBuildRef.Match(
                onPending: _ => (BuildReference?)null,
                onDone: br => br
            );

            if (currentTestBuildRef == null)
            {
                continue;
            }

            var currentTestBuild = baselineBranch.GetTestBuild(currentTestBuildRef);
            var baselineTestBuild = GetBaselineTestBuild(currentRoot.Reference, testJob, baselineBranch);

            var currentResult = BuildReferenceResult.Done(currentTestBuild);

            if (baselineTestBuild == null)
            {
                var baselineResult = new BuildReferenceResult(testJob, 0, BuildStatus.Pending);
                testDiffs[testJob] = new BuildDiffResult(
                    baselineResult,
                    currentResult,
                    BuildDiff.Diff(FailedTestDiffer.Diff(testJob, [], currentTestBuild.FailedTests, flakyTests))
                );
            }
            else
            {
                var baselineResult = BuildReferenceResult.Done(baselineTestBuild);
                testDiffs[testJob] = new BuildDiffResult(
                    baselineResult,
                    currentResult,
                    BuildDiff.Diff(FailedTestDiffer.Diff(testJob, baselineTestBuild.FailedTests, currentTestBuild.FailedTests, flakyTests))
                );
            }
        }

        return new BaselineChainReport(
            baselineBranch.BranchName,
            chainName,
            [.. baselineChains.Select(rc => baselineBranch.GetRootBuild(rc.RootBuild))],
            testDiffs
        );
    }

    private static TestBuild? GetBaselineTestBuild(
        BuildReference rootBuild,
        JobName testJob,
        BaselineBranch baselineBranch)
    {
        var allTestBuilds = baselineBranch.TestBuilds.FirstOrDefault(x => x.JobName == testJob);
        if (allTestBuilds != null)
        {
            for (var i = allTestBuilds.Count - 1; i >= 0; i--)
            {
                var testBuild = allTestBuilds[i];
                if (testBuild.RootBuilds.Any(rb => rb.JobName == rootBuild.JobName && rb.BuildNumber < rootBuild.BuildNumber))
                {
                    return testBuild;
                }
            }
        }
        return null;
    }
}
