using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins.Model;

[TestFixture]
internal sealed class ChainReportTrackerTests
{
    private readonly JobName _testJob1 = new("MyTestJob1");
    private readonly JobName _testJob2 = new("MyTestJob2");

    [Test]
    public void AddRootBuild_AddsNewTracking()
    {
        var store = new InMemoryByChainStore();
        var tracker = new ChainReportTracker("test-chain", store);
        var rootBuild = RandomData.NextRootBuild(commits: 2, testJobNames: [_testJob1.Value, _testJob2.Value]);

        tracker.AddRootBuild(rootBuild, [_testJob1, _testJob2]);

        var serializable = tracker.ToSerializable();
        Assert.That(serializable.BaselineChains.Count, Is.EqualTo(1));
        Assert.That(serializable.ContainsBuild(rootBuild), Is.True);
    }

    [Test]
    public async Task MarkTestDone_UpdatesTestBuildState()
    {
        var store = new InMemoryByChainStore();
        var tracker = new ChainReportTracker("test-chain", store);
        var rootBuild = RandomData.NextRootBuild(commits: 2, testJobNames: [_testJob1.Value, _testJob2.Value]);
        tracker.AddRootBuild(rootBuild, [_testJob1, _testJob2]);

        var testBuildRef = new BuildReference(_testJob1, RandomData.NextBuildNumber);
        await tracker.MarkTestDone(rootBuild.BuildNumber, _testJob1, testBuildRef, () => Task.CompletedTask).ConfigureAwait(false);

        var readyBuilds = tracker.GetReadyForReport();
        Assert.That(readyBuilds, Is.Empty);
    }

    [Test]
    public void GetReadyForReport_ReturnsEmpty_WhenNoTestsAreDone()
    {
        var store = new InMemoryByChainStore();
        var tracker = new ChainReportTracker("test-chain", store);
        var rootBuild = RandomData.NextRootBuild(commits: 2, testJobNames: [_testJob1.Value, _testJob2.Value]);
        tracker.AddRootBuild(rootBuild, [_testJob1, _testJob2]);

        var readyBuilds = tracker.GetReadyForReport();

        Assert.That(readyBuilds, Is.Empty);
    }

    [Test]
    public async Task GetReadyForReport_ReturnsBuild_WhenAllTestsAreDone()
    {
        var store = new InMemoryByChainStore();
        var tracker = new ChainReportTracker("test-chain", store);
        var rootBuild = RandomData.NextRootBuild(commits: 2, testJobNames: [_testJob1.Value, _testJob2.Value]);
        tracker.AddRootBuild(rootBuild, [_testJob1, _testJob2]);

        BaselineChain[]? readyBuilds = null;
        await tracker.MarkTestDone(rootBuild.BuildNumber, _testJob1, new BuildReference(_testJob1, RandomData.NextBuildNumber), () => Task.CompletedTask).ConfigureAwait(false);
        await tracker.MarkTestDone(rootBuild.BuildNumber, _testJob2, new BuildReference(_testJob2, RandomData.NextBuildNumber), () =>
        {
            readyBuilds = tracker.GetReadyForReport();
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        Assert.That(readyBuilds, Has.Length.EqualTo(1));
        Assert.That(readyBuilds![0].RootBuild.BuildNumber, Is.EqualTo(rootBuild.BuildNumber));
    }

    [Test]
    public async Task GetReadyForReport_IncludesPreviousFailedBuilds()
    {
        var store = new InMemoryByChainStore();
        var tracker = new ChainReportTracker("test-chain", store);
        var failedBuild = RandomData.NextRootBuild(buildNumber: 100, isSuccessful: false, commits: 1, testJobNames: [_testJob1.Value]);
        var successBuild = RandomData.NextRootBuild(buildNumber: 101, isSuccessful: true, commits: 1, testJobNames: [_testJob1.Value]);

        tracker.AddRootBuild(failedBuild, [_testJob1]);
        tracker.AddRootBuild(successBuild, [_testJob1]);

        BaselineChain[]? readyBuilds = null;
        await tracker.MarkTestDone(successBuild.BuildNumber, _testJob1, new BuildReference(_testJob1, RandomData.NextBuildNumber), () =>
        {
            readyBuilds = tracker.GetReadyForReport();
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        Assert.That(readyBuilds, Has.Length.EqualTo(2));
        Assert.That(readyBuilds![0].RootBuild.BuildNumber, Is.EqualTo(100));
        Assert.That(readyBuilds[1].RootBuild.BuildNumber, Is.EqualTo(101));
    }

    [Test]
    public async Task GetReadyForReport_StopsAtPreviousSuccessfulBuild()
    {
        var store = new InMemoryByChainStore();
        var tracker = new ChainReportTracker("test-chain", store);
        var successBuild1 = RandomData.NextRootBuild(buildNumber: 99, isSuccessful: true, commits: 1, testJobNames: [_testJob1.Value]);
        var failedBuild = RandomData.NextRootBuild(buildNumber: 100, isSuccessful: false, commits: 1, testJobNames: [_testJob1.Value]);
        var successBuild2 = RandomData.NextRootBuild(buildNumber: 101, isSuccessful: true, commits: 1, testJobNames: [_testJob1.Value]);

        tracker.AddRootBuild(successBuild1, [_testJob1]);
        tracker.AddRootBuild(failedBuild, [_testJob1]);
        tracker.AddRootBuild(successBuild2, [_testJob1]);

        await tracker.MarkTestDone(successBuild1.BuildNumber, _testJob1, new BuildReference(_testJob1, 50), () => Task.CompletedTask).ConfigureAwait(false);

        BaselineChain[]? readyBuilds = null;
        await tracker.MarkTestDone(successBuild2.BuildNumber, _testJob1, new BuildReference(_testJob1, RandomData.NextBuildNumber), () =>
        {
            readyBuilds = tracker.GetReadyForReport();
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        Assert.That(readyBuilds, Has.Length.EqualTo(2));
        Assert.That(readyBuilds![0].RootBuild.BuildNumber, Is.EqualTo(100));
        Assert.That(readyBuilds[1].RootBuild.BuildNumber, Is.EqualTo(101));
    }

    [Test]
    public async Task MarkTestDone_UpdatesReportSentFlag_AfterSendingReport()
    {
        var store = new InMemoryByChainStore();
        var tracker = new ChainReportTracker("test-chain", store);
        var rootBuild = RandomData.NextRootBuild(commits: 2, testJobNames: [_testJob1.Value]);
        tracker.AddRootBuild(rootBuild, [_testJob1]);
        await tracker.MarkTestDone(rootBuild.BuildNumber, _testJob1, new BuildReference(_testJob1, RandomData.NextBuildNumber), () => Task.CompletedTask).ConfigureAwait(false);

        var readyBuilds = tracker.GetReadyForReport();
        Assert.That(readyBuilds, Is.Empty);
    }

    [Test]
    public void ContainsBuild_ReturnsFalse_WhenBuildNotTracked()
    {
        var store = new InMemoryByChainStore();
        var tracker = new ChainReportTracker("test-chain", store);

        var serializable = tracker.ToSerializable();
        Assert.That(serializable.ContainsBuild(123), Is.False);
    }

    [Test]
    public void ContainsBuild_ReturnsTrue_WhenBuildIsTracked()
    {
        var store = new InMemoryByChainStore();
        var tracker = new ChainReportTracker("test-chain", store);
        var rootBuild = RandomData.NextRootBuild(buildNumber: 123, commits: 1, testJobNames: [_testJob1.Value]);
        tracker.AddRootBuild(rootBuild, [_testJob1]);

        var serializable = tracker.ToSerializable();
        Assert.That(serializable.ContainsBuild(rootBuild), Is.True);
    }

    [Test]
    public async Task Serialization_RoundTrip_PreservesTrackerState()
    {
        var store = new InMemoryByChainStore();
        var tracker = new ChainReportTracker("test-chain", store);
        var rootBuild1 = RandomData.NextRootBuild(buildNumber: 100, commits: 1, testJobNames: [_testJob1.Value, _testJob2.Value]);
        var rootBuild2 = RandomData.NextRootBuild(buildNumber: 101, commits: 1, testJobNames: [_testJob1.Value, _testJob2.Value]);

        tracker.AddRootBuild(rootBuild1, [_testJob1, _testJob2]);
        tracker.AddRootBuild(rootBuild2, [_testJob1, _testJob2]);
        await tracker.MarkTestDone(rootBuild1.BuildNumber, _testJob1, new BuildReference(_testJob1, RandomData.NextBuildNumber), () => Task.CompletedTask).ConfigureAwait(false);
        await tracker.MarkTestDone(rootBuild1.BuildNumber, _testJob2, new BuildReference(_testJob2, RandomData.NextBuildNumber), () => Task.CompletedTask).ConfigureAwait(false);

        var serializable = tracker.ToSerializable();
        var restored = serializable.FromSerializable(store);

        var restoredSerializable = restored.ToSerializable();
        Assert.That(restoredSerializable.BaselineChains.Count, Is.EqualTo(2));
        Assert.That(restoredSerializable.ContainsBuild(100), Is.True);
        Assert.That(restoredSerializable.ContainsBuild(101), Is.True);

        var readyBuilds = restored.GetReadyForReport();
        Assert.That(readyBuilds, Is.Empty);
    }

    [Test]
    public void AddRootBuild_CallsSaveAction()
    {
        var saveCount = 0;
        var store = new InMemoryByChainStore();
        var trackingStore = new SaveCountingChainStore(store, () => saveCount++);
        var tracker = new ChainReportTracker("test-chain", trackingStore);
        var rootBuild = RandomData.NextRootBuild(commits: 1, testJobNames: [_testJob1.Value]);

        tracker.AddRootBuild(rootBuild, [_testJob1]);

        Assert.That(saveCount, Is.EqualTo(1));
    }

    private sealed class SaveCountingChainStore(IByChainStore inner, Action onSave) : IByChainStore
    {
        public IEnumerable<string> ChainNames => inner.ChainNames;
        public void Add(string chainName) => inner.Add(chainName);
        public void Save<T>(string chainName, T item)
        {
            onSave();
            inner.Save(chainName, item);
        }
        public T Load<T>(string chainName, Func<T> create) => inner.Load(chainName, create);
    }

    [Test]
    public async Task MarkTestDone_CallsSaveAction()
    {
        var saveCount = 0;
        var store = new InMemoryByChainStore();
        var trackingStore = new SaveCountingChainStore(store, () => saveCount++);
        var tracker = new ChainReportTracker("test-chain", trackingStore);
        var rootBuild = RandomData.NextRootBuild(commits: 1, testJobNames: [_testJob1.Value]);
        tracker.AddRootBuild(rootBuild, [_testJob1]);
        saveCount = 0;

        await tracker.MarkTestDone(rootBuild.BuildNumber, _testJob1, new BuildReference(_testJob1, RandomData.NextBuildNumber), () => Task.CompletedTask).ConfigureAwait(false);

        Assert.That(saveCount, Is.EqualTo(1));
    }

    [Test]
    public async Task MarkTestDone_InvokesSendReport_WhenAllTestsComplete()
    {
        var sendReportCount = 0;
        var store = new InMemoryByChainStore();
        var tracker = new ChainReportTracker("test-chain", store);
        var rootBuild = RandomData.NextRootBuild(commits: 1, testJobNames: [_testJob1.Value, _testJob2.Value]);
        tracker.AddRootBuild(rootBuild, [_testJob1, _testJob2]);

        await tracker.MarkTestDone(rootBuild.BuildNumber, _testJob1, new BuildReference(_testJob1, RandomData.NextBuildNumber), () => { sendReportCount++; return Task.CompletedTask; }).ConfigureAwait(false);
        Assert.That(sendReportCount, Is.EqualTo(0));

        await tracker.MarkTestDone(rootBuild.BuildNumber, _testJob2, new BuildReference(_testJob2, RandomData.NextBuildNumber), () => { sendReportCount++; return Task.CompletedTask; }).ConfigureAwait(false);
        Assert.That(sendReportCount, Is.EqualTo(1));
    }

    [Test]
    public async Task MarkTestDone_MarksReportSent_AfterSendingReport()
    {
        var store = new InMemoryByChainStore();
        var tracker = new ChainReportTracker("test-chain", store);
        var rootBuild = RandomData.NextRootBuild(commits: 1, testJobNames: [_testJob1.Value]);
        tracker.AddRootBuild(rootBuild, [_testJob1]);

        await tracker.MarkTestDone(rootBuild.BuildNumber, _testJob1, new BuildReference(_testJob1, RandomData.NextBuildNumber), () => Task.CompletedTask).ConfigureAwait(false);

        var readyBuilds = tracker.GetReadyForReport();
        Assert.That(readyBuilds, Is.Empty);
    }
}
