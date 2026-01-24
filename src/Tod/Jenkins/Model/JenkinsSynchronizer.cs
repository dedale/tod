using Serilog;
using Tod.Git;

namespace Tod.Jenkins;

internal sealed class JenkinsSynchronizer(IJenkinsClient jenkinsClient, IPostBuildHandler postBuildHandler)
{
    private async Task UpdateReferenceRootBuilds(BuildCollections<RootBuild> allRootBuilds)
    {
        foreach (var rootBuilds in allRootBuilds)
        {
            Log.Debug("Fetching root builds for {@JobName}", rootBuilds.JobName);
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

                Log.Information("Adding root build {@RootBuild} ({@IsSuccessful})", rootBuild.Reference, rootBuild.IsSuccessful ? BuildResultInfo.Success("Success") : BuildResultInfo.Failure("Failure"));
                rootBuilds.TryAdd(rootBuild);
            }
        }
    }

    private async Task<bool> Update(BranchReference branchReference)
    {
        Log.Information("Updating builds for reference branch {BranchName}", branchReference.BranchName);

        await UpdateReferenceRootBuilds(branchReference.RootBuilds).ConfigureAwait(false);

        var newTestBuilds = false;
        foreach (var testBuilds in branchReference.TestBuilds)
        {
            Log.Debug("Fetching test builds for {@JobName}", testBuilds.JobName);
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

                if (testBuild.IsSuccessful)
                {
                    Log.Information("Adding test build {@TestBuild} ({@IsSuccessful})", testBuild.Reference, BuildResultInfo.Success("Success"));
                }
                else
                {
                    Log.Information("Adding test build {@TestBuild} ({Count} {@IsSuccessful})",
                        testBuild.Reference, testData.FailCount, BuildResultInfo.Failure($"failed test{(testData.FailCount > 1 ? "s" : "")}"));
                }
                testBuilds.TryAdd(testBuild);
                newTestBuilds = true;

                foreach (var rootBuild in testBuild.RootBuilds)
                {
                    await postBuildHandler.PostReferenceTestBuild(rootBuild, testBuild.Reference).ConfigureAwait(false);
                }
            }
        }
        return newTestBuilds;
    }

    private async Task UpdateOnDemandRootBuilds(BuildCollections<RootBuild> allRootBuilds)
    {
        foreach (var rootBuilds in allRootBuilds)
        {
            Log.Debug("Fetching root builds for {@JobName}", rootBuilds.JobName);
            var builds = await jenkinsClient.GetLastBuilds(rootBuilds.JobName).ConfigureAwait(false);
            var minBuildNumber = builds.Length > 0 && rootBuilds.Count > 0 ? rootBuilds.Min(r => r.BuildNumber) : int.MinValue;
            foreach (var build in builds.Reverse())
            {
                // After a purge, do not try to add old builds
                if (rootBuilds.Contains(build.Number) || build.Number < minBuildNumber)
                {
                    continue;
                }
                // Test jobs are scheduled only for reference root builds
                var scheduled = true ? [] : await jenkinsClient.GetScheduledJobs(new(rootBuilds.JobName, build.Number)).ConfigureAwait(false);

                Sha1[] commits;
                var parameters = await jenkinsClient.GetBuildParameters(new(rootBuilds.JobName, build.Number)).ConfigureAwait(false);
                if (parameters.TryGetValue("REFSPEC", out var value))
                {
                    commits = [new Sha1(value)];
                }
                else
                {
                    commits = [];
                    Log.Warning("On-demand root build {@RootBuild} is missing REFSPEC parameter (cannot trigger test builds)", new BuildReference(rootBuilds.JobName, build.Number));
                }

                var rootBuild = new RootBuild(
                    rootBuilds.JobName,
                    build.Id,
                    build.Number,
                    build.TimestampUtc,
                    build.TimestampUtc.AddMilliseconds(build.DurationInMs),
                    build.Result == BuildResult.Success,
                    commits,
                    scheduled
                );

                // After a purge, do not try to add old builds
                if (rootBuilds.Count == 0 || rootBuild.BuildNumber > minBuildNumber)
                {
                    Log.Information("Adding root build {@RootBuild} ({@IsSuccessful})",
                        rootBuild.Reference, rootBuild.IsSuccessful ? BuildResultInfo.Success("Success") : BuildResultInfo.Failure("Failure"));
                    rootBuilds.TryAdd(rootBuild, false);

                    if (commits.Length == 1)
                    {
                        await postBuildHandler.PostOnDemandRootBuild(rootBuild.Reference, commits[0], rootBuild.IsSuccessful).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    private async Task Update(OnDemandBuilds onDemandBuilds)
    {
        Log.Information("Updating builds for on-demand");

        await UpdateOnDemandRootBuilds(onDemandBuilds.RootBuilds).ConfigureAwait(false);

        foreach (var testBuilds in onDemandBuilds.TestBuilds)
        {
            Log.Debug("Fetching test builds for {@JobName}", testBuilds.JobName);
            var builds = await jenkinsClient.GetLastBuilds(testBuilds.JobName).ConfigureAwait(false);
            var minBuildNumber = testBuilds.Count > 0 ? testBuilds.Min(r => r.BuildNumber) : int.MinValue;
            foreach (var build in builds.Reverse())
            {
                // After a purge, do not try to add old builds
                if (testBuilds.Contains(build.Number) || build.Number < minBuildNumber)
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

                // After a purge, do not try to add old builds
                if (testBuilds.Count == 0 || testBuild.BuildNumber > minBuildNumber)
                {
                    if (testBuild.IsSuccessful)
                    {
                        Log.Information("Adding test build {@TestBuild} ({@IsSuccessful})", testBuild.Reference, BuildResultInfo.Success("Success"));
                    }
                    else
                    {
                        Log.Information("Adding test build {@TestBuild} ({Count} {@IsSuccessful})",
                            testBuild.Reference, failCount, BuildResultInfo.Failure($"failed test{(failCount > 1 ? "s" : "")}"));
                    }
                    testBuilds.TryAdd(testBuild, false);

                    await postBuildHandler.PostOnDemandTestBuild(rootBuild, testBuild.Reference).ConfigureAwait(false);
                }
            }
        }
    }

    public async Task Update(Workspace workspace)
    {
        Log.Information("Workspace synchronization started");
        var updateFlakies = false;
        foreach (var branchReference in workspace.BranchReferences)
        {
            updateFlakies |= await Update(branchReference).ConfigureAwait(false);
        }
        await Update(workspace.OnDemandBuilds).ConfigureAwait(false);
        Log.Information("Workspace synchronization done");

        if (updateFlakies)
        {
            Log.Information("Flaky tests analysis started");
            workspace.FlakyTests.Update(workspace.BranchReferences);
            Log.Information("Flaky tests analysis done");
        }
    }
}
