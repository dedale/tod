using Serilog;
using Tod.Git;

namespace Tod.Jenkins;

internal sealed class JenkinsSynchronizer(IJenkinsClient jenkinsClient, IPostBuildHandler postBuildHandler)
{
    private async Task UpdateReferenceRootBuilds(BuildCollections<RootBuild> allRootBuilds)
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

                Log.Information("Adding root build {RootBuild} ({IsSuccessful})", rootBuild, rootBuild.IsSuccessful ? "Success" : "Failure");
                rootBuilds.TryAdd(rootBuild);
            }
        }
    }

    private async Task Update(BranchReference branchReference)
    {
        Log.Information("Updating builds for reference branch {BranchName}", branchReference.BranchName);

        await UpdateReferenceRootBuilds(branchReference.RootBuilds).ConfigureAwait(false);

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

                Log.Information("Adding test build {JobName} #{BuildNumber} ({IsSuccessful})",
                    testBuild.JobName, testBuild.BuildNumber, testBuild.IsSuccessful ? "Success" : $"{testData.FailCount} failed tests");
                testBuilds.TryAdd(testBuild);

                foreach (var rootBuild in testBuild.RootBuilds)
                {
                    await postBuildHandler.PostReferenceTestBuild(rootBuild, testBuild.Reference).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task UpdateOnDemandRootBuilds(BuildCollections<RootBuild> allRootBuilds)
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
                    Log.Warning("On-demand root build {RootBuild} is missing REFSPEC parameter (cannot trigger test builds)", new BuildReference(rootBuilds.JobName, build.Number));
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

                Log.Information("Adding root build {RootBuild} ({IsSuccessful})", rootBuild, rootBuild.IsSuccessful ? "Success" : "Failure");
                rootBuilds.TryAdd(rootBuild);

                if (commits.Length == 1)
                {
                    await postBuildHandler.PostOnDemandRootBuild(rootBuild.Reference, commits[0], rootBuild.IsSuccessful).ConfigureAwait(false);
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

                var info = testBuild.IsSuccessful ? "Success" : $"{failCount} failed test{(failCount == 1 ? "" : "s")}";
                Log.Information("Adding test build {JobName} #{BuildNumber} ({Info})",
                    testBuild.JobName,
                    testBuild.BuildNumber,
                    info);
                testBuilds.TryAdd(testBuild);

                await postBuildHandler.PostOnDemandTestBuild(rootBuild, testBuild.Reference).ConfigureAwait(false);
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
