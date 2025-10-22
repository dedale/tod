using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class RequestuildDiffTests
{
    [Test]
    public void Ctor_IsNotDone()
    {
        var diff = new RequestBuildDiff(new("MainTest"), new("OnDemandTest"));
        Assert.That(diff.ReferenceBuild.IsDone, Is.False);
        Assert.That(diff.OnDemandBuild.IsDone, Is.False);
        Assert.That(diff.IsDone, Is.False);
    }

    [Test]
    public void TryGetPendingReference_ReturnsPending_OnlyWhenPending()
    {
        using (Assert.EnterMultipleScope())
        {
            var refJob = new JobName("MainTest");
            var diff = new RequestBuildDiff(refJob, new("OnDemandTest"));
            Assert.That(diff.TryGetPendingReference(out var jobName), Is.True);
            Assert.That(jobName, Is.EqualTo(refJob));

            var buildNumber = RandomData.NextBuildNumber;
            diff = diff.TriggerOnDemand(buildNumber);
            Assert.That(diff.TryGetPendingReference(out jobName), Is.True);
            Assert.That(jobName, Is.EqualTo(refJob));

            diff = diff.DoneOnDemand();
            Assert.That(diff.TryGetPendingReference(out jobName), Is.True);
            Assert.That(jobName, Is.EqualTo(refJob));

            diff = new RequestBuildDiff(new("MainTest"), new("OnDemandTest")).DoneReference(RandomData.NextBuildNumber);
            Assert.That(diff.TryGetPendingReference(out jobName), Is.False);
            Assert.That(jobName, Is.Null);
        }
    }

    [Test]
    public void DoneReference_WithMatchingBuild_IsDone()
    {
        var diff = new RequestBuildDiff(new("MainTest"), new("OnDemandTest"));
        diff = diff.DoneReference(RandomData.NextBuildNumber);
        Assert.That(diff.ReferenceBuild.IsDone, Is.True);
        Assert.That(diff.OnDemandBuild.IsDone, Is.False);
        Assert.That(diff.IsDone, Is.False);
    }

    [Test]
    public void TriggerOnDemand_WithPendingBuild_IsTriggered()
    {
        var diff = new RequestBuildDiff(new("MainTest"), new("OnDemandTest"));
        diff = diff.TriggerOnDemand(RandomData.NextBuildNumber);
        Assert.That(diff.ReferenceBuild.IsDone, Is.False);
        Assert.That(diff.OnDemandBuild.IsDone, Is.False);
        Assert.That(diff.IsDone, Is.False);
    }

    [Test]
    public void TryGetTriggered_ReturnsTriggered_OnlyWhenTriggered()
    {
        using (Assert.EnterMultipleScope())
        {
            var diff = new RequestBuildDiff(new("MainTest"), new("OnDemandTest"));
            Assert.That(diff.TryGetTriggered(out var testBuild), Is.False);
            Assert.That(testBuild, Is.Null);

            var buildNumber = RandomData.NextBuildNumber;
            diff = diff.TriggerOnDemand(buildNumber);
            Assert.That(diff.TryGetTriggered(out testBuild), Is.True);
            Assert.That(testBuild, Is.EqualTo(new BuildReference("OnDemandTest", buildNumber)));

            diff = diff.DoneOnDemand();
            Assert.That(diff.TryGetTriggered(out testBuild), Is.False);
            Assert.That(testBuild, Is.Null);

            diff = new RequestBuildDiff(new("MainTest"), new("OnDemandTest")).DoneReference(RandomData.NextBuildNumber);
            Assert.That(diff.TryGetTriggered(out testBuild), Is.False);
            Assert.That(testBuild, Is.Null);
        }
    }

    [Test]
    public void DoneOnDemand_WithMatchingBuild_IsDone()
    {
        using (Assert.EnterMultipleScope())
        {
            var diff = new RequestBuildDiff(new("MainTest"), new("OnDemandTest"));
            Assert.That(() => diff.DoneOnDemand(), Throws.InvalidOperationException.And.Message.EqualTo("Not triggered"));
            diff = diff.TriggerOnDemand(RandomData.NextBuildNumber);
            diff = diff.DoneOnDemand();
            Assert.That(diff.ReferenceBuild.IsDone, Is.False);
            Assert.That(diff.OnDemandBuild.IsDone, Is.True);
            Assert.That(diff.IsDone, Is.False);
            Assert.That(() => diff.DoneOnDemand(), Throws.InvalidOperationException.And.Message.EqualTo("Already done"));
        }
    }

    [Test]
    public void SerializationRoundTrip_Works()
    {
        using (Assert.EnterMultipleScope())
        {
            var diff = new RequestBuildDiff(new("MainTest"), new("OnDemandTest"));
            var clone = diff.SerializationRoundTrip<RequestBuildDiff, RequestBuildDiff.Serializable>();
            Assert.That(clone.ReferenceBuild.IsDone, Is.False);
            Assert.That(clone.OnDemandBuild.IsDone, Is.False);

            diff = diff.TriggerOnDemand(42);
            clone = diff.SerializationRoundTrip<RequestBuildDiff, RequestBuildDiff.Serializable>();
            Assert.That(clone.ReferenceBuild.IsDone, Is.False);
            Assert.That(clone.OnDemandBuild.IsDone, Is.False);
            Assert.That(clone.OnDemandBuild.Match(
                onPending: _ => false,
                onTriggered: build => build == new BuildReference("OnDemandTest", 42),
                onDone: _ => false
            ), Is.True);

            diff = diff.DoneOnDemand();
            clone = diff.SerializationRoundTrip<RequestBuildDiff, RequestBuildDiff.Serializable>();
            Assert.That(clone.ReferenceBuild.IsDone, Is.False);
            Assert.That(clone.OnDemandBuild.IsDone, Is.True);

            diff = new RequestBuildDiff(new("MainTest"), new("OnDemandTest")).DoneReference(84);
            clone = diff.SerializationRoundTrip<RequestBuildDiff, RequestBuildDiff.Serializable>();
            Assert.That(clone.ReferenceBuild.IsDone, Is.True);
            Assert.That(clone.OnDemandBuild.IsDone, Is.False);
        }
    }
}
