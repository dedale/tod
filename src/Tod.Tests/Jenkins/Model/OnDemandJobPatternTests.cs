using NUnit.Framework;
using System.Diagnostics;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class OnDemandJobPatternTests
{
    [Test]
    public void IsMatch_WithRootJob()
    {
        var config = new OnDemandJobConfig("CUSTOM-build", true);
        var pattern = new OnDemandJobPattern(config);
        Assert.That(pattern.IsMatch(new JobName("CUSTOM-build"), out var jobMatch), Is.True);
        Assert.That(jobMatch, Is.Not.Null);
        Debug.Assert(jobMatch is not null);
        jobMatch.Match(
            onRoot: rootName => { },
            onTest: testName => Assert.Fail("Expected root job match")
        );
        jobMatch.Match(
            onRoot: rootName =>
            {
                return 0;
            },
            onTest: testName =>
            {
                Assert.Fail("Expected root job match");
                return 0;
            }
        );
    }

    [Test]
    public void IsMatch_WithTestJob()
    {
        var config = new OnDemandJobConfig("CUSTOM-(?<test>.*)", false);
        var pattern = new OnDemandJobPattern(config);
        Assert.That(pattern.IsMatch(new JobName("CUSTOM-dev-tests"), out var jobMatch), Is.True);
        Assert.That(jobMatch, Is.Not.Null);
        Debug.Assert(jobMatch is not null);
        jobMatch.Match(
            onRoot: rootName => Assert.Fail("Expected test job match"),
            onTest: testName => Assert.That(testName.Value, Is.EqualTo("dev-tests"))
        );
        jobMatch.Match(
            onRoot: rootName =>
            {
                Assert.Fail("Expected test job match");
                return 0;
            },
            onTest: testName =>
            {
                Assert.That(testName.Value, Is.EqualTo("dev-tests"));
                return 0;
            }
        );
    }
}
