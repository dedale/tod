using Moq;
using NUnit.Framework;
using System.Diagnostics;
using Tod.Jenkins;
using Tod.Net;
using Tod.Tests.IO;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class RequestReportSenderTests
{
    private readonly BranchName _mainBranch = new("main");
    private readonly JobName _referenceRootJob = new("MAIN-build");
    private readonly JobName _referenceTestJob = new("MAIN-test");
    private readonly JobName _onDemandRootJob = new("CUSTOM-build");
    private readonly JobName _onDemandTestJob = new("CUSTOM-test");

    private TempDirectory _temp;
    private Mock<IJobLinker> _mockJobLinker;
    private Mock<IMailSender> _mockMailSender;
    private RequestReportSender _reportSender;

    [SetUp]
    public void SetUp()
    {
        _temp = new TempDirectory();
        _mockJobLinker = new Mock<IJobLinker>(MockBehavior.Strict);
        _mockMailSender = new Mock<IMailSender>(MockBehavior.Strict);
        _reportSender = new RequestReportSender(_mockJobLinker.Object, _mockMailSender.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _temp.Dispose();
        _mockJobLinker.VerifyAll();
        _mockMailSender.VerifyAll();
    }

    private static readonly string s_user = "user";
    private static readonly string s_userEmail = $"user@example.org";

    private Task<RequestState> CreateRequestState(IOnDemandStore onDemandStore, BranchName? refBranch = null)
    {
        var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), refBranch ?? _mainBranch, ["test"], s_user, s_userEmail);
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

        _mockJobLinker.Setup(j => j.GetUrl(It.IsAny<JobName>()))
            .Returns("http://example.org/job-link");

        _mockMailSender.Setup(m => m.Send(It.IsAny<string>(), "On-Demand Report", It.IsAny<string>(), It.IsAny<string>()))
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

        _mockJobLinker.Setup(j => j.GetUrl(It.IsAny<JobName>()))
            .Returns("http://example.org/job-link");

        _mockMailSender.Setup(m => m.Send(It.IsAny<string>(), "On-Demand Report", It.IsAny<string>(), It.IsAny<string>()))
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
            BuildReferenceResult.Done(new(_referenceTestJob, RandomData.NextBuildNumber), true),
            BuildReferenceResult.Queued(_onDemandTestJob),
            BuildDiff.OnDemandTriggered(_onDemandTestJob));

        var report = new RequestReport([new ChainReport(
            BuildReferenceResult.Queued(_onDemandRootJob),
            [buildDiffResult])]);

        _mockJobLinker.Setup(j => j.GetUrl(It.IsAny<JobName>()))
            .Returns("http://example.org/job-link");
        _mockJobLinker.Setup(j => j.GetUrl(It.IsAny<JobName>(), It.IsAny<int>()))
            .Returns("http://example.org/job-link");

        _mockMailSender.Setup(m => m.Send(It.IsAny<string>(), "On-Demand Report", It.IsAny<string>(), It.IsAny<string>()))
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

        _mockJobLinker.Setup(j => j.GetUrl(It.IsAny<JobName>()))
            .Returns("http://example.org/job-link");

        _mockMailSender.Setup(m => m.Send(It.IsAny<string>(), "On-Demand Report", It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        Assert.DoesNotThrowAsync(() => _reportSender.Send(requestState, workspace));
    }

    [Test]
    public async Task Send_DoneWithWorkspace_CompletesSuccessfully()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithNewRootBuilds(_onDemandRootJob)
            .WithNewTestBuilds(_onDemandTestJob)
            .WithFlakies(out var flakyStore);

        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        var workspace = GetWorkspace(referenceStore, onDemandStore, flakyStore);
        workspace.OnDemandRequests.Add(requestState);

        var chainDiff = requestState.ChainDiffs[0];
        var rootBuildRef = new BuildReference(chainDiff.OnDemandRoot.JobName, RandomData.NextBuildNumber);
        var rootBuild = RandomData.NextRootBuild(jobName: rootBuildRef.JobName.Value, buildNumber: rootBuildRef.BuildNumber, testJobNames: [_onDemandTestJob.Value]);
        Assert.That(workspace.OnDemandBuilds.TryAdd(rootBuild), Is.True);

        Assert.That(workspace.OnDemandRequests.TryGetRootQueued(rootBuildRef.JobName, requestState.Request.Commit, out var lockedRequest), Is.True);
        Debug.Assert(lockedRequest != null);
        try
        {
            Func<JobName, Task> triggerBuild = _ => Task.CompletedTask;
            await lockedRequest.Update(r => r.TriggerTests(rootBuildRef, triggerBuild)).ConfigureAwait(false);
        }
        finally
        {
            lockedRequest.Dispose();
        }

        using (var lockedRequests = workspace.OnDemandRequests.GetPendingReferenceTest(chainDiff.ReferenceRoot, _referenceTestJob))
        {
            Assert.That(lockedRequests, Has.Count.EqualTo(1));
            lockedRequest = lockedRequests[0];
            var testBuild = new BuildReference(_referenceTestJob, RandomData.NextBuildNumber);
            {
                await lockedRequest.Update(r => Task.FromResult(r.DoneReferenceTestBuild(rootBuildRef, testBuild))).ConfigureAwait(false);
            }
        }

        var requestBuildDiff = chainDiff.TestBuildDiffs.First();
        Assert.That(workspace.OnDemandRequests.TryGetTestQueued(rootBuildRef, _onDemandTestJob, out lockedRequest), Is.True);
        Debug.Assert(lockedRequest != null);
        try
        {
            var testBuild = new BuildReference(_onDemandTestJob, RandomData.NextBuildNumber);
            workspace.OnDemandBuilds.TryAdd(RandomData.NextTestBuild(
                testJobName: testBuild.JobName.Value,
                buildNumber: testBuild.BuildNumber,
                rootBuild: rootBuildRef));
            requestState = await lockedRequest.Update(r => Task.FromResult(r.DoneOnDemandTestBuild(rootBuildRef, testBuild))).ConfigureAwait(false);
        }
        finally
        {
            lockedRequest.Dispose();
        }

        _mockJobLinker.Setup(j => j.GetUrl(It.IsAny<JobName>()))
            .Returns("http://example.org/job-link");
        _mockJobLinker.Setup(j => j.GetUrl(It.IsAny<JobName>(), It.IsAny<int>()))
            .Returns("http://example.org/job-link");

        _mockMailSender.Setup(m => m.Send(It.IsAny<string>(), "On-Demand Report", It.IsAny<string>(), It.IsAny<string>()))
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
            BuildReferenceResult.Done(new(_referenceTestJob, RandomData.NextBuildNumber), true),
            BuildReferenceResult.Done(onDemandTest, true),
            BuildDiff.Diff(failedTestDiff));

        var report = new RequestReport([new ChainReport(
            BuildReferenceResult.Done(onDemandRoot, true),
            [buildDiffResult])]);

        _mockJobLinker.Setup(j => j.GetUrl(It.IsAny<JobName>()))
            .Returns("http://example.org/job-link");
        _mockJobLinker.Setup(j => j.GetUrl(It.IsAny<JobName>(), It.IsAny<int>()))
            .Returns("http://example.org/job-link");

        _mockMailSender.Setup(m => m.Send(It.IsAny<string>(), "On-Demand Report", It.IsAny<string>(), It.Is<string>(m => m.Contains(inMail))))
            .Returns(Task.CompletedTask);

        Assert.DoesNotThrowAsync(() => _reportSender.Send(requestState, report));
    }

    [Test]
    public Task Send_FailedTestsDiffOK_InMail()
    {
        var failedTestsDiff = new FailedTestDiff(TestBuildDiffStatus.OK, []);

        return Send_FailedTestDiff(failedTestsDiff, "✅ OK");
    }

    [TestCase(1, "🔴 1 new failed test")]
    [TestCase(2, "🔴 2 new failed tests")]
    public Task Send_FailedTestsDiffNewFailures_InMail(int count, string status)
    {
        var failedTestResults = Enumerable.Range(1, count)
            .Select(i => new FailedTestResult(new FailedTest("ClassName", $"TestName{i}", "Details"), Newness.New, false))
            .ToArray();
        var failedTestsDiff = new FailedTestDiff(TestBuildDiffStatus.NewFailures, failedTestResults);

        return Send_FailedTestDiff(failedTestsDiff, status);
    }

    [TestCase(1, "🔴 1 new failed test (incl. 🟠 1 flaky)")]
    [TestCase(2, "🔴 2 new failed tests (incl. 🟠 2 flaky)")]
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

        return Send_FailedTestDiff(failedTestsDiff, "⚠ same failed tests (not included in report)");
    }

    [TestCase(1, "🔴 1 updated failed test")]
    [TestCase(2, "🔴 2 updated failed tests")]
    public Task Send_FailedTestsDiffUpdatedFailures_InMail(int count, string status)
    {
        var failedTestResults = Enumerable.Range(1, count)
            .Select(i => new FailedTestResult(new FailedTest("ClassName", $"TestName{i}", "Details"), Newness.Updated, false))
            .ToArray();
        var failedTestsDiff = new FailedTestDiff(TestBuildDiffStatus.UpdatedFailures, failedTestResults);

        return Send_FailedTestDiff(failedTestsDiff, status);
    }

    [TestCase(1, "🔴 1 updated failed test (incl. 🟠 1 flaky)")]
    [TestCase(2, "🔴 2 updated failed tests (incl. 🟠 2 flaky)")]
    public Task Send_FailedTestsDiffFlakyUpdatedFailures_InMail(int count, string status)
    {
        var failedTestResults = Enumerable.Range(1, count)
            .Select(i => new FailedTestResult(new FailedTest("ClassName", $"TestName{i}", "Details"), Newness.Updated, true))
            .ToArray();
        var failedTestsDiff = new FailedTestDiff(TestBuildDiffStatus.UpdatedFailures, failedTestResults);

        return Send_FailedTestDiff(failedTestsDiff, status);
    }

    [TestCase(1, 1, "🔴 1 new failed test<br />🔴 1 updated failed test<br />⚠ same failed tests (not included in report)")]
    [TestCase(2, 1, "🔴 2 new failed tests<br />🔴 1 updated failed test<br />⚠ same failed tests (not included in report)")]
    [TestCase(1, 2, "🔴 1 new failed test<br />🔴 2 updated failed tests<br />⚠ same failed tests (not included in report)")]
    [TestCase(2, 2, "🔴 2 new failed tests<br />🔴 2 updated failed tests<br />⚠ same failed tests (not included in report)")]
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

    [TestCase(1, 1, "🔴 1 new failed test (incl. 🟠 1 flaky)<br />🔴 1 updated failed test (incl. 🟠 1 flaky)<br />⚠ same failed tests (not included in report)")]
    [TestCase(2, 1, "🔴 2 new failed tests (incl. 🟠 2 flaky)<br />🔴 1 updated failed test (incl. 🟠 1 flaky)<br />⚠ same failed tests (not included in report)")]
    [TestCase(1, 2, "🔴 1 new failed test (incl. 🟠 1 flaky)<br />🔴 2 updated failed tests (incl. 🟠 2 flaky)<br />⚠ same failed tests (not included in report)")]
    [TestCase(2, 2, "🔴 2 new failed tests (incl. 🟠 2 flaky)<br />🔴 2 updated failed tests (incl. 🟠 2 flaky)<br />⚠ same failed tests (not included in report)")]
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

    [Test]
    public Task Send_WithVeryLongTestDetails_InMail()
    {
        var failedTestResults = new FailedTestResult[] {
            new(new FailedTest("ClassName", $"TestName", new string('z', 10000)), Newness.New, false),
        };
        var failedTestsDiff = new FailedTestDiff(TestBuildDiffStatus.NewFailures, failedTestResults);

        return Send_FailedTestDiff(failedTestsDiff, "zzzzz...");
    }
}
