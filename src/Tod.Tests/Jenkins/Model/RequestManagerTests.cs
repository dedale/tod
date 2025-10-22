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
    private readonly JobName _onDemandRootJob = new("CUSTOM-build");
    private TempDirectory _temp;
    private Workspace _workspace;
    private Mock<IFilterManager> _filterManager;
    private Mock<IJenkinsClient> _jenkinsClient;
    private Mock<IReportSender> _requestReport;
    private RequestManager _requestManager;

    [SetUp]
    public void SetUp()
    {
        _temp = new TempDirectory();
        var onDemandRequests = new OnDemandRequests(Path.Combine(_temp.Directory.Path, "requests"));
        _workspace = new Workspace(_temp.Directory.Path, [], new OnDemandBuilds(_onDemandRootJob), onDemandRequests);
        _filterManager = new Mock<IFilterManager>(MockBehavior.Strict);
        _jenkinsClient = new Mock<IJenkinsClient>(MockBehavior.Strict);
        _requestReport = new Mock<IReportSender>(MockBehavior.Strict);
        _requestManager = new RequestManager(_workspace, _filterManager.Object, _jenkinsClient.Object, _requestReport.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _temp.Dispose();
        _filterManager.VerifyAll();
        _jenkinsClient.VerifyAll();
        _requestReport.VerifyAll();
    }

    private BranchReference AddBranchReference(BranchName branchName, out Sha1 sha1)
    {
        return AddBranchReference(branchName, out sha1, out _);
    }

    private BranchReference AddBranchReference(BranchName branchName, out Sha1 sha1, out RootBuild rootBuild)
    {
        var jobName = $"{branchName.Value.ToUpperInvariant()}-build";
        var branchReference = new BranchReference(_mainBranch, new(jobName));
        rootBuild = RandomData.NextRootBuild(jobName: jobName);
        branchReference.TryAdd(rootBuild);
        _workspace.BranchReferences.Add(branchReference);
        sha1 = rootBuild.Commits[1];
        return branchReference;
    }

    private BranchReference AddMainBranchReference(out Sha1 sha1)
    {
        return AddBranchReference(_mainBranch, out sha1);
    }

    [Test]
    public async Task Register_ValidRequest_AddsToWorkspace()
    {
        var branchName = new BranchName("main");
        AddBranchReference(branchName, out Sha1 sha1, out RootBuild rootBuild);

        var requestFilters = new[] { "integration" };

        var request = Request.Create(RandomData.NextSha1(), rootBuild.Commits[1], branchName, requestFilters);

        var expectedBuildNumber = RandomData.NextBuildNumber;
        _jenkinsClient.Setup(c => c.TriggerBuild(_onDemandRootJob, request.Commit, It.IsAny<int>())).ReturnsAsync(expectedBuildNumber);

        _filterManager.Setup(m => m.GetTestBuildDiffs(requestFilters, branchName))
            .Returns([new(new("MAIN-test"), new("CUSTOM-test"))]);

        Assert.That(_workspace.OnDemandRequests.ActiveRequests, Is.Empty);

        await _requestManager.Register(request, [new(_referenceRootJob, _onDemandRootJob)]).ConfigureAwait(false);

        Assert.That(_workspace.OnDemandRequests.ActiveRequests.Single().Value.Request.Id, Is.EqualTo(request.Id));
    }

    [Test]
    public void Register_UnknownBranch_ThrowsInvalidOperationException()
    {
        AddMainBranchReference(out _);

        var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), new("unknown"), ["integration"]);

        Assert.That(() => _requestManager.Register(request, [new(_referenceRootJob, _onDemandRootJob)]).ConfigureAwait(false),
            Throws.InvalidOperationException.With.Message.EqualTo("Cannot use 'unknown' branch for reference"));
    }

    [Test]
    public void Register_UnknownCommit_ThrowsInvalidOperationException()
    {
        var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), _mainBranch, ["integration"]);

        var branchReference = new BranchReference(_mainBranch, _referenceRootJob);
        _workspace.BranchReferences.Add(branchReference);

        Assert.That(() => _requestManager.Register(request, [new(_referenceRootJob, _onDemandRootJob)]).ConfigureAwait(false),
            Throws.InvalidOperationException.With.Message.StartWith("Unknown parent commit"));
    }

    [Test]
    public async Task Register_ValidRequest_ReuseExistingReferenceBuilds()
    {
        var branchName = new BranchName("main");
        var branchReference = AddBranchReference(branchName, out Sha1 sha1, out RootBuild rootBuild);

        var refTestBuild = new BuildReference("MAIN-test", RandomData.NextBuildNumber);
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
        var request = Request.Create(RandomData.NextSha1(), rootBuild.Commits[1], branchName, requestFilters);

        _jenkinsClient.Setup(c => c.TriggerBuild(_onDemandRootJob, request.Commit, It.IsAny<int>()))
            .ReturnsAsync(RandomData.NextBuildNumber);

        _filterManager.Setup(m => m.GetTestBuildDiffs(requestFilters, branchName))
            .Returns([new RequestBuildDiff(new("MAIN-test"), new("CUSTOM-test"))]);

        await _requestManager.Register(request, [new(_referenceRootJob, _onDemandRootJob)]).ConfigureAwait(false);

        var cachedRequest = _workspace.OnDemandRequests.ActiveRequests.Single();
        Assert.That(cachedRequest.Value.ChainDiffs[0].TestBuildDiffs.Single().ReferenceBuild.IsDone, Is.True);
    }

    [Test]
    public async Task Register_DoneRequest_SendReport()
    {
        var branchName = new BranchName("main");
        var branchReference = AddBranchReference(branchName, out Sha1 sha1, out RootBuild rootBuild);

        var refTestBuild = new BuildReference("MAIN-test", RandomData.NextBuildNumber);
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
        var request = Request.Create(RandomData.NextSha1(), rootBuild.Commits[0], branchName, requestFilters);

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
        _workspace.OnDemandBuilds.TryAdd(onDemandRootBuild);

        _workspace.OnDemandBuilds.TryAdd(new TestBuild(
            new JobName("CUSTOM-test"),
            "test-id",
            RandomData.NextBuildNumber,
            DateTime.UtcNow.AddHours(-2),
            DateTime.UtcNow.AddHours(-1),
            true,
            onDemandRootBuild.Reference,
            []
        ));

        _filterManager.Setup(m => m.GetTestBuildDiffs(requestFilters, branchName))
            .Returns([new RequestBuildDiff(new("MAIN-test"), new("CUSTOM-test"))]);

        _requestReport.Setup(x => x.Send(It.Is<RequestState>(r => r.Request.Id == request.Id && r.IsDone == true), It.IsAny<Workspace>()));

        var rootDiffs = new RootDiff[] { new(_referenceRootJob, _onDemandRootJob) };

        await _requestManager.Register(request, rootDiffs).ConfigureAwait(false);
    }

    [Test]
    public async Task Register_IgnoresFailedOnDemandRootBuilds()
    {
        var branchName = new BranchName("main");
        var branchReference = AddBranchReference(branchName, out Sha1 sha1, out RootBuild rootBuild);

        var refTestBuild = new BuildReference("MAIN-test", RandomData.NextBuildNumber);
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
        var request = Request.Create(RandomData.NextSha1(), rootBuild.Commits[0], branchName, requestFilters);

        var onDemandRootBuild = new RootBuild(
            new JobName("   "),
            "custom-build-id",
            RandomData.NextBuildNumber,
            DateTime.UtcNow.AddHours(-2),
            DateTime.UtcNow.AddHours(-1),
            false,
            [RandomData.NextSha1()],
            []
        );
        _workspace.OnDemandBuilds.TryAdd(onDemandRootBuild);

        _jenkinsClient.Setup(c => c.TriggerBuild(_onDemandRootJob, request.Commit, It.IsAny<int>()))
            .ReturnsAsync(RandomData.NextBuildNumber);

        _filterManager.Setup(m => m.GetTestBuildDiffs(requestFilters, branchName))
            .Returns([new RequestBuildDiff(new("MAIN-test"), new("CUSTOM-test"))]);

        await _requestManager.Register(request, [new(_referenceRootJob, _onDemandRootJob)]).ConfigureAwait(false);
    }

    [Test]
    public async Task Register_IgnoresOtherTestBuilds()
    {
        var branchName = new BranchName("main");
        var branchReference = AddBranchReference(branchName, out Sha1 sha1, out RootBuild rootBuild);

        var refTestBuild = new BuildReference("MAIN-test", RandomData.NextBuildNumber);
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
        var request = Request.Create(RandomData.NextSha1(), rootBuild.Commits[0], branchName, requestFilters);

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
        _workspace.OnDemandBuilds.TryAdd(onDemandRootBuild);

        _workspace.OnDemandBuilds.TryAdd(new TestBuild(
            new JobName("CUSTOM-test"),
            "test-id",
            RandomData.NextBuildNumber,
            DateTime.UtcNow.AddHours(-2),
            DateTime.UtcNow.AddHours(-1),
            true,
            new BuildReference(onDemandRootBuild.JobName, RandomData.NextBuildNumber),
            []
        ));

        _filterManager.Setup(m => m.GetTestBuildDiffs(requestFilters, branchName))
            .Returns([new RequestBuildDiff(new("MAIN-test"), new("CUSTOM-test"))]);

        _jenkinsClient.Setup(c => c.TriggerBuild(new JobName("CUSTOM-test"), request.Commit, It.IsAny<int>()))
            .ReturnsAsync(RandomData.NextBuildNumber);

        await _requestManager.Register(request, [new(_referenceRootJob, _onDemandRootJob)]).ConfigureAwait(false);
    }

    [Test]
    public void TriggerOnDemandTests_ValidRequest_TriggersTests()
    {
        var onDemandRoot = new BuildReference(_onDemandRootJob, RandomData.NextBuildNumber);
        var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), new("main"), ["integration"]);

        var diffs = new List<RequestBuildDiff> {
            new(new("MAIN-test"), new("CUSTOM-test"))
        };
        var requestState = RequestState.New(request, new BuildReference(_referenceRootJob, RandomData.NextBuildNumber), onDemandRoot, diffs);

        _workspace.OnDemandRequests.Add(requestState);

        _jenkinsClient.Setup(c => c.TriggerBuild(new JobName("CUSTOM-test"), request.Commit, It.IsAny<int>()))
            .ReturnsAsync(RandomData.NextBuildNumber);

        _requestManager.PostOnDemandRootBuild(onDemandRoot, true);

        Assert.That(_workspace.OnDemandRequests.ActiveRequests.Single().Value.ChainDiffs[0].Status, Is.EqualTo(ChainStatus.TestsTriggered));
    }

    [Test]
    public void TriggerOnDemandTests_FailedRootBuild_TriggersNone()
    {
        var onDemandRoot = new BuildReference(_onDemandRootJob, RandomData.NextBuildNumber);
        var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), new("main"), ["integration"]);

        var diffs = new List<RequestBuildDiff> {
            new(new("MAIN-test"), new("CUSTOM-test"))
        };
        var requestState = RequestState.New(request, new BuildReference(_referenceRootJob, RandomData.NextBuildNumber), onDemandRoot, diffs);

        _workspace.OnDemandRequests.Add(requestState);

        _requestManager.PostOnDemandRootBuild(onDemandRoot, false);

        Assert.That(_workspace.OnDemandRequests.ActiveRequests, Is.Empty);
    }

    [TestCase("MAIN-test", "CUSTOM-test")]
    [TestCase("CUSTOM-test", "MAIN-test")]
    public void UpdateRequests_WithActiveRequests_UpdatesStates(string job1, string job2)
    {
        AddMainBranchReference(out Sha1 sha1);

        var referenceRoot = new BuildReference(_referenceRootJob, RandomData.NextBuildNumber);
        var onDemandRoot = new BuildReference(_onDemandRootJob, RandomData.NextBuildNumber);
        var request = Request.Create(RandomData.NextSha1(), sha1, new("main"), ["integration"]);

        var diffs = new List<RequestBuildDiff> {
            new(new("MAIN-test"), new("CUSTOM-test"))
        };
        var requestState = RequestState.New(request, referenceRoot, onDemandRoot, diffs);
        var testBuildNumber = RandomData.NextBuildNumber;
        requestState = requestState.TriggerTests((job, refSpec) => Task.FromResult(testBuildNumber));

        _workspace.OnDemandRequests.Add(requestState);

        _requestReport.Setup(x => x.Send(It.Is<RequestState>(r => r.Request.Id == request.Id && r.IsDone == true), It.IsAny<Workspace>()));

        foreach (var job in new[] { job1, job2 })
        {
            if (job == "MAIN-test")
            {
                var refTestBuild = RandomData.NextTestBuild(testJobName: "MAIN-test", rootBuild: referenceRoot);
                Assert.That(_workspace.BranchReferences.First().TryAdd(refTestBuild), Is.True);
                _requestManager.PostReferenceTestBuild(referenceRoot, refTestBuild.Reference);
            }
            if (job == "CUSTOM-test")
            {
                var onDemandTestBuild = RandomData.NextTestBuild(testJobName: "CUSTOM-test", buildNumber: testBuildNumber, rootBuild: onDemandRoot);
                Assert.That(_workspace.OnDemandBuilds.TryAdd(onDemandTestBuild), Is.True);
                _requestManager.PostOnDemandTestBuild(onDemandRoot, onDemandTestBuild.Reference);
            }
            if (job == job1)
            {
                Assert.That(_workspace.OnDemandRequests.ActiveRequests.Single().Value.ChainDiffs[0].Status, Is.EqualTo(ChainStatus.TestsTriggered));
            }
        }
        Assert.That(_workspace.OnDemandRequests.ActiveRequests, Is.Empty);
    }
}
