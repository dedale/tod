namespace Tod.Jenkins;

internal sealed record ReferenceChainReport(
    BranchName BranchName,
    string ChainName,
    RootBuild[] RootBuilds,
    Dictionary<JobName, FailedTestDiff> TestDiffs
);

internal sealed class ReferenceReportBuilder
{
    public static ReferenceChainReport? Build(
        ReferenceChain[] referenceChains,
        string chainName,
        BranchReference branchReference,
        IFlakyTests flakyTests)
    {
        if (referenceChains.Length == 0)
        {
            return null;
        }

        var testDiffs = new Dictionary<JobName, FailedTestDiff>();
        var currentRootRef = referenceChains[^1].RootBuild;
        var currentRoot = branchReference.GetRootBuild(currentRootRef);

        foreach (var (testJob, testBuildRef) in referenceChains[^1].TestBuilds)
        {
            var currentTestBuildRef = testBuildRef.Match(
                onPending: _ => (BuildReference?)null,
                onDone: br => br
            );

            if (currentTestBuildRef == null)
            {
                continue;
            }

            var currentTest = branchReference.GetTestBuild(currentTestBuildRef);

            var baseline = GetBaselineTestBuild(currentRoot.Reference, testJob, branchReference);

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

        return new ReferenceChainReport(
            branchReference.BranchName,
            chainName,
            [.. referenceChains.Select(rc => branchReference.GetRootBuild(rc.RootBuild))],
            testDiffs
        );
    }

    private static TestBuild? GetBaselineTestBuild(
        BuildReference rootBuild,
        JobName testJob,
        BranchReference branchReference)
    {
        var allTestBuilds = branchReference.TestBuilds.FirstOrDefault(x => x.JobName == testJob);
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
