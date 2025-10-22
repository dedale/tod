using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class RequestBuildReferenceTests
{
    [Test]
    public void Create_PendingBuildReference()
    {
        var reference = RequestBuildReference.Create(new("MyTestJob"));
        reference.Match(
            onPending: jobName => Assert.That(jobName.Value, Is.EqualTo("MyTestJob")),
            onTriggered: _ => Assert.Fail("Expected pending build reference"),
            onDone: _ => Assert.Fail("Expected pending build reference")
        );
        reference.Match(
            onPending: jobName =>
            {
                Assert.That(jobName.Value, Is.EqualTo("MyTestJob"));
                return 0;
            },
            onTriggered: _ =>
            {
                Assert.Fail("Expected pending build reference");
                return 0;
            },
            onDone: buildReference =>
            {
                Assert.Fail("Expected pending build reference");
                return 0;
            }
        );
        Assert.That(reference.IsDone, Is.False);
    }

    [Test]
    public void TryGetPendingReference_ReturnsPending_OnlyWhenPending()
    {
        var jobName = new JobName("MyTestJob");
        var pending = RequestBuildReference.Create(jobName);
        Assert.That(pending.TryGetPendingReference(out var pendingJob), Is.True);
        Assert.That(pendingJob, Is.EqualTo(jobName));
        var triggered = pending.Trigger(RandomData.NextBuildNumber);
        Assert.That(triggered.TryGetPendingReference(out _), Is.False);
        var done = triggered.DoneTriggered();
        Assert.That(done.TryGetPendingReference(out _), Is.False);
    }

    [Test]
    public void Trigger_ReturnsTriggered_OnlyWhenTriggered()
    {
        var jobName = new JobName("MyTestJob");
        var pending = RequestBuildReference.Create(jobName);
        var triggered = pending.Trigger(RandomData.NextBuildNumber);
        triggered.Match(
            onPending: _ => Assert.Fail("Expected triggered build reference"),
            onTriggered: buildReference => 
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(buildReference.JobName, Is.EqualTo(jobName));
                    Assert.That(buildReference.BuildNumber, Is.GreaterThan(0));
                }
            },
            onDone: _ => Assert.Fail("Expected triggered build reference")
        );
        triggered.Match(
            onPending: _ =>
            {
                Assert.Fail("Expected triggered build reference");
                return 0;
            },
            onTriggered: buildReference =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(buildReference.JobName, Is.EqualTo(jobName));
                    Assert.That(buildReference.BuildNumber, Is.GreaterThan(0));
                }
                return 0;
            },
            onDone: _ =>
            {
                Assert.Fail("Expected triggered build reference");
                return 0;
            }
        );
        Assert.That(() => triggered.Trigger(RandomData.NextBuildNumber), Throws.InvalidOperationException.And.Message.EqualTo("Already triggered"));
        var done = triggered.DoneTriggered();
        Assert.That(() => done.Trigger(RandomData.NextBuildNumber), Throws.InvalidOperationException.And.Message.EqualTo("Already done"));
    }

    [Test]
    public void TryGetTriggered_ReturnsTriggered_OnlyWhenTriggered()
    {
        var pending = RequestBuildReference.Create(new("MyTestJob"));
        Assert.That(pending.TryGetTriggered(out _), Is.False);
        var triggered = pending.Trigger(42);
        Assert.That(triggered.TryGetTriggered(out var buildReference), Is.True);
        Assert.That(buildReference, Is.EqualTo(new BuildReference("MyTestJob", 42)));
        var done = triggered.DoneTriggered();
        Assert.That(done.TryGetTriggered(out _), Is.False);
    }

    [Test]
    public void CompleteReference_WithPendingBuild_IsCompleted()
    {
        using (Assert.EnterMultipleScope())
        {
            var pending = RequestBuildReference.Create(new("MyTestJob"));
            var buildNumber = RandomData.NextBuildNumber;
            pending.DoneReference(buildNumber).Match(
                onPending: _ => Assert.Fail("Expected done build reference"),
                onTriggered: _ => Assert.Fail("Expected done build reference"),
                onDone: buildReference =>
                {
                    Assert.That(buildReference.JobName.Value, Is.EqualTo("MyTestJob"));
                    Assert.That(buildReference.BuildNumber, Is.EqualTo(buildNumber));
                }
            );
            pending.DoneReference(buildNumber).Match(
                onPending: _ =>
                {
                    Assert.Fail("Expected done build reference");
                    return 0;
                },
                onTriggered: _ =>
                {
                    Assert.Fail("Expected done build reference");
                    return 0;
                },
                onDone: buildReference =>
                {
                    Assert.That(buildReference.JobName.Value, Is.EqualTo("MyTestJob"));
                    Assert.That(buildReference.BuildNumber, Is.EqualTo(buildNumber));
                    return 0;
                }
            );
            var triggered = pending.Trigger(RandomData.NextBuildNumber);
            Assert.That(() => triggered.DoneReference(RandomData.NextBuildNumber), Throws.InvalidOperationException.And.Message.EqualTo("Reference builds are not triggered"));
            var done = triggered.DoneTriggered();
            Assert.That(() => done.DoneReference(RandomData.NextBuildNumber), Throws.InvalidOperationException.And.Message.EqualTo("Already done"));
        }
    }

    [Test]
    public void DoneTriggered_IsDone_OnlyWhenTriggered()
    {
        using (Assert.EnterMultipleScope())
        {
            var pending = RequestBuildReference.Create(new("MyTestJob"));
            Assert.That(pending.DoneTriggered, Throws.InvalidOperationException.And.Message.EqualTo("Not triggered"));
            var buildNumber = RandomData.NextBuildNumber;
            var triggered = pending.Trigger(buildNumber);
            var done = triggered.DoneTriggered();
            done.Match(
                onPending: _ => Assert.Fail("Expected done build reference"),
                onTriggered: _ => Assert.Fail("Expected done build reference"),
                onDone: buildReference =>
                {
                    Assert.That(buildReference.JobName.Value, Is.EqualTo("MyTestJob"));
                    Assert.That(buildReference.BuildNumber, Is.EqualTo(buildNumber));
                }
            );
            Assert.That(done.DoneTriggered, Throws.InvalidOperationException.And.Message.EqualTo("Already done"));
        }
    }

    [Test]
    public void IsDone_ReturnsFalse_WhenNotDone()
    {
        var pending = RequestBuildReference.Create(new("MyTestJob"));
        Assert.That(pending.IsDone, Is.False);
        var triggered = pending.Trigger(42);
        Assert.That(triggered.IsDone, Is.False);
        var done = triggered.DoneTriggered();
        Assert.That(done.IsDone, Is.True);
    }

    [Test]
    public void JobName_Works()
    {
        var jobName = new JobName(Guid.NewGuid().ToString());
        var pending = RequestBuildReference.Create(jobName);
        Assert.That(pending.JobName, Is.EqualTo(jobName));
        var triggered = pending.Trigger(42);
        Assert.That(triggered.JobName, Is.EqualTo(jobName));
        var done = triggered.DoneTriggered();
        Assert.That(done.JobName, Is.EqualTo(jobName));
    }

    [Test]
    public void TestSerializable_Pending()
    {
        var pending = RequestBuildReference.Create(new("MyTestJob"));
        var clone = pending.SerializationRoundTrip<RequestBuildReference, RequestBuildReference.Serializable>();
        Assert.That(clone.Match(
            onPending: job =>
            {
                Assert.That(job.Value, Is.EqualTo("MyTestJob"));
                return true;
            },
            onTriggered: _ => false,
            onDone: _ => false
        ), Is.True);
    }

    [Test]
    public void TestSerializable_Triggered()
    {
        var triggered = RequestBuildReference.Create(new("MyTestJob")).Trigger(42);
        var clone = triggered.SerializationRoundTrip<RequestBuildReference, RequestBuildReference.Serializable>();
        Assert.That(clone.Match(
            onPending: _ => false,
            onTriggered: build =>
            {
                Assert.That(build, Is.EqualTo(new BuildReference("MyTestJob", 42)));
                return true;
            },
            onDone: _ => false
        ), Is.True);
    }

    [Test]
    public void TestSerializable_Done()
    {
        var done = RequestBuildReference.Create(new("MyTestJob")).Trigger(42).DoneTriggered();
        var clone = done.SerializationRoundTrip<RequestBuildReference, RequestBuildReference.Serializable>();
        Assert.That(clone.Match(
            onPending: _ => false,
            onTriggered: _ => false,
            onDone: build =>
            {
                Assert.That(build, Is.EqualTo(new BuildReference("MyTestJob", 42)));
                return true;
            }
        ), Is.True);
    }

    [Test]
    public void Equals_Works()
    {
        var pending1 = RequestBuildReference.Create(new("MyTestJob"));
        var pending2 = RequestBuildReference.Create(new("MyTestJob"));
        var pending3 = RequestBuildReference.Create(new("OtherJob"));
        Assert.That(pending1, Is.EqualTo(pending2));
        Assert.That(pending1, Is.Not.EqualTo(pending3));

        var triggered1 = pending1.Trigger(42);
        var triggered2 = pending2.Trigger(42);
        var triggered3 = pending2.Trigger(43);
        Assert.That(triggered1, Is.EqualTo(triggered2));
        Assert.That(triggered1, Is.Not.EqualTo(triggered3));

        var done1 = triggered1.DoneTriggered();
        var done2 = triggered2.DoneTriggered();
        var done3 = triggered2.DoneTriggered();
        Assert.That(done1, Is.EqualTo(done2));
        Assert.That(done1, Is.EqualTo(done3));

        Assert.That(pending1, Is.Not.EqualTo(triggered1));
        Assert.That(triggered1, Is.Not.EqualTo(pending1));

        Assert.That(pending1, Is.Not.EqualTo(done1));
        Assert.That(done1, Is.Not.EqualTo(pending1));

        Assert.That(triggered1, Is.Not.EqualTo(done1));
        Assert.That(done1, Is.Not.EqualTo(triggered1));
    }
}
