using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class RequestReportBuilderTests
{
    private readonly BranchName mainBranch = new("main");

    private readonly JobName mainBuildJob = new("MAIN-build");
    private readonly JobName mainTestJob = new("MAIN-test");
    private readonly JobName customBuildJob = new("CUSTOM-build");
    private readonly JobName customTestJob = new("CUSTOM-test");

    // Helper method to create a simple RequestState
    private RequestState CreateRequestState(
        BuildReference? onDemandRoot = null,
        RequestBuildDiff[]? buildDiffs = null)
    {
        var request = Request.Create(
            RandomData.NextSha1(),
            RandomData.NextSha1(),
            mainBranch,
            ["test"]);
        
        return RequestState.New(
            request,
            new BuildReference(mainBuildJob, RandomData.NextBuildNumber),
            onDemandRoot ?? new BuildReference(customBuildJob, RandomData.NextBuildNumber),
            buildDiffs?.ToList() ?? [new RequestBuildDiff(mainTestJob, customTestJob)]);
    }

    [TestCase(true, BuildStatus.Success)]
    [TestCase(false, BuildStatus.Failed)]
    public void New_OnDemandRootDone_ReturnsRootResultWithDoneStatus(bool success, BuildStatus status)
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(mainBranch, mainBuildJob, out var referenceStore)
            .WithOnDemandStore(customBuildJob, out var onDemandStore)
            .WithNewRootBuilds(customBuildJob)
            .WithNewTestBuilds(customTestJob);

        // Arrange
        var onDemandRoot = new BuildReference(customBuildJob, 100);
        var requestState = CreateRequestState(onDemandRoot: onDemandRoot)
            .TriggerTests((job, commit) => Task.FromResult(RandomData.NextBuildNumber));

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(mainBuildJob);
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(customBuildJob);

        var rootBuild = RandomData.NextRootBuild(customBuildJob.Value, 100, isSuccessful: success, testJobNames: [ customTestJob.Value ]);
        onDemandBuilds.TryAdd(rootBuild);

        // Act
        var report = new RequestReportBuilder().Build(requestState, branchReference, onDemandBuilds);

        // Assert
        Assert.That(report.ChainReports[0].RootResult.JobName.Value, Is.EqualTo(customBuildJob.Value));
        Assert.That(report.ChainReports[0].RootResult.Number, Is.EqualTo(100));
        Assert.That(report.ChainReports[0].RootResult.Status, Is.EqualTo(status));
    }

    [Test]
    public void New_OnDemandRootTriggered_ReturnsRootResultWithTriggeredStatus()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(mainBranch, mainBuildJob, out var referenceStore)
            .WithOnDemandStore(customBuildJob, out var onDemandStore);

        // Arrange
        var onDemandRoot = new BuildReference(customBuildJob, 100);
        var requestState = CreateRequestState(onDemandRoot: onDemandRoot);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(mainBuildJob);
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(customBuildJob);

        // Act
        var report = new RequestReportBuilder().Build(requestState, branchReference, onDemandBuilds);

        // Assert
        Assert.That(report.ChainReports[0].RootResult.JobName.Value, Is.EqualTo(customBuildJob.Value));
        Assert.That(report.ChainReports[0].RootResult.Number, Is.EqualTo(100));
        Assert.That(report.ChainReports[0].RootResult.Status, Is.EqualTo(BuildStatus.Triggered));
    }

    [Test]
    public void New_OnDemandTestBuildPending_ReturnsOnDemandPendingDiff()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(mainBranch, mainBuildJob, out var referenceStore)
            .WithOnDemandStore(customBuildJob, out var onDemandStore);

        // Arrange
        var requestState = CreateRequestState();
        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(mainBuildJob);
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(customBuildJob);

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
        using var mocks = StoreMocks.New()
            .WithReferenceStore(mainBranch, mainBuildJob, out var referenceStore)
            .WithOnDemandStore(customBuildJob, out var onDemandStore);

        // Arrange
        var testBuildNumber = RandomData.NextBuildNumber;
        var buildDiff = new RequestBuildDiff(mainTestJob, customTestJob)
            .TriggerOnDemand(testBuildNumber);
        var requestState = CreateRequestState(buildDiffs: [buildDiff]);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(mainBuildJob);
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(customBuildJob);

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
        using var mocks = StoreMocks.New()
            .WithReferenceStore(mainBranch, mainBuildJob, out var referenceStore)
            .WithNewTestBuilds(mainTestJob)
            .WithOnDemandStore(customBuildJob, out var onDemandStore)
            .WithNewTestBuilds(customTestJob);

        // Arrange
        var onDemandBuildNumber = RandomData.NextBuildNumber;
        var buildDiff = new RequestBuildDiff(mainTestJob, customTestJob)
            .TriggerOnDemand(onDemandBuildNumber)
            .DoneOnDemand();
        var requestState = CreateRequestState(buildDiffs: [buildDiff]);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(mainBuildJob);
        branchReference.TryAddTest(mainTestJob);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(customBuildJob);
        onDemandBuilds.TryAddTest(customTestJob);
        var testBuild = RandomData.NextTestBuild(customTestJob.Value, onDemandBuildNumber);
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
        using var mocks = StoreMocks.New()
            .WithReferenceStore(mainBranch, mainBuildJob, out var referenceStore)
            .WithNewTestBuilds(mainTestJob)
            .WithOnDemandStore(customBuildJob, out var onDemandStore)
            .WithNewTestBuilds(customTestJob);

        // Arrange
        var onDemandBuildNumber = RandomData.NextBuildNumber;
        var referenceBuildNumber = RandomData.NextBuildNumber;
        
        var buildDiff = new RequestBuildDiff(mainTestJob, customTestJob)
            .DoneReference(referenceBuildNumber)
            .TriggerOnDemand(onDemandBuildNumber)
            .DoneOnDemand();
        var requestState = CreateRequestState(buildDiffs: [buildDiff]);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(mainBuildJob);
        branchReference.TryAddTest(mainTestJob);
        var refTestBuild = RandomData.NextTestBuild(mainTestJob.Value, referenceBuildNumber);
        branchReference.TryAdd(refTestBuild);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(customBuildJob);
        onDemandBuilds.TryAddTest(customTestJob);
        var onDemandTestBuild = RandomData.NextTestBuild(customTestJob.Value, onDemandBuildNumber);
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
        using var mocks = StoreMocks.New()
            .WithReferenceStore(mainBranch, mainBuildJob, out var referenceStore)
            .WithNewTestBuilds(mainTestJob)
            .WithOnDemandStore(customBuildJob, out var onDemandStore)
            .WithNewTestBuilds(customTestJob);

        // Arrange
        var onDemandBuildNumber = RandomData.NextBuildNumber;
        var referenceBuildNumber = RandomData.NextBuildNumber;
        
        var buildDiff = new RequestBuildDiff(mainTestJob, customTestJob)
            .DoneReference(referenceBuildNumber)
            .TriggerOnDemand(onDemandBuildNumber)
            .DoneOnDemand();
        var requestState = CreateRequestState(buildDiffs: [buildDiff]);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(mainBuildJob);
        branchReference.TryAddTest(mainTestJob);
        var refTestBuild = new TestBuild(
            mainTestJob,
            "ref-id",
            referenceBuildNumber,
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow,
            false,
            new BuildReference(mainBuildJob, 1),
            [new FailedTest("ClassA", "Test1", "Old error")]);
        branchReference.TryAdd(refTestBuild);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(customBuildJob);
        onDemandBuilds.TryAddTest(customTestJob);
        var onDemandTestBuild = new TestBuild(
            customTestJob,
            "custom-id",
            onDemandBuildNumber,
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow,
            false,
            new BuildReference(customBuildJob, 1),
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
        using var mocks = StoreMocks.New()
            .WithReferenceStore(mainBranch, mainBuildJob, out var referenceStore)
            .WithOnDemandStore(customBuildJob, out var onDemandStore)
            .WithNewTestBuilds(customTestJob);

        // Arrange
        var buildDiff1 = new RequestBuildDiff(new JobName("MAIN-test1"), new JobName("CUSTOM-test1"));
        var buildDiff2 = new RequestBuildDiff(new JobName("MAIN-test2"), new JobName("CUSTOM-test2"));
        var requestState = CreateRequestState(buildDiffs: [buildDiff1, buildDiff2]);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(mainBuildJob);
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(customBuildJob);

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
        using var mocks = StoreMocks.New()
            .WithReferenceStore(mainBranch, mainBuildJob, out var referenceStore)
            .WithOnDemandStore(customBuildJob, out var onDemandStore)
            .WithNewTestBuilds(customTestJob);

        var onDemandBuildNumber = RandomData.NextBuildNumber;
        var builDiff = new RequestBuildDiff(mainTestJob, customTestJob)
            .TriggerOnDemand(onDemandBuildNumber)
            .DoneOnDemand();
        
        var serializable = builDiff.ToSerializable();
        serializable.ReferenceBuild = builDiff.ReferenceBuild.Trigger(200).ToSerializable();
        var corruptedBuildDiff = serializable.FromSerializable();

        var requestState = CreateRequestState(buildDiffs: [corruptedBuildDiff]);
        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(mainBuildJob);
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(customBuildJob);
        var testBuild = RandomData.NextTestBuild(customTestJob.Value, onDemandBuildNumber);
        onDemandBuilds.TryAdd(testBuild);

        Assert.That(() => new RequestReportBuilder().Build(requestState, branchReference, onDemandBuilds), Throws.InvalidOperationException);
    }
}