using NUnit.Framework;
using System.Text.Json;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class BuildCollectionTests
{
    [Test]
    public void Constructor_WithJobName_CreatesEmptyCollection()
    {
        var jobName = new JobName("TestJob");
        var collection = new BuildCollection<RootBuild>(jobName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(collection.JobName, Is.EqualTo(jobName));
            Assert.That(collection.Count, Is.Zero);
            Assert.That(collection, Is.Empty);
        }
    }

    [Test]
    public void Constructor_WithBuilds_AddsAllBuilds()
    {
        var jobName = new JobName("TestJob");
        var builds = new[] {
            RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 1),
            RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 2)
        };

        var collection = new BuildCollection<RootBuild>(jobName, builds);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(collection.Count, Is.EqualTo(2));
            Assert.That(collection, Is.EquivalentTo(builds));
        }
    }

    [Test]
    public void Constructor_WithDuplicateBuilds_ThrowsArgumentException()
    {
        var jobName = new JobName("TestJob");
        var buildNumber = RandomData.NextBuildNumber;
        var builds = new[] {
            RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: buildNumber),
            RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: buildNumber)
        };

        Assert.That(() => new BuildCollection<RootBuild>(jobName, builds),
            Throws.ArgumentException.With.Message.EqualTo("Duplicate builds in the initial collection. (Parameter 'builds')"));
    }

    [Test]
    public void TryAdd_WithValidBuild_ReturnsTrue()
    {
        var jobName = new JobName("TestJob");
        var collection = new BuildCollection<RootBuild>(jobName);
        var build = RandomData.NextRootBuild(jobName: jobName.Value);

        var result = collection.TryAdd(build);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(collection.Count, Is.EqualTo(1));
            Assert.That(collection.Contains(build.BuildNumber), Is.True);
            Assert.That(collection[0], Is.EqualTo(build));
        }
    }

    [Test]
    public void TryAdd_WithDuplicateBuildNumber_ReturnsFalse()
    {
        var jobName = new JobName("TestJob");
        var collection = new BuildCollection<RootBuild>(jobName);
        var buildNumber = RandomData.NextBuildNumber;
        var build1 = RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: buildNumber);
        var build2 = RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: buildNumber);

        collection.TryAdd(build1);
        var result = collection.TryAdd(build2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(collection.Count, Is.EqualTo(1));
            Assert.That(collection[0], Is.EqualTo(build1));
        }
    }

    [Test]
    public void TryAdd_WithDifferentJobName_ThrowsArgumentException()
    {
        var jobName = new JobName("TestJob");
        var collection = new BuildCollection<RootBuild>(jobName);
        var build = RandomData.NextRootBuild(jobName: "OtherJob");

        Assert.That(() => collection.TryAdd(build),
            Throws.ArgumentException.With.Message.EqualTo($"Build job name '{build.JobName}' does not match collection job name '{jobName}'. (Parameter 'build')"));
    }

    [Test]
    public void TryAdd_WithDecreasingBuildNumber_ThrowsInvalidOperationException()
    {
        var jobName = new JobName("TestJob");
        var collection = new BuildCollection<RootBuild>(jobName);
        var build1 = RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 2);
        var build2 = RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 1);

        collection.TryAdd(build1);

        Assert.That(() => collection.TryAdd(build2),
            Throws.InvalidOperationException.With.Message.EqualTo("Builds must be added in ascending order by build number."));
    }

    [Test]
    public void Serialization_Roundtrip_PreservesAllData()
    {
        var jobName = new JobName("TestJob");
        var builds = new[] {
            RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 1),
            RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 2)
        };
        var original = new BuildCollection<RootBuild>(jobName, builds);

        var serializable = new BuildCollection<RootBuild>.Serializable(original);
        var json = JsonSerializer.Serialize(serializable);
        var deserialized = JsonSerializer.Deserialize<BuildCollection<RootBuild>.Serializable>(json);
        var roundtrip = deserialized!.ToBuildCollection();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(roundtrip.JobName, Is.EqualTo(original.JobName));
            Assert.That(roundtrip.Count, Is.EqualTo(original.Count));
            for (var i = 0; i < original.Count; i++)
            {
                Assert.That(roundtrip[i].BuildNumber, Is.EqualTo(original[i].BuildNumber));
                Assert.That(roundtrip[i].JobName, Is.EqualTo(original[i].JobName));
                Assert.That(roundtrip[i].Id, Is.EqualTo(original[i].Id));
            }
        }
    }

    [Test]
    public void Indexer_WithBadJobName_ThrowsArgumentException()
    {
        var collection = new BuildCollection<RootBuild>(new JobName("TestJob"));
        var build = new BuildReference("OtherJob", RandomData.NextBuildNumber);
        Assert.That(() => collection[build],
            Throws.ArgumentException.With.Message.EqualTo($"Build job name 'OtherJob' does not match collection job name 'TestJob'. (Parameter 'buildReference')"));
    }
}
