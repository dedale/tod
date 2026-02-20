namespace Tod.Jenkins;

internal sealed record BaselineChainReport(
    BranchName BranchName,
    string ChainName,
    RootBuild[] RootBuilds,
    Dictionary<JobName, FailedTestDiff> TestDiffs
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

        var testDiffs = new Dictionary<JobName, FailedTestDiff>();
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

            var currentTest = baselineBranch.GetTestBuild(currentTestBuildRef);

            var baseline = GetBaselineTestBuild(currentRoot.Reference, testJob, baselineBranch);

            if (baseline == null)
            {
                testDiffs[testJob] = FailedTestDiffer.Diff(
                    testJob,
                    [],
                    currentTest.FailedTests,
                    flakyTests
                );
            }
            else
            {
                testDiffs[testJob] = FailedTestDiffer.Diff(
                    testJob,
                    baseline.FailedTests,
                    currentTest.FailedTests,
                    flakyTests
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
