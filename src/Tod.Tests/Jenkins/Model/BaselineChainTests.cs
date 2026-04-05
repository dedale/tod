using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins.Model;

[TestFixture]
internal sealed class BaselineChainTests
{
    private readonly JobName _testJob1 = new("TestJob1");
    private readonly JobName _testJob2 = new("TestJob2");
    private readonly JobName _rootJob = new("RootJob");

    [Test]
    public void Constructor_InitializesPropertiesCorrectly()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1),
            [_testJob2] = BaseTestBuildReference.Create(_testJob2)
        };

        var chain = new BaselineChain(rootBuild, true, testBuilds);

        Assert.That(chain.RootBuild, Is.EqualTo(rootBuild));
        Assert.That(chain.RootBuildSucceeded, Is.True);
        Assert.That(chain.TestBuilds, Is.EqualTo(testBuilds));
        Assert.That(chain.ReportSent, Is.False);
    }

    [Test]
    public void Constructor_WithReportSent_SetsReportSentFlag()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>();

        var chain = new BaselineChain(rootBuild, true, testBuilds, ReportSent: true);

        Assert.That(chain.ReportSent, Is.True);
    }

    [Test]
    public void AllTestsDone_ReturnsFalse_WhenTestsArePending()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1),
            [_testJob2] = BaseTestBuildReference.Create(_testJob2)
        };

        var chain = new BaselineChain(rootBuild, true, testBuilds);

        Assert.That(chain.AllTestsDone, Is.False);
    }

    [Test]
    public void AllTestsDone_ReturnsTrue_WhenAllTestsAreDone()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuild1 = new BuildReference(_testJob1, 50);
        var testBuild2 = new BuildReference(_testJob2, 51);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1).DoneBaseline(testBuild1.BuildNumber),
            [_testJob2] = BaseTestBuildReference.Create(_testJob2).DoneBaseline(testBuild2.BuildNumber)
        };

        var chain = new BaselineChain(rootBuild, true, testBuilds);

        Assert.That(chain.AllTestsDone, Is.True);
    }

    [Test]
    public void AllTestsDone_ReturnsTrue_WhenNoTestBuilds()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>();

        var chain = new BaselineChain(rootBuild, true, testBuilds);

        Assert.That(chain.AllTestsDone, Is.True);
    }

    [Test]
    public void AllTestsDone_ReturnsFalse_WhenRootBuildFailed()
    {
        var rootBuild = new BuildReference(_rootJob, 100);

        var chain = new BaselineChain(rootBuild, false, []);

        Assert.That(chain.AllTestsDone, Is.False);
    }

    [Test]
    public void MarkTestDone_UpdatesTestBuildReference()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1),
            [_testJob2] = BaseTestBuildReference.Create(_testJob2)
        };
        var chain = new BaselineChain(rootBuild, true, testBuilds);
        var testBuild = new BuildReference(_testJob1, 50);

        var updated = chain.MarkTestDone(_testJob1, testBuild);

        Assert.That(updated.TestBuilds[_testJob1].IsDone, Is.True);
        Assert.That(updated.TestBuilds[_testJob2].IsDone, Is.False);
    }

    [Test]
    public void MarkTestDone_ReturnsNewInstance()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1)
        };
        var chain = new BaselineChain(rootBuild, true, testBuilds);
        var testBuild = new BuildReference(_testJob1, 50);

        var updated = chain.MarkTestDone(_testJob1, testBuild);

        Assert.That(updated, Is.Not.SameAs(chain));
        Assert.That(chain.TestBuilds[_testJob1].IsDone, Is.False);
        Assert.That(updated.TestBuilds[_testJob1].IsDone, Is.True);
    }

    [Test]
    public void MarkTestDone_ReturnsSameInstance_WhenTestJobNotFound()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1)
        };
        var chain = new BaselineChain(rootBuild, true, testBuilds);
        var nonExistentJob = new JobName("NonExistent");
        var testBuild = new BuildReference(nonExistentJob, 50);

        var updated = chain.MarkTestDone(nonExistentJob, testBuild);

        Assert.That(updated, Is.SameAs(chain));
    }

    [Test]
    public void MarkTestDone_DoesNotModifyOriginalDictionary()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1),
            [_testJob2] = BaseTestBuildReference.Create(_testJob2)
        };
        var chain = new BaselineChain(rootBuild, true, testBuilds);
        var testBuild = new BuildReference(_testJob1, 50);

        var updated = chain.MarkTestDone(_testJob1, testBuild);

        Assert.That(chain.TestBuilds[_testJob1].IsDone, Is.False);
        Assert.That(updated.TestBuilds[_testJob1].IsDone, Is.True);
        Assert.That(updated.TestBuilds[_testJob2].IsDone, Is.False);
    }

    [Test]
    public void MarkReportSent_SetsReportSentFlag()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>();
        var chain = new BaselineChain(rootBuild, true, testBuilds);

        var updated = chain.MarkReportSent();

        Assert.That(chain.ReportSent, Is.False);
        Assert.That(updated.ReportSent, Is.True);
    }

    [Test]
    public void MarkReportSent_ReturnsNewInstance()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>();
        var chain = new BaselineChain(rootBuild, true, testBuilds);

        var updated = chain.MarkReportSent();

        Assert.That(updated, Is.Not.SameAs(chain));
    }

    [Test]
    public void Serialization_RoundTrip_PreservesAllProperties()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuild = new BuildReference(_testJob1, 50);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1).DoneBaseline(testBuild.BuildNumber),
            [_testJob2] = BaseTestBuildReference.Create(_testJob2)
        };
        var chain = new BaselineChain(rootBuild, false, testBuilds, ReportSent: true);

        var serializable = chain.ToSerializable();
        var restored = serializable.FromSerializable();

        Assert.That(restored.RootBuild, Is.EqualTo(chain.RootBuild));
        Assert.That(restored.RootBuildSucceeded, Is.EqualTo(chain.RootBuildSucceeded));
        Assert.That(restored.ReportSent, Is.EqualTo(chain.ReportSent));
        Assert.That(restored.TestBuilds.Count, Is.EqualTo(chain.TestBuilds.Count));
        Assert.That(restored.TestBuilds[_testJob1].IsDone, Is.True);
        Assert.That(restored.TestBuilds[_testJob2].IsDone, Is.False);
    }

    [Test]
    public void Serialization_PreservesTestBuildReferences()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuild1 = new BuildReference(_testJob1, 50);
        var testBuild2 = new BuildReference(_testJob2, 51);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1).DoneBaseline(testBuild1.BuildNumber),
            [_testJob2] = BaseTestBuildReference.Create(_testJob2).DoneBaseline(testBuild2.BuildNumber)
        };
        var chain = new BaselineChain(rootBuild, true, testBuilds);

        var serializable = chain.ToSerializable();
        var restored = serializable.FromSerializable();

        Assert.That(restored.AllTestsDone, Is.True);
        restored.TestBuilds[_testJob1].Match(
            onPending: _ => Assert.Fail("Expected done reference"),
            onDone: br => Assert.That(br.BuildNumber, Is.EqualTo(50))
        );
        restored.TestBuilds[_testJob2].Match(
            onPending: _ => Assert.Fail("Expected done reference"),
            onDone: br => Assert.That(br.BuildNumber, Is.EqualTo(51))
        );
    }

    [Test]
    public void Serialization_PreservesPendingTestBuilds()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1),
            [_testJob2] = BaseTestBuildReference.Create(_testJob2)
        };
        var chain = new BaselineChain(rootBuild, true, testBuilds);

        var serializable = chain.ToSerializable();
        var restored = serializable.FromSerializable();

        Assert.That(restored.AllTestsDone, Is.False);
        restored.TestBuilds[_testJob1].Match(
            onPending: job => Assert.That(job, Is.EqualTo(_testJob1)),
            onDone: _ => Assert.Fail("Expected pending reference")
        );
        restored.TestBuilds[_testJob2].Match(
            onPending: job => Assert.That(job, Is.EqualTo(_testJob2)),
            onDone: _ => Assert.Fail("Expected pending reference")
        );
    }

    [Test]
    public void Serialization_WithEmptyTestBuilds_Works()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>();
        var chain = new BaselineChain(rootBuild, true, testBuilds);

        var serializable = chain.ToSerializable();
        var restored = serializable.FromSerializable();

        Assert.That(restored.RootBuild, Is.EqualTo(chain.RootBuild));
        Assert.That(restored.TestBuilds.Count, Is.EqualTo(0));
        Assert.That(restored.AllTestsDone, Is.True);
    }

    [Test]
    public void RecordEquality_SameInstance_IsEqual()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1)
        };

        var chain1 = new BaselineChain(rootBuild, true, testBuilds);
        var chain2 = chain1;

        Assert.That(chain1, Is.EqualTo(chain2));
    }

    [Test]
    public void RecordEquality_WithExpression_SharesTestBuildsDictionary()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1)
        };

        var chain1 = new BaselineChain(rootBuild, true, testBuilds);
        var chain2 = chain1 with { RootBuildSucceeded = false };

        Assert.That(chain1.TestBuilds, Is.SameAs(chain2.TestBuilds));
        Assert.That(chain1.RootBuildSucceeded, Is.True);
        Assert.That(chain2.RootBuildSucceeded, Is.False);
    }

    [Test]
    public void RecordEquality_DifferentWhenReportSentDiffers()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>();

        var chain1 = new BaselineChain(rootBuild, true, testBuilds, ReportSent: false);
        var chain2 = new BaselineChain(rootBuild, true, testBuilds, ReportSent: true);

        Assert.That(chain1, Is.Not.EqualTo(chain2));
    }

    [Test]
    public void WithExpression_CreatesNewInstanceWithChanges()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>();
        var chain = new BaselineChain(rootBuild, true, testBuilds);

        var modified = chain with { ReportSent = true };

        Assert.That(chain.ReportSent, Is.False);
        Assert.That(modified.ReportSent, Is.True);
        Assert.That(modified.RootBuild, Is.EqualTo(chain.RootBuild));
    }

    [Test]
    public void MarkTestDone_MultipleCalls_UpdatesCorrectly()
    {
        var rootBuild = new BuildReference(_rootJob, 100);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1),
            [_testJob2] = BaseTestBuildReference.Create(_testJob2)
        };
        var chain = new BaselineChain(rootBuild, true, testBuilds);
        var testBuild1 = new BuildReference(_testJob1, 50);
        var testBuild2 = new BuildReference(_testJob2, 51);

        var updated1 = chain.MarkTestDone(_testJob1, testBuild1);
        var updated2 = updated1.MarkTestDone(_testJob2, testBuild2);

        Assert.That(chain.AllTestsDone, Is.False);
        Assert.That(updated1.AllTestsDone, Is.False);
        Assert.That(updated2.AllTestsDone, Is.True);
    }
}
