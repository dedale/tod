using NUnit.Framework;
using System.Diagnostics;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class RequestTestBuildReferenceTests
{
    [Test]
    public void Create_PendingBuildReference()
    {
        var reference = RequestTestBuildReference.Create(new("MyTestJob"));
        reference.Match(
            onPending: jobName => Assert.That(jobName.Value, Is.EqualTo("MyTestJob")),
            onQueued: _ => Assert.Fail("Expected pending build reference"),
            onDone: _ => Assert.Fail("Expected pending build reference")
        );
        reference.Match(
            onPending: jobName =>
            {
                Assert.That(jobName.Value, Is.EqualTo("MyTestJob"));
                return 0;
            },
            onQueued: _ =>
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
        var pending = RequestTestBuildReference.Create(jobName);
        Assert.That(pending.TryGetPendingReference(out var pendingJob), Is.True);
        Assert.That(pendingJob, Is.EqualTo(jobName));
        var triggered = pending.Queue();
        Assert.That(triggered.TryGetPendingReference(out _), Is.False);
        var done = triggered.DoneQueued(RandomData.NextBuildNumber);
        Assert.That(done.TryGetPendingReference(out _), Is.False);
    }

    [Test]
    public void Queue_ReturnsQueued_OnlyWhenQueued()
    {
        var jobName = new JobName("MyTestJob");
        var pending = RequestTestBuildReference.Create(jobName);
        var queued = pending.Queue();
        queued.Match(
            onPending: _ => Assert.Fail("Expected queued build reference"),
            onQueued: job => 
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(job, Is.EqualTo(jobName));
                }
            },
            onDone: _ => Assert.Fail("Expected queued build reference")
        );
        queued.Match(
            onPending: _ =>
            {
                Assert.Fail("Expected queued build reference");
                return 0;
            },
            onQueued: job =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(job, Is.EqualTo(jobName));
                }
                return 0;
            },
            onDone: _ =>
            {
                Assert.Fail("Expected queued build reference");
                return 0;
            }
        );
        Assert.That(queued.Queue, Throws.InvalidOperationException.And.Message.EqualTo("Already queued"));
        var done = queued.DoneQueued(RandomData.NextBuildNumber);
        Assert.That(done.Queue, Throws.InvalidOperationException.And.Message.EqualTo("Already done"));
    }

    [Test]
    public void TryGetQueued_ReturnsQueued_OnlyWhenQueued()
    {
        var pending = RequestTestBuildReference.Create(new("MyTestJob"));
        Assert.That(pending.TryGetQueued(out _), Is.False);
        var queued = pending.Queue();
        Assert.That(queued.TryGetQueued(out var job), Is.True);
        Debug.Assert(job is not null);
        Assert.That(job.Value, Is.EqualTo("MyTestJob"));
        var done = queued.DoneQueued(RandomData.NextBuildNumber);
        Assert.That(done.TryGetQueued(out _), Is.False);
    }

    [Test]
    public void DoneQueued_IsDone_OnlyWhenQueued()
    {
        using (Assert.EnterMultipleScope())
        {
            var pending = RequestTestBuildReference.Create(new("MyTestJob"));
            Assert.That(() => pending.DoneQueued(RandomData.NextBuildNumber), Throws.InvalidOperationException.And.Message.EqualTo("Not triggered"));
            var queued = pending.Queue();
            var buildNumber = RandomData.NextBuildNumber;
            var done = queued.DoneQueued(buildNumber);
            done.Match(
                onPending: _ => Assert.Fail("Expected done build reference"),
                onQueued: _ => Assert.Fail("Expected done build reference"),
                onDone: buildReference =>
                {
                    Assert.That(buildReference.JobName.Value, Is.EqualTo("MyTestJob"));
                    Assert.That(buildReference.BuildNumber, Is.EqualTo(buildNumber));
                }
            );
            Assert.That(() => done.DoneQueued(RandomData.NextBuildNumber), Throws.InvalidOperationException.And.Message.EqualTo("Already done"));
        }
    }

    [Test]
    public void IsDone_ReturnsFalse_WhenNotDone()
    {
        var pending = RequestTestBuildReference.Create(new("MyTestJob"));
        Assert.That(pending.IsDone, Is.False);
        var queued = pending.Queue();
        Assert.That(queued.IsDone, Is.False);
        var done = queued.DoneQueued(RandomData.NextBuildNumber);
        Assert.That(done.IsDone, Is.True);
    }

    [Test]
    public void JobName_Works()
    {
        var jobName = new JobName(Guid.NewGuid().ToString());
        var pending = RequestTestBuildReference.Create(jobName);
        Assert.That(pending.JobName, Is.EqualTo(jobName));
        var queued = pending.Queue();
        Assert.That(queued.JobName, Is.EqualTo(jobName));
        var done = queued.DoneQueued(RandomData.NextBuildNumber);
        Assert.That(done.JobName, Is.EqualTo(jobName));
    }

    [Test]
    public void TestSerializable_Pending()
    {
        var pending = RequestTestBuildReference.Create(new("MyTestJob"));
        var clone = pending.SerializationRoundTrip<RequestTestBuildReference, RequestTestBuildReference.Serializable>();
        Assert.That(clone.Match(
            onPending: job =>
            {
                Assert.That(job.Value, Is.EqualTo("MyTestJob"));
                return true;
            },
            onQueued: _ => false,
            onDone: _ => false
        ), Is.True);
    }

    [Test]
    public void TestSerializable_Queued()
    {
        var queued = RequestTestBuildReference.Create(new("MyTestJob")).Queue();
        var clone = queued.SerializationRoundTrip<RequestTestBuildReference, RequestTestBuildReference.Serializable>();
        Assert.That(clone.Match(
            onPending: _ => false,
            onQueued: job =>
            {
                Assert.That(job, Is.EqualTo(new JobName("MyTestJob")));
                return true;
            },
            onDone: _ => false
        ), Is.True);
    }

    [Test]
    public void TestSerializable_Done()
    {
        var done = RequestTestBuildReference.Create(new("MyTestJob")).Queue().DoneQueued(42);
        var clone = done.SerializationRoundTrip<RequestTestBuildReference, RequestTestBuildReference.Serializable>();
        Assert.That(clone.Match(
            onPending: _ => false,
            onQueued: _ => false,
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
        var pending1 = RequestTestBuildReference.Create(new("MyTestJob"));
        var pending2 = RequestTestBuildReference.Create(new("MyTestJob"));
        var pending3 = RequestTestBuildReference.Create(new("OtherJob"));
        Assert.That(pending1, Is.EqualTo(pending2));
        Assert.That(pending1, Is.Not.EqualTo(pending3));

        var queued1 = pending1.Queue();
        var queued2 = pending2.Queue();
        var queued3 = pending3.Queue();
        Assert.That(queued1, Is.EqualTo(queued2));
        Assert.That(queued1, Is.Not.EqualTo(queued3));

        var done1 = queued1.DoneQueued(42);
        var done2 = queued2.DoneQueued(42);
        var done3 = queued3.DoneQueued(42);
        Assert.That(done1, Is.EqualTo(done2));
        Assert.That(done1, Is.Not.EqualTo(done3));

        Assert.That(pending1, Is.Not.EqualTo(queued1));
        Assert.That(queued1, Is.Not.EqualTo(pending1));

        Assert.That(pending1, Is.Not.EqualTo(done1));
        Assert.That(done1, Is.Not.EqualTo(pending1));

        Assert.That(queued1, Is.Not.EqualTo(done1));
        Assert.That(done1, Is.Not.EqualTo(queued1));
    }
}
