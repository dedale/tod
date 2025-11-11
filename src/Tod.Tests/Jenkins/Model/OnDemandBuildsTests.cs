using NUnit.Framework;
using System.Diagnostics;
using Tod.Jenkins;
using Tod.Tests.IO;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class OnDemandBuildsTests
{
    private readonly JobName _rootJob = new("MyJob");
    private readonly JobName _testJob1 = new("MyTestJob1");
    private readonly JobName _testJob2 = new("MyTestJob2");

    [Test]
    public void TryAdd_RootBuildTwice_OnlyFirstTime()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithOnDemandStore(_rootJob, out var onDemandStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var onDemandBuilds = new OnDemandBuilds(onDemandStore);
            onDemandBuilds.TryAddRoot(_rootJob);
            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, testJobNames: [_testJob1.Value, _testJob2.Value]);

            Assert.That(onDemandBuilds.RootBuilds, Has.Count.EqualTo(1));
            Assert.That(onDemandBuilds.RootBuilds[0], Has.Count.EqualTo(0));
            Assert.That(onDemandBuilds.TestBuilds, Has.Count.EqualTo(0));

            var added = onDemandBuilds.TryAdd(rootBuild);
            Assert.That(added, Is.True);
            Assert.That(onDemandBuilds.RootBuilds, Has.Count.EqualTo(1));
            Assert.That(onDemandBuilds.RootBuilds[0].JobName.Value, Is.EqualTo(_rootJob.Value));
            Assert.That(onDemandBuilds.RootBuilds[0], Has.Count.EqualTo(1));
            Assert.That(onDemandBuilds.RootBuilds[0].Contains(rootBuild.BuildNumber), Is.True);
            Assert.That(onDemandBuilds.TestBuilds, Has.Count.EqualTo(2));
            Assert.That(onDemandBuilds.TestBuilds[0].JobName.Value, Is.EqualTo(_testJob1.Value));
            Assert.That(onDemandBuilds.TestBuilds[0], Has.Count.EqualTo(0));
            Assert.That(onDemandBuilds.TestBuilds[1].JobName.Value, Is.EqualTo(_testJob2.Value));
            Assert.That(onDemandBuilds.TestBuilds[1], Has.Count.EqualTo(0));
            added = onDemandBuilds.TryAdd(rootBuild);
            Assert.That(added, Is.False);
        }
    }

    [Test]
    public void Serialization_WithRootBuildsOnly_Works()
    {
        using (Assert.EnterMultipleScope())
        {
            using var temp = new TempDirectory();
            var onDemandStore = new OnDemandStore(temp.Path);
            var jobName = "MyJob";
            var onDemandBuilds = new OnDemandBuilds(onDemandStore);
            onDemandBuilds.TryAddRoot(new(jobName));
            var rootBuild = RandomData.NextRootBuild(jobName: jobName);
            var added = onDemandBuilds.TryAdd(rootBuild);
            Assert.That(added, Is.True);
            var clone = new OnDemandBuilds(onDemandStore);
            Assert.That(clone.RootBuilds, Has.Count.EqualTo(onDemandBuilds.RootBuilds.Count));
        }
    }

    [Test]
    public void TryAdd_TestBuildTwice_OnlyFirstTime()
    {
        var testJobName = new JobName("MyTestJob");

        using var mocks = StoreMocks.New()
            .WithOnDemandStore(_rootJob, out var onDemandStore)
            .WithNewTestBuilds(testJobName);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_rootJob);

        var testBuild = RandomData.NextTestBuild(testJobName: testJobName.Value);
        var added = onDemandBuilds.TryAdd(testBuild);
        Assert.That(added, Is.True);
        added = onDemandBuilds.TryAdd(testBuild);
        Assert.That(added, Is.False);
    }

    [Test]
    public void TryFind_TestBuildWithoutBuilds_ReturnNone()
    {
        using var mocks = StoreMocks.New()
            .WithOnDemandStore(_rootJob, out var onDemandStore);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_rootJob);
        Assert.That(onDemandBuilds.TryFindTestBuild(new("MyTestJob"), new BuildReference(_rootJob, 42), out var foundTestBuild), Is.False);
        Assert.That(foundTestBuild, Is.Null);
    }

    [Test]
    public void TryFind_TestBuildIgnoreOldBuilds()
    {
        using (Assert.EnterMultipleScope())
        {
            var testJobName = new JobName("MyTestJob");

            using var mocks = StoreMocks.New()
                .WithOnDemandStore(_rootJob, out var onDemandStore)
                .WithNewTestBuilds(testJobName);

            var onDemandBuilds = new OnDemandBuilds(onDemandStore);
            onDemandBuilds.TryAddRoot(_rootJob);
            var rootBuild = new BuildReference(_rootJob, RandomData.NextBuildNumber);
            var testBuildNumber = RandomData.NextBuildNumber;
            var testBuild = RandomData.NextTestBuild(buildNumber: testBuildNumber, rootBuild: rootBuild);
            Assert.That(onDemandBuilds.TryAdd(testBuild), Is.True);
            rootBuild = rootBuild.Next();
            Assert.That(onDemandBuilds.TryFindTestBuild(testJobName, rootBuild, out var foundTestBuild), Is.False);
            Assert.That(foundTestBuild, Is.Null);
            testBuildNumber++;
            testBuild = RandomData.NextTestBuild(buildNumber: testBuildNumber, rootBuild: rootBuild);
            Assert.That(onDemandBuilds.TryAdd(testBuild), Is.True);
            Assert.That(onDemandBuilds.TryFindTestBuild(testJobName, rootBuild, out foundTestBuild), Is.True);
            Debug.Assert(foundTestBuild is not null);
            Assert.That(foundTestBuild.Reference, Is.EqualTo(testBuild.Reference));
        }
    }

    [Test]
    public void TryFindTestBuild_WithNewerRootBuild_ReturnsNone()
    {
        using (Assert.EnterMultipleScope())
        {
            var testJobName = new JobName("MyTestJob");

            using var mocks = StoreMocks.New()
                .WithOnDemandStore(_rootJob, out var onDemandStore)
                .WithNewTestBuilds(testJobName);

            var onDemandBuilds = new OnDemandBuilds(onDemandStore);
            onDemandBuilds.TryAddRoot(_rootJob);
            var buildNumber = RandomData.NextBuildNumber;
            var rootBuild = new BuildReference(_rootJob, buildNumber);
            var testBuild = RandomData.NextTestBuild(rootBuild: rootBuild.Next());
            Assert.That(onDemandBuilds.TryAdd(testBuild), Is.True);
            Assert.That(onDemandBuilds.TryFindTestBuild(testJobName, rootBuild, out var foundTestBuild), Is.False);
            Debug.Assert(foundTestBuild is null);
        }
    }

    [Test]
    public void TryFindTestBuild_WithOlderAndNewerRootBuilds_ReturnsNone()
    {
        using (Assert.EnterMultipleScope())
        {
            var testJobName = new JobName("MyTestJob");

            using var mocks = StoreMocks.New()
                .WithOnDemandStore(_rootJob, out var onDemandStore)
                .WithNewTestBuilds(testJobName);

            var onDemandBuilds = new OnDemandBuilds(onDemandStore);
            onDemandBuilds.TryAddRoot(_rootJob);
            var buildNumber = RandomData.NextBuildNumber;
            var rootBuild = new BuildReference(_rootJob, buildNumber);
            var oldTestBuild = RandomData.NextTestBuild(rootBuild: new BuildReference(rootBuild.JobName, rootBuild.BuildNumber - 1));
            Assert.That(onDemandBuilds.TryAdd(oldTestBuild), Is.True);
            var newTtestBuild = RandomData.NextTestBuild(buildNumber: oldTestBuild.BuildNumber + 1, rootBuild: rootBuild.Next());
            Assert.That(onDemandBuilds.TryAdd(newTtestBuild), Is.True);
            Assert.That(onDemandBuilds.TryFindTestBuild(testJobName, rootBuild, out var foundTestBuild), Is.False);
            Debug.Assert(foundTestBuild is null);
        }
    }

    [Test]
    public void TryFind_TestBuildIgnoreOtherRootBuilds_ReturnsNone()
    {
        using (Assert.EnterMultipleScope())
        {
            var oldRootJob = new JobName("OldJob");
            var testJobName = new JobName("MyTestJob");

            using var mocks = StoreMocks.New()
                .WithOnDemandStore(_rootJob, out var onDemandStore)
                .WithNewTestBuilds(testJobName);

            var onDemandBuilds = new OnDemandBuilds(onDemandStore);
            onDemandBuilds.TryAddRoot(_rootJob);
            var buildNumber = RandomData.NextBuildNumber;
            var rootBuild = new BuildReference(_rootJob, buildNumber);
            var oldTestBuild = RandomData.NextTestBuild(rootBuild: new BuildReference(oldRootJob, RandomData.NextBuildNumber));
            Assert.That(onDemandBuilds.TryAdd(oldTestBuild), Is.True);
            var newTtestBuild = RandomData.NextTestBuild(buildNumber: oldTestBuild.BuildNumber + 1, rootBuild: rootBuild.Next());
            Assert.That(onDemandBuilds.TryAdd(newTtestBuild), Is.True);
            Assert.That(onDemandBuilds.TryFindTestBuild(testJobName, rootBuild, out var foundTestBuild), Is.False);
            Debug.Assert(foundTestBuild is null);
        }
    }

    [Test]
    public void Serialization_Works()
    {
        using (Assert.EnterMultipleScope())
        {
            using var temp = new TempDirectory();
            var onDemandStore = new OnDemandStore(temp.Path);
            var jobName = "MyJob";
            var onDemandBuilds = new OnDemandBuilds(onDemandStore);
            onDemandBuilds.TryAddRoot(new(jobName));
            var rootBuild = RandomData.NextRootBuild(jobName: jobName);
            onDemandBuilds.TryAdd(rootBuild);
            var testBuild = RandomData.NextTestBuild(testJobName: "MyTestJob", rootBuild: rootBuild.Reference);
            onDemandBuilds.TryAdd(testBuild);
            var clone = new OnDemandBuilds(new OnDemandStore(temp.Path));
            Assert.That(clone.RootBuilds, Has.Count.EqualTo(onDemandBuilds.RootBuilds.Count));
            Assert.That(clone.TestBuilds, Has.Count.EqualTo(onDemandBuilds.TestBuilds.Count));
            Assert.That(clone.TestBuilds[0], Has.Count.EqualTo(onDemandBuilds.TestBuilds[0].Count));
        }
    }

    [Test]
    public void TryGetRootBuild_WithExistingBuild_ReturnsTrue()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithOnDemandStore(_rootJob, out var onDemandStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var onDemandBuilds = new OnDemandBuilds(onDemandStore);
            onDemandBuilds.TryAddRoot(_rootJob);
            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value);
            onDemandBuilds.TryAdd(rootBuild);
            Assert.That(onDemandBuilds.TryGetRootBuild(rootBuild.Reference, out var foundBuild), Is.True);
            Debug.Assert(foundBuild is not null);
            Assert.That(foundBuild.Reference, Is.EqualTo(rootBuild.Reference));
        }
    }

    [Test]
    public void TryGetRootBuild_WithNonExistingBuild_ReturnsFalse()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithOnDemandStore(_rootJob, out var onDemandStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var onDemandBuilds = new OnDemandBuilds(onDemandStore);
            onDemandBuilds.TryAddRoot(_rootJob);
            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value);
            onDemandBuilds.TryAdd(rootBuild);
            var nonExistingBuild = new BuildReference(_rootJob, rootBuild.BuildNumber + 1);
            Assert.That(onDemandBuilds.TryGetRootBuild(nonExistingBuild, out var foundBuild), Is.False);
            Assert.That(foundBuild, Is.Null);
        }
    }
}
