using Moq;
using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class ReportSenderTests
{
    private Mock<IRequestReportBuilder> _mockBuilder;
    private ReportSender _reportSender;

    [SetUp]
    public void SetUp()
    {
        _mockBuilder = new Mock<IRequestReportBuilder>(MockBehavior.Strict);
        _reportSender = new ReportSender(_mockBuilder.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _mockBuilder.VerifyAll();
    }

    private static RequestState CreateRequestState(BranchName? branchName = null)
    {
        var request = Request.Create(
            RandomData.NextSha1(),
            RandomData.NextSha1(),
            branchName ?? new BranchName("main"),
            ["test"]);

        return RequestState.New(
            request,
            new BuildReference("REF-build", RandomData.NextBuildNumber),
            new BuildReference("CUSTOM-build", RandomData.NextBuildNumber),
            [new RequestBuildDiff(new JobName("REF-test"), new JobName("CUSTOM-test"))]);
    }

    private static Workspace CreateWorkspace(BranchName branchName)
    {
        var branchReference = new BranchReference(branchName, new JobName($"{branchName.Value.ToUpperInvariant()}-build"));
        var onDemandBuilds = new OnDemandBuilds(new JobName("CUSTOM-build"));
        return new Workspace(
            "test-path",
            [branchReference],
            onDemandBuilds,
            new OnDemandRequests("test-requests"));
    }

    [Test]
    public void Send_ValidRequest_CallsBuilderWithCorrectParameters()
    {
        // Arrange
        var branchName = new BranchName("main");
        var requestState = CreateRequestState(branchName);
        var workspace = CreateWorkspace(branchName);

        var expectedReport = new RequestReport([new ChainReport(
            BuildReferenceResult.Pending(new JobName("CUSTOM-build")),
            [])]);

        _mockBuilder
            .Setup(b => b.Build(
                requestState,
                It.Is<BranchReference>(br => br.BranchName == branchName),
                workspace.OnDemandBuilds))
            .Returns(expectedReport);

        // Act
        _reportSender.Send(requestState, workspace);

        // Assert
        _mockBuilder.Verify(b => b.Build(
            requestState,
            It.Is<BranchReference>(br => br.BranchName == branchName),
            workspace.OnDemandBuilds), Times.Once);
    }

    [Test]
    public void Send_MultipleBranchReferences_SelectsCorrectBranch()
    {
        // Arrange
        var targetBranch = new BranchName("feature");
        var otherBranch = new BranchName("main");

        var requestState = CreateRequestState(targetBranch);

        var targetBranchRef = new BranchReference(targetBranch, new JobName("FEATURE-build"));
        var otherBranchRef = new BranchReference(otherBranch, new JobName("MAIN-build"));
        var onDemandBuilds = new OnDemandBuilds(new JobName("CUSTOM-build"));

        var workspace = new Workspace(
            "test-path",
            [targetBranchRef, otherBranchRef],
            onDemandBuilds,
            new OnDemandRequests("test-requests"));

        var expectedReport = new RequestReport([new ChainReport(BuildReferenceResult.Pending(new JobName("CUSTOM-build")), [])]);

        _mockBuilder
            .Setup(b => b.Build(
                requestState,
                It.Is<BranchReference>(br => br.BranchName == targetBranch),
                workspace.OnDemandBuilds))
            .Returns(expectedReport);

        // Act
        _reportSender.Send(requestState, workspace);

        // Assert
        _mockBuilder.Verify(b => b.Build(
            requestState,
            It.Is<BranchReference>(br => br.BranchName == targetBranch),
            workspace.OnDemandBuilds), Times.Once);
    }

    [Test]
    public void Send_NoMatchingBranchReference_ThrowsInvalidOperationException()
    {
        // Arrange
        var requestBranch = new BranchName("feature");
        var workspaceBranch = new BranchName("main");

        var requestState = CreateRequestState(requestBranch);
        var workspace = CreateWorkspace(workspaceBranch);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _reportSender.Send(requestState, workspace));
    }

    [Test]
    public void Send_BuilderReturnsReport_CompletesSuccessfully()
    {
        // Arrange
        var branchName = new BranchName("main");
        var requestState = CreateRequestState(branchName);
        var workspace = CreateWorkspace(branchName);

        var buildDiffResult = new BuildDiffResult(
            BuildReferenceResult.Triggered(new BuildReference("CUSTOM-test", 42)),
            BuildDiff.OnDemandTriggered(42));

        var report = new RequestReport([new ChainReport(
            BuildReferenceResult.Triggered(new BuildReference("CUSTOM-build", 100)),
            [buildDiffResult])]);

        _mockBuilder
            .Setup(b => b.Build(
                It.IsAny<RequestState>(),
                It.IsAny<BranchReference>(),
                It.IsAny<OnDemandBuilds>()))
            .Returns(report);

        // Act & Assert - Should not throw
        Assert.DoesNotThrow(() => _reportSender.Send(requestState, workspace));
    }
}
