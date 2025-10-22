using NUnit.Framework;
using System.Diagnostics;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class JobMatchCollectionTests
{
    private static readonly ReferenceJobConfig[] s_configs =
    [
        new("MAIN-build", new("main"), true),
        new("MAIN-(?<test>.*)", new("main"), false),
        new("PROD-build", new("prod"), true),
        new("PROD-(?<test>.*)", new("prod"), false),
    ];
    private static readonly JobMatchCollection<ReferenceJobMatch, ReferenceJobPattern> s_jobMatchCollection =
        new(s_configs.Select(c => new ReferenceJobPattern(c)));

    [Test]
    public void FindFirst_RefRootJob()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(s_jobMatchCollection.FindFirst(new("MAIN-build"), out var jobMatch), Is.True);
            Assert.That(jobMatch, Is.Not.Null);
            Debug.Assert(jobMatch is not null);
            jobMatch.Match(
                (branch, root) => Assert.That(branch.Value, Is.EqualTo("main")),
                (branch, test) => Assert.Fail("Expected root job match")
            );
        }
    }

    [Test]
    public void FindFirst_TestJob()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(s_jobMatchCollection.FindFirst(new("MAIN-integration-tests"), out var jobMatch), Is.True);
            Assert.That(jobMatch, Is.Not.Null);
            Debug.Assert(jobMatch is not null);
            jobMatch.Match(
                (branch, root) => Assert.Fail("Expected test job match"),
                (branch, test) => {
                    Assert.That(branch.Value, Is.EqualTo("main"));
                    Assert.That(test.Value, Is.EqualTo("integration-tests"));
                }
            );
        }
    }

    [Test]
    public void FindFirst_ProdRootJob()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(s_jobMatchCollection.FindFirst(new("PROD-build"), out var jobMatch), Is.True);
            Assert.That(jobMatch, Is.Not.Null);
            Debug.Assert(jobMatch is not null);
            jobMatch.Match(
                (branch, root) => Assert.That(branch.Value, Is.EqualTo("prod")),
                (branch, test) => Assert.Fail("Expected root job match")
            );
        }
    }

    [Test]
    public void FindFirst_ProdTestJob()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(s_jobMatchCollection.FindFirst(new("PROD-dev-tests"), out var jobMatch), Is.True);
            Assert.That(jobMatch, Is.Not.Null);
            Debug.Assert(jobMatch is not null);
            jobMatch.Match(
                (branch, root) => Assert.Fail("Expected test job match"),
                (branch, test) => {
                    Assert.That(branch.Value, Is.EqualTo("prod"));
                    Assert.That(test.Value, Is.EqualTo("dev-tests"));
                }
            );
        }
    }

    [Test]
    public void FindFirst_UnknownJob_FindNode()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(s_jobMatchCollection.FindFirst(new("CUSTOM-build"), out var jobMatch), Is.False);
            Assert.That(jobMatch, Is.Null);
        }
    }
}
