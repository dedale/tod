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
    private Mock<IRequestReportBuilder> _mockReportBuilder;
    private Mock<IMailSender> _mockMailSender;
    private ReportSender _reportSender;

    [SetUp]
    public void SetUp()
    {
        _temp = new TempDirectory();
        _mockReportBuilder = new Mock<IRequestReportBuilder>(MockBehavior.Strict);
        _mockMailSender = new Mock<IMailSender>(MockBehavior.Strict);
        _reportSender = new ReportSender(_mockReportBuilder.Object, _mockMailSender.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _temp.Dispose();
        _mockReportBuilder.VerifyAll();
        _mockMailSender.VerifyAll();
    }

    private static string GetUserEmail(string userName) => $"{userName}@example.org";

    private Task<RequestState> CreateRequestState(IOnDemandStore onDemandStore, BranchName? refBranch = null)
    {
        var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), refBranch ?? _mainBranch, ["test"], GetUserEmail);
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

    private Workspace GetWorkspace(IReferenceStore referenceStore, IOnDemandStore onDemandStore)
    {
        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(_referenceRootJob);
     
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_onDemandRootJob);
        
        return new Workspace([branchReference], onDemandBuilds, new OnDemandRequests(_temp.Path));
    }

    [Test]
    public async Task Send_ValidRequest_CallsBuilderWithCorrectParameters()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithRootJobs(_onDemandRootJob);

        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        var workspace = GetWorkspace(referenceStore, onDemandStore);

        var expectedReport = new RequestReport([new ChainReport(
            BuildReferenceResult.Pending(_onDemandRootJob),
            [])]);

        _mockReportBuilder
            .Setup(b => b.Build(
                requestState,
                It.Is<BranchReference>(br => br.BranchName == _mainBranch),
                workspace.OnDemandBuilds))
            .Returns(expectedReport);

        _mockMailSender.Setup(m => m.Send(It.IsAny<string>(), "On-Demand Report", It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _reportSender.Send(requestState, workspace);
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
            .WithRootJobs(_onDemandRootJob);

        var requestState = await CreateRequestState(onDemandStore, refBranch: featureBranch).ConfigureAwait(false);

        var featureBranchRef = new BranchReference(featureReferenceStore);
        featureBranchRef.TryAddRoot(featureBuildJob);

        var mainBranchRef = new BranchReference(mainReferenceStore);
        mainBranchRef.TryAddRoot(mainBuildJob);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(_onDemandRootJob);

        var workspace = new Workspace(
            [featureBranchRef, mainBranchRef],
            onDemandBuilds,
            new OnDemandRequests(_temp.Path));

        var expectedReport = new RequestReport([new ChainReport(BuildReferenceResult.Pending(_onDemandRootJob), [])]);

        _mockReportBuilder
            .Setup(b => b.Build(
                requestState,
                It.Is<BranchReference>(br => br.BranchName == featureBranch),
                onDemandBuilds))
            .Returns(expectedReport);

        _mockMailSender.Setup(m => m.Send(It.IsAny<string>(), "On-Demand Report", It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _reportSender.Send(requestState, workspace);
    }

    [Test]
    public async Task Send_NoMatchingBranchReference_ThrowsInvalidOperationException()
    {
        var requestBranch = new BranchName("feature");

        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithRootJobs(_onDemandRootJob);

        var requestState = await CreateRequestState(onDemandStore, refBranch: requestBranch).ConfigureAwait(false);
        var workspace = GetWorkspace(referenceStore, onDemandStore);

        Assert.Throws<InvalidOperationException>(() => _reportSender.Send(requestState, workspace));
    }

    [Test]
    public async Task Send_BuilderReturnsReport_CompletesSuccessfully()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithRootJobs(_onDemandRootJob);

        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        var workspace = GetWorkspace(referenceStore, onDemandStore);

        var buildDiffResult = new BuildDiffResult(
            BuildReferenceResult.Queued(_onDemandTestJob),
            BuildDiff.OnDemandTriggered(_onDemandTestJob));

        var report = new RequestReport([new ChainReport(
            BuildReferenceResult.Queued(_onDemandRootJob),
            [buildDiffResult])]);

        _mockReportBuilder
            .Setup(b => b.Build(
                It.IsAny<RequestState>(),
                It.IsAny<BranchReference>(),
                It.IsAny<OnDemandBuilds>()))
            .Returns(report);

        _mockMailSender.Setup(m => m.Send(It.IsAny<string>(), "On-Demand Report", It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        Assert.DoesNotThrow(() => _reportSender.Send(requestState, workspace));
    }

    private async Task Send_FailedTestDiff(FailedTestDiff failedTestDiff, string inMail)
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithRootJobs(_onDemandRootJob);

        var onDemandRoot = new BuildReference(_onDemandRootJob, RandomData.NextBuildNumber);
        var onDemandTest = new BuildReference(_onDemandTestJob, RandomData.NextBuildNumber);

        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        await requestState.TriggerTests(onDemandRoot, _ => Task.CompletedTask).ConfigureAwait(false);
        var workspace = GetWorkspace(referenceStore, onDemandStore);

        var buildDiffResult = new BuildDiffResult(
            BuildReferenceResult.Done(onDemandTest, true),
            BuildDiff.Diff(failedTestDiff));

        var report = new RequestReport([new ChainReport(
            BuildReferenceResult.Done(onDemandRoot, true),
            [buildDiffResult])]);

        _mockReportBuilder
            .Setup(b => b.Build(
                It.IsAny<RequestState>(),
                It.IsAny<BranchReference>(),
                It.IsAny<OnDemandBuilds>()))
            .Returns(report);

        _mockMailSender.Setup(m => m.Send(It.IsAny<string>(), "On-Demand Report", It.Is<string>(m => m.Contains(inMail))))
            .Returns(Task.CompletedTask);

        Assert.DoesNotThrow(() => _reportSender.Send(requestState, workspace));
    }

    [Test]
    public Task Send_FailedTestsDiffOK_InMail()
    {
        var failedTestsDiff = new FailedTestDiff(TestBuildDiffStatus.OK, [], []);

        return Send_FailedTestDiff(failedTestsDiff, "Diff Status: OK");
    }

    [Test]
    public Task Send_FailedTestsDiffNewFailures_InMail()
    {
        var failedTestsDiff = new FailedTestDiff(TestBuildDiffStatus.NewFailures, [], [new FailedTest("ClassName", "TestName", "Details")]);

        return Send_FailedTestDiff(failedTestsDiff, "Diff Status: New Failures");
    }

    [Test]
    public Task Send_FailedTestsDiffSameFailures_InMail()
    {
        var failedTestsDiff = new FailedTestDiff(TestBuildDiffStatus.SameFailures, [], []);

        return Send_FailedTestDiff(failedTestsDiff, "Diff Status: Same Failures");
    }

    [Test]
    public Task Send_FailedTestsDiffUpdatedFailures_InMail()
    {
        var failedTestsDiff = new FailedTestDiff(TestBuildDiffStatus.UpdatedFailures, [new FailedTest("ClassName", "TestName", "Details")], []);

        return Send_FailedTestDiff(failedTestsDiff, "Diff Status: Updated Failures");
    }

    [Test]
    public Task Send_FailedTestsDiffAllCases_InMail()
    {
        var status = TestBuildDiffStatus.NewFailures
            | TestBuildDiffStatus.SameFailures
            | TestBuildDiffStatus.UpdatedFailures;
        var failedTestsDiff = new FailedTestDiff(status, [new FailedTest("ClassName1", "TestName1", "Details1")], [new FailedTest("ClassName2", "TestName2", "Details2")]);

        return Send_FailedTestDiff(failedTestsDiff, "Diff Status: New Failures ❌, Updated Failures ❌, Same Failures ⚠️");
    }
}
