using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class RefTestBuildReferenceTests
{
    [Test]
    public void CompleteReference_WithPendingBuild_IsCompleted()
    {
        using (Assert.EnterMultipleScope())
        {
            var pending = BaseTestBuildReference.Create(new("MyTestJob"));
            var buildNumber = RandomData.NextBuildNumber;
            var done = pending.DoneBaseline(buildNumber);
            done.Match(
                onPending: _ => Assert.Fail("Expected done build reference"),
                onDone: buildReference =>
                {
                    Assert.That(buildReference.JobName.Value, Is.EqualTo("MyTestJob"));
                    Assert.That(buildReference.BuildNumber, Is.EqualTo(buildNumber));
                }
            );
            done.Match(
                onPending: _ =>
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
            Assert.That(() => done.DoneBaseline(RandomData.NextBuildNumber), Throws.InvalidOperationException.And.Message.EqualTo("Already done"));
        }
    }

    [Test]
    public void Equals_Works()
    {
        var pending1 = BaseTestBuildReference.Create(new("JobA"));
        var pending2 = BaseTestBuildReference.Create(new("JobA"));
        var pending3 = BaseTestBuildReference.Create(new("JobB"));
        Assert.That(pending1, Is.EqualTo(pending2));
        Assert.That(pending1, Is.Not.EqualTo(pending3));

        var done1 = pending1.DoneBaseline(100);
        var done2 = pending2.DoneBaseline(100);
        var done3 = pending1.DoneBaseline(200);
        Assert.That(done1, Is.EqualTo(done2));
        Assert.That(done1, Is.Not.EqualTo(done3));
        Assert.That(pending1, Is.Not.EqualTo(done1));
        Assert.That(done1, Is.Not.EqualTo(pending1));
    }
}
