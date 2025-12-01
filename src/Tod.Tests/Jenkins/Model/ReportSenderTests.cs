using Moq;
using NUnit.Framework;
using Tod.Jenkins;
using Tod.Net;
using Tod.Tests.IO;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class ReportSenderTests
{
    private readonly BranchName _mainBranch = new("main");
    private readonly JobName _referenceRootJob = new("MAIN-build");
    private readonly JobName _referenceTestJob = new("MAIN-test");
    private readonly JobName _onDemandRootJob = new("CUSTOM-build");
    private readonly JobName _onDemandTestJob = new("CUSTOM-test");

    private TempDirectory _temp;
    private Mock<IJobLinker> _mockJobLinker;
    private Mock<IMailSender> _mockMailSender;
    private ReportSender _reportSender;

    [SetUp]
    public void SetUp()
    {
        _temp = new TempDirectory();
        _mockJobLinker = new Mock<IJobLinker>(MockBehavior.Strict);
        _mockMailSender = new Mock<IMailSender>(MockBehavior.Strict);
        _reportSender = new ReportSender(_mockJobLinker.Object, _mockMailSender.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _temp.Dispose();
        _mockJobLinker.VerifyAll();
        _mockMailSender.VerifyAll();
    }

    private static readonly string s_userEmail = $"user@example.org";

    private Task<RequestState> CreateRequestState(IOnDemandStore onDemandStore, BranchName? refBranch = null)
    {
        var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), refBranch ?? _mainBranch, ["test"], s_userEmail);
        var onDemandRoot = RequestRootBuildReference.Queue(_onDemandRootJob, request.Commit);
        var chains = new RequestChain[] {
            new(
                new BuildReference(_referenceRootJob, RandomData.NextBuildNumber),
                onDemandRoot,
                [ new RequestBuildDiff(_referenceTestJob, _onDemandTestJob) ]
            )
        };
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        Func<OnDemandJobKind, JobName, TriggerParameters, Task> triggerBuild = (_, _, _) => Task.CompletedTask;
        return RequestState.New(request, chains, onDemandBuilds, triggerBuild);
    }

    private Workspace GetWorkspace(IReferenceStore referenceStore, IOnDemandStore onDemandStore, IFlakyStore flakyStore)
    {
        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(_referenceRootJob);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_onDemandRootJob);

        return new Workspace([branchReference], onDemandBuilds, new OnDemandRequests(_temp.Path), new FlakyTests(flakyStore));
    }

    [Test]
    public async Task Send_ValidRequest_CallsBuilderWithCorrectParameters()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithRootJobs(_onDemandRootJob)
            .WithFlakies(out var flakyStore);

        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        var workspace = GetWorkspace(referenceStore, onDemandStore, flakyStore);

        var report = new RequestReport([new ChainReport(
            BuildReferenceResult.Pending(_onDemandRootJob),
            [])]);

        _mockMailSender.Setup(m => m.Send(It.IsAny<string>(), "On-Demand Report", It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        await _reportSender.Send(requestState, report).ConfigureAwait(false);
    }

    [Test]
    public async Task Send_MultipleBranchReferences_SelectsCorrectBranch()
    {
        var featureBranch = new BranchName("feature");

        var featureBuildJob = new JobName("FEATURE-build");
        var mainBuildJob = new JobName("MAIN-build");

        using var mocks = StoreMocks.New()
            .WithReferenceStore(featureBranch, featureBuildJob, out var featureReferenceStore)
            .WithReferenceStore(_mainBranch, mainBuildJob, out var mainReferenceStore)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithRootJobs(_onDemandRootJob)
            .WithFlakies(out var flakyStore);

        var requestState = await CreateRequestState(onDemandStore, refBranch: featureBranch).ConfigureAwait(false);

        var featureBranchRef = new BranchReference(featureReferenceStore);
        featureBranchRef.TryAddRoot(featureBuildJob);

        var mainBranchRef = new BranchReference(mainReferenceStore);
        mainBranchRef.TryAddRoot(mainBuildJob);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_onDemandRootJob);

        var flakyTests = new FlakyTests(flakyStore);

        var workspace = new Workspace(
            [featureBranchRef, mainBranchRef],
            onDemandBuilds,
            new OnDemandRequests(_temp.Path),
            flakyTests);

        var report = new RequestReport([new ChainReport(BuildReferenceResult.Pending(_onDemandRootJob), [])]);

        _mockMailSender.Setup(m => m.Send(It.IsAny<string>(), "On-Demand Report", It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        await _reportSender.Send(requestState, report).ConfigureAwait(false);
    }

    [Test]
    public async Task Send_BuilderReturnsReport_CompletesSuccessfully()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithRootJobs(_onDemandRootJob)
            .WithFlakies(out var flakyStore);

        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        var workspace = GetWorkspace(referenceStore, onDemandStore, flakyStore);

        var buildDiffResult = new BuildDiffResult(
            BuildReferenceResult.Queued(_onDemandTestJob),
            BuildDiff.OnDemandTriggered(_onDemandTestJob));

        var report = new RequestReport([new ChainReport(
            BuildReferenceResult.Queued(_onDemandRootJob),
            [buildDiffResult])]);

        _mockJobLinker.Setup(j => j.GetUrl(It.IsAny<JobName>()))
            .Returns("http://example.org/job-link");

        _mockMailSender.Setup(m => m.Send(It.IsAny<string>(), "On-Demand Report", It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        Assert.DoesNotThrowAsync(() => _reportSender.Send(requestState, report));
    }

    [Test]
    public async Task Send_WithWorkspace_CompletesSuccessfully()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithRootJobs(_onDemandRootJob)
            .WithFlakies(out var flakyStore);

        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        var workspace = GetWorkspace(referenceStore, onDemandStore, flakyStore);

        var buildDiffResult = new BuildDiffResult(
            BuildReferenceResult.Queued(_onDemandTestJob),
            BuildDiff.OnDemandTriggered(_onDemandTestJob));

        var report = new RequestReport([new ChainReport(
            BuildReferenceResult.Queued(_onDemandRootJob),
            [buildDiffResult])]);

        _mockJobLinker.Setup(j => j.GetUrl(It.IsAny<JobName>()))
            .Returns("http://example.org/job-link");

        _mockMailSender.Setup(m => m.Send(It.IsAny<string>(), "On-Demand Report", It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        Assert.DoesNotThrowAsync(() => _reportSender.Send(requestState, workspace));
    }

    private async Task Send_FailedTestDiff(FailedTestDiff failedTestDiff, string inMail)
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithRootJobs(_onDemandRootJob)
            .WithFlakies(out var flakyStore);

        var onDemandRoot = new BuildReference(_onDemandRootJob, RandomData.NextBuildNumber);
        var onDemandTest = new BuildReference(_onDemandTestJob, RandomData.NextBuildNumber);

        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        await requestState.TriggerTests(onDemandRoot, _ => Task.CompletedTask).ConfigureAwait(false);
        var workspace = GetWorkspace(referenceStore, onDemandStore, flakyStore);

        var buildDiffResult = new BuildDiffResult(
            BuildReferenceResult.Done(onDemandTest, true),
            BuildDiff.Diff(failedTestDiff));

        var report = new RequestReport([new ChainReport(
            BuildReferenceResult.Done(onDemandRoot, true),
            [buildDiffResult])]);

        _mockJobLinker.Setup(j => j.GetUrl(It.IsAny<JobName>(), It.IsAny<int>()))
            .Returns("http://example.org/job-link");

        _mockMailSender.Setup(m => m.Send(It.IsAny<string>(), "On-Demand Report", It.Is<string>(m => m.Contains(inMail))))
            .Returns(Task.CompletedTask);

        Assert.DoesNotThrowAsync(() => _reportSender.Send(requestState, report));
    }

    [Test]
    public Task Send_FailedTestsDiffOK_InMail()
    {
        var failedTestsDiff = new FailedTestDiff(TestBuildDiffStatus.OK, []);

        return Send_FailedTestDiff(failedTestsDiff, "Diff Status: OK");
    }

    [TestCase(1, "Diff Status: 1 New Failure")]
    [TestCase(2, "Diff Status: 2 New Failures")]
    public Task Send_FailedTestsDiffNewFailures_InMail(int count, string status)
    {
        var failedTestResults = Enumerable.Range(1, count)
            .Select(i => new FailedTestResult(new FailedTest("ClassName", $"TestName{i}", "Details"), Newness.New, false))
            .ToArray();
        var failedTestsDiff = new FailedTestDiff(TestBuildDiffStatus.NewFailures, failedTestResults);

        return Send_FailedTestDiff(failedTestsDiff, status);
    }

    [TestCase(1, "Diff Status: 1 New Failure ❌ (1 flaky test)")]
    [TestCase(2, "Diff Status: 2 New Failures ❌ (2 flaky tests)")]
    public Task Send_FailedTestsDiffNewFlakyFailures_InMail(int count, string status)
    {
        var failedTestResults = Enumerable.Range(1, count)
            .Select(i => new FailedTestResult(new FailedTest("ClassName", $"TestName{i}", "Details"), Newness.New, true))
            .ToArray();
        var failedTestsDiff = new FailedTestDiff(TestBuildDiffStatus.NewFailures, failedTestResults);

        return Send_FailedTestDiff(failedTestsDiff, status);
    }

    [Test]
    public Task Send_FailedTestsDiffSameFailures_InMail()
    {
        var failedTestsDiff = new FailedTestDiff(TestBuildDiffStatus.SameFailures, []);

        return Send_FailedTestDiff(failedTestsDiff, "Diff Status: Same Failures");
    }

    [TestCase(1, "Diff Status: 1 Updated Failure")]
    [TestCase(2, "Diff Status: 2 Updated Failures")]
    public Task Send_FailedTestsDiffUpdatedFailures_InMail(int count, string status)
    {
        var failedTestResults = Enumerable.Range(1, count)
            .Select(i => new FailedTestResult(new FailedTest("ClassName", $"TestName{i}", "Details"), Newness.Updated, false))
            .ToArray();
        var failedTestsDiff = new FailedTestDiff(TestBuildDiffStatus.UpdatedFailures, failedTestResults);

        return Send_FailedTestDiff(failedTestsDiff, status);
    }

    [TestCase(1, "Diff Status: 1 Updated Failure ❌ (1 flaky test)")]
    [TestCase(2, "Diff Status: 2 Updated Failures ❌ (2 flaky tests)")]
    public Task Send_FailedTestsDiffFlakyUpdatedFailures_InMail(int count, string status)
    {
        var failedTestResults = Enumerable.Range(1, count)
            .Select(i => new FailedTestResult(new FailedTest("ClassName", $"TestName{i}", "Details"), Newness.Updated, true))
            .ToArray();
        var failedTestsDiff = new FailedTestDiff(TestBuildDiffStatus.UpdatedFailures, failedTestResults);

        return Send_FailedTestDiff(failedTestsDiff, status);
    }

    [TestCase(1, 1, "Diff Status: 1 New Failure ❌, 1 Updated Failure ❌, Same Failures ⚠️")]
    [TestCase(2, 1, "Diff Status: 2 New Failures ❌, 1 Updated Failure ❌, Same Failures ⚠️")]
    [TestCase(1, 2, "Diff Status: 1 New Failure ❌, 2 Updated Failures ❌, Same Failures ⚠️")]
    [TestCase(2, 2, "Diff Status: 2 New Failures ❌, 2 Updated Failures ❌, Same Failures ⚠️")]
    public Task Send_FailedTestsDiffAllCases_InMail(int added, int updated, string statusMessage)
    {
        var failedTestResults = Enumerable.Range(1, added)
            .Select(i => new FailedTestResult(new FailedTest("ClassName1", $"TestName{i}", "Details"), Newness.New, false))
            .Concat(
                Enumerable.Range(1, updated)
                .Select(i => new FailedTestResult(new FailedTest("ClassName2", $"TestName{i}", "Details"), Newness.Updated, false))
            )        
            .ToArray();
        var status = TestBuildDiffStatus.NewFailures
            | TestBuildDiffStatus.SameFailures
            | TestBuildDiffStatus.UpdatedFailures;
        var failedTestsDiff = new FailedTestDiff(status, failedTestResults);

        return Send_FailedTestDiff(failedTestsDiff, statusMessage);
    }

    [TestCase(1, 1, "Diff Status: 1 New Failure ❌ (1 flaky test), 1 Updated Failure ❌ (1 flaky test), Same Failures ⚠️")]
    [TestCase(2, 1, "Diff Status: 2 New Failures ❌ (2 flaky tests), 1 Updated Failure ❌ (1 flaky test), Same Failures ⚠️")]
    [TestCase(1, 2, "Diff Status: 1 New Failure ❌ (1 flaky test), 2 Updated Failures ❌ (2 flaky tests), Same Failures ⚠️")]
    [TestCase(2, 2, "Diff Status: 2 New Failures ❌ (2 flaky tests), 2 Updated Failures ❌ (2 flaky tests), Same Failures ⚠️")]
    public Task Send_FailedTestsDiffAllFlakyCases_InMail(int added, int updated, string statusMessage)
    {
        var failedTestResults = Enumerable.Range(1, added)
            .Select(i => new FailedTestResult(new FailedTest("ClassName1", $"TestName{i}", "Details"), Newness.New, true))
            .Concat(
                Enumerable.Range(1, updated)
                .Select(i => new FailedTestResult(new FailedTest("ClassName2", $"TestName{i}", "Details"), Newness.Updated, true))
            )
            .ToArray();
        var status = TestBuildDiffStatus.NewFailures
            | TestBuildDiffStatus.SameFailures
            | TestBuildDiffStatus.UpdatedFailures;
        var failedTestsDiff = new FailedTestDiff(status, failedTestResults);

        return Send_FailedTestDiff(failedTestsDiff, statusMessage);
    }
}
