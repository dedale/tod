using Moq;
using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class JenkinsSynchronizerTests
{
    private readonly BranchName _mainBranch = new("main");

    private readonly JobName _refRootJob = new("MAIN-build");
    private readonly JobName _refTestJob1 = new("MAIN-test1");
    private readonly JobName _refTestJob2 = new("MAIN-test2");
    private readonly JobName _onDemandRootJob = new("CUSTOM-build");
    private readonly JobName _onDemandTestJob1 = new("CUSTOM-test1");
    private readonly JobName _onDemandTestJob2 = new("CUSTOM-test2");

    private Workspace NewWorkspace(OnDemandBuilds onDemandBuilds)
    {
        return new Workspace([], onDemandBuilds, new OnDemandRequests("requests"));
    }

    private Workspace NewWorkspace(BranchReference branchReference, JobName onDemandRootJobName, IOnDemandStore onDemandStore)
    {
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(onDemandRootJobName);
        return new Workspace([branchReference], onDemandBuilds, new OnDemandRequests("requests"));
    }

    [Test]
    public async Task Update_BranchReference_AddNewRootBuilds()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithReferenceStore(_mainBranch, _refRootJob, out var referenceStore)
                .WithNewRootBuilds(_refRootJob)
                .WithTestobs(_refTestJob1, _refTestJob2)
                .WithOnDemandStore(_onDemandRootJob, out var onDemandStore);

            var branchReference = new BranchReference(referenceStore);
            branchReference.TryAddRoot(_refRootJob);
            branchReference.TryAddTest(_refTestJob1);
            branchReference.TryAddTest(_refTestJob2);
            var buildCount = 2;
            var builds = RandomBuilds.Generate(buildCount).ToArray();
            var triggered = Enumerable.Range(0, buildCount)
                .Select(_ => new BuildReference[] {
                new(_refTestJob1, RandomData.NextBuildNumber),
                new(_refTestJob2, RandomData.NextBuildNumber),
                })
                .ToArray();
            var client = new Mock<IJenkinsClient>(MockBehavior.Strict);
            client.Setup(x => x.GetLastBuilds(_refRootJob, 100)).ReturnsAsync(builds);
            for (var i = 0; i < buildCount; i++)
            {
                client.Setup(x => x.GetTriggeredBuilds(new(_refRootJob, builds[i].Number))).ReturnsAsync(triggered[i]);
            }
            client.Setup(x => x.GetLastBuilds(_refTestJob1, 100)).ReturnsAsync([]);
            client.Setup(x => x.GetLastBuilds(_refTestJob2, 100)).ReturnsAsync([]);
            client.Setup(x => x.GetLastBuilds(_onDemandRootJob, 100)).ReturnsAsync([]);
            var handler = new Mock<IPostBuildHandler>(MockBehavior.Strict);
            var synchronizer = new JenkinsSynchronizer(client.Object, handler.Object);
            var workspace = NewWorkspace(branchReference, _onDemandRootJob, onDemandStore);
            await synchronizer.Update(workspace).ConfigureAwait(false);
            Assert.That(branchReference.RootBuilds, Has.Count.EqualTo(1));
            Assert.That(branchReference.RootBuilds[0], Has.Count.EqualTo(buildCount));
            var rootBuilds = branchReference.RootBuilds[0].ToList();
            for (var i = 0; i < buildCount; i++)
            {
                var j = buildCount - i - 1; // Builds are added in reverse order
                Assert.That(rootBuilds[j].Id, Is.EqualTo(builds[i].Id));
                Assert.That(rootBuilds[j].BuildNumber, Is.EqualTo(builds[i].Number));
                Assert.That(rootBuilds[j].IsSuccessful, Is.EqualTo(builds[i].Result == BuildResult.Success));
                Assert.That(rootBuilds[j].StartTimeUtc, Is.EqualTo(builds[i].TimestampUtc));
                Assert.That(rootBuilds[j].EndTimeUtc, Is.EqualTo(builds[i].TimestampUtc.AddMilliseconds(builds[i].DurationInMs)));
                Assert.That(rootBuilds[j].Commits, Is.EqualTo(builds[i].GetCommits()));
                Assert.That(rootBuilds[j].Triggered, Is.EqualTo(triggered[i]));
            }
            Assert.That(branchReference.TestBuilds, Has.Count.EqualTo(2));
            Assert.That(branchReference.TestBuilds[0].JobName.Value, Is.EqualTo("MAIN-test1"));
            Assert.That(branchReference.TestBuilds[1].JobName.Value, Is.EqualTo("MAIN-test2"));
            client.VerifyAll();
            handler.VerifyAll();
        }
    }

    [Test]
    public async Task Update_BranchReference_DoNotAddKnownRootBuilds()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _refRootJob, out var referenceStore)
            .WithNewRootBuilds(_refRootJob)
            .WithTestobs(_refTestJob1, _refTestJob2)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(_refRootJob);
        var builds = RandomBuilds.Generate(1).ToArray();
        var build = builds[0];
        var triggered = new BuildReference[] {
            new(_refTestJob1, RandomData.NextBuildNumber),
            new(_refTestJob2, RandomData.NextBuildNumber),
        };
        // TryAdd on RootBuilds not branchReference as we do not care about triggered builds here
        branchReference.RootBuilds[0].TryAdd(new RootBuild(
            _refRootJob,
            build.Id,
            build.Number,
            build.TimestampUtc,
            build.TimestampUtc.AddMilliseconds(build.DurationInMs),
            build.Result == BuildResult.Success,
            build.GetCommits(),
            triggered
        ));
        var client = new Mock<IJenkinsClient>(MockBehavior.Strict);
        client.Setup(x => x.GetLastBuilds(_refRootJob, 100)).ReturnsAsync(builds);
        client.Setup(x => x.GetLastBuilds(_onDemandRootJob, 100)).ReturnsAsync([]);
        var handler = new Mock<IPostBuildHandler>(MockBehavior.Strict);
        var synchronizer = new JenkinsSynchronizer(client.Object, handler.Object);
        var workspace = NewWorkspace(branchReference, _onDemandRootJob, onDemandStore);
        await synchronizer.Update(workspace).ConfigureAwait(false);
        Assert.That(branchReference.RootBuilds, Has.Count.EqualTo(1));
        client.VerifyAll();
        handler.VerifyAll();
    }

    [Test]
    public async Task Update_BranchReference_AddNewTestBuilds()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _refRootJob, out var referenceStore)
            .WithNewRootBuilds(_refRootJob)
            .WithNewTestBuilds(_refTestJob1)
            .WithTestobs(_refTestJob2)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(_refRootJob);
        var buildCount = 2;
        var rootBuilds = RandomBuilds.Generate(2).ToArray();
        var triggeredNumbers = Enumerable.Range(0, buildCount).Select(_ => RandomData.NextBuildNumber).ToArray();
        var triggered = Enumerable.Range(0, buildCount)
            .Select(i => new BuildReference[] {
                new(_refTestJob1, triggeredNumbers[0] - i),
                new(_refTestJob2, triggeredNumbers[1] - i),
            })
            .ToArray();
        for (var i = 0; i < buildCount; i++)
        {
            var j = buildCount - i - 1;
            var build = rootBuilds[j];
            branchReference.TryAdd(new RootBuild(
                _refRootJob,
                build.Id,
                build.Number,
                build.TimestampUtc,
                build.TimestampUtc.AddMilliseconds(build.DurationInMs),
                build.Result == BuildResult.Success,
                build.GetCommits(),
                triggered[j]
            ));
        }
        var builds = RandomBuilds.Generate(
            buildCount,
            buildNumbers: [.. triggered.Select(x => x[0].BuildNumber)],
            success: [.. Enumerable.Range(0, buildCount).Select(i => i % 2 == 0)]
        ).ToArray();
        var failCounts = Enumerable.Range(0, buildCount).Select(i => i % 2 == 0 ? 0 : 2).ToArray();
        var failedTests = RandomFailedTests.Generate(2).ToArray();
        var client = new Mock<IJenkinsClient>(MockBehavior.Strict);
        client.Setup(x => x.GetLastBuilds(_refRootJob, 100)).ReturnsAsync([]);
        client.Setup(x => x.GetLastBuilds(_refTestJob1, 100)).ReturnsAsync(builds);
        client.Setup(x => x.GetLastBuilds(_refTestJob2, 100)).ReturnsAsync([]);
        client.Setup(x => x.GetLastBuilds(_onDemandRootJob, 100)).ReturnsAsync([]);
        for (var i = 0; i < buildCount; i++)
        {
            var build = builds[i];
            var failCount = failCounts[i];
            client.Setup(x => x.GetFailCount(new(_refTestJob1, build.Number))).ReturnsAsync(failCount);
            if (failCount > 0)
            {
                client.Setup(x => x.GetFailedTests(new(_refTestJob1, build.Number))).ReturnsAsync(failedTests);
            }
        }
        var handler = new Mock<IPostBuildHandler>(MockBehavior.Strict);
        for (var i = 0; i < buildCount; i++)
        {
            handler.Setup(x => x.PostReferenceTestBuild(new BuildReference(_refRootJob, rootBuilds[i].Number), triggered[i][0]));
        }
        var synchronizer = new JenkinsSynchronizer(client.Object, handler.Object);
        var workspace = NewWorkspace(branchReference, _onDemandRootJob, onDemandStore);
        await synchronizer.Update(workspace).ConfigureAwait(false);
        var testCollection = branchReference.TestBuilds.Single(x => x.JobName == _refTestJob1);
        var testBuilds = testCollection.ToList();
        Assert.That(testBuilds, Has.Count.EqualTo(2));
        for (var i = 0; i < buildCount; i++)
        {
            var j = buildCount - i - 1;
            Assert.That(testBuilds[j].Id, Is.EqualTo(builds[i].Id));
            Assert.That(testBuilds[j].BuildNumber, Is.EqualTo(builds[i].Number));
        }
        client.VerifyAll();
        handler.VerifyAll();
    }

    [Test]
    public async Task Update_BranchReference_DoNotAddKnownTestBuilds()
    {
        var testJobName = new JobName("MAIN-test");

        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _refRootJob, out var referenceStore)
            .WithNewRootBuilds(_refRootJob)
            .WithNewTestBuilds(testJobName)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(_refRootJob);
        var rootBuilds = RandomBuilds.Generate(1).ToArray();
        var rootBuild = rootBuilds[0];
        var triggered = new BuildReference[] {
            new(testJobName, RandomData.NextBuildNumber),
        };
        branchReference.TryAdd(new RootBuild(
            _refRootJob,
            rootBuild.Id,
            rootBuild.Number,
            rootBuild.TimestampUtc,
            rootBuild.TimestampUtc.AddMilliseconds(rootBuild.DurationInMs),
            rootBuild.Result == BuildResult.Success,
            rootBuild.GetCommits(),
            triggered
        ));
        branchReference.TryAdd(new TestBuild(
            testJobName,
            Guid.NewGuid().ToString(),
            triggered[0].BuildNumber,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5),
            true,
            new BuildReference(_refRootJob, rootBuild.Number),
            []
        ));
        var testBuildReference = triggered[0];
        var testBuilds = RandomBuilds.Generate(1, [testBuildReference.BuildNumber]).ToArray();
        var testBuild = testBuilds[0];
        var failedTests = RandomFailedTests.Generate(2).ToArray();
        var client = new Mock<IJenkinsClient>(MockBehavior.Strict);
        client.Setup(x => x.GetLastBuilds(_refRootJob, 100)).ReturnsAsync([]);
        client.Setup(x => x.GetLastBuilds(testJobName, 100)).ReturnsAsync(testBuilds);
        client.Setup(x => x.GetLastBuilds(_onDemandRootJob, 100)).ReturnsAsync([]);
        var handler = new Mock<IPostBuildHandler>(MockBehavior.Strict);
        var synchronizer = new JenkinsSynchronizer(client.Object, handler.Object);
        var workspace = NewWorkspace(branchReference, _onDemandRootJob, onDemandStore);
        await synchronizer.Update(workspace).ConfigureAwait(false);
        var testCollection = branchReference.TestBuilds.Single(x => x.JobName == testJobName);
        Assert.That(testCollection, Has.Count.EqualTo(1));
        client.VerifyAll();
        handler.VerifyAll();
    }

    [Test]
    public async Task Update_BranchReference_WithUnexpectedOtherTestBuild()
    {
        var testJobName = new JobName("MAIN-test");
        var otherTestJobName = new JobName("MAIN-other-test");

        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _refRootJob, out var referenceStore)
            .WithNewRootBuilds(_refRootJob)
            .WithNewTestBuilds(testJobName)
            .WithNewTestBuilds(otherTestJobName)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(_refRootJob);
        var rootBuilds = RandomBuilds.Generate(1).ToArray();
        var rootBuild = rootBuilds[0];
        var triggered = new BuildReference[] {
            new(testJobName, RandomData.NextBuildNumber),
        };
        branchReference.TryAdd(new RootBuild(
            _refRootJob,
            rootBuild.Id,
            rootBuild.Number,
            rootBuild.TimestampUtc,
            rootBuild.TimestampUtc.AddMilliseconds(rootBuild.DurationInMs),
            rootBuild.Result == BuildResult.Success,
            rootBuild.GetCommits(),
            triggered
        ));
        branchReference.TryAdd(new TestBuild(
            testJobName,
            Guid.NewGuid().ToString(),
            triggered[0].BuildNumber,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5),
            true,
            new BuildReference(_refRootJob, rootBuild.Number),
            []
        ));
        branchReference.TryAdd(new TestBuild(
            otherTestJobName,
            Guid.NewGuid().ToString(),
            RandomData.NextBuildNumber,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5),
            true,
            new BuildReference(_refRootJob, RandomData.NextBuildNumber),
            []
        ));
        var testBuildReference = triggered[0];
        var testBuilds = RandomBuilds.Generate(1, [testBuildReference.BuildNumber]).ToArray();
        var testBuild = testBuilds[0];
        var failedTests = RandomFailedTests.Generate(2).ToArray();
        var client = new Mock<IJenkinsClient>(MockBehavior.Strict);
        client.Setup(x => x.GetLastBuilds(_refRootJob, 100)).ReturnsAsync([]);
        client.Setup(x => x.GetLastBuilds(testJobName, 100)).ReturnsAsync(testBuilds);
        client.Setup(x => x.GetLastBuilds(otherTestJobName, 100)).ReturnsAsync([]);
        client.Setup(x => x.GetLastBuilds(_onDemandRootJob, 100)).ReturnsAsync([]);
        var handler = new Mock<IPostBuildHandler>(MockBehavior.Strict);
        var synchronizer = new JenkinsSynchronizer(client.Object, handler.Object);
        var workspace = NewWorkspace(branchReference, _onDemandRootJob, onDemandStore);
        await synchronizer.Update(workspace).ConfigureAwait(false);
        var testCollection = branchReference.TestBuilds.Single(x => x.JobName == testJobName);
        Assert.That(testCollection, Has.Count.EqualTo(1));
        client.VerifyAll();
        handler.VerifyAll();
    }

    [Test]
    public async Task Update_BranchReference_IgnoreTestBuildsWithoutKnownRootBuild()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _refRootJob, out var referenceStore)
            .WithNewRootBuilds(_refRootJob)
            .WithNewTestBuilds(_refTestJob1)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(_refRootJob);
        var rootBuilds = RandomBuilds.Generate(1).ToArray();
        var triggered = new BuildReference[] {
            new(_refTestJob1, RandomData.NextBuildNumber),
        };
        var build = rootBuilds[0];
        branchReference.TryAdd(new RootBuild(
            _refRootJob,
            build.Id,
            build.Number,
            build.TimestampUtc,
            build.TimestampUtc.AddMilliseconds(build.DurationInMs),
            build.Result == BuildResult.Success,
            build.GetCommits(),
            triggered
        ));
        var builds = RandomBuilds.Generate(1, [RandomData.NextBuildNumber]).ToArray();
        var client = new Mock<IJenkinsClient>(MockBehavior.Strict);
        client.Setup(x => x.GetLastBuilds(_refRootJob, 100)).ReturnsAsync([]);
        client.Setup(x => x.GetLastBuilds(_refTestJob1, 100)).ReturnsAsync(builds);
        client.Setup(x => x.GetLastBuilds(_onDemandRootJob, 100)).ReturnsAsync([]);
        var handler = new Mock<IPostBuildHandler>(MockBehavior.Strict);
        var synchronizer = new JenkinsSynchronizer(client.Object, handler.Object);
        var workspace = NewWorkspace(branchReference, _onDemandRootJob, onDemandStore);
        await synchronizer.Update(workspace).ConfigureAwait(false);
        var testCollection = branchReference.TestBuilds.Single(x => x.JobName == _refTestJob1);
        var testBuilds = testCollection.ToList();
        Assert.That(testBuilds, Has.Count.EqualTo(0));
        client.VerifyAll();
        handler.VerifyAll();
    }

    [Test]
    public async Task Update_OnDemand_AddNewRootBuilds()
    {
        using var mocks = StoreMocks.New()
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithNewRootBuilds(_onDemandRootJob)
            .WithTestobs(_onDemandTestJob1, _onDemandTestJob2);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_onDemandRootJob);
        onDemandBuilds.TryAddTest(_onDemandTestJob1);
        onDemandBuilds.TryAddTest(_onDemandTestJob2);
        var builds = RandomBuilds.Generate(1).ToArray();
        var build = builds[0];
        var triggered = new BuildReference[] {
            new(_onDemandTestJob1, RandomData.NextBuildNumber),
            new(_onDemandTestJob2, RandomData.NextBuildNumber),
        };
        var client = new Mock<IJenkinsClient>(MockBehavior.Strict);
        client.Setup(x => x.GetLastBuilds(_onDemandRootJob, 100)).ReturnsAsync(builds);
        client.Setup(x => x.GetTriggeredBuilds(new(_onDemandRootJob, build.Number))).ReturnsAsync(triggered);
        client.Setup(x => x.GetLastBuilds(_onDemandTestJob1, 100)).ReturnsAsync([]);
        client.Setup(x => x.GetLastBuilds(_onDemandTestJob2, 100)).ReturnsAsync([]);
        var handler = new Mock<IPostBuildHandler>(MockBehavior.Strict);
        handler.Setup(x => x.PostOnDemandRootBuild(new BuildReference(_onDemandRootJob, build.Number), build.Result == BuildResult.Success));
        var synchronizer = new JenkinsSynchronizer(client.Object, handler.Object);
        var workspace = NewWorkspace(onDemandBuilds);
        await synchronizer.Update(workspace).ConfigureAwait(false);
        Assert.That(onDemandBuilds.RootBuilds, Has.Count.EqualTo(1));
        var rootBuild = onDemandBuilds.RootBuilds[0].First();
        Assert.That(rootBuild.Id, Is.EqualTo(build.Id));
        Assert.That(rootBuild.BuildNumber, Is.EqualTo(build.Number));
        Assert.That(rootBuild.IsSuccessful, Is.EqualTo(build.Result == BuildResult.Success));
        Assert.That(rootBuild.StartTimeUtc, Is.EqualTo(build.TimestampUtc));
        Assert.That(rootBuild.EndTimeUtc, Is.EqualTo(build.TimestampUtc.AddMilliseconds(build.DurationInMs)));
        Assert.That(rootBuild.Commits, Is.EqualTo(build.GetCommits()));
        Assert.That(rootBuild.Triggered, Is.EqualTo(triggered));
        client.VerifyAll();
        handler.VerifyAll();
    }

    [Test]
    public async Task Update_OnDemand_DoNotAddKnownRootBuilds()
    {
        using var mocks = StoreMocks.New()
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithNewRootBuilds(_onDemandRootJob)
            .WithTestobs(_onDemandTestJob1, _onDemandTestJob2);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_onDemandRootJob);
        var builds = RandomBuilds.Generate(1).ToArray();
        var build = builds[0];
        var triggered = new BuildReference[] {
            new(_onDemandTestJob1, RandomData.NextBuildNumber),
            new(_onDemandTestJob2, RandomData.NextBuildNumber),
        };
        onDemandBuilds.RootBuilds[0].TryAdd(new RootBuild(
            _onDemandRootJob,
            build.Id,
            build.Number,
            build.TimestampUtc,
            build.TimestampUtc.AddMilliseconds(build.DurationInMs),
            build.Result == BuildResult.Success,
            build.GetCommits(),
            triggered
        ));
        var client = new Mock<IJenkinsClient>(MockBehavior.Strict);
        client.Setup(x => x.GetLastBuilds(_onDemandRootJob, 100)).ReturnsAsync(builds);
        var handler = new Mock<IPostBuildHandler>(MockBehavior.Strict);
        var synchronizer = new JenkinsSynchronizer(client.Object, handler.Object);
        var workspace = NewWorkspace(onDemandBuilds);
        await synchronizer.Update(workspace).ConfigureAwait(false);
        Assert.That(onDemandBuilds.RootBuilds, Has.Count.EqualTo(1));
        client.VerifyAll();
        handler.VerifyAll();
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task Update_OnDemand_AddNewTestBuilds(int failedTestCount)
    {
        using var mocks = StoreMocks.New()
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithNewRootBuilds(_onDemandRootJob)
            .WithNewTestBuilds(_onDemandTestJob1)
            .WithTestobs(_onDemandTestJob2);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_onDemandRootJob);
        var rootBuilds = RandomBuilds.Generate(1).ToArray();
        var rootBuild = rootBuilds[0];
        var triggered = new BuildReference[] {
            new(_onDemandTestJob1, RandomData.NextBuildNumber),
            new(_onDemandTestJob2, RandomData.NextBuildNumber),
        };
        onDemandBuilds.TryAdd(new RootBuild(
            _onDemandRootJob,
            rootBuild.Id,
            rootBuild.Number,
            rootBuild.TimestampUtc,
            rootBuild.TimestampUtc.AddMilliseconds(rootBuild.DurationInMs),
            true,
            rootBuild.GetCommits(),
            triggered
        ));
        var testBuildReference = triggered[0];
        var testBuilds = RandomBuilds.Generate(1, [testBuildReference.BuildNumber], [failedTestCount == 0]).ToArray();
        var testBuild = testBuilds[0];
        var failedTests = RandomFailedTests.Generate(failedTestCount).ToArray();
        var client = new Mock<IJenkinsClient>(MockBehavior.Strict);
        client.Setup(x => x.GetLastBuilds(_onDemandRootJob, 100)).ReturnsAsync([]);
        client.Setup(x => x.GetLastBuilds(_onDemandTestJob1, 100)).ReturnsAsync(testBuilds);
        client.Setup(x => x.GetLastBuilds(_onDemandTestJob2, 100)).ReturnsAsync([]);
        client.Setup(x => x.TryGetRootBuild(new(_onDemandTestJob1, testBuild.Number))).ReturnsAsync(onDemandBuilds.RootBuilds[0][0].Reference);
        client.Setup(x => x.GetFailCount(new(_onDemandTestJob1, testBuild.Number))).ReturnsAsync(failedTestCount);
        if (failedTestCount > 0)
        {
            client.Setup(x => x.GetFailedTests(new(_onDemandTestJob1, testBuild.Number))).ReturnsAsync(failedTests);
        }
        var handler = new Mock<IPostBuildHandler>(MockBehavior.Strict);
        handler.Setup(x => x.PostOnDemandTestBuild(new(_onDemandRootJob, rootBuild.Number), testBuildReference));
        var synchronizer = new JenkinsSynchronizer(client.Object, handler.Object);
        var workspace = NewWorkspace(onDemandBuilds);
        await synchronizer.Update(workspace).ConfigureAwait(false);
        var testCollection = onDemandBuilds.TestBuilds.Single(x => x.JobName == _onDemandTestJob1);
        Assert.That(testCollection, Has.Count.EqualTo(1));
        client.VerifyAll();
        handler.VerifyAll();
    }

    [Test]
    public async Task Update_OnDemand_DoNotGetFailedTestsForSuccessfulBuilds()
    {
        using var mocks = StoreMocks.New()
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithNewRootBuilds(_onDemandRootJob)
            .WithNewTestBuilds(_onDemandTestJob1)
            .WithTestobs(_onDemandTestJob2);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_onDemandRootJob);
        var rootBuilds = RandomBuilds.Generate(1).ToArray();
        var rootBuild = rootBuilds[0];
        var triggered = new BuildReference[] {
            new(_onDemandTestJob1, RandomData.NextBuildNumber),
            new(_onDemandTestJob2, RandomData.NextBuildNumber),
        };
        onDemandBuilds.TryAdd(new RootBuild(
            _onDemandRootJob,
            rootBuild.Id,
            rootBuild.Number,
            rootBuild.TimestampUtc,
            rootBuild.TimestampUtc.AddMilliseconds(rootBuild.DurationInMs),
            true,
            rootBuild.GetCommits(),
            triggered
        ));
        var testBuildReference = triggered[0];
        var testBuilds = RandomBuilds.Generate(1, [testBuildReference.BuildNumber]).ToArray();
        var testBuild = testBuilds[0];
        var client = new Mock<IJenkinsClient>(MockBehavior.Strict);
        client.Setup(x => x.GetLastBuilds(_onDemandRootJob, 100)).ReturnsAsync([]);
        client.Setup(x => x.GetLastBuilds(_onDemandTestJob1, 100)).ReturnsAsync(testBuilds);
        client.Setup(x => x.GetLastBuilds(_onDemandTestJob2, 100)).ReturnsAsync([]);
        client.Setup(x => x.TryGetRootBuild(new(_onDemandTestJob1, testBuild.Number))).ReturnsAsync(onDemandBuilds.RootBuilds[0][0].Reference);
        client.Setup(x => x.GetFailCount(new(_onDemandTestJob1, testBuild.Number))).ReturnsAsync(0);
        var handler = new Mock<IPostBuildHandler>(MockBehavior.Strict);
        handler.Setup(x => x.PostOnDemandTestBuild(new(_onDemandRootJob, rootBuild.Number), testBuildReference));
        var synchronizer = new JenkinsSynchronizer(client.Object, handler.Object);
        var workspace = NewWorkspace(onDemandBuilds);
        await synchronizer.Update(workspace).ConfigureAwait(false);
        var testCollection = onDemandBuilds.TestBuilds.Single(x => x.JobName == _onDemandTestJob1);
        Assert.That(testCollection, Has.Count.EqualTo(1));
        client.VerifyAll();
        handler.VerifyAll();
    }

    [Test]
    public async Task Update_OnDemand_IgnoreTestBuildsWithoutKnownRootBuild()
    {
        using var mocks = StoreMocks.New()
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithNewRootBuilds(_onDemandRootJob)
            .WithNewTestBuilds(_onDemandTestJob1)
            .WithTestobs(_onDemandTestJob2);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_onDemandRootJob);
        onDemandBuilds.TryAddTest(_onDemandTestJob1);
        onDemandBuilds.TryAddTest(_onDemandTestJob2);
        var rootBuilds = RandomBuilds.Generate(1).ToArray();
        var rootBuild = rootBuilds[0];
        var triggered = new BuildReference[] {
            new(_onDemandTestJob1, RandomData.NextBuildNumber),
            new(_onDemandTestJob2, RandomData.NextBuildNumber),
        };
        onDemandBuilds.TryAdd(new RootBuild(
            _onDemandRootJob,
            rootBuild.Id,
            rootBuild.Number,
            rootBuild.TimestampUtc,
            rootBuild.TimestampUtc.AddMilliseconds(rootBuild.DurationInMs),
            rootBuild.Result == BuildResult.Success,
            rootBuild.GetCommits(),
            triggered
        ));
        var testBuildReference = triggered[0];
        var testBuilds = RandomBuilds.Generate(1).ToArray();
        var testBuild = testBuilds[0];
        var failedTests = RandomFailedTests.Generate(2).ToArray();
        var client = new Mock<IJenkinsClient>(MockBehavior.Strict);
        client.Setup(x => x.GetLastBuilds(_onDemandRootJob, 100)).ReturnsAsync([]);
        client.Setup(x => x.GetLastBuilds(_onDemandTestJob1, 100)).ReturnsAsync(testBuilds);
        client.Setup(x => x.GetLastBuilds(_onDemandTestJob2, 100)).ReturnsAsync([]);
        client.Setup(x => x.TryGetRootBuild(new(_onDemandTestJob1, testBuild.Number))).ReturnsAsync((BuildReference?)null);
        var handler = new Mock<IPostBuildHandler>(MockBehavior.Strict);
        var synchronizer = new JenkinsSynchronizer(client.Object, handler.Object);
        var workspace = NewWorkspace(onDemandBuilds);
        await synchronizer.Update(workspace).ConfigureAwait(false);
        var testCollection = onDemandBuilds.TestBuilds.Single(x => x.JobName == _onDemandTestJob1);
        Assert.That(testCollection, Has.Count.EqualTo(0));
        client.VerifyAll();
        handler.VerifyAll();
    }

    [Test]
    public async Task Update_OnDemand_DoNotAddKnownTestBuilds()
    {
        var testJobName = new JobName("CUSTOM-test");

        using var mocks = StoreMocks.New()
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithNewRootBuilds(_onDemandRootJob)
            .WithNewTestBuilds(testJobName);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_onDemandRootJob);
        var rootBuilds = RandomBuilds.Generate(1).ToArray();
        var rootBuild = rootBuilds[0];
        var triggered = new BuildReference[] {
            new(testJobName, RandomData.NextBuildNumber),
        };
        onDemandBuilds.TryAdd(new RootBuild(
            _onDemandRootJob,
            rootBuild.Id,
            rootBuild.Number,
            rootBuild.TimestampUtc,
            rootBuild.TimestampUtc.AddMilliseconds(rootBuild.DurationInMs),
            rootBuild.Result == BuildResult.Success,
            rootBuild.GetCommits(),
            triggered
        ));
        onDemandBuilds.TryAdd(new TestBuild(
            testJobName,
            Guid.NewGuid().ToString(),
            triggered[0].BuildNumber,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5),
            true,
            new BuildReference(_onDemandRootJob, rootBuild.Number),
            []
        ));
        var testBuildReference = triggered[0];
        var testBuilds = RandomBuilds.Generate(1, [testBuildReference.BuildNumber]).ToArray();
        var testBuild = testBuilds[0];
        var failedTests = RandomFailedTests.Generate(2).ToArray();
        var client = new Mock<IJenkinsClient>(MockBehavior.Strict);
        client.Setup(x => x.GetLastBuilds(_onDemandRootJob, 100)).ReturnsAsync([]);
        client.Setup(x => x.GetLastBuilds(testJobName, 100)).ReturnsAsync(testBuilds);
        var handler = new Mock<IPostBuildHandler>(MockBehavior.Strict);
        var synchronizer = new JenkinsSynchronizer(client.Object, handler.Object);
        var workspace = NewWorkspace(onDemandBuilds);
        await synchronizer.Update(workspace).ConfigureAwait(false);
        var testCollection = onDemandBuilds.TestBuilds.Single(x => x.JobName == testJobName);
        Assert.That(testCollection, Has.Count.EqualTo(1));
        client.VerifyAll();
        handler.VerifyAll();
    }

    [Test]
    public async Task Update_OnDemand_WithUnexpectedOtherTestBuild()
    {
        var testJobName = new JobName("CUSTOM-test");
        var otherTestJobName = new JobName("CUSTOM-other-test");

        using var mocks = StoreMocks.New()
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithNewRootBuilds(_onDemandRootJob)
            .WithNewTestBuilds(testJobName)
            .WithNewTestBuilds(otherTestJobName);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_onDemandRootJob);
        var rootBuilds = RandomBuilds.Generate(1).ToArray();
        var rootBuild = rootBuilds[0];
        var triggered = new BuildReference[] {
            new(testJobName, RandomData.NextBuildNumber),
        };
        onDemandBuilds.TryAdd(new RootBuild(
            _onDemandRootJob,
            rootBuild.Id,
            rootBuild.Number,
            rootBuild.TimestampUtc,
            rootBuild.TimestampUtc.AddMilliseconds(rootBuild.DurationInMs),
            rootBuild.Result == BuildResult.Success,
            rootBuild.GetCommits(),
            triggered
        ));
        onDemandBuilds.TryAdd(new TestBuild(
            testJobName,
            Guid.NewGuid().ToString(),
            triggered[0].BuildNumber,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5),
            true,
            new BuildReference(_onDemandRootJob, rootBuild.Number),
            []
        ));
        onDemandBuilds.TryAdd(new TestBuild(
            otherTestJobName,
            Guid.NewGuid().ToString(),
            RandomData.NextBuildNumber,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5),
            true,
            new BuildReference(_onDemandRootJob, RandomData.NextBuildNumber),
            []
        ));
        var testBuildReference = triggered[0];
        var testBuilds = RandomBuilds.Generate(1, [testBuildReference.BuildNumber]).ToArray();
        var testBuild = testBuilds[0];
        var failedTests = RandomFailedTests.Generate(2).ToArray();
        var client = new Mock<IJenkinsClient>(MockBehavior.Strict);
        client.Setup(x => x.GetLastBuilds(_onDemandRootJob, 100)).ReturnsAsync([]);
        client.Setup(x => x.GetLastBuilds(testJobName, 100)).ReturnsAsync(testBuilds);
        client.Setup(x => x.GetLastBuilds(otherTestJobName, 100)).ReturnsAsync([]);
        var handler = new Mock<IPostBuildHandler>(MockBehavior.Strict);
        var synchronizer = new JenkinsSynchronizer(client.Object, handler.Object);
        var workspace = NewWorkspace(onDemandBuilds);
        await synchronizer.Update(workspace).ConfigureAwait(false);
        var testCollection = onDemandBuilds.TestBuilds.Single(x => x.JobName == testJobName);
        Assert.That(testCollection, Has.Count.EqualTo(1));
        client.VerifyAll();
        handler.VerifyAll();
    }
}
