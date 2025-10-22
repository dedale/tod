using NUnit.Framework;
using System.Diagnostics;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class ReferenceJobPatternTests
{
    [Test]
    public void IsMatch_WithRootJob()
    {
        var config = new ReferenceJobConfig("MAIN-(?<root>build)", new("main"), true);
        var pattern = new ReferenceJobPattern(config);
        Assert.That(pattern.IsMatch(new("MAIN-build"), out var jobMatch), Is.True);
        Assert.That(jobMatch, Is.Not.Null);
        jobMatch.Match(
            onRoot: (branchName, rootName) => Assert.That(branchName.Value, Is.EqualTo("main")),
            onTest: (branchName, testName) => Assert.Fail("Expected root job match")
        );
        jobMatch.Match(
            onRoot: (branchName, rootName) =>
            {
                Assert.That(branchName.Value, Is.EqualTo("main"));
                Assert.That(rootName.Value, Is.EqualTo("build"));
                return 0;
            },
            onTest: (branchName, testName) =>
            {
                Assert.Fail("Expected root job match");
                return 0;
            }
        );
    }

    [Test]
    public void IsMatch_WithTestJob()
    {
        using (Assert.EnterMultipleScope())
        {
            var config = new ReferenceJobConfig("PROD-(?<test>.*)", new("prod"), false);
            var pattern = new ReferenceJobPattern(config);
            Assert.That(pattern.IsMatch(new("PROD-integration-tests"), out var jobMatch), Is.True);
            Assert.That(jobMatch, Is.Not.Null);
            Debug.Assert(jobMatch is not null);
            jobMatch.Match(
                onRoot: (branchName, rootName) => Assert.Fail("Expected test job match"),
                onTest: (branchName, testName) =>
                {
                    Assert.That(branchName.Value, Is.EqualTo("prod"));
                    Assert.That(testName.Value, Is.EqualTo("integration-tests"));
                }
            );
            jobMatch.Match(
                onRoot: (branchName, rootName) =>
                {
                    Assert.Fail("Expected test job match");
                    return 0;
                },
                onTest: (branchName, testName) =>
                {
                    Assert.That(branchName.Value, Is.EqualTo("prod"));
                    Assert.That(testName.Value, Is.EqualTo("integration-tests"));
                    return 0;
                }
            );
        }
    }
}
