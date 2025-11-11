using Moq;
using NUnit.Framework;
using Tod.Jenkins;
using Tod.Tests.IO;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class BuildCollectionTests
{
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
        _store.Setup(s => s.BuildBranch).Returns(BuildBranch.Create(new BranchName("main")));
        _store.Setup(s => s.Load(jobName, It.IsAny<Func<JobName, BuildCollection<RootBuild>.InnerCollection.Serializable>>()))
            .Returns((JobName j, Func<JobName, BuildCollection<RootBuild>.InnerCollection.Serializable> f) => f(j));
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
        var storeFactory = new ByJobNameStoreFactory(BuildBranch.Create(new BranchName("main")), jsonPath);

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
        var storeFactory = new ByJobNameStoreFactory(BuildBranch.Create(new BranchName("main")), jsonPath);

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
}
