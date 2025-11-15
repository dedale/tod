using NUnit.Framework;
using System.Diagnostics;
using System.Text.Json;
using Tod.Git;
using Tod.Jenkins;
using Tod.Tests.IO;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class OnDemandRequestsTests : IDisposable
{
    private static readonly JobName s_onDemandRootJob = new("OnDemandJob");
    private static readonly JobName s_onDemandTestJob = new("OnDemandTest");

    private static readonly JobName s_referenceRootJob = new("ReferenceJob");
    private static readonly JobName s_referenceTestJob = new("ReferenceTest");

    private static Task<RequestState> CreateRequestState(IOnDemandStore onDemandStore, BuildReference? referenceRoot = null)
    {
        var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), new("main"), ["test"]);
        var onDemandRoot = RequestRootBuildReference.Queue(s_onDemandRootJob, request.Commit);
        var chains = new RequestChain[] {
            new(
                referenceRoot ?? new BuildReference(s_referenceRootJob, RandomData.NextBuildNumber),
                onDemandRoot,
                [ new RequestBuildDiff(s_referenceTestJob, s_onDemandTestJob) ]
            )
        };
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        Func<JobName, Sha1, Task> triggerRootBuild = (job, commit) => Task.CompletedTask;
        Func<JobName, int, Task> triggerTestBuild = (job, buildNumber) => Task.CompletedTask;
        return RequestState.New(request, chains, onDemandBuilds, triggerRootBuild, triggerTestBuild);
    }

    private static async Task<RequestState> CreateRequestStateTriggered(IOnDemandStore onDemandStore)
    {
        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        return requestState.TriggerTests(RandomData.NextBuildNumber, (job, refSpec) => Task.FromResult(RandomData.NextBuildNumber));
    }

    private static async Task<RequestState> CreateRequestStateDone(IOnDemandStore onDemandStore, BuildReference? referenceRoot = null)
    {
        var rootBuildNumber = RandomData.NextBuildNumber;
        var testBuildNumber = RandomData.NextBuildNumber;
        var request = await CreateRequestState(onDemandStore, referenceRoot: referenceRoot).ConfigureAwait(false);
        var chainDiff = request.ChainDiffs[0];
        request = request
            .TriggerTests(rootBuildNumber, (job, refSpec) => Task.FromResult(testBuildNumber))
            .DoneReferenceTestBuild(chainDiff.ReferenceRoot, new BuildReference(s_referenceTestJob, RandomData.NextBuildNumber))
            .DoneOnDemandTestBuild(new(s_onDemandRootJob, rootBuildNumber), new BuildReference(s_onDemandTestJob, testBuildNumber));
        Assert.That(request.IsDone, Is.True);
        return request;
    }

    private readonly TempDirectory _temp;
    private OnDemandRequests _requests;

    public OnDemandRequestsTests()
    {
        _temp = new TempDirectory();
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    [SetUp]
    public void SetUp()
    {
        _requests = new OnDemandRequests(_temp.Path);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.GetFiles(_temp.Path).ToList().ForEach(f =>
        {
            try
            {
                File.Delete(f);
            }
            catch (IOException)
            {
            }
        });
        Assert.That(Directory.GetFiles(_temp.Path), Is.Empty);
    }

    private static StoreMocks.BuildStoreMocks OnDemandStoreMocks(out IOnDemandStore onDemandStore)
    {
        return StoreMocks.New()
            .WithOnDemandStore(s_onDemandRootJob, out onDemandStore)
            .WithRootJobs(s_onDemandRootJob);
    }

    [Test]
    public void ActiveRequests_WithNoRequests_ReturnsEmptyCollection()
    {
        Assert.That(_requests.ActiveRequests, Is.Empty);
    }

    [Test]
    public async Task ActiveRequests_WithOnlyDoneRequests_ReturnsEmptyCollection()
    {
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await CreateRequestStateDone(onDemandStore).ConfigureAwait(false);
        _requests.Add(requestState);

        Assert.That(_requests.ActiveRequests, Is.Empty);
    }

    [Test]
    public async Task ActiveRequests_WithActiveRequests_ReturnsOnlyActiveRequests()
    {
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var activeState = await CreateRequestStateTriggered(onDemandStore).ConfigureAwait(false);
        var doneState = await CreateRequestStateDone(onDemandStore).ConfigureAwait(false);
        _requests.Add(activeState);
        _requests.Add(doneState);

        Assert.That(_requests.ActiveRequests.Single().Value.Request.Id, Is.EqualTo(activeState.Request.Id));
    }

    [Test]
    public async Task Add_NewRequest_AddsToCollection()
    {
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);

        _requests.Add(requestState);

        Assert.That(_requests.ActiveRequests.Single().Value.Request.Id, Is.EqualTo(requestState.Request.Id));
    }

    [Test]
    public async Task Add_DuplicateRequest_ThrowsArgumentException()
    {
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        _requests.Add(requestState);

        Assert.That(() => _requests.Add(requestState), Throws.ArgumentException);
    }

    [Test]
    public async Task Update_ExistingRequest_UpdatesState()
    {
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var originalState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        var cached = _requests.Add(originalState);

        using (var locked = _requests.ActiveRequests.Single().Lock(nameof(Update_ExistingRequest_UpdatesState)))
        {
            locked.Update(request => request.TriggerTests(RandomData.NextBuildNumber, (job, refSpec) => Task.FromResult(RandomData.NextBuildNumber)));
        }

        Assert.That(_requests.ActiveRequests.Single().Value.ChainDiffs[0].Status, Is.EqualTo(ChainStatus.TestsTriggered));
        var requests2 = new OnDemandRequests(_temp.Path);
        Assert.That(requests2.ActiveRequests.Single().Value.ChainDiffs[0].Status, Is.EqualTo(ChainStatus.TestsTriggered));
    }

    [Test]
    public async Task TryGetRootQueued_MatchingOnDemandRoot_ReturnsCorrectRequest()
    {
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        _requests.Add(requestState);

        Assert.That(_requests.TryGetRootQueued(s_onDemandRootJob, requestState.Request.Commit, out var result), Is.True);
        Debug.Assert(result is not null);
        Assert.That(result.Value.Request.Id, Is.EqualTo(requestState.Request.Id));
        result.Dispose();
    }

    [Test]
    public async Task TryGetRootQueued_OtherJob_ReturnsFalse()
    {
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        _requests.Add(requestState);
        Assert.That(_requests.TryGetRootQueued(new JobName("OtherJob"), requestState.Request.Commit, out var _), Is.False);
    }

    [Test]
    public async Task TryGetRootQueued_OtherCommit_ReturnsFalse()
    {
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        _requests.Add(requestState);
        Assert.That(_requests.TryGetRootQueued(s_onDemandRootJob, RandomData.NextSha1(), out var _), Is.False);
    }

    [Test]
    public async Task TryGetRootQueued_NonRootTriggeredStatus_DoesNotMatch()
    {
        var onDemandRoot = new BuildReference("OnDemandJob", 42);
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await CreateRequestStateTriggered(onDemandStore).ConfigureAwait(false);
        _requests.Add(requestState);
        Assert.That(_requests.TryGetRootQueued(onDemandRoot.JobName, requestState.Request.Commit, out var _), Is.False);
    }

    [Test]
    public void TryGetTestQueued_NoRequests_ReturnsFalse()
    {
        var rootBuild = new BuildReference("MainJob", 21);
        var testJob = new JobName("TestJob");
        Assert.That(_requests.TryGetTestQueued(rootBuild, testJob, out var foundRequest), Is.False);
        Assert.That(foundRequest, Is.Null);
    }

    [Test]
    public async Task TryGetTestQueued_NoMatchingTestBuild_ReturnsFalse()
    {
        var rootBuildNumber = RandomData.NextBuildNumber;
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        requestState = requestState.TriggerTests(rootBuildNumber, (job, refSpec) => Task.FromResult(RandomData.NextBuildNumber));
        _requests.Add(requestState);
        var testJob = new JobName("OtherTestJob");
        Assert.That(_requests.TryGetTestQueued(new(s_onDemandRootJob, rootBuildNumber), testJob, out var foundRequest), Is.False);
        Assert.That(foundRequest, Is.Null);
    }

    [Test]
    public async Task TryGetTestQueued_MatchingTestBuild_ReturnsTrue()
    {
        var rootBuildNumber = RandomData.NextBuildNumber;
        var testBuildNumber = RandomData.NextBuildNumber;
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        requestState = requestState.TriggerTests(rootBuildNumber, (job, refSpec) => Task.FromResult(testBuildNumber));
        _requests.Add(requestState);
        Assert.That(_requests.TryGetTestQueued(new(s_onDemandRootJob, rootBuildNumber), s_onDemandTestJob, out var foundRequest), Is.True);
        Debug.Assert(foundRequest is not null);
        Assert.That(foundRequest.Value.Request.Id, Is.EqualTo(requestState.Request.Id));
        foundRequest.Dispose();
    }

    [Test]
    public async Task TryGetTestQueued_CompletedTestBuild_ReturnsFalse()
    {
        var buildNumber = RandomData.NextBuildNumber;
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        var onDemandRoot = new BuildReference(s_onDemandRootJob, RandomData.NextBuildNumber);
        requestState = requestState
            .TriggerTests(onDemandRoot.BuildNumber, (job, refSpec) => Task.FromResult(buildNumber))
            .DoneOnDemandTestBuild(onDemandRoot, new BuildReference("OnDemandTest", buildNumber));
        _requests.Add(requestState);
        var testJob = new JobName("OnDemandTest");
        Assert.That(_requests.TryGetTestQueued(onDemandRoot, testJob, out var foundRequest), Is.False);
        Assert.That(foundRequest, Is.Null);
    }

    [Test]
    public async Task TryGetTestQueued_MultipleRequests_ReturnsMatchingRequest()
    {
        var rootBuildNumber1 = RandomData.NextBuildNumber;
        var onDemandRoot1 = new BuildReference(s_onDemandRootJob, rootBuildNumber1);
        var testBuildNumber1 = RandomData.NextBuildNumber;
        var testBuildNumber2 = RandomData.NextBuildNumber;
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState1 = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        requestState1 = requestState1.TriggerTests(rootBuildNumber1, (job, refSpec) => Task.FromResult(testBuildNumber1));
        var requestState2 = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        requestState2 = requestState2.TriggerTests(RandomData.NextBuildNumber, (job, refSpec) => Task.FromResult(testBuildNumber2));
        _requests.Add(requestState1);
        _requests.Add(requestState2);
        var testJob = new JobName("OnDemandTest");
        Assert.That(_requests.TryGetTestQueued(onDemandRoot1, testJob, out var foundRequest), Is.True);
        Debug.Assert(foundRequest is not null);
        Assert.That(foundRequest.Value.Request.Id, Is.EqualTo(requestState1.Request.Id));
        foundRequest.Dispose();
    }

    [Test]
    public async Task TryGetTestQueued_QueuedOnDemandRoot_ReturnsFalse()
    {
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        var serializable = requestState.ToSerializable();
        // Creates invalid state where OnDemandRoot is still pending but tests are triggered, needed for full coverage
        serializable.ChainDiffs[0].OnDemandRoot = RequestRootBuildReference.Queue(s_onDemandRootJob, requestState.Request.Commit).ToSerializable();
        requestState = serializable.FromSerializable();
        _requests.Add(requestState);
        var onDemandRoot = new BuildReference("OnDemandJob", RandomData.NextBuildNumber);
        var testJob = new JobName("OnDemandTest");
        Assert.That(_requests.TryGetTestQueued(onDemandRoot, testJob, out var _), Is.False);
    }

    [Test]
    public void GetPendingReferenceTest_NoRequests_ReturnsEmptyList()
    {
        var rootBuild = new BuildReference("MainBuild", 42);
        var testJob = new JobName("TestJob");
        using var result = _requests.GetPendingReferenceTest(rootBuild, testJob);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetPendingReferenceTest_NoMatchingRootBuild_ReturnsEmptyList()
    {
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        _requests.Add(requestState);
        var rootBuild = new BuildReference("OtherBuild", 42);
        var testJob = new JobName("TestJob");
        using var result = _requests.GetPendingReferenceTest(rootBuild, testJob);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetPendingReferenceTest_MatchingRequestAndJob_ReturnsRequest()
    {
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        _requests.Add(requestState);
        var rootBuild = requestState.ChainDiffs[0].ReferenceRoot;
        var testJob = new JobName("ReferenceTest");
        using var result = _requests.GetPendingReferenceTest(rootBuild, testJob);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Value.Request.Id, Is.EqualTo(requestState.Request.Id));
        }
    }

    [Test]
    public async Task GetPendingReferenceTest_CompletedReference_ReturnsEmptyList()
    {
        var rootBuild = new BuildReference("ReferenceJob", RandomData.NextBuildNumber);
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await CreateRequestState(onDemandStore).ConfigureAwait(false);
        requestState = requestState
            .DoneReferenceTestBuild(requestState.ChainDiffs[0].ReferenceRoot, new BuildReference("ReferenceTest", RandomData.NextBuildNumber));
        _requests.Add(requestState);
        var testJob = new JobName("ReferenceTest");
        using var result = _requests.GetPendingReferenceTest(rootBuild, testJob);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetPendingReferenceTest_MultipleRequests_ReturnsMatchingRequests()
    {
        var rootBuild = new BuildReference("ReferenceJob", RandomData.NextBuildNumber);
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState1 = await CreateRequestState(onDemandStore, referenceRoot: rootBuild).ConfigureAwait(false);
        var requestState2 = await CreateRequestState(onDemandStore, referenceRoot: rootBuild).ConfigureAwait(false);
        var requestState3 = await CreateRequestState(onDemandStore, referenceRoot: rootBuild.Next()).ConfigureAwait(false);
        var requestState4 = await CreateRequestStateDone(onDemandStore, referenceRoot: rootBuild).ConfigureAwait(false);
        _requests.Add(requestState1);
        _requests.Add(requestState2);
        _requests.Add(requestState3);
        _requests.Add(requestState4);
        var testJob = new JobName("ReferenceTest");
        using var result = _requests.GetPendingReferenceTest(rootBuild, testJob);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(2));
            var ids = result.Select(r => r.Value.Request.Id).ToList();
            Assert.That(ids, Does.Contain(requestState1.Request.Id));
            Assert.That(ids, Does.Contain(requestState2.Request.Id));
        }
    }

    [Test]
    public async Task GetPendingReferenceTest_IgnoreOtherTests()
    {
        var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), new("main"), ["test"]);
        var referenceRoot = new BuildReference("ReferenceJob", RandomData.NextBuildNumber);
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState1 = await CreateRequestState(onDemandStore, referenceRoot: referenceRoot).ConfigureAwait(false);
        var onDemandRoot = new BuildReference("OnDemandJob", RandomData.NextBuildNumber);

        var otherRequest = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), new("main"), ["test"]);
        var otherOnDemandRoot = RequestRootBuildReference.Queue(s_onDemandRootJob, otherRequest.Commit);
        var chains = new RequestChain[] {
            new(
                referenceRoot,
                otherOnDemandRoot,
                [ new RequestBuildDiff(new("ReferenceTest2"), new("OnDemandTest2")) ]
            )
        };
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        Func<JobName, Sha1, Task> triggerRootBuild = (job, commit) => Task.CompletedTask;
        Func<JobName, int, Task> triggerTestBuild = (job, buildNumber) => Task.CompletedTask;
        var otherRequestState = RequestState.New(request, chains, onDemandBuilds, triggerRootBuild, triggerTestBuild);

        var testJob = new JobName("ReferenceTest");
        using var result = _requests.GetPendingReferenceTest(referenceRoot, testJob);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task Serialization_Works()
    {
        using (Assert.EnterMultipleScope())
        {
            var diffs = new List<RequestBuildDiff>
            {
                new(new("MainTest1"), new("OnDemandTest1")),
                new(new("MainTest2"), new("OnDemandTest2")),
            };
            var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), new("main"), ["tests"]);
            var onDemandRoot = RequestRootBuildReference.Queue(s_onDemandRootJob, request.Commit);
            var chains = new RequestChain[] {
                new(
                    new BuildReference(s_referenceRootJob, RandomData.NextBuildNumber),
                    onDemandRoot,
                    [ new RequestBuildDiff(s_referenceTestJob, s_onDemandTestJob) ]
                )
            };
            using var mocks = OnDemandStoreMocks(out var onDemandStore);
            var onDemandBuilds = new OnDemandBuilds(onDemandStore);
            Func<JobName, Sha1, Task> triggerRootBuild = (job, commit) => Task.CompletedTask;
            Func<JobName, int, Task> triggerTestBuild = (job, buildNumber) => Task.CompletedTask;
            var requestState = await RequestState.New(request, chains, onDemandBuilds, triggerRootBuild, triggerTestBuild);
            _requests.Add(requestState);

            var json = JsonSerializer.Serialize(_requests, new JsonSerializerOptions { WriteIndented = true });
            var clone = JsonSerializer.Deserialize<OnDemandRequests>(json)!;

            var requestClone = clone.ActiveRequests.Single().Value;
            Assert.That(requestClone.Request, Is.EqualTo(requestState.Request));
            Assert.That(requestClone.ChainDiffs, Has.Length.EqualTo(requestState.ChainDiffs.Length));
            for (var i = 0; i < requestClone.ChainDiffs.Length; i++)
            {
                var chainClone = requestClone.ChainDiffs[i];
                var chainOriginal = requestState.ChainDiffs[i];
                Assert.That(chainClone.ReferenceRoot, Is.EqualTo(chainOriginal.ReferenceRoot));
                Assert.That(chainClone.OnDemandRoot, Is.EqualTo(chainOriginal.OnDemandRoot));
                Assert.That(chainClone.Status, Is.EqualTo(chainOriginal.Status));
                var chainDiffCount = chainOriginal.TestBuildDiffs.Count();
                Assert.That(chainClone.TestBuildDiffs.Count(), Is.EqualTo(chainDiffCount));
                for (var j = 0; j < chainDiffCount; j++)
                {
                    Assert.That(chainClone.TestBuildDiffs.Count(), Is.EqualTo(chainOriginal.TestBuildDiffs.Count()));
                }
            }
        }
    }
}
