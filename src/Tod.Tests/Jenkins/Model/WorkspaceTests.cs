using Moq;
using NUnit.Framework;
using System.Diagnostics;
using Tod.Jenkins;
using Tod.Tests.IO;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class WorkspaceTests
{
    [Test]
    public void SerializationRoundTrip_Works()
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
                    new BuildReference(new JobName("MAIN-test"), 1),
                    new BuildReference(new JobName("MAIN-test2"), 1),
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
            var branchReference = new BranchReference(
                new BranchName("main"),
                new JobName("MAIN-build")
            );
            Assert.That(branchReference.TryAdd(rootBuild), Is.True);
            Assert.That(branchReference.TryAdd(testBuild), Is.True);
            var workspace = new Workspace(
                temp.Directory.Path,
                [branchReference],
                new OnDemandBuilds(new JobName("CUSTOM-build")),
                new OnDemandRequests(Path.Combine(temp.Directory.Path, "requests"))
            );
            var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), new("main"), ["integration"]);
            var requestState = RequestState.New(
                request,
                new BuildReference("MAIN-build", 1),
                new BuildReference("CUSTOM-build", 42),
                [new RequestBuildDiff(new("MAIN-test"), new("CUSTOM-test"))]
            );
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
            var clone = workspace.SerializationRoundTrip<Workspace, Workspace.Serializable>();
            Assert.That(clone.BranchReferences, Has.Count.EqualTo(1));
            Assert.That(clone.BranchReferences[0].RootBuilds, Has.Count.EqualTo(1));
            Assert.That(clone.BranchReferences[0].TestBuilds, Has.Count.EqualTo(2));
            Assert.That(clone.BranchReferences[0].TestBuilds[0], Has.Count.EqualTo(1)); // MAIN-test
            Assert.That(clone.BranchReferences[0].TestBuilds[1], Has.Count.EqualTo(0)); // MAIN-test2
            Assert.That(clone.OnDemandBuilds.RootBuilds, Has.Count.EqualTo(1));
            Assert.That(clone.OnDemandRequests.ActiveRequests.Single().Value.Request.Id, Is.EqualTo(request.Id));
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
        var filters = new[]
        {
            new TestFilter("tests", "^tests$", "tests"),
        };
        var config = JenkinsConfig.New("http://localhost:8080", referenceJobs: refJobConfigs, onDemandJobs: onDemandJobConfigs, filters: filters);
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
        var workspace = Workspace.New(Path.Combine(temp.Directory.Path, "Workspace.json"), jobGroups);
        Assert.That(workspace.BranchReferences, Has.Count.EqualTo(1));
        Assert.That(workspace.BranchReferences[0].TestBuilds, Has.Count.EqualTo(1));
        Assert.That(workspace.BranchReferences[0].TestBuilds[0].JobName.Value, Is.EqualTo("MAIN-tests"));
        Assert.That(workspace.OnDemandBuilds.TestBuilds, Has.Count.EqualTo(1));
        Assert.That(workspace.OnDemandBuilds.TestBuilds[0].JobName.Value, Is.EqualTo("CUSTOM-tests"));
    }

    [Test]
    public async Task Load_Works()
    {
        var jobGroups = await GetJobGroups().ConfigureAwait(false);
        using var temp = new TempDirectory();
        Workspace.New(Path.Combine(temp.Directory.Path, "Workspace.json"), jobGroups);
        using var lockedWorkspace = Workspace.Load(Path.Combine(temp.Directory.Path, "Workspace.json"), nameof(Load_Works));
        Assert.That(lockedWorkspace.Value.BranchReferences, Has.Count.EqualTo(1));
        Assert.That(lockedWorkspace.Value.BranchReferences[0].TestBuilds, Has.Count.EqualTo(1));
        Assert.That(lockedWorkspace.Value.BranchReferences[0].TestBuilds[0].JobName.Value, Is.EqualTo("MAIN-tests"));
        Assert.That(lockedWorkspace.Value.OnDemandBuilds.TestBuilds, Has.Count.EqualTo(1));
        Assert.That(lockedWorkspace.Value.OnDemandBuilds.TestBuilds[0].JobName.Value, Is.EqualTo("CUSTOM-tests"));
    }

    [Test]
    public async Task LoadUnlocked_Works()
    {
        var jobGroups = await GetJobGroups().ConfigureAwait(false);
        using var temp = new TempDirectory();
        Workspace.New(Path.Combine(temp.Directory.Path, "Workspace.json"), jobGroups);
        var workspace = Workspace.LoadUnlocked(Path.Combine(temp.Directory.Path, "Workspace.json"));
        Assert.That(workspace.BranchReferences, Has.Count.EqualTo(1));
        Assert.That(workspace.BranchReferences[0].TestBuilds, Has.Count.EqualTo(1));
        Assert.That(workspace.BranchReferences[0].TestBuilds[0].JobName.Value, Is.EqualTo("MAIN-tests"));
        Assert.That(workspace.OnDemandBuilds.TestBuilds, Has.Count.EqualTo(1));
        Assert.That(workspace.OnDemandBuilds.TestBuilds[0].JobName.Value, Is.EqualTo("CUSTOM-tests"));
    }
}
