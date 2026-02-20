using Moq;
using NUnit.Framework;
using System.Diagnostics;
using Tod.Git;
using Tod.Jenkins;
using Tod.Tests.IO;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class WorkspaceTests
{
    private static readonly string s_userEmail = $"user@example.org";
    private static readonly string s_user = "user";

    [Test]
    public async Task SerializationRoundTrip_Works()
    {
        using (Assert.EnterMultipleScope())
        using (var temp = new TempDirectory())
        {
            var rootBuild = new RootBuild(
                new JobName("MAIN-build"),
                "build-id-1",
                1,
                DateTime.UtcNow.AddHours(-2),
                DateTime.UtcNow.AddHours(-1),
                true,
                [RandomData.NextSha1()],
                [
                    new JobName("MAIN-test"),
                    new JobName("MAIN-test2"),
                ]
            );
            var testBuild = new TestBuild(
                new JobName("MAIN-test"),
                "test-build-id-1",
                1,
                DateTime.UtcNow.AddHours(-1),
                DateTime.UtcNow.AddMinutes(-30),
                true,
                new BuildReference(new JobName("MAIN-build"), 1),
                []
            );

            var workspaceStore = new WorkspaceStore(temp.Path);
            var baselineStore = workspaceStore.GetBaselineStore(new BranchName("main"));

            var baselineBranch = new BaselineBranch(baselineStore);
            baselineBranch.TryAddRoot(new JobName("MAIN-build"));
            Assert.That(baselineBranch.TryAdd(rootBuild), Is.True);
            Assert.That(baselineBranch.TryAdd(testBuild), Is.True);

            var onDemandStore = workspaceStore.OnDemandStore;
            var onDemandBuilds = new OnDemandBuilds(onDemandStore);
            onDemandBuilds.TryAddRoot(new JobName("CUSTOM-build"));

            var flakyStore = workspaceStore.FlakyStore;
            var flakyTests = new FlakyTests(flakyStore);

            var workspace = new Workspace(
                [baselineBranch],
                onDemandBuilds,
                new OnDemandRequests(Path.Combine(temp.Path, "requests")),
                flakyTests
            );
            var request = Request.Create(RandomData.NextSha1(), new(new("main"), RandomData.NextSha1()), ["integration"], s_user, s_userEmail);

            var chain = new RequestChain(
                new BuildReference(new JobName("MAIN-build"), 1),
                RequestRootBuildReference.Queue(new JobName("CUSTOM-build"), request.Commit),
                [
                    new RequestBuildDiff(
                        new JobName("MAIN-test"),
                        new JobName("CUSTOM-test")
                    ),
                ]
            );
            Func<OnDemandJobKind, JobName, TriggerParameters, Task> triggerBuild = (_, _, _) => Task.CompletedTask;
            var requestState = await RequestState.New(request, [chain], onDemandBuilds, triggerBuild).ConfigureAwait(false);

            workspace.OnDemandRequests.Add(requestState);
            var onDemandRootBuild = new RootBuild(
                new JobName("CUSTOM-build"),
                "custom-build-id-1",
                1,
                DateTime.UtcNow.AddHours(-2),
                DateTime.UtcNow.AddHours(-1),
                true,
                [RandomData.NextSha1()],
                []
            );
            workspace.OnDemandBuilds.TryAdd(onDemandRootBuild);
            workspaceStore.FlakyStore.Save(workspace.FlakyTests);
            var clone = Workspace.Load(temp.Path, new WorkspaceStore(temp.Path));
            var baselineBranches = clone.BaselineBranches.ToList();
            Assert.That(baselineBranches, Has.Count.EqualTo(1));
            Assert.That(baselineBranches[0].RootBuilds, Has.Count.EqualTo(1));
            Assert.That(baselineBranches[0].TestBuilds, Has.Count.EqualTo(2));
            Assert.That(baselineBranches[0].TestBuilds[0], Has.Count.EqualTo(1)); // MAIN-test
            Assert.That(baselineBranches[0].TestBuilds[1], Has.Count.EqualTo(0)); // MAIN-test2
            Assert.That(clone.OnDemandBuilds.RootBuilds, Has.Count.EqualTo(1));
            Assert.That(clone.OnDemandRequests.ActiveRequests.Single().Value.Request.Id, Is.EqualTo(request.Id));
            // TODO Assert flaky tests loaded
        }
    }

    private static async Task<JobGroups> GetJobGroups(params JobName[] extraJobs)
    {
        var refJobConfigs = new[]
        {
            new BaselineJobConfig("MAIN-(?<root>.*build)", new("main"), true),
            new BaselineJobConfig("MAIN-(?<test>.*)", new("main"), false),

            new BaselineJobConfig("PROD-(?<root>.*build)", new("PROD"), true),
            new BaselineJobConfig("PROD-(?<test>.*)", new("PROD"), false),
        };
        var onDemandJobConfigs = new[]
        {
            new OnDemandJobConfig("CUSTOM-(?<root>.*build)", true),
            new OnDemandJobConfig("CUSTOM-(?<test>.*)", false),
        };
        var testFilters = new[]
        {
            new TestFilter("tests", "^tests$", "tests"),
        };
        var config = JenkinsConfig.New("http://localhost:8080", baselineJobs: refJobConfigs, onDemandJobs: onDemandJobConfigs, testFilters: testFilters);
        var jenkinsClient = new Mock<IJenkinsClient>(MockBehavior.Strict);
        var allJobs = new List<JobName> {
            new("MAIN-build"),
            new("MAIN-tests"),
            new("CUSTOM-build"),
            new("CUSTOM-tests"),
        };
        allJobs.AddRange(extraJobs);
        jenkinsClient.Setup(x => x.GetJobNames(config.MultiBranchFolders)).ReturnsAsync([.. allJobs]);
        var jobManager = new JobManager(config, jenkinsClient.Object);
        var jobGroups = await jobManager.TryLoad().ConfigureAwait(false);
        Debug.Assert(jobGroups is not null);
        return jobGroups;
    }

    [Test]
    public async Task New_Works()
    {
        var jobGroups = await GetJobGroups().ConfigureAwait(false);
        using var temp = new TempDirectory();
        var workspace = Workspace.New(temp.Path, jobGroups);
        var baselineBranches = workspace.BaselineBranches.ToList();
        Assert.That(baselineBranches, Has.Count.EqualTo(1));
        Assert.That(baselineBranches[0].TestBuilds, Has.Count.EqualTo(1));
        Assert.That(baselineBranches[0].TestBuilds[0].JobName.Value, Is.EqualTo("MAIN-tests"));
        Assert.That(workspace.OnDemandBuilds.TestBuilds, Has.Count.EqualTo(1));
        Assert.That(workspace.OnDemandBuilds.TestBuilds[0].JobName.Value, Is.EqualTo("CUSTOM-tests"));
    }

    [Test]
    public async Task Load_Works()
    {
        var jobGroups = await GetJobGroups().ConfigureAwait(false);
        using var temp = new TempDirectory();
        Workspace.New(temp.Path, jobGroups);

        var workspaceStore = new WorkspaceStore(temp.Path);
        var workspace = Workspace.Load(temp.Path, workspaceStore);
        var baselineBranches = workspace.BaselineBranches.ToList();
        Assert.That(baselineBranches, Has.Count.EqualTo(1));
        Assert.That(baselineBranches[0].TestBuilds, Has.Count.EqualTo(1));
        Assert.That(baselineBranches[0].TestBuilds[0].JobName.Value, Is.EqualTo("MAIN-tests"));
        Assert.That(workspace.OnDemandBuilds.TestBuilds, Has.Count.EqualTo(1));
        Assert.That(workspace.OnDemandBuilds.TestBuilds[0].JobName.Value, Is.EqualTo("CUSTOM-tests"));
    }

    [Test]
    public async Task GetGitReference_WithWantedBranch_Found()
    {
        using var temp = new TempDirectory();
        var jobGroups = await GetJobGroups().ConfigureAwait(false);
        var workspace = Workspace.New(temp.Path, jobGroups);
        var config = JenkinsConfig.New("http://localhost:8080", rootFilters: [new RootFilter("build", "^build$")]);
        var filterManager = new FilterManager(config, jobGroups);
        var wantedBranch = new BranchName("main");
        var rootFilters = new string[] { "build" };
        var commits = new Sha1[] { RandomData.NextSha1(), RandomData.NextSha1(), };
        var rootBuild = RandomData.NextRootBuild(
            jobName: "MAIN-build",
            buildNumber: RandomData.NextBuildNumber,
            isSuccessful: true,
            commits: 1
        );
        rootBuild.Commits[0] = commits[1];
        workspace.BaselineBranches.First().TryAdd(rootBuild);
        var gitReference = workspace.GetGitReference(filterManager, wantedBranch, rootFilters, commits, out var rootDiffs);
        Assert.That(gitReference, Is.Not.Null);
        Debug.Assert(gitReference is not null);
        Assert.That(gitReference.Branch, Is.EqualTo(wantedBranch));
        Assert.That(rootDiffs, Has.Length.EqualTo(1));
        Assert.That(rootDiffs[0].BaselineJob.Value, Is.EqualTo("MAIN-build"));
    }

    [Test]
    public async Task GetGitReference_WithWantedBranch_NotFound()
    {
        using var temp = new TempDirectory();
        var jobGroups = await GetJobGroups().ConfigureAwait(false);
        var workspace = Workspace.New(temp.Path, jobGroups);
        var config = JenkinsConfig.New("http://localhost:8080", rootFilters: [new RootFilter("build", "^build$")]);
        var filterManager = new FilterManager(config, jobGroups);
        var wantedBranch = new BranchName("main");
        var rootFilters = new string[] { "build" };
        var commits = new Sha1[] { RandomData.NextSha1(), RandomData.NextSha1(), };
        var rootBuild = RandomData.NextRootBuild(
            jobName: "MAIN-build",
            buildNumber: RandomData.NextBuildNumber,
            isSuccessful: true,
            commits: 1
        );
        workspace.BaselineBranches.First().TryAdd(rootBuild);
        var gitReference = workspace.GetGitReference(filterManager, wantedBranch, rootFilters, commits, out var rootDiffs);
        Assert.That(gitReference, Is.Null);
    }

    [Test]
    public async Task GetGitReference_WithoutWantedBranch_Found()
    {
        using var temp = new TempDirectory();
        var jobGroups = await GetJobGroups().ConfigureAwait(false);
        var workspace = Workspace.New(temp.Path, jobGroups);
        var config = JenkinsConfig.New("http://localhost:8080", rootFilters: [new RootFilter("build", "^build$")]);
        var filterManager = new FilterManager(config, jobGroups);
        var rootFilters = new string[] { "build" };
        var commits = new Sha1[] { RandomData.NextSha1(), RandomData.NextSha1(), };
        var rootBuild = RandomData.NextRootBuild(
            jobName: "MAIN-build",
            buildNumber: RandomData.NextBuildNumber,
            isSuccessful: true,
            commits: 1
        );
        rootBuild.Commits[0] = commits[1];
        workspace.BaselineBranches.First().TryAdd(rootBuild);
        var gitReference = workspace.GetGitReference(filterManager, null, rootFilters, commits, out var rootDiffs);
        Assert.That(gitReference, Is.Not.Null);
        Debug.Assert(gitReference is not null);
        Assert.That(gitReference.Branch.Value, Is.EqualTo("main"));
        Assert.That(rootDiffs, Has.Length.EqualTo(1));
        Assert.That(rootDiffs[0].BaselineJob.Value, Is.EqualTo("MAIN-build"));
    }

    [Test]
    public async Task GetGitReference_WithoutWantedBranch_NotFound()
    {
        using var temp = new TempDirectory();
        var jobGroups = await GetJobGroups().ConfigureAwait(false);
        var workspace = Workspace.New(temp.Path, jobGroups);
        var config = JenkinsConfig.New("http://localhost:8080", rootFilters: [new RootFilter("build", "^build$")]);
        var filterManager = new FilterManager(config, jobGroups);
        var rootFilters = new string[] { "build" };
        var commits = new Sha1[] { RandomData.NextSha1(), RandomData.NextSha1(), };
        var rootBuild = RandomData.NextRootBuild(
            jobName: "MAIN-build",
            buildNumber: RandomData.NextBuildNumber,
            isSuccessful: true,
            commits: 1
        );
        workspace.BaselineBranches.First().TryAdd(rootBuild);
        var gitReference = workspace.GetGitReference(filterManager, null, rootFilters, commits, out var rootDiffs);
        Assert.That(gitReference, Is.Null);
    }

    [Test]
    public async Task UpdateJobs_WithNewJobs_AddsJob()
    {
        using var temp = new TempDirectory();

        var initialJobGroups = await GetJobGroups().ConfigureAwait(false);
        var workspace = Workspace.New(temp.Path, initialJobGroups);

        var updatedJobGroups = await GetJobGroups([
            new JobName("MAIN-Domain-build"),
            new JobName("MAIN-Domain-tests"),
            new JobName("CUSTOM-Domain-build"),
            new JobName("CUSTOM-Domain-tests"),
        ]).ConfigureAwait(false);
        Debug.Assert(updatedJobGroups is not null);
        var workspaceStore = new WorkspaceStore(temp.Path);
        workspace.UpdateJobs(workspaceStore, updatedJobGroups);

        using (Assert.EnterMultipleScope())
        {
            for (var i = 0; i < 2; i++)
            {
                Assert.That(workspace.BaselineBranches.Count(), Is.EqualTo(1));
                var baselineBranch = workspace.BaselineBranches.First();
                var rootJobNames = baselineBranch.RootBuilds.Select(b => b.JobName.Value).ToList();
                Assert.That(rootJobNames, Is.EquivalentTo(["MAIN-build", "MAIN-Domain-build"]));
                var testJobNames = baselineBranch.TestBuilds.Select(b => b.JobName.Value).ToList();
                Assert.That(testJobNames, Is.EquivalentTo(["MAIN-tests", "MAIN-Domain-tests"]));
                rootJobNames = [.. workspace.OnDemandBuilds.RootBuilds.Select(b => b.JobName.Value)];
                Assert.That(rootJobNames, Is.EquivalentTo(["CUSTOM-build", "CUSTOM-Domain-build"]));
                testJobNames = [.. workspace.OnDemandBuilds.TestBuilds.Select(b => b.JobName.Value)];
                Assert.That(testJobNames, Is.EquivalentTo(["CUSTOM-tests", "CUSTOM-Domain-tests"]));

                if (i == 0)
                {
                    workspace = Workspace.Load(temp.Path, new WorkspaceStore(temp.Path));
                }
            }
        }
    }

    [Test]
    public async Task UpdateJobs_WithNewBranch_AddsJob()
    {
        using var temp = new TempDirectory();

        var initialJobGroups = await GetJobGroups().ConfigureAwait(false);
        var workspace = Workspace.New(temp.Path, initialJobGroups);

        var updatedJobGroups = await GetJobGroups([
            new JobName("PROD-build"),
            new JobName("PROD-tests"),
        ]).ConfigureAwait(false);
        Debug.Assert(updatedJobGroups is not null);
        var workspaceStore = new WorkspaceStore(temp.Path);
        workspace.UpdateJobs(workspaceStore, updatedJobGroups);

        using (Assert.EnterMultipleScope())
        {
            for (var i = 0; i < 2; i++)
            {
                Assert.That(workspace.BaselineBranches.Count(), Is.EqualTo(2));
                var branchReferenceByBranch = workspace.BaselineBranches.ToDictionary(b => b.BranchName.Value);

                var mainReference = branchReferenceByBranch["main"];
                var rootJobNames = mainReference.RootBuilds.Select(b => b.JobName.Value).ToList();
                Assert.That(rootJobNames, Is.EquivalentTo(["MAIN-build"]));
                var testJobNames = mainReference.TestBuilds.Select(b => b.JobName.Value).ToList();
                Assert.That(testJobNames, Is.EquivalentTo(["MAIN-tests"]));

                var prodReference = branchReferenceByBranch["PROD"];
                rootJobNames = [.. prodReference.RootBuilds.Select(b => b.JobName.Value)];
                Assert.That(rootJobNames, Is.EquivalentTo(["PROD-build"]));
                testJobNames = [.. prodReference.TestBuilds.Select(b => b.JobName.Value)];
                Assert.That(testJobNames, Is.EquivalentTo(["PROD-tests"]));

                rootJobNames = [.. workspace.OnDemandBuilds.RootBuilds.Select(b => b.JobName.Value)];
                Assert.That(rootJobNames, Is.EquivalentTo(["CUSTOM-build"]));
                testJobNames = [.. workspace.OnDemandBuilds.TestBuilds.Select(b => b.JobName.Value)];
                Assert.That(testJobNames, Is.EquivalentTo(["CUSTOM-tests"]));

                if (i == 0)
                {
                    workspace = Workspace.Load(temp.Path, new WorkspaceStore(temp.Path));
                }
            }
        }
    }

    [Test]
    public async Task UpdateJobs_WithRemovedJobs_AddsJob()
    {
        using var temp = new TempDirectory();

        var initialJobGroups = await GetJobGroups([
            new JobName("MAIN-Domain-build"),
            new JobName("MAIN-Domain-tests"),
            new JobName("CUSTOM-Domain-build"),
            new JobName("CUSTOM-Domain-tests"),
        ]).ConfigureAwait(false);
        var workspace = Workspace.New(temp.Path, initialJobGroups);

        var updatedJobGroups = await GetJobGroups().ConfigureAwait(false);
        Debug.Assert(updatedJobGroups is not null);
        var workspaceStore = new WorkspaceStore(temp.Path);
        workspace.UpdateJobs(workspaceStore, updatedJobGroups);

        workspace = Workspace.Load(temp.Path, new WorkspaceStore(temp.Path));

        using (Assert.EnterMultipleScope())
        {
            for (var i = 0; i < 2; i++)
            {
                Assert.That(workspace.BaselineBranches.Count(), Is.EqualTo(1));
                var baselineBranch = workspace.BaselineBranches.First();
                var rootJobNames = baselineBranch.RootBuilds.Select(b => b.JobName.Value).ToList();
                Assert.That(rootJobNames, Is.EqualTo(["MAIN-build"]));
                var testJobNames = baselineBranch.TestBuilds.Select(b => b.JobName.Value).ToList();
                Assert.That(testJobNames, Is.EqualTo(["MAIN-tests"]));
                rootJobNames = [.. workspace.OnDemandBuilds.RootBuilds.Select(b => b.JobName.Value)];
                Assert.That(rootJobNames, Is.EqualTo(["CUSTOM-build"]));
                testJobNames = [.. workspace.OnDemandBuilds.TestBuilds.Select(b => b.JobName.Value)];
                Assert.That(testJobNames, Is.EqualTo(["CUSTOM-tests"]));

                if (i == 0)
                {
                    workspace = Workspace.Load(temp.Path, new WorkspaceStore(temp.Path));
                }
            }
        }
    }

    [Test]
    public async Task RemoveBuildsOlderThan_Works()
    {
        using var temp = new TempDirectory();
        var jobGroups = await GetJobGroups().ConfigureAwait(false);
        var workspace = Workspace.New(temp.Path, jobGroups);
        var baselineBranch = workspace.BaselineBranches.First();
        var oldBuild = RandomData.NextRootBuild(
            jobName: "MAIN-build",
            buildNumber: 1,
            isSuccessful: true,
            commits: 1,
            startUtc: DateTime.UtcNow.AddDays(-10),
            endUtc: DateTime.UtcNow.AddDays(-9)
        );
        var recentBuild = RandomData.NextRootBuild(
            jobName: "MAIN-build",
            buildNumber: 2,
            isSuccessful: true,
            commits: 1,
            startUtc: DateTime.UtcNow.AddDays(-2),
            endUtc: DateTime.UtcNow.AddDays(-1)
        );
        baselineBranch.TryAdd(oldBuild);
        baselineBranch.TryAdd(recentBuild);
        var removed = workspace.RemoveBuildsOlderThan(DateTime.UtcNow.AddDays(-5));
        Assert.That(removed, Is.EqualTo(1));
        var buildCollections = baselineBranch.RootBuilds.ToList();
        Assert.That(buildCollections, Has.Count.EqualTo(1));
        var builds = buildCollections[0].ToList();
        Assert.That(builds, Has.Count.EqualTo(1));
        Assert.That(builds[0].BuildNumber, Is.EqualTo(2));

        var reloaded = Workspace.Load(temp.Path, new WorkspaceStore(temp.Path));
        baselineBranch = reloaded.BaselineBranches.First();
        buildCollections = [.. baselineBranch.RootBuilds];
        Assert.That(buildCollections, Has.Count.EqualTo(1));
        builds = [.. buildCollections[0]];
        Assert.That(builds, Has.Count.EqualTo(1));
        Assert.That(builds[0].BuildNumber, Is.EqualTo(2));
    }
}
