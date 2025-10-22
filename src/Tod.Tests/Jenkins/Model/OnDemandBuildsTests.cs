using NUnit.Framework;
using System.Diagnostics;
using System.Text.Json;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class OnDemandBuildsTests
{
    [Test]
    public void TryAdd_RootBuildTwice_OnlyFirstTime()
    {
        using (Assert.EnterMultipleScope())
        {
            var jobName = "MyJob";
            var onDemandBuilds = new OnDemandBuilds(new(jobName));
            var testJobName1 = "MyTestJob1";
            var testJobName2 = "MyTestJob2";
            var rootBuild = RandomData.NextRootBuild(jobName: jobName, testJobNames: [testJobName1, testJobName2]);
            Assert.That(onDemandBuilds.RootBuilds, Has.Count.EqualTo(1));
            Assert.That(onDemandBuilds.RootBuilds[0], Has.Count.EqualTo(0));
            Assert.That(onDemandBuilds.TestBuilds, Has.Count.EqualTo(0));
            var added = onDemandBuilds.TryAdd(rootBuild);
            Assert.That(added, Is.True);
            Assert.That(onDemandBuilds.RootBuilds, Has.Count.EqualTo(1));
            Assert.That(onDemandBuilds.RootBuilds[0].JobName.Value, Is.EqualTo(jobName));
            Assert.That(onDemandBuilds.RootBuilds[0], Has.Count.EqualTo(1));
            Assert.That(onDemandBuilds.RootBuilds[0].Contains(rootBuild.BuildNumber), Is.True);
            Assert.That(onDemandBuilds.TestBuilds, Has.Count.EqualTo(2));
            Assert.That(onDemandBuilds.TestBuilds[0].JobName.Value, Is.EqualTo(testJobName1));
            Assert.That(onDemandBuilds.TestBuilds[0], Has.Count.EqualTo(0));
            Assert.That(onDemandBuilds.TestBuilds[1].JobName.Value, Is.EqualTo(testJobName2));
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
            var jobName = "MyJob";
            var onDemandBuilds = new OnDemandBuilds(new(jobName));
            var rootBuild = RandomData.NextRootBuild(jobName: jobName);
            var added = onDemandBuilds.TryAdd(rootBuild);
            Assert.That(added, Is.True);
            var json = JsonSerializer.Serialize(new OnDemandBuilds.Serializable(onDemandBuilds));
            var reloaded = JsonSerializer.Deserialize<OnDemandBuilds.Serializable>(json);
            Assert.That(reloaded, Is.Not.Null);
            Debug.Assert(reloaded is not null);
            var clone = reloaded.FromSerializable();
            Assert.That(clone.RootBuilds, Has.Count.EqualTo(onDemandBuilds.RootBuilds.Count));
        }
    }

    [Test]
    public void TryAdd_TestBuildTwice_OnlyFirstTime()
    {
        var onDemandBuilds = new OnDemandBuilds(new("MyJob"));
        var testBuild = RandomData.NextTestBuild(testJobName: "MyTestJob");
        var added = onDemandBuilds.TryAdd(testBuild);
        Assert.That(added, Is.True);
        added = onDemandBuilds.TryAdd(testBuild);
        Assert.That(added, Is.False);
    }

    [Test]
    public void TryFind_TestBuildWithoutBuilds_ReturnNone()
    {
        var onDemandBuilds = new OnDemandBuilds(new("MyJob"));
        Assert.That(onDemandBuilds.TryFindTestBuild(new("MyTestJob"), new BuildReference("MyJob", 42), out var foundTestBuild), Is.False);
        Assert.That(foundTestBuild, Is.Null);
    }

    [Test]
    public void TryFind_TestBuildIgnoreOldBuilds()
    {
        using (Assert.EnterMultipleScope())
        {
            var rootJobName = "MyJob";
            var onDemandBuilds = new OnDemandBuilds(new(rootJobName));
            var rootBuild = new BuildReference(rootJobName, RandomData.NextBuildNumber);
            var testBuildNumber = RandomData.NextBuildNumber;
            Assert.That(onDemandBuilds.TryAdd(RandomData.NextTestBuild(buildNumber: testBuildNumber, rootBuild: rootBuild)), Is.True);
            rootBuild = rootBuild.Next();
            Assert.That(onDemandBuilds.TryFindTestBuild(new("MyTestJob"), rootBuild, out var foundTestBuild), Is.False);
            Assert.That(foundTestBuild, Is.Null);
            testBuildNumber++;
            var testBuild = RandomData.NextTestBuild(buildNumber: testBuildNumber, rootBuild: rootBuild);
            Assert.That(onDemandBuilds.TryAdd(testBuild), Is.True);
            Assert.That(onDemandBuilds.TryFindTestBuild(new("MyTestJob"), rootBuild, out foundTestBuild), Is.True);
            Debug.Assert(foundTestBuild is not null);
            Assert.That(foundTestBuild.Reference, Is.EqualTo(testBuild.Reference));
        }
    }

    [Test]
    public void TryFindTstBuild_WithNewerRootBuild_ReturnsNone()
    {
        using (Assert.EnterMultipleScope())
        {
            var rootJobName = "MyJob";
            var onDemandBuilds = new OnDemandBuilds(new(rootJobName));
            var buildNumber = RandomData.NextBuildNumber;
            var rootBuild = new BuildReference(rootJobName, buildNumber);
            var testBuild = RandomData.NextTestBuild(rootBuild: rootBuild.Next());
            Assert.That(onDemandBuilds.TryAdd(testBuild), Is.True);
            Assert.That(onDemandBuilds.TryFindTestBuild(new("MyTestJob"), rootBuild, out var foundTestBuild), Is.False);
            Debug.Assert(foundTestBuild is null);
        }
    }

    [Test]
    public void TryFindTestBuild_WithOlderAndNewerRootBuilds_ReturnsNone()
    {
        using (Assert.EnterMultipleScope())
        {
            var rootJobName = "MyJob";
            var onDemandBuilds = new OnDemandBuilds(new(rootJobName));
            var buildNumber = RandomData.NextBuildNumber;
            var rootBuild = new BuildReference(rootJobName, buildNumber);
            var oldTestBuild = RandomData.NextTestBuild(rootBuild: new BuildReference(rootBuild.JobName, rootBuild.BuildNumber - 1));
            Assert.That(onDemandBuilds.TryAdd(oldTestBuild), Is.True);
            var newTtestBuild = RandomData.NextTestBuild(buildNumber: oldTestBuild.BuildNumber + 1, rootBuild: rootBuild.Next());
            Assert.That(onDemandBuilds.TryAdd(newTtestBuild), Is.True);
            Assert.That(onDemandBuilds.TryFindTestBuild(new("MyTestJob"), rootBuild, out var foundTestBuild), Is.False);
            Debug.Assert(foundTestBuild is null);
        }
    }

    [Test]
    public void TryFind_TestBuildIgnoreOtherRootBuilds_ReturnsNone()
    {
        using (Assert.EnterMultipleScope())
        {
            var oldRootJobName = "OldJob";
            var rootJobName = "MyJob";
            var onDemandBuilds = new OnDemandBuilds(new(rootJobName));
            var buildNumber = RandomData.NextBuildNumber;
            var rootBuild = new BuildReference(rootJobName, buildNumber);
            var oldTestBuild = RandomData.NextTestBuild(rootBuild: new BuildReference(oldRootJobName, RandomData.NextBuildNumber));
            Assert.That(onDemandBuilds.TryAdd(oldTestBuild), Is.True);
            var newTtestBuild = RandomData.NextTestBuild(buildNumber: oldTestBuild.BuildNumber + 1, rootBuild: rootBuild.Next());
            Assert.That(onDemandBuilds.TryAdd(newTtestBuild), Is.True);
            Assert.That(onDemandBuilds.TryFindTestBuild(new("MyTestJob"), rootBuild, out var foundTestBuild), Is.False);
            Debug.Assert(foundTestBuild is null);
        }
    }

    [Test]
    public void Serialization_Works()
    {
        using (Assert.EnterMultipleScope())
        {
            var jobName = "MyJob";
            var onDemandBuilds = new OnDemandBuilds(new(jobName));
            var rootBuild = RandomData.NextRootBuild(jobName: jobName);
            onDemandBuilds.TryAdd(rootBuild);
            var testBuild = RandomData.NextTestBuild(testJobName: "MyTestJob", rootBuild: rootBuild.Reference);
            onDemandBuilds.TryAdd(testBuild);
            var json = JsonSerializer.Serialize(new OnDemandBuilds.Serializable(onDemandBuilds));
            var reloaded = JsonSerializer.Deserialize<OnDemandBuilds.Serializable>(json);
            Assert.That(reloaded, Is.Not.Null);
            Debug.Assert(reloaded is not null);
            var clone = reloaded.FromSerializable();
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
            var jobName = "MyJob";
            var onDemandBuilds = new OnDemandBuilds(new(jobName));
            var rootBuild = RandomData.NextRootBuild(jobName: jobName);
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
            var jobName = "MyJob";
            var onDemandBuilds = new OnDemandBuilds(new(jobName));
            var rootBuild = RandomData.NextRootBuild(jobName: jobName);
            onDemandBuilds.TryAdd(rootBuild);
            var nonExistingBuild = new BuildReference(jobName, rootBuild.BuildNumber + 1);
            Assert.That(onDemandBuilds.TryGetRootBuild(nonExistingBuild, out var foundBuild), Is.False);
            Assert.That(foundBuild, Is.Null);
        }
    }
}
