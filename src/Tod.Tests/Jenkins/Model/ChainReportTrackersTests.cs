using Moq;
using NUnit.Framework;
using System.Diagnostics.CodeAnalysis;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins.Model;

[TestFixture]
internal sealed class ChainReportTrackersTests
{
    private readonly JobName _testJob1 = new("MyTestJob1");

    [Test]
    public void Ctor_CreateIfNeeded()
    {
        var store = new Mock<IByChainStore>(MockBehavior.Strict);
        store.Setup(x => x.ChainNames).Returns(["chain1"]);
        store.Setup(x => x.Load("chain1", It.IsAny<Func<ChainReportTracker.Serializable>>()))
            .Returns((string chain, Func<ChainReportTracker.Serializable> f) => f());
        var trackers = new ChainReportTrackers(store.Object);
        Assert.That(trackers, Is.Not.Null);
        store.VerifyAll();
    }

    [Test]
    public void GetOrCreate_CreatesNewTracker_WhenNotExists()
    {
        var store = new InMemoryByChainStore();
        var trackers = new ChainReportTrackers(store);

        var tracker = trackers.GetOrCreate("chain1");

        Assert.That(tracker, Is.Not.Null);
        //Assert.That(tracker.Count, Is.EqualTo(0));
        Assert.That(store.ChainNames, Contains.Item("chain1"));
    }

    [Test]
    public void GetOrCreate_ReturnsSameTracker_WhenCalledTwice()
    {
        var store = new InMemoryByChainStore();
        var trackers = new ChainReportTrackers(store);

        var tracker1 = trackers.GetOrCreate("chain1");
        var tracker2 = trackers.GetOrCreate("chain1");

        Assert.That(tracker1, Is.SameAs(tracker2));
    }

    [Test]
    public void Get_ReturnsNull_WhenTrackerNotExists()
    {
        var store = new InMemoryByChainStore();
        var trackers = new ChainReportTrackers(store);

        var tracker = trackers.Get("nonexistent");

        Assert.That(tracker, Is.Null);
    }

    [ExcludeFromCodeCoverage]
    private static ChainReportTracker.Serializable CreateNotNeeded()
    {
        throw new InvalidOperationException("This method should not be called during the test");
    }

    [Test]
    public void Save_PersistsTrackerToStore()
    {
        var store = new InMemoryByChainStore();
        var trackers = new ChainReportTrackers(store);
        var tracker = trackers.GetOrCreate("chain1");
        var rootBuild = RandomData.NextRootBuild(commits: 1, testJobNames: [_testJob1.Value]);
        tracker.AddRootBuild(rootBuild, [_testJob1]);

        trackers.Save("chain1");

        var loaded = store.Load("chain1", CreateNotNeeded);
        Assert.That(loaded.BaselineChains.Count, Is.EqualTo(1));
        Assert.That(loaded.ContainsBuild(rootBuild), Is.True);
    }

    [Test]
    public void SaveAll_PersistsAllTrackers()
    {
        var store = new InMemoryByChainStore();
        var trackers = new ChainReportTrackers(store);
        var tracker1 = trackers.GetOrCreate("chain1");
        var tracker2 = trackers.GetOrCreate("chain2");
        var rootBuild1 = RandomData.NextRootBuild(buildNumber: 100, commits: 1, testJobNames: [_testJob1.Value]);
        var rootBuild2 = RandomData.NextRootBuild(buildNumber: 200, commits: 1, testJobNames: [_testJob1.Value]);
        tracker1.AddRootBuild(rootBuild1, [_testJob1]);
        tracker2.AddRootBuild(rootBuild2, [_testJob1]);

        trackers.SaveAll();

        var loaded1 = store.Load("chain1", CreateNotNeeded);
        var loaded2 = store.Load("chain2", CreateNotNeeded);
        Assert.That(loaded1.ContainsBuild(rootBuild1), Is.True);
        Assert.That(loaded2.ContainsBuild(rootBuild2), Is.True);
    }

    [Test]
    public void Constructor_LoadsExistingTrackers()
    {
        var store = new InMemoryByChainStore();
        var tracker = new ChainReportTracker("chain1", store);
        var rootBuild = RandomData.NextRootBuild(commits: 1, testJobNames: [_testJob1.Value]);
        tracker.AddRootBuild(rootBuild, [_testJob1]);
        store.Add("chain1");
        store.Save("chain1", tracker.ToSerializable());

        var trackers = new ChainReportTrackers(store);
        tracker = trackers.Get("chain1");
        var loaded = tracker?.ToSerializable();

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.ContainsBuild(rootBuild), Is.True);
    }
}
