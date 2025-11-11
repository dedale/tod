using Moq;
using NUnit.Framework;
using Tod.Jenkins;
using Tod.Tests.IO;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class ReportSenderTests
{
    private readonly BranchName _mainBranch = new("main");
    private readonly JobName _referenceRootJob = new("MAIN-build");
    private readonly JobName _onDemandRootJob = new("CUSTOM-build");
    private readonly JobName _onDemandTestJob = new("CUSTOM-test");

    private TempDirectory _temp;
    private Mock<IRequestReportBuilder> _mockBuilder;
    private ReportSender _reportSender;

    [SetUp]
    public void SetUp()
    {
        _temp = new TempDirectory();
        _mockBuilder = new Mock<IRequestReportBuilder>(MockBehavior.Strict);
        _reportSender = new ReportSender(_mockBuilder.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _temp.Dispose();
        _mockBuilder.VerifyAll();
    }

    private RequestState CreateRequestState(BranchName? branchName = null)
    {
        var request = Request.Create(
            RandomData.NextSha1(),
            RandomData.NextSha1(),
            branchName ?? _mainBranch,
            ["test"]);

        return RequestState.New(
            request,
            new BuildReference("REF-build", RandomData.NextBuildNumber),
            new BuildReference(_onDemandRootJob, RandomData.NextBuildNumber),
            [new RequestBuildDiff(new JobName("REF-test"), _onDemandTestJob)]);
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
    public void Send_ValidRequest_CallsBuilderWithCorrectParameters()
    {
        var requestState = CreateRequestState(_mainBranch);

        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore);

        var workspace = GetWorkspace(referenceStore, onDemandStore);

        var expectedReport = new RequestReport([new ChainReport(
            BuildReferenceResult.Pending(_onDemandRootJob),
            [])]);

        _mockBuilder
            .Setup(b => b.Build(
                requestState,
                It.Is<BranchReference>(br => br.BranchName == _mainBranch),
                workspace.OnDemandBuilds))
            .Returns(expectedReport);

        _reportSender.Send(requestState, workspace);
    }

    [Test]
    public void Send_MultipleBranchReferences_SelectsCorrectBranch()
    {
        var featureBranch = new BranchName("feature");

        var featureBuildJob = new JobName("FEATURE-build");
        var mainBuildJob = new JobName("MAIN-build");
        var customBuildJob = _onDemandRootJob;

        using var mocks = StoreMocks.New()
            .WithReferenceStore(featureBranch, featureBuildJob, out var featureReferenceStore)
            .WithReferenceStore(_mainBranch, mainBuildJob, out var mainReferenceStore)
            .WithOnDemandStore(customBuildJob, out var onDemandStore);

        var requestState = CreateRequestState(featureBranch);

        var featureBranchRef = new BranchReference(featureReferenceStore);
        featureBranchRef.TryAddRoot(featureBuildJob);

        var mainBranchRef = new BranchReference(mainReferenceStore);
        mainBranchRef.TryAddRoot(mainBuildJob);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        onDemandBuilds.TryAddRoot(customBuildJob);

        var workspace = new Workspace(
            [featureBranchRef, mainBranchRef],
            onDemandBuilds,
            new OnDemandRequests(_temp.Path));

        var expectedReport = new RequestReport([new ChainReport(BuildReferenceResult.Pending(customBuildJob), [])]);

        _mockBuilder
            .Setup(b => b.Build(
                requestState,
                It.Is<BranchReference>(br => br.BranchName == featureBranch),
                workspace.OnDemandBuilds))
            .Returns(expectedReport);

        _reportSender.Send(requestState, workspace);
    }

    [Test]
    public void Send_NoMatchingBranchReference_ThrowsInvalidOperationException()
    {
        var requestBranch = new BranchName("feature");

        var requestState = CreateRequestState(requestBranch);

        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore);

        var workspace = GetWorkspace(referenceStore, onDemandStore);

        Assert.Throws<InvalidOperationException>(() => _reportSender.Send(requestState, workspace));
    }

    [Test]
    public void Send_BuilderReturnsReport_CompletesSuccessfully()
    {
        var requestState = CreateRequestState(_mainBranch);

        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore);

        var workspace = GetWorkspace(referenceStore, onDemandStore);

        var buildDiffResult = new BuildDiffResult(
            BuildReferenceResult.Triggered(new BuildReference(_onDemandTestJob, 42)),
            BuildDiff.OnDemandTriggered(42));

        var report = new RequestReport([new ChainReport(
            BuildReferenceResult.Triggered(new BuildReference(_onDemandRootJob, 100)),
            [buildDiffResult])]);

        _mockBuilder
            .Setup(b => b.Build(
                It.IsAny<RequestState>(),
                It.IsAny<BranchReference>(),
                It.IsAny<OnDemandBuilds>()))
            .Returns(report);

        Assert.DoesNotThrow(() => _reportSender.Send(requestState, workspace));
    }
}
