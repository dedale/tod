using NUnit.Framework;
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
        var buildNumber = RandomData.NextBuildNumber;
        BuildDiff.OnDemandTriggered(buildNumber).Match(
            onNotComparable: msg => Assert.That(msg, Is.EqualTo($"Build #{buildNumber} not done")),
            onComparable: _ => Assert.Fail("Should not be comparable"));
    }

    [Test]
    public void OnDemandTriggered_MatchWithReturn_ReturnsNotComparableResult()
    {
        var buildNumber = RandomData.NextBuildNumber;
        var result = BuildDiff.OnDemandTriggered(buildNumber).Match(
            onNotComparable: msg => msg.Length,
            onComparable: _ => 0);
        Assert.That(result, Is.EqualTo($"Build #{buildNumber} not done".Length));
    }

    [Test]
    public void ReferencePending_Match_CallsOnNotComparable()
    {
        BuildDiff.ReferencePending.Match(
            onNotComparable: msg => Assert.That(msg, Is.EqualTo("No reference build")),
            onComparable: _ => Assert.Fail("Should not be comparable"));
    }

    [Test]
    public void ReferencePending_MatchWithReturn_ReturnsNotComparableResult()
    {
        var result = BuildDiff.ReferencePending.Match(
            onNotComparable: msg => msg,
            onComparable: _ => "");
        Assert.That(result, Is.EqualTo("No reference build"));
    }

    [Test]
    public void Diff_Match_CallsOnComparable()
    {
        var failedTestDiff = new FailedTestDiff(
            TestBuildDiffStatus.NewFailures,
            [],
            [new FailedTest("ClassA", "Test1", "Error")]);
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
            [new FailedTest("ClassB", "Test2", "Updated error")],
            []);
        var result = BuildDiff.Diff(failedTestDiff).Match(
            onNotComparable: _ => -1,
            onComparable: diff => diff.Updated.Length);
        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public void Diff_WithEmptyFailedTestDiff_CallsOnComparable()
    {
        var failedTestDiff = new FailedTestDiff(TestBuildDiffStatus.OK, [], []);
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
            new FailedTest("ClassA", "Test1", "Updated error 1"),
            new FailedTest("ClassB", "Test2", "Updated error 2")
        };
        var added = new[]
        {
            new FailedTest("ClassC", "Test3", "New error 1"),
            new FailedTest("ClassD", "Test4", "New error 2")
        };
        var failedTestDiff = new FailedTestDiff(
            TestBuildDiffStatus.NewFailures | TestBuildDiffStatus.UpdatedFailures,
            updated,
            added);
        var capturedDiff = BuildDiff.Diff(failedTestDiff).Match(
            onNotComparable: _ => (FailedTestDiff?)null,
            onComparable: diff => diff);
        Assert.That(capturedDiff, Is.Not.Null);
        Assert.That(capturedDiff!.Updated, Has.Length.EqualTo(2));
        Assert.That(capturedDiff.Added, Has.Length.EqualTo(2));
        Assert.That(capturedDiff.Status, Is.EqualTo(TestBuildDiffStatus.NewFailures | TestBuildDiffStatus.UpdatedFailures));
    }

    [Test]
    public void OnDemandTriggered_WithDifferentBuildNumbers_ProducesDifferentMessages()
    {
        var buildDiff1 = BuildDiff.OnDemandTriggered(10);
        var buildDiff2 = BuildDiff.OnDemandTriggered(999);
        var message1 = buildDiff1.Match(onNotComparable: msg => msg, onComparable: _ => "");
        var message2 = buildDiff2.Match(onNotComparable: msg => msg, onComparable: _ => "");
        Assert.That(message1, Is.EqualTo("Build #10 not done"));
        Assert.That(message2, Is.EqualTo("Build #999 not done"));
        Assert.That(message1, Is.Not.EqualTo(message2));
    }
}
