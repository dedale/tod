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
            var referenceStore = workspaceStore.GetReferenceStore(new BranchName("main"));

            var branchReference = new BranchReference(referenceStore);
            branchReference.TryAddRoot(new JobName("MAIN-build"));
            Assert.That(branchReference.TryAdd(rootBuild), Is.True);
            Assert.That(branchReference.TryAdd(testBuild), Is.True);

            var onDemandStore = workspaceStore.OnDemandStore;
            var onDemandBuilds = new OnDemandBuilds(onDemandStore);
            onDemandBuilds.TryAddRoot(new JobName("CUSTOM-build"));

            var flakyStore = workspaceStore.FlakyStore;
            var flakyTests = new FlakyTests(flakyStore);

            var workspace = new Workspace(
                [branchReference],
                onDemandBuilds,
                new OnDemandRequests(Path.Combine(temp.Path, "requests")),
                flakyTests
            );
            var request = Request.Create(RandomData.NextSha1(), new(new("main"), RandomData.NextSha1()), ["integration"], s_userEmail);

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
            var branchReferences = clone.BranchReferences.ToList();
            Assert.That(branchReferences, Has.Count.EqualTo(1));
            Assert.That(branchReferences[0].RootBuilds, Has.Count.EqualTo(1));
            Assert.That(branchReferences[0].TestBuilds, Has.Count.EqualTo(2));
            Assert.That(branchReferences[0].TestBuilds[0], Has.Count.EqualTo(1)); // MAIN-test
            Assert.That(branchReferences[0].TestBuilds[1], Has.Count.EqualTo(0)); // MAIN-test2
            Assert.That(clone.OnDemandBuilds.RootBuilds, Has.Count.EqualTo(1));
            Assert.That(clone.OnDemandRequests.ActiveRequests.Single().Value.Request.Id, Is.EqualTo(request.Id));
            // TODO Assert flaky tests loaded
        }
    }

    private static async Task<JobGroups> GetJobGroups()
    {
        var refJobConfigs = new[]
        {
            new ReferenceJobConfig("MAIN-(?<root>build)", new("main"), true),
            new ReferenceJobConfig("MAIN-(?<test>.*)", new("main"), false),
        };
        var onDemandJobConfigs = new[]
        {
            new OnDemandJobConfig("CUSTOM-(?<root>build)", true),
            new OnDemandJobConfig("CUSTOM-(?<test>.*)", false),
        };
        var testFilters = new[]
        {
            new TestFilter("tests", "^tests$", "tests"),
        };
        var config = JenkinsConfig.New("http://localhost:8080", referenceJobs: refJobConfigs, onDemandJobs: onDemandJobConfigs, testFilters: testFilters);
        var jenkinsClient = new Mock<IJenkinsClient>(MockBehavior.Strict);
        jenkinsClient.Setup(x => x.GetJobNames(config.MultiBranchFolders)).ReturnsAsync(
        [
            new JobName("MAIN-build"),
            new JobName("MAIN-tests"),
            new JobName("CUSTOM-build"),
            new JobName("CUSTOM-tests"),
        ]);
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
        var branchReferences = workspace.BranchReferences.ToList();
        Assert.That(branchReferences, Has.Count.EqualTo(1));
        Assert.That(branchReferences[0].TestBuilds, Has.Count.EqualTo(1));
        Assert.That(branchReferences[0].TestBuilds[0].JobName.Value, Is.EqualTo("MAIN-tests"));
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
        var branchReferences = workspace.BranchReferences.ToList();
        Assert.That(branchReferences, Has.Count.EqualTo(1));
        Assert.That(branchReferences[0].TestBuilds, Has.Count.EqualTo(1));
        Assert.That(branchReferences[0].TestBuilds[0].JobName.Value, Is.EqualTo("MAIN-tests"));
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
        workspace.BranchReferences.First().TryAdd(rootBuild);
        var gitReference = workspace.GetGitReference(filterManager, wantedBranch, rootFilters, commits, out var rootDiffs);
        Assert.That(gitReference, Is.Not.Null);
        Debug.Assert(gitReference is not null);
        Assert.That(gitReference.Branch, Is.EqualTo(wantedBranch));
        Assert.That(rootDiffs, Has.Length.EqualTo(1));
        Assert.That(rootDiffs[0].ReferenceJob.Value, Is.EqualTo("MAIN-build"));
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
        workspace.BranchReferences.First().TryAdd(rootBuild);
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
        workspace.BranchReferences.First().TryAdd(rootBuild);
        var gitReference = workspace.GetGitReference(filterManager, null, rootFilters, commits, out var rootDiffs);
        Assert.That(gitReference, Is.Not.Null);
        Debug.Assert(gitReference is not null);
        Assert.That(gitReference.Branch.Value, Is.EqualTo("main"));
        Assert.That(rootDiffs, Has.Length.EqualTo(1));
        Assert.That(rootDiffs[0].ReferenceJob.Value, Is.EqualTo("MAIN-build"));
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
        workspace.BranchReferences.First().TryAdd(rootBuild);
        var gitReference = workspace.GetGitReference(filterManager, null, rootFilters, commits, out var rootDiffs);
        Assert.That(gitReference, Is.Null);
    }
}
