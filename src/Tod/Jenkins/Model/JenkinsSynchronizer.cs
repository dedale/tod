using Serilog;

namespace Tod.Jenkins;

internal sealed class JenkinsSynchronizer(IJenkinsClient jenkinsClient, IPostBuildHandler postBuildHandler)
{
    private async Task UpdateRootBuilds(BuildCollections<RootBuild> allRootBuilds, bool onDemand)
    {
        foreach (var rootBuilds in allRootBuilds)
        {
            Log.Debug("Fetching root builds for {JobName}", rootBuilds.JobName);
            var builds = await jenkinsClient.GetLastBuilds(rootBuilds.JobName).ConfigureAwait(false);
            foreach (var build in builds.Reverse())
            {
                if (rootBuilds.Contains(build.Number))
                {
                    continue;
                }
                var scheduled = await jenkinsClient.GetScheduledJobs(new(rootBuilds.JobName, build.Number)).ConfigureAwait(false);
                var rootBuild = new RootBuild(
                    rootBuilds.JobName,
                    build.Id,
                    build.Number,
                    build.TimestampUtc,
                    build.TimestampUtc.AddMilliseconds(build.DurationInMs),
                    build.Result == BuildResult.Success,
                    build.GetCommits(),
                    scheduled
                );
                // Reference root builds are always done when creating requests
                if (onDemand)
                {
                    postBuildHandler.PostOnDemandRootBuild(rootBuild.Reference, rootBuild.IsSuccessful);
                }
                Log.Information("Adding root build {RootBuild} ({IsSuccessful})", rootBuild, rootBuild.IsSuccessful ? "Success" : "Failure");
                rootBuilds.TryAdd(rootBuild);
            }
        }
    }

    private async Task Update(BranchReference branchReference)
    {
        Log.Information("Updating builds for reference branch {BranchName}", branchReference.BranchName);

        await UpdateRootBuilds(branchReference.RootBuilds, false).ConfigureAwait(false);

        foreach (var testBuilds in branchReference.TestBuilds)
        {
            var builds = await jenkinsClient.GetLastBuilds(testBuilds.JobName).ConfigureAwait(false);
            foreach (var build in builds.Reverse())
            {
                if (testBuilds.Contains(build.Number))
                {
                    continue;
                }
                var testData = await jenkinsClient.GetTestData(new(testBuilds.JobName, build.Number)).ConfigureAwait(false);
                FailedTest[] failedTests;
                if (testData.FailCount > 0)
                {
                    failedTests = await jenkinsClient.GetFailedTests(new(testBuilds.JobName, build.Number)).ConfigureAwait(false);
                }
                else
                {
                    failedTests = [];
                }
                var testBuild = new TestBuild(
                    testBuilds.JobName,
                    build.Id,
                    build.Number,
                    build.TimestampUtc,
                    build.TimestampUtc.AddMilliseconds(build.DurationInMs),
                    build.Result == BuildResult.Success,
                    testData.UpstreamBuilds,
                    failedTests
                );
                foreach (var rootBuild in testBuild.RootBuilds)
                {
                    postBuildHandler.PostReferenceTestBuild(rootBuild, testBuild.Reference);
                }
                Log.Information("Adding test build {JobName} #{BuildNumber} ({IsSuccessful})",
                    testBuild.JobName, testBuild.BuildNumber, testBuild.IsSuccessful ? "Success" : $"{testData.FailCount} failed tests");
                testBuilds.TryAdd(testBuild);
            }
        }
    }

    private async Task Update(OnDemandBuilds onDemandBuilds)
    {
        Log.Information("Updating builds for on-demand");

        await UpdateRootBuilds(onDemandBuilds.RootBuilds, true).ConfigureAwait(false);

        foreach (var testBuilds in onDemandBuilds.TestBuilds)
        {
            var builds = await jenkinsClient.GetLastBuilds(testBuilds.JobName).ConfigureAwait(false);
            foreach (var build in builds.Reverse())
            {
                if (testBuilds.Contains(build.Number))
                {
                    continue;
                }
                var rootBuild = await jenkinsClient.TryGetRootBuild(new(testBuilds.JobName, build.Number)).ConfigureAwait(false);
                if (rootBuild is null)
                {
                    continue;
                }
                var failCount = await jenkinsClient.GetFailCount(new(testBuilds.JobName, build.Number)).ConfigureAwait(false);
                FailedTest[] failedTests;
                if (failCount > 0)
                {
                    failedTests = await jenkinsClient.GetFailedTests(new(testBuilds.JobName, build.Number)).ConfigureAwait(false);
                }
                else
                {
                    failedTests = [];
                }
                var testBuild = new TestBuild(
                    testBuilds.JobName,
                    build.Id,
                    build.Number,
                    build.TimestampUtc,
                    build.TimestampUtc.AddMilliseconds(build.DurationInMs),
                    build.Result == BuildResult.Success,
                    [rootBuild],
                    failedTests
                );
                postBuildHandler.PostOnDemandTestBuild(rootBuild, testBuild.Reference);
                var info = testBuild.IsSuccessful ? "Success" : $"{failCount} failed test{(failCount == 1 ? "" : "s")}";
                Log.Information("Adding test build {JobName} #{BuildNumber} ({Info})",
                    testBuild.JobName,
                    testBuild.BuildNumber,
                    info);
                testBuilds.TryAdd(testBuild);
            }
        }
    }

    public async Task Update(Workspace workspace)
    {
        Log.Information("Workspace synchronization started");
        foreach (var branchReference in workspace.BranchReferences)
        {
            await Update(branchReference).ConfigureAwait(false);
        }
        await Update(workspace.OnDemandBuilds).ConfigureAwait(false);
        Log.Information("Workspace synchronization done");
    }
}
