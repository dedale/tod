using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class RequestReportBuilderTests
{
    // Helper method to create a simple RequestState
    private static RequestState CreateRequestState(
        BuildReference? onDemandRoot = null,
        RequestBuildDiff[]? buildDiffs = null)
    {
        var request = Request.Create(
            RandomData.NextSha1(),
            RandomData.NextSha1(),
            new BranchName("main"),
            ["test"]);
        
        return RequestState.New(
            request,
            new BuildReference("MAIN-build", RandomData.NextBuildNumber),
            onDemandRoot ?? new BuildReference("CUSTOM-build", RandomData.NextBuildNumber),
            buildDiffs?.ToList() ?? [new RequestBuildDiff(new("MAIN-test"), new("CUSTOM-test"))]);
    }

    [TestCase(true, BuildStatus.Success)]
    [TestCase(false, BuildStatus.Failed)]
    public void New_OnDemandRootDone_ReturnsRootResultWithDoneStatus(bool success, BuildStatus status)
    {
        // Arrange
        var onDemandRoot = new BuildReference("CUSTOM-build", 100);
        var requestState = CreateRequestState(onDemandRoot: onDemandRoot)
            .TriggerTests((job, commit) => Task.FromResult(RandomData.NextBuildNumber));

        var branchReference = new BranchReference(new BranchName("main"), new JobName("MAIN-build"));
        var onDemandBuilds = new OnDemandBuilds(new JobName("CUSTOM-build"));
        var rootBuild = RandomData.NextRootBuild("CUSTOM-build", 100, isSuccessful: success);
        onDemandBuilds.TryAdd(rootBuild);

        // Act
        var report = new RequestReportBuilder().Build(requestState, branchReference, onDemandBuilds);

        // Assert
        Assert.That(report.ChainReports[0].RootResult.JobName.Value, Is.EqualTo("CUSTOM-build"));
        Assert.That(report.ChainReports[0].RootResult.Number, Is.EqualTo(100));
        Assert.That(report.ChainReports[0].RootResult.Status, Is.EqualTo(status));
    }

    [Test]
    public void New_OnDemandRootTriggered_ReturnsRootResultWithTriggeredStatus()
    {
        // Arrange
        var onDemandRoot = new BuildReference("CUSTOM-build", 100);
        var requestState = CreateRequestState(onDemandRoot: onDemandRoot);
        
        var branchReference = new BranchReference(new BranchName("main"), new JobName("MAIN-build"));
        var onDemandBuilds = new OnDemandBuilds(new JobName("CUSTOM-build"));

        // Act
        var report = new RequestReportBuilder().Build(requestState, branchReference, onDemandBuilds);

        // Assert
        Assert.That(report.ChainReports[0].RootResult.JobName.Value, Is.EqualTo("CUSTOM-build"));
        Assert.That(report.ChainReports[0].RootResult.Number, Is.EqualTo(100));
        Assert.That(report.ChainReports[0].RootResult.Status, Is.EqualTo(BuildStatus.Triggered));
    }

    [Test]
    public void New_OnDemandTestBuildPending_ReturnsOnDemandPendingDiff()
    {
        // Arrange
        var requestState = CreateRequestState();
        var branchReference = new BranchReference(new BranchName("main"), new JobName("MAIN-build"));
        var onDemandBuilds = new OnDemandBuilds(new JobName("CUSTOM-build"));

        // Act
        var report = new RequestReportBuilder().Build(requestState, branchReference, onDemandBuilds);

        // Assert
        var chainReport = report.ChainReports[0];
        Assert.That(chainReport.BuildDiffs, Has.Length.EqualTo(1));
        Assert.That(chainReport.BuildDiffs[0].Result.Status, Is.EqualTo(BuildStatus.Pending));

        var message = chainReport.BuildDiffs[0].Diff.Match(
            onNotComparable: msg => msg,
            onComparable: _ => "");
        Assert.That(message, Is.EqualTo("Build not run"));
    }

    [Test]
    public void New_OnDemandTestBuildTriggered_ReturnsOnDemandTriggeredDiff()
    {
        // Arrange
        var testBuildNumber = RandomData.NextBuildNumber;
        var buildDiff = new RequestBuildDiff(new JobName("MAIN-test"), new JobName("CUSTOM-test"))
            .TriggerOnDemand(testBuildNumber);
        var requestState = CreateRequestState(buildDiffs: [buildDiff]);
        
        var branchReference = new BranchReference(new BranchName("main"), new JobName("MAIN-build"));
        var onDemandBuilds = new OnDemandBuilds(new JobName("CUSTOM-build"));

        // Act
        var report = new RequestReportBuilder().Build(requestState, branchReference, onDemandBuilds);

        // Assert
        var chainReport = report.ChainReports[0];
        Assert.That(chainReport.BuildDiffs, Has.Length.EqualTo(1));
        Assert.That(chainReport.BuildDiffs[0].Result.Status, Is.EqualTo(BuildStatus.Triggered));
        Assert.That(chainReport.BuildDiffs[0].Result.Number, Is.EqualTo(testBuildNumber));

        var message = chainReport.BuildDiffs[0].Diff.Match(
            onNotComparable: msg => msg,
            onComparable: _ => "");
        Assert.That(message, Is.EqualTo($"Build #{testBuildNumber} not done"));
    }

    [Test]
    public void New_ReferenceTestBuildPending_ReturnsReferencePendingDiff()
    {
        // Arrange
        var onDemandBuildNumber = RandomData.NextBuildNumber;
        var buildDiff = new RequestBuildDiff(new JobName("MAIN-test"), new JobName("CUSTOM-test"))
            .TriggerOnDemand(onDemandBuildNumber)
            .DoneOnDemand();
        var requestState = CreateRequestState(buildDiffs: [buildDiff]);
        
        var branchReference = new BranchReference(new BranchName("main"), new JobName("MAIN-build"));
        branchReference.TryAdd(new JobName("MAIN-test"));
        
        var onDemandBuilds = new OnDemandBuilds(new JobName("CUSTOM-build"));
        onDemandBuilds.TryAdd(new JobName("CUSTOM-test"));
        var testBuild = RandomData.NextTestBuild("CUSTOM-test", onDemandBuildNumber);
        onDemandBuilds.TryAdd(testBuild);

        // Act
        var report = new RequestReportBuilder().Build(requestState, branchReference, onDemandBuilds);

        // Assert
        var chainReport = report.ChainReports[0];
        Assert.That(chainReport.BuildDiffs, Has.Length.EqualTo(1));
        Assert.That(chainReport.BuildDiffs[0].Result.Status, Is.EqualTo(BuildStatus.Success));

        var message = chainReport.BuildDiffs[0].Diff.Match(
            onNotComparable: msg => msg,
            onComparable: _ => "");
        Assert.That(message, Is.EqualTo("No reference build"));
    }

    [Test]
    public void New_BothTestBuildsDone_ReturnsComparableDiff()
    {
        // Arrange
        var onDemandBuildNumber = RandomData.NextBuildNumber;
        var referenceBuildNumber = RandomData.NextBuildNumber;
        
        var buildDiff = new RequestBuildDiff(new JobName("MAIN-test"), new JobName("CUSTOM-test"))
            .DoneReference(referenceBuildNumber)
            .TriggerOnDemand(onDemandBuildNumber)
            .DoneOnDemand();
        var requestState = CreateRequestState(buildDiffs: [buildDiff]);
        
        var branchReference = new BranchReference(new BranchName("main"), new JobName("MAIN-build"));
        branchReference.TryAdd(new JobName("MAIN-test"));
        var refTestBuild = RandomData.NextTestBuild("MAIN-test", referenceBuildNumber);
        branchReference.TryAdd(refTestBuild);
        
        var onDemandBuilds = new OnDemandBuilds(new JobName("CUSTOM-build"));
        onDemandBuilds.TryAdd(new JobName("CUSTOM-test"));
        var onDemandTestBuild = RandomData.NextTestBuild("CUSTOM-test", onDemandBuildNumber);
        onDemandBuilds.TryAdd(onDemandTestBuild);

        // Act
        var report = new RequestReportBuilder().Build(requestState, branchReference, onDemandBuilds);

        // Assert
        var chainReport = report.ChainReports[0];
        Assert.That(chainReport.BuildDiffs, Has.Length.EqualTo(1));
        Assert.That(chainReport.BuildDiffs[0].Result.Status, Is.EqualTo(BuildStatus.Success));

        var diffResult = chainReport.BuildDiffs[0].Diff.Match(
            onNotComparable: _ => (FailedTestDiff?)null,
            onComparable: diff => diff);
        Assert.That(diffResult, Is.Not.Null);
    }

    [Test]
    public void New_BothTestBuildsDoneWithNewFailures_ReturnsDiffWithAddedTests()
    {
        // Arrange
        var onDemandBuildNumber = RandomData.NextBuildNumber;
        var referenceBuildNumber = RandomData.NextBuildNumber;
        
        var buildDiff = new RequestBuildDiff(new JobName("MAIN-test"), new JobName("CUSTOM-test"))
            .DoneReference(referenceBuildNumber)
            .TriggerOnDemand(onDemandBuildNumber)
            .DoneOnDemand();
        var requestState = CreateRequestState(buildDiffs: [buildDiff]);
        
        var branchReference = new BranchReference(new BranchName("main"), new JobName("MAIN-build"));
        branchReference.TryAdd(new JobName("MAIN-test"));
        var refTestBuild = new TestBuild(
            new JobName("MAIN-test"),
            "ref-id",
            referenceBuildNumber,
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow,
            false,
            new BuildReference("MAIN-build", 1),
            [new FailedTest("ClassA", "Test1", "Old error")]);
        branchReference.TryAdd(refTestBuild);
        
        var onDemandBuilds = new OnDemandBuilds(new JobName("CUSTOM-build"));
        onDemandBuilds.TryAdd(new JobName("CUSTOM-test"));
        var onDemandTestBuild = new TestBuild(
            new JobName("CUSTOM-test"),
            "custom-id",
            onDemandBuildNumber,
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow,
            false,
            new BuildReference("CUSTOM-build", 1),
            [
                new FailedTest("ClassA", "Test1", "Old error"),
                new FailedTest("ClassB", "Test2", "New error")
            ]);
        onDemandBuilds.TryAdd(onDemandTestBuild);

        // Act
        var report = new RequestReportBuilder().Build(requestState, branchReference, onDemandBuilds);

        // Assert
        var chainReport = report.ChainReports[0];
        Assert.That(chainReport.BuildDiffs, Has.Length.EqualTo(1));
        Assert.That(chainReport.BuildDiffs[0].Result.Status, Is.EqualTo(BuildStatus.Failed));

        var diffResult = chainReport.BuildDiffs[0].Diff.Match(
            onNotComparable: _ => (FailedTestDiff?)null,
            onComparable: diff => diff);
        Assert.That(diffResult, Is.Not.Null);
        Assert.That(diffResult!.Added, Has.Length.EqualTo(1));
        Assert.That(diffResult.Added[0].ClassName, Is.EqualTo("ClassB"));
        Assert.That(diffResult.Added[0].TestName, Is.EqualTo("Test2"));
        Assert.That(diffResult.Updated, Is.Empty);
    }

    [Test]
    public void New_MultipleTestBuilds_ReturnsMultipleBuildDiffs()
    {
        // Arrange
        var buildDiff1 = new RequestBuildDiff(new JobName("MAIN-test1"), new JobName("CUSTOM-test1"));
        var buildDiff2 = new RequestBuildDiff(new JobName("MAIN-test2"), new JobName("CUSTOM-test2"));
        var requestState = CreateRequestState(buildDiffs: [buildDiff1, buildDiff2]);
        
        var branchReference = new BranchReference(new BranchName("main"), new JobName("MAIN-build"));
        var onDemandBuilds = new OnDemandBuilds(new JobName("CUSTOM-build"));

        // Act
        var report = new RequestReportBuilder().Build(requestState, branchReference, onDemandBuilds);

        // Assert
        var chainReport = report.ChainReports[0];
        Assert.That(chainReport.BuildDiffs, Has.Length.EqualTo(2));
        Assert.That(chainReport.BuildDiffs[0].Result.JobName.Value, Is.EqualTo("CUSTOM-test1"));
        Assert.That(chainReport.BuildDiffs[1].Result.JobName.Value, Is.EqualTo("CUSTOM-test2"));
    }

    [Test]
    public void New_UnexpectedTriggeredRef_Throws()
    {
        var onDemandBuildNumber = RandomData.NextBuildNumber;
        var builDiff = new RequestBuildDiff(new JobName("MAIN-test"), new JobName("CUSTOM-test"))
            .TriggerOnDemand(onDemandBuildNumber)
            .DoneOnDemand();
        
        var serializable = builDiff.ToSerializable();
        serializable.ReferenceBuild = builDiff.ReferenceBuild.Trigger(200).ToSerializable();
        var corruptedBuildDiff = serializable.FromSerializable();

        var requestState = CreateRequestState(buildDiffs: [corruptedBuildDiff]);
        var branchReference = new BranchReference(new BranchName("main"), new JobName("MAIN-build"));
        var onDemandBuilds = new OnDemandBuilds(new JobName("CUSTOM-build"));
        var testBuild = RandomData.NextTestBuild("CUSTOM-test", onDemandBuildNumber);
        onDemandBuilds.TryAdd(testBuild);

        Assert.That(() => new RequestReportBuilder().Build(requestState, branchReference, onDemandBuilds), Throws.InvalidOperationException);
    }
}