using NUnit.Framework;
using System.Diagnostics;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class BuildDiffTests
{
    [Test]
    public void OnDemandPending_Match_CallsOnNotComparable()
    {
        BuildDiff.OnDemandPending.Match(
            onNotComparable: msg => Assert.That(msg, Is.EqualTo("Build not run")),
            onComparable: _ => Assert.Fail("Should not be comparable"));
    }

    [Test]
    public void OnDemandPending_MatchWithReturn_ReturnsNotComparableResult()
    {
        var result = BuildDiff.OnDemandPending.Match(
            onNotComparable: msg => $"Not comparable: {msg}",
            onComparable: _ => "Comparable");
        Assert.That(result, Is.EqualTo("Not comparable: Build not run"));
    }

    [Test]
    public void OnDemandTriggered_Match_CallsOnNotComparable()
    {
        var jobName = new JobName(Guid.NewGuid().ToString());
        BuildDiff.OnDemandTriggered(jobName).Match(
            onNotComparable: msg => Assert.That(msg, Is.EqualTo($"Build {jobName} not done")),
            onComparable: _ => Assert.Fail("Should not be comparable"));
    }

    [Test]
    public void OnDemandTriggered_MatchWithReturn_ReturnsNotComparableResult()
    {
        var jobName = new JobName(Guid.NewGuid().ToString());
        var result = BuildDiff.OnDemandTriggered(jobName).Match(
            onNotComparable: msg => msg.Length,
            onComparable: _ => 0);
        Assert.That(result, Is.EqualTo($"Build {jobName} not done".Length));
    }

    [Test]
    public void BaselinePending_Match_CallsOnNotComparable()
    {
        BuildDiff.BaselinePending.Match(
            onNotComparable: msg => Assert.That(msg, Is.EqualTo("No baseline build")),
            onComparable: _ => Assert.Fail("Should not be comparable"));
    }

    [Test]
    public void BaselinePending_MatchWithReturn_ReturnsNotComparableResult()
    {
        var result = BuildDiff.BaselinePending.Match(
            onNotComparable: msg => msg,
            onComparable: _ => "");
        Assert.That(result, Is.EqualTo("No baseline build"));
    }

    [Test]
    public void Diff_Match_CallsOnComparable()
    {
        var failedTestDiff = new FailedTestDiff(
            TestBuildDiffStatus.NewFailures,
            [new FailedTestResult(new FailedTest("ClassA", "Test1", "Error"), Newness.Updated, false)]);
        BuildDiff.Diff(failedTestDiff).Match(
            onNotComparable: _ => Assert.Fail("Should be comparable"),
            onComparable: diff => Assert.That(diff, Is.SameAs(failedTestDiff)));
    }

    [Test]
    public void Diff_MatchWithReturn_ReturnsComparableResult()
    {
        // Arrange
        var failedTestDiff = new FailedTestDiff(
            TestBuildDiffStatus.UpdatedFailures,
            [new FailedTestResult(new FailedTest("ClassB", "Test2", "Updated error"), Newness.Updated, false)]);
        var result = BuildDiff.Diff(failedTestDiff).Match(
            onNotComparable: _ => -1,
            onComparable: diff => diff.FailedTests.Length);
        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public void Diff_WithEmptyFailedTestDiff_CallsOnComparable()
    {
        var failedTestDiff = new FailedTestDiff(TestBuildDiffStatus.OK, []);
        var result = BuildDiff.Diff(failedTestDiff).Match(
            onNotComparable: _ => false,
            onComparable: _ => true);
        Assert.That(result, Is.True);
    }

    [Test]
    public void Diff_WithMultipleFailures_PreservesDiffData()
    {
        var updated = new[]
        {
            new FailedTestResult(new FailedTest("ClassA", "Test1", "Updated error 1"), Newness.Updated, false),
            new FailedTestResult(new FailedTest("ClassB", "Test2", "Updated error 2"), Newness.Updated, false)
        };
        var added = new[]
        {
            new FailedTestResult(new FailedTest("ClassC", "Test3", "New error 1"), Newness.New, false),
            new FailedTestResult(new FailedTest("ClassD", "Test4", "New error 2"), Newness.New, false)
        };
        var failedTestDiff = new FailedTestDiff(
            TestBuildDiffStatus.NewFailures | TestBuildDiffStatus.UpdatedFailures,
            updated.Concat(added).ToArray());
        var capturedDiff = BuildDiff.Diff(failedTestDiff).Match(
            onNotComparable: _ => (FailedTestDiff?)null,
            onComparable: diff => diff);
        Assert.That(capturedDiff, Is.Not.Null);
        Debug.Assert(capturedDiff != null);
        Assert.That(capturedDiff.FailedTests.Count(t => t.Newness == Newness.Updated), Is.EqualTo(2));
        Assert.That(capturedDiff.FailedTests.Count(t => t.Newness == Newness.New), Is.EqualTo(2));
        Assert.That(capturedDiff.Status, Is.EqualTo(TestBuildDiffStatus.NewFailures | TestBuildDiffStatus.UpdatedFailures));
    }

    [Test]
    public void OnDemandTriggered_WithDifferentBuildNumbers_ProducesDifferentMessages()
    {
        var jobName1 = new JobName("Job1");
        var jobName2 = new JobName("Job2");
        var buildDiff1 = BuildDiff.OnDemandTriggered(jobName1);
        var buildDiff2 = BuildDiff.OnDemandTriggered(jobName2);
        var message1 = buildDiff1.Match(onNotComparable: msg => msg, onComparable: _ => "");
        var message2 = buildDiff2.Match(onNotComparable: msg => msg, onComparable: _ => "");
        Assert.That(message1, Is.EqualTo($"Build {jobName1} not done"));
        Assert.That(message2, Is.EqualTo($"Build {jobName2} not done"));
        Assert.That(message1, Is.Not.EqualTo(message2));
    }
}
