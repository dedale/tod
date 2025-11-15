using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class RequestRootBuildReferenceTests
{
    [Test]
    public void Queue_CreatesQueuedReference()
    {
        var jobName = new JobName("TestJob");
        var commit = RandomData.NextSha1();

        var reference = RequestRootBuildReference.Queue(jobName, commit);

        reference.Match(
            onQueued: (job, sha) =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(job, Is.EqualTo(jobName));
                    Assert.That(sha, Is.EqualTo(commit));
                }
            },
            onDone: _ => Assert.Fail("Expected queued build reference")
        );
    }

    [Test]
    public void Queue_WithGenericMatch_ReturnsCorrectValue()
    {
        var jobName = new JobName("TestJob");
        var commit = RandomData.NextSha1();

        var reference = RequestRootBuildReference.Queue(jobName, commit);

        var result = reference.Match(
            onQueued: (job, sha) =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(job, Is.EqualTo(jobName));
                    Assert.That(sha, Is.EqualTo(commit));
                }
                return true;
            },
            onDone: _ =>
            {
                Assert.Fail("Expected queued build reference");
                return false;
            }
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void DoneQueued_TransitionsFromQueuedToDone()
    {
        var jobName = new JobName("TestJob");
        var commit = RandomData.NextSha1();
        var buildNumber = RandomData.NextBuildNumber;

        var queued = RequestRootBuildReference.Queue(jobName, commit);
        var done = queued.DoneQueued(buildNumber);

        done.Match(
            onQueued: (_, _) => Assert.Fail("Expected done build reference"),
            onDone: buildRef =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(buildRef.JobName, Is.EqualTo(jobName));
                    Assert.That(buildRef.BuildNumber, Is.EqualTo(buildNumber));
                }
            }
        );
    }

    [Test]
    public void DoneQueued_WhenAlreadyDone_ThrowsInvalidOperationException()
    {
        var jobName = new JobName("TestJob");
        var commit = RandomData.NextSha1();
        var queued = RequestRootBuildReference.Queue(jobName, commit);
        var done = queued.DoneQueued(RandomData.NextBuildNumber);

        Assert.That(() => done.DoneQueued(RandomData.NextBuildNumber),
            Throws.InvalidOperationException.With.Message.EqualTo("Already done"));
    }

    [Test]
    public void IsDone_ReturnsFalse_WhenQueued()
    {
        var jobName = new JobName("TestJob");
        var commit = RandomData.NextSha1();
        var queued = RequestRootBuildReference.Queue(jobName, commit);

        Assert.That(queued.IsDone, Is.False);
    }

    [Test]
    public void IsDone_ReturnsTrue_WhenDone()
    {
        var jobName = new JobName("TestJob");
        var commit = RandomData.NextSha1();
        var queued = RequestRootBuildReference.Queue(jobName, commit);
        var done = queued.DoneQueued(RandomData.NextBuildNumber);

        Assert.That(done.IsDone, Is.True);
    }

    [Test]
    public void BuildNumber_ThrowsInvalidOperationException_WhenQueued()
    {
        var jobName = new JobName("TestJob");
        var commit = RandomData.NextSha1();
        var queued = RequestRootBuildReference.Queue(jobName, commit);

        Assert.That(() => queued.BuildNumber,
            Throws.InvalidOperationException.With.Message.EqualTo("Not done"));
    }

    [Test]
    public void BuildNumber_ReturnsCorrectValue_WhenDone()
    {
        var jobName = new JobName("TestJob");
        var commit = RandomData.NextSha1();
        var buildNumber = RandomData.NextBuildNumber;
        var queued = RequestRootBuildReference.Queue(jobName, commit);
        var done = queued.DoneQueued(buildNumber);

        Assert.That(done.BuildNumber, Is.EqualTo(buildNumber));
    }

    [Test]
    public void JobName_ReturnsCorrectValue_WhenQueued()
    {
        var jobName = new JobName("TestJob");
        var commit = RandomData.NextSha1();
        var queued = RequestRootBuildReference.Queue(jobName, commit);

        Assert.That(queued.JobName, Is.EqualTo(jobName));
    }

    [Test]
    public void JobName_ReturnsCorrectValue_WhenDone()
    {
        var jobName = new JobName("TestJob");
        var commit = RandomData.NextSha1();
        var queued = RequestRootBuildReference.Queue(jobName, commit);
        var done = queued.DoneQueued(RandomData.NextBuildNumber);

        Assert.That(done.JobName, Is.EqualTo(jobName));
    }

    [Test]
    public void Serialization_Roundtrip_Queued()
    {
        var jobName = new JobName("TestJob");
        var commit = RandomData.NextSha1();
        var queued = RequestRootBuildReference.Queue(jobName, commit);

        var clone = queued.SerializationRoundTrip<RequestRootBuildReference, RequestRootBuildReference.Serializable>();

        Assert.That(clone.Match(
            onQueued: (job, sha) =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(job, Is.EqualTo(jobName));
                    Assert.That(sha, Is.EqualTo(commit));
                }
                return true;
            },
            onDone: _ => false
        ), Is.True);
    }

    [Test]
    public void Serialization_Roundtrip_Done()
    {
        var jobName = new JobName("TestJob");
        var commit = RandomData.NextSha1();
        var buildNumber = RandomData.NextBuildNumber;
        var done = RequestRootBuildReference.Queue(jobName, commit).DoneQueued(buildNumber);

        var clone = done.SerializationRoundTrip<RequestRootBuildReference, RequestRootBuildReference.Serializable>();

        Assert.That(clone.Match(
            onQueued: (_, _) => false,
            onDone: buildRef =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(buildRef.JobName, Is.EqualTo(jobName));
                    Assert.That(buildRef.BuildNumber, Is.EqualTo(buildNumber));
                }
                return true;
            }
        ), Is.True);
    }

    [Test]
    public void Equals_QueuedReferences_WithSameValues_AreEqual()
    {
        var jobName = new JobName("TestJob");
        var commit = RandomData.NextSha1();
        var queued1 = RequestRootBuildReference.Queue(jobName, commit);
        var queued2 = RequestRootBuildReference.Queue(jobName, commit);

        Assert.That(queued1.Equals(queued2), Is.True);
    }

    [Test]
    public void Equals_QueuedReferences_WithDifferentJobNames_AreNotEqual()
    {
        var commit = RandomData.NextSha1();
        var queued1 = RequestRootBuildReference.Queue(new JobName("Job1"), commit);
        var queued2 = RequestRootBuildReference.Queue(new JobName("Job2"), commit);

        Assert.That(queued1.Equals(queued2), Is.False);
    }

    [Test]
    public void Equals_QueuedReferences_WithDifferentCommits_AreNotEqual()
    {
        var jobName = new JobName("TestJob");
        var queued1 = RequestRootBuildReference.Queue(jobName, RandomData.NextSha1());
        var queued2 = RequestRootBuildReference.Queue(jobName, RandomData.NextSha1());

        Assert.That(queued1.Equals(queued2), Is.False);
    }

    [Test]
    public void Equals_DoneReferences_WithSameValues_AreEqual()
    {
        var jobName = new JobName("TestJob");
        var commit = RandomData.NextSha1();
        var buildNumber = RandomData.NextBuildNumber;
        var done1 = RequestRootBuildReference.Queue(jobName, commit).DoneQueued(buildNumber);
        var done2 = RequestRootBuildReference.Queue(jobName, commit).DoneQueued(buildNumber);

        Assert.That(done1.Equals(done2), Is.True);
    }

    [Test]
    public void Equals_DoneReferences_WithDifferentBuildNumbers_AreNotEqual()
    {
        var jobName = new JobName("TestJob");
        var commit = RandomData.NextSha1();
        var done1 = RequestRootBuildReference.Queue(jobName, commit).DoneQueued(RandomData.NextBuildNumber);
        var done2 = RequestRootBuildReference.Queue(jobName, commit).DoneQueued(RandomData.NextBuildNumber);

        Assert.That(done1.Equals(done2), Is.False);
    }

    [Test]
    public void Equals_QueuedAndDone_AreNotEqual()
    {
        var jobName = new JobName("TestJob");
        var commit = RandomData.NextSha1();
        var queued = RequestRootBuildReference.Queue(jobName, commit);
        var done = queued.DoneQueued(RandomData.NextBuildNumber);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(queued.Equals(done), Is.False);
            Assert.That(done.Equals(queued), Is.False);
        }
    }

    [Test]
    public void Equals_WithNull_ReturnsFalse()
    {
        var jobName = new JobName("TestJob");
        var commit = RandomData.NextSha1();
        var queued = RequestRootBuildReference.Queue(jobName, commit);

        Assert.That(queued.Equals(null), Is.False);
    }
}
