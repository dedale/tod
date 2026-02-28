using Moq;
using NUnit.Framework;
using Tod.Jenkins;
using Tod.Tests.IO;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class BuildCollectionTests
{
    private static readonly BuildBranch s_mainBuildBranch = BuildBranch.Create(new BranchName("main"));
    private Mock<IByJobNameStore> _store;

    [SetUp]
    public void SetUp()
    {
        _store = new Mock<IByJobNameStore>(MockBehavior.Strict);
    }

    [TearDown]
    public void TearDown()
    {
        _store.VerifyAll();
    }

    private void StoreSetupLoad(JobName jobName)
    {
        _store.Setup(s => s.BuildBranch).Returns(s_mainBuildBranch);
        _store.Setup(s => s.Load(jobName, It.IsAny<Func<BuildCollection<RootBuild>.InnerCollection.Serializable>>()))
            .Returns((JobName j, Func<BuildCollection<RootBuild>.InnerCollection.Serializable> f) => f());
    }

    [Test]
    public void Constructor_WithJobName_CreatesEmptyCollection()
    {
        var jobName = new JobName("MyJob");
        StoreSetupLoad(jobName);
        var collection = new BuildCollection<RootBuild>(jobName, _store.Object);

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
        using var temp = new TempDirectory();
        var jsonPath = Path.Combine(temp.Path, "Builds.json");
        var storeFactory = new ByJobNameStoreFactory(s_mainBuildBranch, jsonPath);

        var jobName = new JobName("TestJob");
        var builds = new[] {
            RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 1),
            RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 2)
        };

        var store = storeFactory.New();
        store.Add(jobName);
        var collection = new BuildCollection<RootBuild>(jobName, store);
        collection.TryAdd(builds[0]);
        collection.TryAdd(builds[1]);

        store = storeFactory.New();
        collection = new BuildCollection<RootBuild>(jobName, store);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(collection.Count, Is.EqualTo(2));
            Assert.That(collection.Select(b => b.Reference), Is.EquivalentTo(builds.Select(b => b.Reference)));
        }
    }

    [Test]
    public void TryAdd_WithValidBuild_ReturnsTrue()
    {
        var jobName = new JobName("TestJob");
        StoreSetupLoad(jobName);
        _store.Setup(s => s.Save(jobName, It.IsAny<BuildCollection<RootBuild>.InnerCollection.Serializable>()));
        var collection = new BuildCollection<RootBuild>(jobName, _store.Object);
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
        StoreSetupLoad(jobName);
        _store.Setup(s => s.Save(jobName, It.IsAny<BuildCollection<RootBuild>.InnerCollection.Serializable>()));
        var collection = new BuildCollection<RootBuild>(jobName, _store.Object);
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
        StoreSetupLoad(jobName);
        var collection = new BuildCollection<RootBuild>(jobName, _store.Object);
        var build = RandomData.NextRootBuild(jobName: "OtherJob");

        Assert.That(() => collection.TryAdd(build),
            Throws.ArgumentException.With.Message.EqualTo($"Build job name '{build.JobName}' does not match collection job name '{jobName}'. (Parameter 'build')"));
    }

    [Test]
    public void TryAdd_WithDecreasingBuildNumber_ThrowsInvalidOperationException()
    {
        var jobName = new JobName("TestJob");
        StoreSetupLoad(jobName);
        _store.Setup(s => s.Save(jobName, It.IsAny<BuildCollection<RootBuild>.InnerCollection.Serializable>()));
        var collection = new BuildCollection<RootBuild>(jobName, _store.Object);
        var build1 = RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 2);
        var build2 = RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 1);

        collection.TryAdd(build1);

        Assert.That(() => collection.TryAdd(build2),
            Throws.InvalidOperationException.With.Message.EqualTo("Builds must be added in ascending order by build number."));
    }

    [Test]
    public void Serialization_Roundtrip_PreservesAllData()
    {
        using var temp = new TempDirectory();
        var jsonPath = Path.Combine(temp.Path, "Builds.json");
        var storeFactory = new ByJobNameStoreFactory(s_mainBuildBranch, jsonPath);

        var jobName = new JobName("TestJob");
        var builds = new[] {
            RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 1),
            RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 2)
        };
        var store = storeFactory.New();
        store.Add(jobName);
        var original = new BuildCollection<RootBuild>(jobName, store);
        original.TryAdd(builds[0]);
        original.TryAdd(builds[1]);

        store = storeFactory.New();
        var roundtrip = new BuildCollection<RootBuild>(jobName, store);

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
        var jobName = new JobName("TestJob");
        StoreSetupLoad(jobName);
        var collection = new BuildCollection<RootBuild>(jobName, _store.Object);
        var build = new BuildReference("OtherJob", RandomData.NextBuildNumber);
        Assert.That(() => collection[build],
            Throws.ArgumentException.With.Message.EqualTo($"Build job name 'OtherJob' does not match collection job name 'TestJob'. (Parameter 'buildReference')"));
    }

    [Test]
    public void AverageDuration_WithEmptyCollection_ReturnsZero()
    {
        var jobName = new JobName("TestJob");
        StoreSetupLoad(jobName);
        var collection = new BuildCollection<RootBuild>(jobName, _store.Object);
        Assert.That(collection.AverageDuration, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void AverageDuration_WithSingleBuild_ReturnsBuildDuration()
    {
        using var temp = new TempDirectory();
        var jsonPath = Path.Combine(temp.Path, "Builds.json");
        var storeFactory = new ByJobNameStoreFactory(s_mainBuildBranch, jsonPath);

        var jobName = new JobName("TestJob");
        var store = storeFactory.New();
        store.Add(jobName);

        var startTime = DateTime.UtcNow.AddHours(-2);
        var endTime = startTime.AddMinutes(30);
        var expectedDuration = endTime - startTime;

        var build = new RootBuild(
            jobName,
            "build-id-1",
            RandomData.NextBuildNumber,
            startTime,
            endTime,
            true,
            [RandomData.NextSha1()],
            []
        );

        var collection = new BuildCollection<RootBuild>(jobName, store);
        collection.TryAdd(build);

        Assert.That(collection.AverageDuration, Is.EqualTo(expectedDuration));
    }

    [Test]
    public void AverageDuration_WithMultipleBuilds_ReturnsAverageDuration()
    {
        using var temp = new TempDirectory();
        var jsonPath = Path.Combine(temp.Path, "Builds.json");
        var storeFactory = new ByJobNameStoreFactory(s_mainBuildBranch, jsonPath);

        var jobName = new JobName("TestJob");
        var store = storeFactory.New();
        store.Add(jobName);

        var buildNumber = RandomData.NextBuildNumber;
        var baseTime = DateTime.UtcNow.AddHours(-5);
        var builds = new[]
        {
            RandomData.NextRootBuild(buildNumber: ++buildNumber, startUtc: baseTime, endUtc: baseTime.AddMinutes(20), jobName: jobName.Value),
            RandomData.NextRootBuild(buildNumber: ++buildNumber, startUtc: baseTime.AddHours(1), endUtc: baseTime.AddHours(1).AddMinutes(40), jobName: jobName.Value),
            RandomData.NextRootBuild(buildNumber: ++buildNumber, startUtc: baseTime.AddHours(2), endUtc: baseTime.AddHours(2).AddMinutes(30), jobName: jobName.Value),
        };

        var collection = new BuildCollection<RootBuild>(jobName, store);
        foreach (var build in builds)
        {
            collection.TryAdd(build);
        }

        // Average: (20 + 40 + 30) / 3 = 30 minutes
        Assert.That(collection.AverageDuration, Is.EqualTo(TimeSpan.FromMinutes(30)));
    }

    [Test]
    public void AverageDuration_IsCached_AfterFirstAccess()
    {
        using var temp = new TempDirectory();
        var jsonPath = Path.Combine(temp.Path, "Builds.json");
        var storeFactory = new ByJobNameStoreFactory(s_mainBuildBranch, jsonPath);

        var jobName = new JobName("TestJob");
        var store = storeFactory.New();
        store.Add(jobName);

        var startTime = DateTime.UtcNow.AddHours(-2);
        var build = RandomData.NextRootBuild(startUtc: startTime, endUtc: startTime.AddMinutes(25), jobName: jobName.Value);

        var collection = new BuildCollection<RootBuild>(jobName, store);
        collection.TryAdd(build);

        var firstAccess = collection.AverageDuration;
        Assert.That(firstAccess, Is.EqualTo(TimeSpan.FromMinutes(25)));
        var secondAccess = collection.AverageDuration;
        Assert.That(secondAccess, Is.EqualTo(firstAccess));
    }

    [Test]
    public void AverageDuration_WithTestBuilds_ReturnsAverageDuration()
    {
        using var temp = new TempDirectory();
        var jsonPath = Path.Combine(temp.Path, "Builds.json");
        var storeFactory = new ByJobNameStoreFactory(s_mainBuildBranch, jsonPath);

        var jobName = new JobName("TestJob");
        var store = storeFactory.New();
        store.Add(jobName);

        var buildNumber = RandomData.NextBuildNumber;
        var baseTime = DateTime.UtcNow.AddHours(-3);
        var builds = new[]
        {
            RandomData.NextTestBuild(buildNumber: ++buildNumber, startUtc: baseTime, endUtc: baseTime.AddMinutes(15), testJobName: jobName.Value),
            RandomData.NextTestBuild(buildNumber: ++buildNumber, startUtc: baseTime.AddHours(1), endUtc: baseTime.AddHours(1).AddMinutes(25), testJobName: jobName.Value),
        };

        var collection = new BuildCollection<TestBuild>(jobName, store);
        foreach (var build in builds)
        {
            collection.TryAdd(build);
        }

        // Average: (15 + 25) / 2 = 20 minutes
        Assert.That(collection.AverageDuration, Is.EqualTo(TimeSpan.FromMinutes(20)));
    }

    [Test]
    public void AverageDuration_WithVaryingDurations_CalculatesCorrectly()
    {
        using var temp = new TempDirectory();
        var jsonPath = Path.Combine(temp.Path, "Builds.json");
        var storeFactory = new ByJobNameStoreFactory(s_mainBuildBranch, jsonPath);

        var jobName = new JobName("TestJob");
        var store = storeFactory.New();
        store.Add(jobName);

        var buildNumber = RandomData.NextBuildNumber;
        var baseTime = DateTime.UtcNow.AddDays(-1);
        var builds = new[]
        {
            RandomData.NextRootBuild(buildNumber: ++buildNumber, startUtc: baseTime, endUtc: baseTime.AddSeconds(90), jobName: jobName.Value),
            RandomData.NextRootBuild(buildNumber: ++buildNumber, startUtc: baseTime.AddHours(1), endUtc: baseTime.AddHours(1).AddHours(2).AddMinutes(30), jobName: jobName.Value),
            RandomData.NextRootBuild(buildNumber: ++buildNumber, startUtc: baseTime.AddHours(4), endUtc: baseTime.AddHours(4).AddMinutes(45), jobName: jobName.Value),
        };

        var collection = new BuildCollection<RootBuild>(jobName, store);
        foreach (var build in builds)
        {
            collection.TryAdd(build);
        }

        // Average: (1.5 + 150 + 45) / 3 = 65.5 minutes
        Assert.That(collection.AverageDuration, Is.EqualTo(TimeSpan.FromMinutes(65.5)));
    }

    [Test]
    public void RemoveBuildsOlderThan_WithNoOldBuilds_ReturnsZero()
    {
        var jobName = new JobName("TestJob");
        StoreSetupLoad(jobName);
        _store.Setup(s => s.Save(jobName, It.IsAny<BuildCollection<RootBuild>.InnerCollection.Serializable>()));
        var collection = new BuildCollection<RootBuild>(jobName, _store.Object);
        var now = DateTime.UtcNow;
        collection.TryAdd(RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 1, startUtc: now.AddMinutes(-10), endUtc: now.AddMinutes(-5)));
        collection.TryAdd(RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 2, startUtc: now.AddMinutes(-5), endUtc: now));

        var removed = collection.RemoveBuildsOlderThan(now.AddHours(-1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(removed, Is.Zero);
            Assert.That(collection.Count, Is.EqualTo(2));
        }
    }

    [Test]
    public void RemoveBuildsOlderThan_WithAllOldBuilds_KeepsLastBuild()
    {
        var jobName = new JobName("TestJob");
        StoreSetupLoad(jobName);
        _store.Setup(s => s.Save(jobName, It.IsAny<BuildCollection<RootBuild>.InnerCollection.Serializable>()));
        var collection = new BuildCollection<RootBuild>(jobName, _store.Object);
        var threshold = DateTime.UtcNow;
        collection.TryAdd(RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 1, endUtc: threshold.AddDays(-3)));
        collection.TryAdd(RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 2, endUtc: threshold.AddDays(-2)));
        collection.TryAdd(RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 3, endUtc: threshold.AddDays(-1)));

        var removed = collection.RemoveBuildsOlderThan(threshold);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(removed, Is.EqualTo(2));
            Assert.That(collection.Count, Is.EqualTo(1));
            Assert.That(collection[0].BuildNumber, Is.EqualTo(3));
        }
    }

    [Test]
    public void RemoveBuildsOlderThan_WithSomeOldBuilds_RemovesOnlyOldOnes()
    {
        var jobName = new JobName("TestJob");
        StoreSetupLoad(jobName);
        _store.Setup(s => s.Save(jobName, It.IsAny<BuildCollection<RootBuild>.InnerCollection.Serializable>()));
        var collection = new BuildCollection<RootBuild>(jobName, _store.Object);
        var threshold = DateTime.UtcNow.AddDays(-2);
        collection.TryAdd(RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 1, endUtc: threshold.AddDays(-2)));
        collection.TryAdd(RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 2, endUtc: threshold.AddDays(-1)));
        collection.TryAdd(RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 3, endUtc: threshold.AddDays(1)));
        collection.TryAdd(RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 4, endUtc: threshold.AddDays(2)));

        var removed = collection.RemoveBuildsOlderThan(threshold);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(removed, Is.EqualTo(2));
            Assert.That(collection.Count, Is.EqualTo(2));
            Assert.That(collection[0].BuildNumber, Is.EqualTo(3));
            Assert.That(collection[1].BuildNumber, Is.EqualTo(4));
        }
    }

    [Test]
    public void RemoveBuildsOlderThan_WithEmptyCollection_ReturnsZero()
    {
        var jobName = new JobName("TestJob");
        StoreSetupLoad(jobName);
        var collection = new BuildCollection<RootBuild>(jobName, _store.Object);

        var removed = collection.RemoveBuildsOlderThan(DateTime.UtcNow);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(removed, Is.Zero);
            Assert.That(collection.Count, Is.Zero);
        }
    }

    [Test]
    public void RemoveBuildsOlderThan_WithSingleBuild_KeepsIt()
    {
        var jobName = new JobName("TestJob");
        StoreSetupLoad(jobName);
        _store.Setup(s => s.Save(jobName, It.IsAny<BuildCollection<RootBuild>.InnerCollection.Serializable>()));
        var collection = new BuildCollection<RootBuild>(jobName, _store.Object);
        var build = RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 1, endUtc: DateTime.UtcNow.AddDays(-10));
        collection.TryAdd(build);

        var removed = collection.RemoveBuildsOlderThan(DateTime.UtcNow);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(removed, Is.Zero);
            Assert.That(collection.Count, Is.EqualTo(1));
            Assert.That(collection[0].BuildNumber, Is.EqualTo(1));
        }
    }

    [Test]
    public void RemoveBuildsOlderThan_WhenSomethingIsRemoved_SavesOnce()
    {
        var jobName = new JobName("TestJob");
        StoreSetupLoad(jobName);
        var saveCount = 0;
        _store.Setup(s => s.Save(jobName, It.IsAny<BuildCollection<RootBuild>.InnerCollection.Serializable>()))
            .Callback(() => saveCount++);
        var collection = new BuildCollection<RootBuild>(jobName, _store.Object);
        var threshold = DateTime.UtcNow;
        collection.TryAdd(RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 1, endUtc: threshold.AddDays(-2)));
        collection.TryAdd(RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 2, endUtc: threshold.AddDays(-1)));
        collection.TryAdd(RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 3, endUtc: threshold.AddDays(1)));
        var savesBeforeRemove = saveCount;

        collection.RemoveBuildsOlderThan(threshold);

        Assert.That(saveCount - savesBeforeRemove, Is.EqualTo(1));
    }

    [Test]
    public void RemoveBuildsOlderThan_WhenNothingIsRemoved_DoesNotSave()
    {
        var jobName = new JobName("TestJob");
        StoreSetupLoad(jobName);
        var saveCount = 0;
        _store.Setup(s => s.Save(jobName, It.IsAny<BuildCollection<RootBuild>.InnerCollection.Serializable>()))
            .Callback(() => saveCount++);
        var collection = new BuildCollection<RootBuild>(jobName, _store.Object);
        collection.TryAdd(RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 1, endUtc: DateTime.UtcNow.AddDays(1)));
        collection.TryAdd(RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: 2, endUtc: DateTime.UtcNow.AddDays(2)));
        var savesBeforeRemove = saveCount;

        collection.RemoveBuildsOlderThan(DateTime.UtcNow.AddDays(-1));

        Assert.That(saveCount - savesBeforeRemove, Is.Zero);
    }
}
