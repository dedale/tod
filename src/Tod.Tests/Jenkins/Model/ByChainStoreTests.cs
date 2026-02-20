using NUnit.Framework;
using System.Diagnostics.CodeAnalysis;
using Tod.Jenkins;
using Tod.Tests.IO;

namespace Tod.Tests.Jenkins.Model;

internal static class ChainReportTrackerTestExtensions
{
    public static bool ContainsBuild(this ChainReportTracker.Serializable tracker, RootBuild rootBuild)
    {
        return tracker.BaselineChains.Any(b => b.RootBuild == rootBuild.Reference);
    }
}

[TestFixture]
internal sealed class ByChainStoreTests
{
    private readonly JobName _testJob1 = new("MyTestJob1");
    private readonly JobName _testJob2 = new("MyTestJob2");

    private TempDirectory _temp;
    private ByChainStore _store;

    [SetUp]
    public void SetUp()
    {
        _temp = new TempDirectory();
        var chainsJsonPath = Path.Combine(_temp.Path, "Chains.json");
        _store = new ByChainStore(chainsJsonPath);
    }

    [TearDown]
    public void TearDown()
    {
        _temp.Dispose();
    }

    [Test]
    public void Constructor_CreatesEmptyStore_WhenNoFileExists()
    {
        Assert.That(_store.ChainNames, Is.Empty);
    }

    [Test]
    public void Constructor_LoadsChainNames_WhenFileExists()
    {
        _store.Add("chain1");
        _store.Add("chain2");

        var newStore = new ByChainStore(Path.Combine(_temp.Path, "Chains.json"));

        Assert.That(newStore.ChainNames.Count(), Is.EqualTo(2));
        Assert.That(newStore.ChainNames, Contains.Item("chain1"));
        Assert.That(newStore.ChainNames, Contains.Item("chain2"));
    }

    [Test]
    public void Add_AddsNewChainName()
    {
        _store.Add("chain1");

        Assert.That(_store.ChainNames.Count(), Is.EqualTo(1));
        Assert.That(_store.ChainNames, Contains.Item("chain1"));
    }

    [Test]
    public void Add_DoesNotAddDuplicate()
    {
        _store.Add("chain1");
        _store.Add("chain1");

        Assert.That(_store.ChainNames.Count(), Is.EqualTo(1));
    }

    [Test]
    public void Add_PersistsToFile()
    {
        _store.Add("chain1");

        var chainsJsonPath = Path.Combine(_temp.Path, "Chains.json");
        Assert.That(File.Exists(chainsJsonPath), Is.True);

        var newStore = new ByChainStore(chainsJsonPath);
        Assert.That(newStore.ChainNames, Contains.Item("chain1"));
    }

    [Test]
    public void Save_StoresNewTracker()
    {
        var tracker = new ChainReportTracker("chain1", _store);
        var rootBuild = RandomData.NextRootBuild(commits: 1, testJobNames: [_testJob1.Value]);

        tracker.AddRootBuild(rootBuild, [_testJob1]);

        _store.Save("chain1", tracker.ToSerializable());

        var trackerJsonPath = Path.Combine(_temp.Path, "chain1.json");
        Assert.That(File.Exists(trackerJsonPath), Is.True);
    }

    [ExcludeFromCodeCoverage]
    private static ChainReportTracker.Serializable CreateNotNeeded()
    {
        throw new InvalidOperationException("This method should not be called");
    }

    [ExcludeFromCodeCoverage]
    private ChainReportTracker.Serializable LoadChain(string chain)
    {
        return _store.Load(chain, CreateNotNeeded);
    }

    [Test]
    public void Save_OverwritesExistingTracker()
    {
        var tracker1 = new ChainReportTracker("chain1", _store);
        var rootBuild1 = RandomData.NextRootBuild(buildNumber: 100, commits: 1, testJobNames: [_testJob1.Value]);
        tracker1.AddRootBuild(rootBuild1, [_testJob1]);

        _store.Save("chain1", tracker1.ToSerializable());

        var tracker2 = new ChainReportTracker("chain1", _store);
        var rootBuild2 = RandomData.NextRootBuild(buildNumber: 200, commits: 1, testJobNames: [_testJob1.Value]);
        tracker2.AddRootBuild(rootBuild2, [_testJob1]);

        _store.Save("chain1", tracker2.ToSerializable());

        var loaded = LoadChain("chain1");
        //Assert.That(loaded.Count, Is.EqualTo(1));
        Assert.That(loaded.ContainsBuild(rootBuild2), Is.True);
        Assert.That(loaded.ContainsBuild(rootBuild1), Is.False);
    }

    private static ChainReportTracker.Serializable Create(string chain)
    {
        return new ChainReportTracker.Serializable(chain, []);
    }

    [Test]
    public void Load_ReturnsEmptyTracker_WhenFileDoesNotExist()
    {
        var tracker = _store.Load("nonexistent", () => new ChainReportTracker.Serializable("nonexistent", []));

        Assert.That(tracker, Is.Not.Null);
        Assert.That(tracker.BaselineChains.Count, Is.EqualTo(0));
    }

    [Test]
    public void Load_ReturnsTrackerFromFile()
    {
        var tracker = new ChainReportTracker("chain1", _store);
        var rootBuild = RandomData.NextRootBuild(buildNumber: 123, commits: 1, testJobNames: [_testJob1.Value]);
        tracker.AddRootBuild(rootBuild, [_testJob1]);

        _store.Save("chain1", tracker.ToSerializable());

        var loaded = LoadChain("chain1");

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded.BaselineChains.Count, Is.EqualTo(1));
        Assert.That(loaded.ContainsBuild(rootBuild), Is.True);
    }

    [Test]
    public async Task Load_PreservesTrackerState()
    {
        var tracker = new ChainReportTracker("chain1", _store);
        var rootBuild = RandomData.NextRootBuild(buildNumber: 100, commits: 1, testJobNames: [_testJob1.Value, _testJob2.Value]);
        tracker.AddRootBuild(rootBuild, [_testJob1, _testJob2]);

        await tracker.MarkTestDone(rootBuild.BuildNumber, _testJob1, new BuildReference(_testJob1, RandomData.NextBuildNumber), () => Task.CompletedTask).ConfigureAwait(false);

        _store.Save("chain1", tracker.ToSerializable());

        var loaded = _store.Load("chain1", CreateNotNeeded);

        Assert.That(loaded.BaselineChains.Count, Is.EqualTo(1));
        Assert.That(loaded.ContainsBuild(rootBuild), Is.True);
    }

    [Test]
    public void SaveAndLoad_RoundTrip_PreservesMultipleBuilds()
    {
        var tracker = new ChainReportTracker("chain1", _store);
        var rootBuild1 = RandomData.NextRootBuild(buildNumber: 100, isSuccessful: false, commits: 1, testJobNames: [_testJob1.Value]);
        var rootBuild2 = RandomData.NextRootBuild(buildNumber: 101, isSuccessful: true, commits: 1, testJobNames: [_testJob1.Value]);

        tracker.AddRootBuild(rootBuild1, [_testJob1]);
        tracker.AddRootBuild(rootBuild2, [_testJob1]);

        _store.Save("chain1", tracker.ToSerializable());

        var loaded = LoadChain("chain1");

        Assert.That(loaded.BaselineChains.Count, Is.EqualTo(2));
        Assert.That(loaded.ContainsBuild(rootBuild1), Is.True);
        Assert.That(loaded.ContainsBuild(rootBuild2), Is.True);
    }

    [Test]
    public void Add_CreatesDirectory_WhenDirectoryDoesNotExist()
    {
        var subDir = Path.Combine(_temp.Path, "subdir");
        var chainsJsonPath = Path.Combine(subDir, "Chains.json");
        var store = new ByChainStore(chainsJsonPath);

        store.Add("chain1");

        Assert.That(Directory.Exists(subDir), Is.True);
        Assert.That(File.Exists(chainsJsonPath), Is.True);
    }

    [Test]
    public void Save_CreatesDirectory_WhenDirectoryDoesNotExist()
    {
        var subDir = Path.Combine(_temp.Path, "subdir");
        var chainsJsonPath = Path.Combine(subDir, "Chains.json");
        var store = new ByChainStore(chainsJsonPath);

        var tracker = new ChainReportTracker("chain1", _store);
        var rootBuild = RandomData.NextRootBuild(commits: 1, testJobNames: [_testJob1.Value]);
        tracker.AddRootBuild(rootBuild, [_testJob1]);

        store.Save("chain1", tracker.ToSerializable());

        Assert.That(Directory.Exists(subDir), Is.True);
        Assert.That(File.Exists(Path.Combine(subDir, "chain1.json")), Is.True);
    }

    [Test]
    public void Load_ReturnsNewTracker_WithSaveAction()
    {
        var loaded = _store.Load("newchain", () => Create("newchain"));

        var tracker = loaded.FromSerializable(_store);
        var rootBuild = RandomData.NextRootBuild(commits: 1, testJobNames: [_testJob1.Value]);
        tracker.AddRootBuild(rootBuild, [_testJob1]);

        loaded = LoadChain("newchain");
        Assert.That(loaded.BaselineChains.Count, Is.EqualTo(1));
    }

    [Test]
    public void SaveAndLoad_MultipleChains_Independent()
    {
        var tracker1 = new ChainReportTracker("chain1", _store);
        var rootBuild1 = RandomData.NextRootBuild(buildNumber: 100, commits: 1, testJobNames: [_testJob1.Value]);
        tracker1.AddRootBuild(rootBuild1, [_testJob1]);

        var tracker2 = new ChainReportTracker("chain2", _store);
        var rootBuild2 = RandomData.NextRootBuild(buildNumber: 200, commits: 1, testJobNames: [_testJob1.Value]);
        tracker2.AddRootBuild(rootBuild2, [_testJob1]);

        _store.Save("chain1", tracker1.ToSerializable());
        _store.Save("chain2", tracker2.ToSerializable());

        var loaded1 = LoadChain("chain1");
        var loaded2 = LoadChain("chain2");

        Assert.That(loaded1.ContainsBuild(rootBuild1), Is.True);
        Assert.That(loaded1.ContainsBuild(rootBuild2), Is.False);
        Assert.That(loaded2.ContainsBuild(rootBuild2), Is.True);
        Assert.That(loaded2.ContainsBuild(rootBuild1), Is.False);
    }
}
