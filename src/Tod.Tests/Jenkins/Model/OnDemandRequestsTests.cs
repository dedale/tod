using NUnit.Framework;
using System.Diagnostics;
using System.Text.Json;
using Tod.Jenkins;
using Tod.Tests.IO;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class OnDemandRequestsTests : IDisposable
{
    private static RequestState CreateRequestState(BuildReference? referenceRoot = null, BuildReference? onDemandRoot = null)
    {
        var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), new("main"), ["test"]);
        onDemandRoot ??= new BuildReference("OnDemandJob", RandomData.NextBuildNumber);
        return RequestState.New(
            request,
            referenceRoot ?? new BuildReference("ReferenceJob", RandomData.NextBuildNumber),
            onDemandRoot,
            [new RequestBuildDiff(new("ReferenceTest"), new("OnDemandTest"))]
        );
    }

    private static RequestState CreateRequestStateTriggered(BuildReference? onDemandRoot = null)
    {
        return CreateRequestState(onDemandRoot: onDemandRoot)
            .TriggerTests((job, refSpec) => Task.FromResult(RandomData.NextBuildNumber));
    }

    private static BuildReference GetOnDemandRoot(RequestState request)
    {
        return request.ChainDiffs[0].OnDemandRoot.Match(
            onPending: _ => throw new InvalidOperationException("OnDemand root build is pending"),
            onTriggered: br => br,
            onDone: br => br
        );
    }

    private static RequestState CreateRequestStateDone(BuildReference? referenceRoot = null)
    {
        var buildNumber = RandomData.NextBuildNumber;
        var request = CreateRequestState(referenceRoot: referenceRoot);
        var chainDiff = request.ChainDiffs[0];
        var onDemandRoot = GetOnDemandRoot(request);
        request = request
            .TriggerTests((job, refSpec) => Task.FromResult(buildNumber))
            .DoneReferenceTestBuild(chainDiff.ReferenceRoot, new BuildReference("ReferenceTest", RandomData.NextBuildNumber))
            .DoneOnDemandTestBuild(onDemandRoot, new BuildReference("OnDemandTest", buildNumber));
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

    [Test]
    public void ActiveRequests_WithNoRequests_ReturnsEmptyCollection()
    {
        Assert.That(_requests.ActiveRequests, Is.Empty);
    }

    [Test]
    public void ActiveRequests_WithOnlyDoneRequests_ReturnsEmptyCollection()
    {
        var requestState = CreateRequestStateDone();
        _requests.Add(requestState);

        Assert.That(_requests.ActiveRequests, Is.Empty);
    }

    [Test]
    public void ActiveRequests_WithActiveRequests_ReturnsOnlyActiveRequests()
    {
        var activeState = CreateRequestState();
        var doneState = CreateRequestStateDone();
        _requests.Add(activeState);
        _requests.Add(doneState);

        Assert.That(_requests.ActiveRequests.Single().Value.Request.Id, Is.EqualTo(activeState.Request.Id));
    }

    [Test]
    public void Add_NewRequest_AddsToCollection()
    {
        var requestState = CreateRequestState();

        _requests.Add(requestState);

        Assert.That(_requests.ActiveRequests.Single().Value.Request.Id, Is.EqualTo(requestState.Request.Id));
    }

    [Test]
    public void Add_DuplicateRequest_ThrowsArgumentException()
    {
        var requestState = CreateRequestState();
        _requests.Add(requestState);

        Assert.That(() => _requests.Add(requestState), Throws.ArgumentException);
    }

    [Test]
    public void Update_ExistingRequest_UpdatesState()
    {
        var originalState = CreateRequestState();
        var cached = _requests.Add(originalState);

        using (var locked = _requests.ActiveRequests.Single().Lock(nameof(Update_ExistingRequest_UpdatesState)))
        {
            locked.Update(request => request.TriggerTests((job, refSpec) => Task.FromResult(RandomData.NextBuildNumber)));
        }

        Assert.That(_requests.ActiveRequests.Single().Value.ChainDiffs[0].Status, Is.EqualTo(ChainStatus.TestsTriggered));
        var requests2 = new OnDemandRequests(_temp.Path);
        Assert.That(requests2.ActiveRequests.Single().Value.ChainDiffs[0].Status, Is.EqualTo(ChainStatus.TestsTriggered));
    }

    [Test]
    public void TryGetRootTriggered_MatchingOnDemandRoot_ReturnsCorrectRequest()
    {
        var onDemandRoot = new BuildReference("OnDemandJob", 42);
        var requestState = CreateRequestState(onDemandRoot: onDemandRoot);
        _requests.Add(requestState);

        Assert.That(_requests.TryGetRootTriggered(onDemandRoot, out var result), Is.True);
        Debug.Assert(result is not null);
        Assert.That(result.Value.Request.Id, Is.EqualTo(requestState.Request.Id));
        result.Dispose();
    }

    [Test]
    public void TryGetRootTriggered_NoMatchingRequest()
    {
        var nonExistentRoot = new BuildReference("OnDemandJob", RandomData.NextBuildNumber);
        var requestState = CreateRequestState();
        _requests.Add(requestState);
        Assert.That(_requests.TryGetRootTriggered(nonExistentRoot, out var _), Is.False);
    }

    [Test]
    public void TryGetRootTriggered_NonRootTriggeredStatus_DoesNotMatch()
    {
        var onDemandRoot = new BuildReference("OnDemandJob", 42);
        var requestState = CreateRequestStateTriggered(onDemandRoot);
        _requests.Add(requestState);
        Assert.That(_requests.TryGetRootTriggered(onDemandRoot, out var _), Is.False);
    }

    [Test]
    public void TryGetTestTriggered_NoRequests_ReturnsFalse()
    {
        var rootBuild = new BuildReference("MainJob", 21);
        var testBuild = new BuildReference("TestJob", 42);
        Assert.That(_requests.TryGetTestTriggered(rootBuild, testBuild, out var foundRequest), Is.False);
        Assert.That(foundRequest, Is.Null);
    }

    [Test]
    public void TryGetTestTriggered_NoMatchingTestBuild_ReturnsFalse()
    {
        var requestState = CreateRequestState()
            .TriggerTests((job, refSpec) => Task.FromResult(RandomData.NextBuildNumber));
        _requests.Add(requestState);
        var testBuild = new BuildReference("OtherTestJob", 42);
        Assert.That(_requests.TryGetTestTriggered(GetOnDemandRoot(requestState), testBuild, out var foundRequest), Is.False);
        Assert.That(foundRequest, Is.Null);
    }

    [Test]
    public void TryGetTestTriggered_MatchingTestBuild_ReturnsTrue()
    {
        var buildNumber = RandomData.NextBuildNumber;
        var requestState = CreateRequestState()
            .TriggerTests((job, refSpec) => Task.FromResult(buildNumber));
        _requests.Add(requestState);
        var testBuild = new BuildReference("OnDemandTest", buildNumber);
        Assert.That(_requests.TryGetTestTriggered(GetOnDemandRoot(requestState), testBuild, out var foundRequest), Is.True);
        Debug.Assert(foundRequest is not null);
        Assert.That(foundRequest.Value.Request.Id, Is.EqualTo(requestState.Request.Id));
        foundRequest.Dispose();
    }

    [Test]
    public void TryGetTestTriggered_CompletedTestBuild_ReturnsFalse()
    {
        var buildNumber = RandomData.NextBuildNumber;
        var requestState = CreateRequestState();
        var onDemandRoot = GetOnDemandRoot(requestState);
        requestState = requestState
            .TriggerTests((job, refSpec) => Task.FromResult(buildNumber))
            .DoneOnDemandTestBuild(onDemandRoot, new BuildReference("OnDemandTest", buildNumber));
        _requests.Add(requestState);
        var testBuild = new BuildReference("OnDemandTest", buildNumber);
        Assert.That(_requests.TryGetTestTriggered(onDemandRoot, testBuild, out var foundRequest), Is.False);
        Assert.That(foundRequest, Is.Null);
    }

    [Test]
    public void TryGetTestTriggered_MultipleRequests_ReturnsMatchingRequest()
    {
        var buildNumber1 = RandomData.NextBuildNumber;
        var buildNumber2 = RandomData.NextBuildNumber;
        var requestState1 = CreateRequestState()
            .TriggerTests((job, refSpec) => Task.FromResult(buildNumber1));
        var requestState2 = CreateRequestState()
            .TriggerTests((job, refSpec) => Task.FromResult(buildNumber2));
        _requests.Add(requestState1);
        _requests.Add(requestState2);
        var testBuild = new BuildReference("OnDemandTest", buildNumber1);
        Assert.That(_requests.TryGetTestTriggered(GetOnDemandRoot(requestState1), testBuild, out var foundRequest), Is.True);
        Debug.Assert(foundRequest is not null);
        Assert.That(foundRequest.Value.Request.Id, Is.EqualTo(requestState1.Request.Id));
        foundRequest.Dispose();
    }

    [Test]
    public void TryGetTestTriggered_PendingOnDemandRoot_ReturnsFalse()
    {
        var requestState = CreateRequestState();
        var serializable = requestState.ToSerializable();
        // Creates invalid state where OnDemandRoot is still pending but tests are triggered, needed for full coverage
        serializable.ChainDiffs[0].OnDemandRoot = RequestBuildReference.Create(new("OnDemandJob")).ToSerializable();
        requestState = serializable.FromSerializable();
        _requests.Add(requestState);
        var onDemandRoot = new BuildReference("OnDemandJob", RandomData.NextBuildNumber);
        var testBuild = new BuildReference("OnDemandTest", RandomData.NextBuildNumber);
        Assert.That(_requests.TryGetTestTriggered(onDemandRoot, testBuild, out var _), Is.False);
    }

    [Test]
    public void TryGetTestTriggered_TriggeredOnDemandRoot_ReturnsFalse()
    {
        var requestState = CreateRequestState();
        var serializable = requestState.ToSerializable();
        var buildNumber = RandomData.NextBuildNumber;
        // Creates invalid state where OnDemandRoot is still pending but tests are triggered, needed for full coverage
        serializable.ChainDiffs[0].OnDemandRoot = RequestBuildReference.Create(new("OnDemandJob")).Trigger(buildNumber).ToSerializable();
        requestState = serializable.FromSerializable();
        _requests.Add(requestState);
        var onDemandRoot = new BuildReference("OnDemandJob", buildNumber);
        var testBuild = new BuildReference("OnDemandTest", RandomData.NextBuildNumber);
        Assert.That(_requests.TryGetTestTriggered(onDemandRoot, testBuild, out var _), Is.False);
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
    public void GetPendingReferenceTest_NoMatchingRootBuild_ReturnsEmptyList()
    {
        var requestState = CreateRequestState();
        _requests.Add(requestState);
        var rootBuild = new BuildReference("OtherBuild", 42);
        var testJob = new JobName("TestJob");
        using var result = _requests.GetPendingReferenceTest(rootBuild, testJob);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetPendingReferenceTest_MatchingRequestAndJob_ReturnsRequest()
    {
        var requestState = CreateRequestState();
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
    public void GetPendingReferenceTest_CompletedReference_ReturnsEmptyList()
    {
        var rootBuild = new BuildReference("ReferenceJob", RandomData.NextBuildNumber);
        var requestState = CreateRequestState();
        requestState = requestState
            .DoneReferenceTestBuild(requestState.ChainDiffs[0].ReferenceRoot, new BuildReference("ReferenceTest", RandomData.NextBuildNumber));
        _requests.Add(requestState);
        var testJob = new JobName("ReferenceTest");
        using var result = _requests.GetPendingReferenceTest(rootBuild, testJob);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetPendingReferenceTest_MultipleRequests_ReturnsMatchingRequests()
    {
        var rootBuild = new BuildReference("ReferenceJob", RandomData.NextBuildNumber);
        var requestState1 = CreateRequestState(referenceRoot: rootBuild);
        var requestState2 = CreateRequestState(referenceRoot: rootBuild);
        var requestState3 = CreateRequestState(referenceRoot: rootBuild.Next());
        var requestState4 = CreateRequestStateDone(referenceRoot: rootBuild);
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
    public void GetPendingReferenceTest_IgnoreOtherTests()
    {
        var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), new("main"), ["test"]);
        var referenceRoot = new BuildReference("ReferenceJob", RandomData.NextBuildNumber);
        var requestState1 = CreateRequestState(referenceRoot: referenceRoot);
        var onDemandRoot = new BuildReference("OnDemandJob", RandomData.NextBuildNumber);
        var otherRequest = RequestState.New(
            request,
            referenceRoot,
            onDemandRoot,
            [new RequestBuildDiff(new("ReferenceTest2"), new("OnDemandTest2"))]
        );
        var testJob = new JobName("ReferenceTest");
        using var result = _requests.GetPendingReferenceTest(referenceRoot, testJob);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Serialization_Works()
    {
        using (Assert.EnterMultipleScope())
        {
            var diffs = new List<RequestBuildDiff>
            {
                new(new("MainTest1"), new("OnDemandTest1")),
                new(new("MainTest2"), new("OnDemandTest2")),
            };
            var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), new("main"), ["tests"]);
            var referenceRoot = new BuildReference("MainBuild", RandomData.NextBuildNumber);
            var onDemandRoot = new BuildReference("OnDemandBuild", RandomData.NextBuildNumber);
            var requestState = RequestState.New(request, referenceRoot, onDemandRoot, diffs);
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
