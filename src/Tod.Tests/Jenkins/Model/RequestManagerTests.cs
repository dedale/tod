using Moq;
using NUnit.Framework;
using Tod.Git;
using Tod.Jenkins;
using Tod.Tests.IO;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class RequestManagerTests
{
    private readonly BranchName _mainBranch = new("main");
    private readonly JobName _referenceRootJob = new("MAIN-build");
    private readonly JobName _referencTestJob = new("MAIN-test");
    private readonly JobName _onDemandRootJob = new("CUSTOM-build");
    private readonly JobName _onDemandTestJob = new("CUSTOM-test");
    private TempDirectory _temp;
    private Mock<IFilterManager> _filterManager;
    private Mock<IJenkinsClient> _jenkinsClient;
    private Mock<IReportSender> _requestReport;

    [SetUp]
    public void SetUp()
    {
        _temp = new TempDirectory();
        _filterManager = new Mock<IFilterManager>(MockBehavior.Strict);
        _jenkinsClient = new Mock<IJenkinsClient>(MockBehavior.Strict);
        _requestReport = new Mock<IReportSender>(MockBehavior.Strict);
    }

    [TearDown]
    public void TearDown()
    {
        _temp.Dispose();
        _filterManager.VerifyAll();
        _jenkinsClient.VerifyAll();
        _requestReport.VerifyAll();
    }

    private BranchReference AddBranchReference(BranchName branchName, IReferenceStore referenceStore, out Sha1 sha1, out RootBuild rootBuild)
    {
        var jobName = $"{branchName.Value.ToUpperInvariant()}-build";
        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(new(jobName));
        rootBuild = RandomData.NextRootBuild(jobName: jobName, testJobNames: [_referencTestJob.Value]);
        branchReference.TryAdd(rootBuild);
        sha1 = rootBuild.Commits[1];
        return branchReference;
    }

    private BranchReference AddMainBranchReference(IReferenceStore referenceStore)
    {
        return AddBranchReference(_mainBranch, referenceStore, out _, out _);
    }

    private BranchReference AddMainBranchReference(IReferenceStore referenceStore, out Sha1 sha1)
    {
        return AddBranchReference(_mainBranch, referenceStore, out sha1, out _);
    }

    private Workspace GetWorkspace(BranchReference branchReference, IOnDemandStore onDemandStore)
    {
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        var onDemandRequests = new OnDemandRequests(Path.Combine(_temp.Path, "requests"));
        var workspace = new Workspace([], onDemandBuilds, onDemandRequests);
        workspace.Add(branchReference);
        return workspace;
    }

    private Workspace GetWorkspace(IOnDemandStore onDemandStore)
    {
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        var onDemandRequests = new OnDemandRequests(Path.Combine(_temp.Path, "requests"));
        var workspace = new Workspace([], onDemandBuilds, onDemandRequests);
        return workspace;
    }

    private RequestManager GetRequestManager(Workspace workspace)
    {
        return new RequestManager(workspace, _filterManager.Object, _jenkinsClient.Object, _requestReport.Object);
    }

    [Test]
    public async Task Register_ValidRequest_AddsToWorkspace()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithNewRootBuilds(_referenceRootJob)
            .WithTestobs(_referencTestJob)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithRootJobs(_onDemandRootJob);

        var branchReference = AddBranchReference(_mainBranch, referenceStore, out Sha1 sha1, out RootBuild rootBuild);

        var requestFilters = new[] { "integration" };

        var request = Request.Create(RandomData.NextSha1(), rootBuild.Commits[1], _mainBranch, requestFilters);

        var expectedBuildNumber = RandomData.NextBuildNumber;
        _jenkinsClient.Setup(c => c.TriggerBuild(_onDemandRootJob, request.Commit, It.IsAny<int>())).ReturnsAsync(expectedBuildNumber);

        _filterManager.Setup(m => m.GetTestBuildDiffs(requestFilters, _mainBranch))
            .Returns([new(_referencTestJob, _onDemandTestJob)]);

        var workspace = GetWorkspace(branchReference, onDemandStore);

        Assert.That(workspace.OnDemandRequests.ActiveRequests, Is.Empty);

        var requestManager = GetRequestManager(workspace);

        await requestManager.Register(request, [new(_referenceRootJob, _onDemandRootJob)]).ConfigureAwait(false);

        Assert.That(workspace.OnDemandRequests.ActiveRequests.Single().Value.Request.Id, Is.EqualTo(request.Id));
    }

    [Test]
    public void Register_UnknownBranch_ThrowsInvalidOperationException()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithNewRootBuilds(_referenceRootJob)
            .WithTestobs(_referencTestJob)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore);

        var branchReference = AddMainBranchReference(referenceStore);

        var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), new("unknown"), ["integration"]);

        var workspace = GetWorkspace(branchReference, onDemandStore);
        var requestManager = GetRequestManager(workspace);

        Assert.That(() => requestManager.Register(request, [new(_referenceRootJob, _onDemandRootJob)]).ConfigureAwait(false),
            Throws.InvalidOperationException.With.Message.EqualTo("Cannot use 'unknown' branch for reference"));
    }

    [Test]
    public void Register_UnknownCommit_ThrowsInvalidOperationException()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithNewRootBuilds(_referenceRootJob)
            .WithTestobs(_referencTestJob)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore);

        var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), _mainBranch, ["integration"]);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(_referenceRootJob);
        var workspace = GetWorkspace(branchReference, onDemandStore);
        var requestManager = GetRequestManager(workspace);

        Assert.That(() => requestManager.Register(request, [new(_referenceRootJob, _onDemandRootJob)]).ConfigureAwait(false),
            Throws.InvalidOperationException.With.Message.StartWith("Unknown parent commit"));
    }

    [Test]
    public async Task Register_ValidRequest_ReuseExistingReferenceBuilds()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithNewRootBuilds(_referenceRootJob)
            .WithNewTestBuilds(_referencTestJob)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithRootJobs(_onDemandRootJob);

        var branchReference = AddBranchReference(_mainBranch, referenceStore, out Sha1 sha1, out RootBuild rootBuild);

        var refTestBuild = new BuildReference(_referencTestJob, RandomData.NextBuildNumber);
        Assert.That(branchReference.TryAdd(new TestBuild(
            refTestBuild.JobName,
            Guid.NewGuid().ToString(),
            refTestBuild.BuildNumber,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5),
            true,
            rootBuild.Reference,
            []
        )), Is.True);

        var requestFilters = new[] { "integration" };
        var request = Request.Create(RandomData.NextSha1(), rootBuild.Commits[1], _mainBranch, requestFilters);

        _jenkinsClient.Setup(c => c.TriggerBuild(_onDemandRootJob, request.Commit, It.IsAny<int>()))
            .ReturnsAsync(RandomData.NextBuildNumber);

        _filterManager.Setup(m => m.GetTestBuildDiffs(requestFilters, _mainBranch))
            .Returns([new RequestBuildDiff(_referencTestJob, _onDemandTestJob)]);

        var workspace = GetWorkspace(branchReference, onDemandStore);
        var requestManager = GetRequestManager(workspace);

        await requestManager.Register(request, [new(_referenceRootJob, _onDemandRootJob)]).ConfigureAwait(false);

        var cachedRequest = workspace.OnDemandRequests.ActiveRequests.Single();
        Assert.That(cachedRequest.Value.ChainDiffs[0].TestBuildDiffs.Single().ReferenceBuild.IsDone, Is.True);
    }

    [Test]
    public async Task Register_DoneRequest_SendReport()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithNewRootBuilds(_referenceRootJob)
            .WithNewTestBuilds(_referencTestJob)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithNewRootBuilds(_onDemandRootJob)
            .WithNewTestBuilds(_onDemandTestJob);

        var branchReference = AddBranchReference(_mainBranch, referenceStore, out Sha1 sha1, out RootBuild rootBuild);

        var refTestBuild = new BuildReference(_referencTestJob, RandomData.NextBuildNumber);
        Assert.That(branchReference.TryAdd(new TestBuild(
            refTestBuild.JobName,
            Guid.NewGuid().ToString(),
            refTestBuild.BuildNumber,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5),
            true,
            rootBuild.Reference,
            []
        )), Is.True);

        var requestFilters = new[] { "integration" };
        var request = Request.Create(RandomData.NextSha1(), rootBuild.Commits[0], _mainBranch, requestFilters);

        var onDemandRootBuild = new RootBuild(
            _onDemandRootJob,
            "custom-build-id",
            RandomData.NextBuildNumber,
            DateTime.UtcNow.AddHours(-2),
            DateTime.UtcNow.AddHours(-1),
            true,
            [request.Commit],
            []
        );

        var workspace = GetWorkspace(branchReference, onDemandStore);

        workspace.OnDemandBuilds.TryAdd(onDemandRootBuild);

        workspace.OnDemandBuilds.TryAdd(new TestBuild(
            _onDemandTestJob,
            "test-id",
            RandomData.NextBuildNumber,
            DateTime.UtcNow.AddHours(-2),
            DateTime.UtcNow.AddHours(-1),
            true,
            onDemandRootBuild.Reference,
            []
        ));

        _filterManager.Setup(m => m.GetTestBuildDiffs(requestFilters, _mainBranch))
            .Returns([new RequestBuildDiff(_referencTestJob, _onDemandTestJob)]);

        _requestReport.Setup(x => x.Send(It.Is<RequestState>(r => r.Request.Id == request.Id && r.IsDone == true), It.IsAny<Workspace>()));

        var rootDiffs = new RootDiff[] { new(_referenceRootJob, _onDemandRootJob) };

        var requestManager = GetRequestManager(workspace);

        await requestManager.Register(request, rootDiffs).ConfigureAwait(false);
    }

    [Test]
    public async Task Register_IgnoresFailedOnDemandRootBuilds()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithNewRootBuilds(_referenceRootJob)
            .WithNewTestBuilds(_referencTestJob)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithNewRootBuilds(_onDemandRootJob);

        var branchReference = AddBranchReference(_mainBranch, referenceStore, out Sha1 sha1, out RootBuild rootBuild);

        var refTestBuild = new BuildReference(_referencTestJob, RandomData.NextBuildNumber);
        Assert.That(branchReference.TryAdd(new TestBuild(
            refTestBuild.JobName,
            Guid.NewGuid().ToString(),
            refTestBuild.BuildNumber,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5),
            true,
            rootBuild.Reference,
            []
        )), Is.True);

        var requestFilters = new[] { "integration" };
        var request = Request.Create(RandomData.NextSha1(), rootBuild.Commits[0], _mainBranch, requestFilters);

        var workspace = GetWorkspace(branchReference, onDemandStore);

        var onDemandRootBuild = new RootBuild(
            _onDemandRootJob,
            "custom-build-id",
            RandomData.NextBuildNumber,
            DateTime.UtcNow.AddHours(-2),
            DateTime.UtcNow.AddHours(-1),
            false,
            [RandomData.NextSha1()],
            []
        );
        workspace.OnDemandBuilds.TryAdd(onDemandRootBuild);

        _jenkinsClient.Setup(c => c.TriggerBuild(_onDemandRootJob, request.Commit, It.IsAny<int>()))
            .ReturnsAsync(RandomData.NextBuildNumber);

        _filterManager.Setup(m => m.GetTestBuildDiffs(requestFilters, _mainBranch))
            .Returns([new RequestBuildDiff(_referencTestJob, _onDemandTestJob)]);

        var requestManager = GetRequestManager(workspace);

        await requestManager.Register(request, [new(_referenceRootJob, _onDemandRootJob)]).ConfigureAwait(false);
    }

    [Test]
    public async Task Register_IgnoresOtherTestBuilds()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithNewRootBuilds(_referenceRootJob)
            .WithNewTestBuilds(_referencTestJob)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithNewRootBuilds(_onDemandRootJob)
            .WithNewTestBuilds(_onDemandTestJob);

        var branchReference = AddBranchReference(_mainBranch, referenceStore, out Sha1 sha1, out RootBuild rootBuild);

        var refTestBuild = new BuildReference(_referencTestJob, RandomData.NextBuildNumber);
        Assert.That(branchReference.TryAdd(new TestBuild(
            refTestBuild.JobName,
            Guid.NewGuid().ToString(),
            refTestBuild.BuildNumber,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5),
            true,
            rootBuild.Reference,
            []
        )), Is.True);

        var requestFilters = new[] { "integration" };
        var request = Request.Create(RandomData.NextSha1(), rootBuild.Commits[0], _mainBranch, requestFilters);

        var workspace = GetWorkspace(branchReference, onDemandStore);

        var onDemandRootBuild = new RootBuild(
            _onDemandRootJob,
            "custom-build-id",
            RandomData.NextBuildNumber,
            DateTime.UtcNow.AddHours(-2),
            DateTime.UtcNow.AddHours(-1),
            true,
            [request.Commit],
            []
        );
        workspace.OnDemandBuilds.TryAdd(onDemandRootBuild);

        workspace.OnDemandBuilds.TryAdd(new TestBuild(
            _onDemandTestJob,
            "test-id",
            RandomData.NextBuildNumber,
            DateTime.UtcNow.AddHours(-2),
            DateTime.UtcNow.AddHours(-1),
            true,
            new BuildReference(onDemandRootBuild.JobName, RandomData.NextBuildNumber),
            []
        ));

        _filterManager.Setup(m => m.GetTestBuildDiffs(requestFilters, _mainBranch))
            .Returns([new RequestBuildDiff(_referencTestJob, _onDemandTestJob)]);

        _jenkinsClient.Setup(c => c.TriggerBuild(_onDemandTestJob, request.Commit, It.IsAny<int>()))
            .ReturnsAsync(RandomData.NextBuildNumber);

        var requestManager = GetRequestManager(workspace);

        await requestManager.Register(request, [new(_referenceRootJob, _onDemandRootJob)]).ConfigureAwait(false);
    }

    [Test]
    public void TriggerOnDemandTests_ValidRequest_TriggersTests()
    {
        using var mocks = StoreMocks.New()
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore);

        var onDemandRoot = new BuildReference(_onDemandRootJob, RandomData.NextBuildNumber);
        var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), _mainBranch, ["integration"]);

        var diffs = new List<RequestBuildDiff> {
            new(_referencTestJob, _onDemandTestJob)
        };
        var requestState = RequestState.New(request, new BuildReference(_referenceRootJob, RandomData.NextBuildNumber), onDemandRoot, diffs);

        var workspace = GetWorkspace(onDemandStore);
        workspace.OnDemandRequests.Add(requestState);

        _jenkinsClient.Setup(c => c.TriggerBuild(_onDemandTestJob, request.Commit, It.IsAny<int>()))
            .ReturnsAsync(RandomData.NextBuildNumber);

        var requestManager = GetRequestManager(workspace);
        requestManager.PostOnDemandRootBuild(onDemandRoot, true);

        Assert.That(workspace.OnDemandRequests.ActiveRequests.Single().Value.ChainDiffs[0].Status, Is.EqualTo(ChainStatus.TestsTriggered));
    }

    [Test]
    public void TriggerOnDemandTests_FailedRootBuild_TriggersNone()
    {
        using var mocks = StoreMocks.New()
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore);

        var onDemandRoot = new BuildReference(_onDemandRootJob, RandomData.NextBuildNumber);
        var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), _mainBranch, ["integration"]);

        var diffs = new List<RequestBuildDiff> {
            new(_referencTestJob, _onDemandTestJob)
        };
        var requestState = RequestState.New(request, new BuildReference(_referenceRootJob, RandomData.NextBuildNumber), onDemandRoot, diffs);

        var workspace = GetWorkspace(onDemandStore);
        workspace.OnDemandRequests.Add(requestState);

        var requestManager = GetRequestManager(workspace);
        requestManager.PostOnDemandRootBuild(onDemandRoot, false);

        Assert.That(workspace.OnDemandRequests.ActiveRequests, Is.Empty);
    }

    [TestCase("MAIN-test", "CUSTOM-test")]
    [TestCase("CUSTOM-test", "MAIN-test")]
    public void UpdateRequests_WithActiveRequests_UpdatesStates(string job1, string job2)
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _referenceRootJob, out var referenceStore)
            .WithNewRootBuilds(_referenceRootJob)
            .WithNewTestBuilds(_referencTestJob)
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore)
            .WithNewTestBuilds(_onDemandTestJob);

        var branchReference = AddMainBranchReference(referenceStore, out Sha1 sha1);

        var referenceRoot = new BuildReference(_referenceRootJob, RandomData.NextBuildNumber);
        var onDemandRoot = new BuildReference(_onDemandRootJob, RandomData.NextBuildNumber);
        var request = Request.Create(RandomData.NextSha1(), sha1, _mainBranch, ["integration"]);

        var diffs = new List<RequestBuildDiff> {
            new(_referencTestJob, _onDemandTestJob)
        };
        var requestState = RequestState.New(request, referenceRoot, onDemandRoot, diffs);
        var testBuildNumber = RandomData.NextBuildNumber;
        requestState = requestState.TriggerTests((job, refSpec) => Task.FromResult(testBuildNumber));

        var workspace = GetWorkspace(branchReference, onDemandStore);

        workspace.OnDemandRequests.Add(requestState);

        _requestReport.Setup(x => x.Send(It.Is<RequestState>(r => r.Request.Id == request.Id && r.IsDone == true), It.IsAny<Workspace>()));

        var requestManager = GetRequestManager(workspace);

        foreach (var job in new[] { job1, job2 })
        {
            if (job == _referencTestJob.Value)
            {
                var refTestBuild = RandomData.NextTestBuild(testJobName: job, rootBuild: referenceRoot);
                Assert.That(workspace.BranchReferences.First().TryAdd(refTestBuild), Is.True);
                requestManager.PostReferenceTestBuild(referenceRoot, refTestBuild.Reference);
            }
            if (job == _onDemandTestJob.Value)
            {
                var onDemandTestBuild = RandomData.NextTestBuild(testJobName: job, buildNumber: testBuildNumber, rootBuild: onDemandRoot);
                Assert.That(workspace.OnDemandBuilds.TryAdd(onDemandTestBuild), Is.True);
                requestManager.PostOnDemandTestBuild(onDemandRoot, onDemandTestBuild.Reference);
            }
            if (job == job1)
            {
                Assert.That(workspace.OnDemandRequests.ActiveRequests.Single().Value.ChainDiffs[0].Status, Is.EqualTo(ChainStatus.TestsTriggered));
            }
        }
        Assert.That(workspace.OnDemandRequests.ActiveRequests, Is.Empty);
    }
}
