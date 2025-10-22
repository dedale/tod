using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class ChainDiffTests
{
    [Test]
    public void TriggerTests_AlreadyTriggered_NoChange()
    {
        // Arrange
        var referenceRoot = new BuildReference("REF-build", 100);
        var onDemandRoot = RequestBuildReference.Create(new JobName("CUSTOM-build"))
            .Trigger(200);
        
        var buildDiff = new RequestBuildDiff(new JobName("REF-test"), new JobName("CUSTOM-test"))
            .TriggerOnDemand(300);
        
        var chainDiff = new ChainDiff(
            ChainStatus.TestsTriggered,
            referenceRoot,
            onDemandRoot,
            [buildDiff]);

        var commit = RandomData.NextSha1();
        var triggerCalled = false;
        
        // Act
        var result = chainDiff.TriggerTests(commit, (jobName, sha) =>
        {
            triggerCalled = true;
            return Task.FromResult(RandomData.NextBuildNumber);
        });

        // Assert
        Assert.That(triggerCalled, Is.False, "Trigger function should not be called for already triggered builds");
        Assert.That(result.Status, Is.EqualTo(ChainStatus.TestsTriggered));
        
        // Verify the build diff remains unchanged
        var testBuildDiff = result.TestBuildDiffs.Single();
        testBuildDiff.OnDemandBuild.Match(
            onPending: _ => Assert.Fail("Build should not be pending"),
            onTriggered: build =>
            {
                Assert.That(build.JobName.Value, Is.EqualTo("CUSTOM-test"));
                Assert.That(build.BuildNumber, Is.EqualTo(300));
            },
            onDone: _ => Assert.Fail("Build should still be triggered, not done"));
    }

    [Test]
    public void TriggerTests_AlreadyDone_NoChange()
    {
        // Arrange
        var referenceRoot = new BuildReference("REF-build", 100);
        var onDemandRoot = RequestBuildReference.Create(new JobName("CUSTOM-build"))
            .Trigger(200);
        // Should be DoneTriggered but we need invalid state for full code coverage

        var buildDiff = new RequestBuildDiff(new JobName("REF-test"), new JobName("CUSTOM-test"))
            .TriggerOnDemand(300)
            .DoneOnDemand();
        
        var chainDiff = new ChainDiff(
            ChainStatus.Done,
            referenceRoot,
            onDemandRoot,
            [buildDiff]);

        var commit = RandomData.NextSha1();
        var triggerCalled = false;
        
        // Act
        var result = chainDiff.TriggerTests(commit, (jobName, sha) =>
        {
            triggerCalled = true;
            return Task.FromResult(RandomData.NextBuildNumber);
        });

        // Assert
        Assert.That(triggerCalled, Is.False, "Trigger function should not be called for already done builds");
        Assert.That(result.Status, Is.EqualTo(ChainStatus.TestsTriggered));
        
        // Verify the build diff remains unchanged
        var testBuildDiff = result.TestBuildDiffs.Single();
        testBuildDiff.OnDemandBuild.Match(
            onPending: _ => Assert.Fail("Build should not be pending"),
            onTriggered: _ => Assert.Fail("Build should be done, not triggered"),
            onDone: build =>
            {
                Assert.That(build.JobName.Value, Is.EqualTo("CUSTOM-test"));
                Assert.That(build.BuildNumber, Is.EqualTo(300));
            });
    }

    [Test]
    public void SerializationRoundTrip_Works()
    {
        // Arrange
        var referenceRoot = new BuildReference("REF-build", 100);
        var onDemandRoot = RequestBuildReference.Create(new JobName("CUSTOM-build"))
            .Trigger(200)
            .DoneTriggered();

        var buildDiff = new RequestBuildDiff(new JobName("REF-test"), new JobName("CUSTOM-test"))
            .TriggerOnDemand(300)
            .DoneOnDemand();

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