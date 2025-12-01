using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class ChainDiffTests
{
    [Test]
    public void TriggerTests_AlreadyTriggered_Throws()
    {
        // Arrange
        var referenceRoot = new BuildReference("REF-build", 100);
        var commit = RandomData.NextSha1();
        var onDemandRootRef = new BuildReference("CUSTOM-build", RandomData.NextBuildNumber);
        var onDemandRoot = RequestRootBuildReference.Queue(onDemandRootRef.JobName, commit)
            .DoneQueued(onDemandRootRef.BuildNumber);

        var buildDiff = new RequestBuildDiff(new JobName("REF-test"), new JobName("CUSTOM-test"))
            .QueueOnDemand();

        var chainDiff = new ChainDiff(
            ChainStatus.TestsTriggered,
            referenceRoot,
            onDemandRoot,
            [buildDiff]);

        // Act
        Assert.That(() => chainDiff.TriggerTests(onDemandRootRef, _ => Task.FromException(new NotImplementedException())),
            Throws.InvalidOperationException.And.Message.EqualTo("Already done"));
    }

    [Test]
    public void TriggerTests_AlreadyDone_Throws()
    {
        // Arrange
        var referenceRoot = new BuildReference("REF-build", 100);
        var commit = RandomData.NextSha1();
        var onDemandRootRef = new BuildReference("CUSTOM-build", RandomData.NextBuildNumber);
        var onDemandRoot = RequestRootBuildReference.Queue(onDemandRootRef.JobName, commit)
            .DoneQueued(onDemandRootRef.BuildNumber);
        // Should be DoneTriggered but we need invalid state for full code coverage

        var buildDiff = new RequestBuildDiff(new JobName("REF-test"), new JobName("CUSTOM-test"))
            .QueueOnDemand()
            .DoneOnDemand(300);

        var chainDiff = new ChainDiff(
            ChainStatus.Done,
            referenceRoot,
            onDemandRoot,
            [buildDiff]);

        // Act
        Assert.That(() => chainDiff.TriggerTests(onDemandRootRef, _ => Task.FromException(new NotImplementedException())),
            Throws.InvalidOperationException.And.Message.EqualTo("Already done"));
    }

    [Test]
    public async Task TriggerTests_OtherQueued_DoesNothing()
    {
        var referenceRoot = new BuildReference("REF-build", 100);
        var commit = RandomData.NextSha1();
        var onDemandRootRef = new BuildReference("CUSTOM-build", RandomData.NextBuildNumber);
        var onDemandRoot = RequestRootBuildReference.Queue(onDemandRootRef.JobName, commit);
        var buildDiff = new RequestBuildDiff(new JobName("REF-test"), new JobName("CUSTOM-test"));
        var chainDiff = new ChainDiff(
            ChainStatus.RootTriggered,
            referenceRoot,
            onDemandRoot,
            [buildDiff]);
        var newChainDiff = await chainDiff.TriggerTests(new BuildReference("OTHER-build", 999), _ => Task.FromException(new InvalidOperationException())).ConfigureAwait(false);
        Assert.That(newChainDiff, Is.SameAs(chainDiff));
    }

    [Test]
    public async Task TriggerTests_OtherDone_DoesNothing()
    {
        var referenceRoot = new BuildReference("REF-build", 100);
        var commit = RandomData.NextSha1();
        var onDemandRootRef = new BuildReference("CUSTOM-build", RandomData.NextBuildNumber);
        var onDemandRoot = RequestRootBuildReference.Queue(onDemandRootRef.JobName, commit).DoneQueued(RandomData.NextBuildNumber);
        var buildDiff = new RequestBuildDiff(new JobName("REF-test"), new JobName("CUSTOM-test")).QueueOnDemand();
        var chainDiff = new ChainDiff(
            ChainStatus.TestsTriggered,
            referenceRoot,
            onDemandRoot,
            [buildDiff]);
        var newChainDiff = await chainDiff.TriggerTests(new BuildReference("OTHER-build", 999), _ => Task.FromException(new InvalidOperationException())).ConfigureAwait(false);
        Assert.That(newChainDiff, Is.SameAs(chainDiff));
    }

    [Test]
    public async Task TriggerTests_Coverage_InvalidState()
    {
        var referenceRoot = new BuildReference("REF-build", 100);
        var commit = RandomData.NextSha1();
        var onDemandRootRef = new BuildReference("CUSTOM-build", RandomData.NextBuildNumber);
        var onDemandRoot = RequestRootBuildReference.Queue(onDemandRootRef.JobName, commit);

        var buildDiff1 = new RequestBuildDiff(new JobName("REF-test1"), new JobName("CUSTOM-test1")).QueueOnDemand();
        var buildDiff2 = new RequestBuildDiff(new JobName("REF-test1"), new JobName("CUSTOM-test1")).QueueOnDemand().DoneOnDemand(RandomData.NextBuildNumber);

        var chainDiff = new ChainDiff(
            ChainStatus.RootTriggered, // Should be TestsTriggered but we need invalid state for full code coverage
            referenceRoot,
            onDemandRoot,
            [buildDiff1, buildDiff2]);

        await chainDiff.TriggerTests(onDemandRootRef, _ => Task.FromException(new InvalidOperationException())).ConfigureAwait(false);
    }

    [Test]
    public async Task TriggerTests_Synchronization_TriggerDone()
    {
        var referenceRoot = new BuildReference("REF-build", 100);
        var commit = RandomData.NextSha1();
        var onDemandRootRef = new BuildReference("CUSTOM-build", RandomData.NextBuildNumber);
        var onDemandRoot = RequestRootBuildReference.Queue(onDemandRootRef.JobName, commit);

        var count = 10;
        var builDiffs = Enumerable.Range(0, count)
            .Select(i => new RequestBuildDiff(new JobName($"REF-test{i + 1}"), new JobName($"CUSTOM-test{i + 1}")))
            .ToList();

        var chainDiff = new ChainDiff(
            ChainStatus.RootTriggered,
            referenceRoot,
            onDemandRoot,
            builDiffs);

        var tcses = Enumerable.Range(0, count).Select(_ => new TaskCompletionSource<bool>()).ToArray();

        async Task Trigger(JobName job)
        {
            await Task.Delay(5).ConfigureAwait(false);
            var i = int.Parse(job.Value["CUSTOM-test".Length..]);
            tcses[i - 1].SetResult(true);
        }

        await chainDiff.TriggerTests(onDemandRootRef, Trigger).ConfigureAwait(false);

        Assert.That(tcses.All(tcs => tcs.Task.IsCompleted), Is.True);

        await Task.WhenAll(tcses.Select(tcs => tcs.Task)).ConfigureAwait(false);
    }

    [Test]
    public void SerializationRoundTrip_Works()
    {
        // Arrange
        var referenceRoot = new BuildReference("REF-build", 100);
        var commit = RandomData.NextSha1();
        var onDemandRoot = RequestRootBuildReference.Queue(new JobName("CUSTOM-build"), commit)
            .DoneQueued(200);

        var buildDiff = new RequestBuildDiff(new JobName("REF-test"), new JobName("CUSTOM-test"))
            .QueueOnDemand()
            .DoneOnDemand(300);

        var chainDiff = new ChainDiff(
            ChainStatus.Done,
            referenceRoot,
            onDemandRoot,
            [buildDiff]);

        var clone = chainDiff.SerializationRoundTrip<ChainDiff, ChainDiff.Serializable>();
        Assert.That(clone.Status, Is.EqualTo(chainDiff.Status));
        Assert.That(clone.ReferenceRoot, Is.EqualTo(chainDiff.ReferenceRoot));
        Assert.That(clone.OnDemandRoot, Is.EqualTo(chainDiff.OnDemandRoot));
        Assert.That(clone.TestBuildDiffs.Count, Is.EqualTo(chainDiff.TestBuildDiffs.Count()));
    }
}