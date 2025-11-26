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
    private Mock<IJobLinker> _mockJobLinker;
    private Mock<IMailSender> _mockMailSender;
    private ReportSender _reportSender;

    [SetUp]
    public void SetUp()
    {
        _temp = new TempDirectory();
        _mockReportBuilder = new Mock<IRequestReportBuilder>(MockBehavior.Strict);
        _mockJobLinker = new Mock<IJobLinker>(MockBehavior.Strict);
        _mockMailSender = new Mock<IMailSender>(MockBehavior.Strict);
        _reportSender = new ReportSender(_mockReportBuilder.Object, _mockJobLinker.Object, _mockMailSender.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _temp.Dispose();
        _mockReportBuilder.VerifyAll();
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

        await _reportSender.Send(requestState, workspace).ConfigureAwait(false);
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

        await _reportSender.Send(requestState, workspace).ConfigureAwait(false);
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

        Assert.ThrowsAsync<InvalidOperationException>(() => _reportSender.Send(requestState, workspace));
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

        _mockJobLinker.Setup(j => j.GetUrl(It.IsAny<JobName>(), It.IsAny<int>()))
            .Returns("http://example.org/job-link");

        _mockMailSender.Setup(m => m.Send(It.IsAny<string>(), "On-Demand Report", It.Is<string>(m => m.Contains(inMail))))
            .Returns(Task.CompletedTask);

        Assert.DoesNotThrowAsync(() => _reportSender.Send(requestState, workspace));
    }

    [Test]
    public Task Send_FailedTestsDiffOK_InMail()
    {
        var failedTestsDiff = new FailedTestDiff(TestBuildDiffStatus.OK, [], []);

        return Send_FailedTestDiff(failedTestsDiff, "Diff Status: OK");
    }

    [TestCase(1, "Diff Status: 1 New Failure")]
    [TestCase(2, "Diff Status: 2 New Failures")]
    public Task Send_FailedTestsDiffNewFailures_InMail(int count, string status)
    {
        var failedTests = Enumerable.Range(1, count)
            .Select(i => new FailedTest("ClassName", $"TestName{i}", "Details"))
            .ToArray();
        var failedTestsDiff = new FailedTestDiff(TestBuildDiffStatus.NewFailures, [], failedTests);

        return Send_FailedTestDiff(failedTestsDiff, status);
    }

    [Test]
    public Task Send_FailedTestsDiffSameFailures_InMail()
    {
        var failedTestsDiff = new FailedTestDiff(TestBuildDiffStatus.SameFailures, [], []);

        return Send_FailedTestDiff(failedTestsDiff, "Diff Status: Same Failures");
    }

    [TestCase(1, "Diff Status: 1 Updated Failure")]
    [TestCase(2, "Diff Status: 2 Updated Failures")]
    public Task Send_FailedTestsDiffUpdatedFailures_InMail(int count, string status)
    {
        var failedTests = Enumerable.Range(1, count)
            .Select(i => new FailedTest("ClassName", $"TestName{i}", "Details"))
            .ToArray();
        var failedTestsDiff = new FailedTestDiff(TestBuildDiffStatus.UpdatedFailures, failedTests, []);

        return Send_FailedTestDiff(failedTestsDiff, status);
    }

    [TestCase(1, 1, "Diff Status: 1 New Failure ❌, 1 Updated Failure ❌, Same Failures ⚠️")]
    [TestCase(2, 1, "Diff Status: 2 New Failures ❌, 1 Updated Failure ❌, Same Failures ⚠️")]
    [TestCase(1, 2, "Diff Status: 1 New Failure ❌, 2 Updated Failures ❌, Same Failures ⚠️")]
    [TestCase(2, 2, "Diff Status: 2 New Failures ❌, 2 Updated Failures ❌, Same Failures ⚠️")]
    public Task Send_FailedTestsDiffAllCases_InMail(int added, int updated, string statusMessage)
    {
        var addedFailedTests = Enumerable.Range(1, added)
            .Select(i => new FailedTest("ClassName1", $"TestName{i}", "Details"))
            .ToArray();
        var updatedFailedTests = Enumerable.Range(1, updated)
            .Select(i => new FailedTest("ClassName2", $"TestName{i}", "Details"))
            .ToArray();
        var status = TestBuildDiffStatus.NewFailures
            | TestBuildDiffStatus.SameFailures
            | TestBuildDiffStatus.UpdatedFailures;
        var failedTestsDiff = new FailedTestDiff(status, updatedFailedTests, addedFailedTests);

        return Send_FailedTestDiff(failedTestsDiff, statusMessage);
    }
}

[TestFixture]
internal sealed class JenkinsJobLinkerTests
{
    [Test]
    public void GetUrl_KnownJob_ReturnsCorrectUrl()
    {
        var config = new JenkinsConfig("http://jenkins.example.org");
        var linker = new JenkinsJobLinker(config);
        var jobName = new JobName("MY-job");
        var buildNumber = 42;
        Assert.That(linker.GetUrl(jobName, buildNumber), Is.EqualTo("http://jenkins.example.org/job/MY-job/42"));
    }

    [Test]
    public void GetUrl_KnownJobWithoutBuildNumber_ReturnsCorrectUrl()
    {
        var config = new JenkinsConfig("http://jenkins.example.org");
        var linker = new JenkinsJobLinker(config);
        var jobName = new JobName("MY-job");
        Assert.That(linker.GetUrl(jobName), Is.EqualTo("http://jenkins.example.org/job/MY-job/"));
    }

    [Test]
    public void GetUrl_JobPath_ReturnsCorrectUrl()
    {
        var config = new JenkinsConfig("http://jenkins.example.org");
        var linker = new JenkinsJobLinker(config);
        var jobName = new JobName("Very/Long/JobName");
        var buildNumber = 7;
        Assert.That(linker.GetUrl(jobName, buildNumber), Is.EqualTo("http://jenkins.example.org/job/Very/job/Long/job/JobName/7"));
    }
}