using NUnit.Framework;
using System.Diagnostics;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class RequestReportBuilderTests
{
    private readonly BranchName _mainBranch = new("main");

    private readonly JobName _mainBuildJob = new("MAIN-build");
    private readonly JobName _mainTestJob = new("MAIN-test");
    private readonly JobName _onDemandBuildJob = new("CUSTOM-build");
    private readonly JobName _onDemandTestJob = new("CUSTOM-test");

    private static readonly string s_user = "user";
    private static readonly string s_userEmail = $"user@example.org";

    private Task<RequestState> CreateRequestState(IOnDemandStore onDemandStore, BuildReference? referenceRoot = null)
    {
        var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), _mainBranch, ["test"], s_user, s_userEmail);
        var onDemandRoot = RequestRootBuildReference.Queue(_onDemandBuildJob, request.Commit);
        var chains = new RequestChain[] {
            new(
                referenceRoot ?? new BuildReference(_mainBuildJob, RandomData.NextBuildNumber),
                onDemandRoot,
                [ new RequestBuildDiff(_mainTestJob, _onDemandTestJob) ]
            )
        };
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        Func<OnDemandJobKind, JobName, TriggerParameters, Task> triggerBuild = (_, _, _) => Task.CompletedTask;
        return RequestState.New(request, chains, onDemandBuilds, triggerBuild);
    }

    [TestCase(true, BuildStatus.Success)]
    [TestCase(false, BuildStatus.Failed)]
    public async Task New_OnDemandRootDone_ReturnsRootResultWithDoneStatus(bool success, BuildStatus status)
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _mainBuildJob, out var referenceStore)
            .WithOnDemandStore(_onDemandBuildJob, out var onDemandStore)
            .WithNewRootBuilds(_onDemandBuildJob)
            .WithNewTestBuilds(_onDemandTestJob)
            .WithFlakies(out var flakyStore);

        // Arrange
        var onDemandRoot = new BuildReference(_onDemandBuildJob, 100);
        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        requestState = await requestState.TriggerTests(onDemandRoot, job => Task.FromResult(RandomData.NextBuildNumber)).ConfigureAwait(false);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(_mainBuildJob);
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_onDemandBuildJob);

        var rootBuild = RandomData.NextRootBuild(_onDemandBuildJob.Value, 100, isSuccessful: success, testJobNames: [_onDemandTestJob.Value]);
        onDemandBuilds.TryAdd(rootBuild);

        var flakyTests = new FlakyTests(flakyStore);

        // Act
        var report = RequestReportBuilder.Instance.Build(requestState, [branchReference], onDemandBuilds, flakyTests);

        // Assert
        Assert.That(report.ChainReports[0].RootResult.JobName.Value, Is.EqualTo(_onDemandBuildJob.Value));
        Assert.That(report.ChainReports[0].RootResult.Number, Is.EqualTo(100));
        Assert.That(report.ChainReports[0].RootResult.Status, Is.EqualTo(status));
    }

    [Test]
    public async Task New_OnDemandRootTriggered_ReturnsRootResultWithTriggeredStatus()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _mainBuildJob, out var referenceStore)
            .WithOnDemandStore(_onDemandBuildJob, out var onDemandStore)
            .WithRootJobs(_onDemandBuildJob)
            .WithFlakies(out var flakyStore);

        // Arrange
        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(_mainBuildJob);
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_onDemandBuildJob);
        var flakyTests = new FlakyTests(flakyStore);

        // Act
        var report = RequestReportBuilder.Instance.Build(requestState, [branchReference], onDemandBuilds, flakyTests);

        // Assert
        Assert.That(report.ChainReports[0].RootResult.JobName.Value, Is.EqualTo(_onDemandBuildJob.Value));
        Assert.That(report.ChainReports[0].RootResult.Number, Is.EqualTo(0));
        Assert.That(report.ChainReports[0].RootResult.Status, Is.EqualTo(BuildStatus.Triggered));
    }

    [Test]
    public async Task New_OnDemandTestBuildPending_ReturnsOnDemandPendingDiff()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _mainBuildJob, out var referenceStore)
            .WithOnDemandStore(_onDemandBuildJob, out var onDemandStore)
            .WithRootJobs(_onDemandBuildJob)
            .WithFlakies(out var flakyStore);

        // Arrange
        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(_mainBuildJob);
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_onDemandBuildJob);
        var flakyTests = new FlakyTests(flakyStore);

        // Act
        var report = RequestReportBuilder.Instance.Build(requestState, [branchReference], onDemandBuilds, flakyTests);

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
    public async Task New_OnDemandTestBuildTriggered_ReturnsOnDemandTriggeredDiff()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _mainBuildJob, out var referenceStore)
            .WithOnDemandStore(_onDemandBuildJob, out var onDemandStore)
            .WithNewRootBuilds(_onDemandBuildJob)
            .WithTestobs(_onDemandTestJob)
            .WithFlakies(out var flakyStore);

        // Arrange
        var rootBuildNumber = RandomData.NextBuildNumber;
        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        requestState = await requestState.TriggerTests(new(_onDemandBuildJob, rootBuildNumber), job => Task.CompletedTask).ConfigureAwait(false);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(_mainBuildJob);
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_onDemandBuildJob);
        var onDemandRootBuild = RandomData.NextRootBuild(_onDemandBuildJob.Value, rootBuildNumber, testJobNames: [_onDemandTestJob.Value]);
        onDemandBuilds.TryAdd(onDemandRootBuild);
        var flakyTests = new FlakyTests(flakyStore);

        // Act
        var report = RequestReportBuilder.Instance.Build(requestState, [branchReference], onDemandBuilds, flakyTests);

        // Assert
        var chainReport = report.ChainReports[0];
        Assert.That(chainReport.BuildDiffs, Has.Length.EqualTo(1));
        Assert.That(chainReport.BuildDiffs[0].Result.Status, Is.EqualTo(BuildStatus.Triggered));
        Assert.That(chainReport.BuildDiffs[0].Result.Number, Is.EqualTo(0));

        var message = chainReport.BuildDiffs[0].Diff.Match(
            onNotComparable: msg => msg,
            onComparable: _ => "");
        Assert.That(message, Is.EqualTo($"Build {_onDemandTestJob} not done"));
    }

    [Test]
    public async Task New_ReferenceTestBuildPending_ReturnsReferencePendingDiff()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _mainBuildJob, out var referenceStore)
            .WithNewTestBuilds(_mainTestJob)
            .WithOnDemandStore(_onDemandBuildJob, out var onDemandStore)
            .WithNewRootBuilds(_onDemandBuildJob)
            .WithNewTestBuilds(_onDemandTestJob)
            .WithFlakies(out var flakyStore);

        // Arrange
        var onDemandRoot = new BuildReference(_onDemandBuildJob, RandomData.NextBuildNumber);
        var onDemandTest = new BuildReference(_onDemandTestJob, RandomData.NextBuildNumber);

        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        requestState = await requestState.TriggerTests(onDemandRoot, job => Task.CompletedTask).ConfigureAwait(false);
        requestState = requestState.DoneOnDemandTestBuild(onDemandRoot, onDemandTest);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(_mainBuildJob);
        branchReference.TryAddTest(_mainTestJob);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_onDemandBuildJob);
        var onDemandRootBuild = RandomData.NextRootBuild(_onDemandBuildJob.Value, onDemandRoot.BuildNumber, testJobNames: [_onDemandTestJob.Value]);
        onDemandBuilds.TryAdd(onDemandRootBuild);
        onDemandBuilds.TryAddTest(_onDemandTestJob);
        var testBuild = RandomData.NextTestBuild(_onDemandTestJob.Value, onDemandTest.BuildNumber);
        onDemandBuilds.TryAdd(testBuild);
        var flakyTests = new FlakyTests(flakyStore);

        // Act
        var report = RequestReportBuilder.Instance.Build(requestState, [branchReference], onDemandBuilds, flakyTests);

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
    public async Task New_BothTestBuildsDone_ReturnsComparableDiff()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _mainBuildJob, out var referenceStore)
            .WithNewTestBuilds(_mainTestJob)
            .WithOnDemandStore(_onDemandBuildJob, out var onDemandStore)
            .WithNewRootBuilds(_onDemandBuildJob)
            .WithNewTestBuilds(_onDemandTestJob)
            .WithFlakies(out var flakyStore);

        // Arrange
        var referenceRoot = new BuildReference(_mainBuildJob, RandomData.NextBuildNumber);
        var referenceTest = new BuildReference(_mainTestJob, RandomData.NextBuildNumber);
        var onDemandRoot = new BuildReference(_onDemandBuildJob, RandomData.NextBuildNumber);
        var onDemandTest = new BuildReference(_onDemandTestJob, RandomData.NextBuildNumber);

        var requestState = await CreateRequestState(onDemandStore, referenceRoot: referenceRoot).ConfigureAwait(false);
        requestState = requestState.DoneReferenceTestBuild(referenceRoot, referenceTest);
        requestState = await requestState.TriggerTests(onDemandRoot, job => Task.CompletedTask).ConfigureAwait(false);
        requestState = requestState.DoneOnDemandTestBuild(onDemandRoot, onDemandTest);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(_mainBuildJob);
        branchReference.TryAddTest(_mainTestJob);
        var refTestBuild = RandomData.NextTestBuild(_mainTestJob.Value, referenceTest.BuildNumber);
        branchReference.TryAdd(refTestBuild);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_onDemandBuildJob);
        onDemandBuilds.TryAddTest(_onDemandTestJob);
        var onDemandRootBuild = RandomData.NextRootBuild(_onDemandBuildJob.Value, onDemandRoot.BuildNumber, testJobNames: [_onDemandTestJob.Value]);
        onDemandBuilds.TryAdd(onDemandRootBuild);
        var onDemandTestBuild = RandomData.NextTestBuild(_onDemandTestJob.Value, onDemandTest.BuildNumber);
        onDemandBuilds.TryAdd(onDemandTestBuild);

        var flakyTests = new FlakyTests(flakyStore);

        // Act
        var report = RequestReportBuilder.Instance.Build(requestState, [branchReference], onDemandBuilds, flakyTests);

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
    public async Task New_BothTestBuildsDoneWithNewFailures_ReturnsDiffWithAddedTests()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _mainBuildJob, out var referenceStore)
            .WithNewTestBuilds(_mainTestJob)
            .WithOnDemandStore(_onDemandBuildJob, out var onDemandStore)
            .WithNewRootBuilds(_onDemandBuildJob)
            .WithNewTestBuilds(_onDemandTestJob)
            .WithFlakies(out var flakyStore);

        // Arrange
        var referenceRoot = new BuildReference(_mainBuildJob, RandomData.NextBuildNumber);
        var referenceTest = new BuildReference(_mainTestJob, RandomData.NextBuildNumber);
        var onDemandRoot = new BuildReference(_onDemandBuildJob, RandomData.NextBuildNumber);
        var onDemandTest = new BuildReference(_onDemandTestJob, RandomData.NextBuildNumber);

        var requestState = await CreateRequestState(onDemandStore, referenceRoot: referenceRoot).ConfigureAwait(false);
        requestState = requestState.DoneReferenceTestBuild(referenceRoot, referenceTest);
        requestState = await requestState.TriggerTests(onDemandRoot, job => Task.CompletedTask).ConfigureAwait(false);
        requestState = requestState.DoneOnDemandTestBuild(onDemandRoot, onDemandTest);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(_mainBuildJob);
        branchReference.TryAddTest(_mainTestJob);
        var refTestBuild = new TestBuild(
            _mainTestJob,
            "ref-id",
            referenceTest.BuildNumber,
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow,
            false,
            referenceRoot,
            [new FailedTest("ClassA", "Test1", "Old error")]);
        branchReference.TryAdd(refTestBuild);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_onDemandBuildJob);
        var onDemandRootBuild = RandomData.NextRootBuild(_onDemandBuildJob.Value, onDemandRoot.BuildNumber, testJobNames: [_onDemandTestJob.Value]);
        onDemandBuilds.TryAdd(onDemandRootBuild);
        onDemandBuilds.TryAddTest(_onDemandTestJob);
        var onDemandTestBuild = new TestBuild(
            _onDemandTestJob,
            "custom-id",
            onDemandTest.BuildNumber,
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow,
            false,
            onDemandRoot,
            [
                new FailedTest("ClassA", "Test1", "Old error"),
                new FailedTest("ClassB", "Test2", "New error")
            ]);
        onDemandBuilds.TryAdd(onDemandTestBuild);

        var flakyTests = new FlakyTests(flakyStore);

        // Act
        var report = RequestReportBuilder.Instance.Build(requestState, [branchReference], onDemandBuilds, flakyTests);

        // Assert
        var chainReport = report.ChainReports[0];
        Assert.That(chainReport.BuildDiffs, Has.Length.EqualTo(1));
        Assert.That(chainReport.BuildDiffs[0].Result.Status, Is.EqualTo(BuildStatus.Failed));

        var diffResult = chainReport.BuildDiffs[0].Diff.Match(
            onNotComparable: _ => (FailedTestDiff?)null,
            onComparable: diff => diff);
        Assert.That(diffResult, Is.Not.Null);
        Debug.Assert(diffResult != null);
        Assert.That(diffResult.FailedTests, Has.Length.EqualTo(1));
        var failedTestResult = diffResult.FailedTests[0];
        Assert.That(failedTestResult.Test.ClassName, Is.EqualTo("ClassB"));
        Assert.That(failedTestResult.Test.TestName, Is.EqualTo("Test2"));
        Assert.That(failedTestResult.Newness, Is.EqualTo(Newness.New));
    }

    [Test]
    public async Task New_MultipleTestBuilds_ReturnsMultipleBuildDiffs()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _mainBuildJob, out var referenceStore)
            .WithOnDemandStore(_onDemandBuildJob, out var onDemandStore)
            .WithRootJobs(_onDemandBuildJob)
            .WithNewTestBuilds(_onDemandTestJob)
            .WithFlakies(out var flakyStore);

        // Arrange
        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(_mainBuildJob);
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_onDemandBuildJob);

        var buildDiff1 = new RequestBuildDiff(new JobName("MAIN-test1"), new JobName("CUSTOM-test1"));
        var buildDiff2 = new RequestBuildDiff(new JobName("MAIN-test2"), new JobName("CUSTOM-test2"));

        var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), _mainBranch, ["test"], s_user, s_userEmail);
        var onDemandRoot = RequestRootBuildReference.Queue(_onDemandBuildJob, request.Commit);
        var chains = new RequestChain[] {
            new(
                new BuildReference(_mainBuildJob, RandomData.NextBuildNumber),
                onDemandRoot,
                [ buildDiff1, buildDiff2 ]
            )
        };
        Func<OnDemandJobKind, JobName, TriggerParameters, Task> triggerBuild = (_, _, _) => Task.CompletedTask;
        var requestState = await RequestState.New(request, chains, onDemandBuilds, triggerBuild).ConfigureAwait(false);

        var flakyTests = new FlakyTests(flakyStore);

        // Act
        var report = RequestReportBuilder.Instance.Build(requestState, [branchReference], onDemandBuilds, flakyTests);

        // Assert
        var chainReport = report.ChainReports[0];
        Assert.That(chainReport.BuildDiffs, Has.Length.EqualTo(2));
        Assert.That(chainReport.BuildDiffs[0].Result.JobName.Value, Is.EqualTo("CUSTOM-test1"));
        Assert.That(chainReport.BuildDiffs[1].Result.JobName.Value, Is.EqualTo("CUSTOM-test2"));
    }

    [Test]
    public async Task Send_NoMatchingBranchReference_ThrowsInvalidOperationException()
    {
        var requestBranch = new BranchName("feature");

        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _mainBuildJob, out var referenceStore)
            .WithOnDemandStore(_onDemandBuildJob, out var onDemandStore)
            .WithRootJobs(_onDemandBuildJob)
            .WithTestobs(_onDemandTestJob)
            .WithFlakies(out var flakyStore);

        var referenceRoot = new BuildReference(_mainBuildJob, RandomData.NextBuildNumber);
        var requestState = await CreateRequestState(onDemandStore, referenceRoot: referenceRoot).ConfigureAwait(false);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);

        var flakyTests = new FlakyTests(flakyStore);

        Assert.Throws<InvalidOperationException>(() => RequestReportBuilder.Instance.Build(requestState, [], onDemandBuilds, flakyTests));
    }
}
