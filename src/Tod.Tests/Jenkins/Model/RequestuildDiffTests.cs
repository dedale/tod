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

            diff = diff.QueueOnDemand();
            Assert.That(diff.TryGetPendingReference(out jobName), Is.True);
            Assert.That(jobName, Is.EqualTo(refJob));

            var buildNumber = RandomData.NextBuildNumber;
            diff = diff.DoneOnDemand(buildNumber);
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
        diff = diff.QueueOnDemand();
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
            Assert.That(diff.TryGetQueued(out var testBuild), Is.False);
            Assert.That(testBuild, Is.Null);

            diff = diff.QueueOnDemand();
            Assert.That(diff.TryGetQueued(out var testJob), Is.True);
            Assert.That(testJob, Is.EqualTo(new JobName("OnDemandTest")));

            var buildNumber = RandomData.NextBuildNumber;
            diff = diff.DoneOnDemand(buildNumber);
            Assert.That(diff.TryGetQueued(out testBuild), Is.False);
            Assert.That(testBuild, Is.Null);

            diff = new RequestBuildDiff(new("MainTest"), new("OnDemandTest")).DoneReference(RandomData.NextBuildNumber);
            Assert.That(diff.TryGetQueued(out testBuild), Is.False);
            Assert.That(testBuild, Is.Null);
        }
    }

    [Test]
    public void DoneOnDemand_WithMatchingBuild_IsDone()
    {
        using (Assert.EnterMultipleScope())
        {
            var diff = new RequestBuildDiff(new("MainTest"), new("OnDemandTest"));
            Assert.That(() => diff.DoneOnDemand(RandomData.NextBuildNumber), Throws.InvalidOperationException.And.Message.EqualTo("Not triggered"));
            diff = diff.QueueOnDemand();
            diff = diff.DoneOnDemand(RandomData.NextBuildNumber);
            Assert.That(diff.ReferenceBuild.IsDone, Is.False);
            Assert.That(diff.OnDemandBuild.IsDone, Is.True);
            Assert.That(diff.IsDone, Is.False);
            Assert.That(() => diff.DoneOnDemand(RandomData.NextBuildNumber), Throws.InvalidOperationException.And.Message.EqualTo("Already done"));
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

            diff = diff.QueueOnDemand();
            clone = diff.SerializationRoundTrip<RequestBuildDiff, RequestBuildDiff.Serializable>();
            Assert.That(clone.ReferenceBuild.IsDone, Is.False);
            Assert.That(clone.OnDemandBuild.IsDone, Is.False);
            Assert.That(clone.OnDemandBuild.Match(
                onPending: _ => false,
                onQueued: job => job == new JobName("OnDemandTest"),
                onDone: _ => false
            ), Is.True);

            diff = diff.DoneOnDemand(RandomData.NextBuildNumber);
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
