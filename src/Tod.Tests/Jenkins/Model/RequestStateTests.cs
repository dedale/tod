using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class RequestStateTests
{
    private readonly Request _request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), new("main"), ["tests"], GetUserEmail);
    private readonly BuildReference _referenceRoot = new("MainBuild", RandomData.NextBuildNumber);
    private readonly JobName _onDemandRootJob = new("OnDemandBuild");
    private readonly BuildReference _onDemandRoot = new("OnDemandBuild", RandomData.NextBuildNumber);

    private static string GetUserEmail(string userName) => $"{userName}@example.org";

    private Task<RequestState> NewState(List<RequestBuildDiff> testBuildDiffs, IOnDemandStore onDemandStore)
    {
        var chain = new RequestChain(_referenceRoot, RequestRootBuildReference.Queue(_onDemandRootJob, _request.Commit), [.. testBuildDiffs]);
        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        Func<OnDemandJobKind, JobName, TriggerParameters, Task> triggerBuild = (_, _, _) => Task.CompletedTask;
        return RequestState.New(_request, [chain], onDemandBuilds, triggerBuild);
    }

    private StoreMocks.BuildStoreMocks OnDemandStoreMocks(out IOnDemandStore onDemandStore)
    {
        return StoreMocks.New()
            .WithOnDemandStore(_onDemandRootJob, out onDemandStore)
            .WithRootJobs(_onDemandRootJob);
    }

    [Test]
    public async Task DoneReferenceTestBuild_WithMatchingBuild_IsDone()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);
        Assert.That(requestState.ChainDiffs[0].TestBuildDiffs.First().ReferenceBuild.IsDone, Is.False);
        var testBuild = new BuildReference("MainTest1", RandomData.NextBuildNumber);
        var update = requestState.DoneReferenceTestBuild(_referenceRoot, testBuild);
        Assert.That(update.ChainDiffs[0].TestBuildDiffs.First().ReferenceBuild.IsDone, Is.True);
    }

    [Test]
    public async Task DoneReferenceTestBuild_WithNoMatchingBuildJob_IsNotDone()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);
        var testBuild = new BuildReference("OtherTest", RandomData.NextBuildNumber);
        var update = requestState.DoneReferenceTestBuild(_referenceRoot, testBuild);
        Assert.That(update.ChainDiffs[0].TestBuildDiffs.First().ReferenceBuild.IsDone, Is.False);
    }

    [Test]
    public async Task DoneReferenceTestBuild_WithNoMatchingBuildNumber_IsNotDone()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);
        var testBuild = new BuildReference("MainTest1", RandomData.NextBuildNumber);
        var update = requestState.DoneReferenceTestBuild(new BuildReference(_referenceRoot.JobName, RandomData.NextBuildNumber), testBuild);
        Assert.That(update.ChainDiffs[0].TestBuildDiffs.First().ReferenceBuild.IsDone, Is.False);
    }

    [Test]
    public async Task TriggerTests_WithPendingBuilds_UpdatesBuilds()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);
        var update = requestState.TriggerTests(_onDemandRoot, job => Task.CompletedTask);
        using (Assert.EnterMultipleScope())
        {
            update.ChainDiffs[0].TestBuildDiffs.ToList().ForEach(diff =>
            {
                diff.OnDemandBuild.Match(
                    onPending: jobName => Assert.Fail("Expected triggered build"),
                    onQueued: job =>
                    {
                        Assert.That(job.Value.StartsWith("OnDemandTest"));
                    },
                    onDone: _ => Assert.Fail("Expected triggered build")
                );
            });
        }
    }

    [Test]
    public async Task TriggerTests_WithTriggeredBuilds_ThrowsAlreadyDone()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);
        var update = requestState.TriggerTests(_onDemandRoot, job => Task.CompletedTask);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => update.TriggerTests(_onDemandRoot, job => throw new InvalidOperationException()),
                Throws.InvalidOperationException.With.Message.EqualTo("Already done")); // Because root job is already done

            update.ChainDiffs[0].TestBuildDiffs.ToList().ForEach(diff =>
            {
                diff.OnDemandBuild.Match(
                    onPending: jobName => Assert.Fail("Expected triggered build"),
                    onQueued: job =>
                    {
                        Assert.That(job.Value.StartsWith("OnDemandTest"));
                    },
                    onDone: _ => Assert.Fail("Expected triggered build")
                );
            });
        }
    }

    [Test]
    public async Task TriggerTests_WithDoneBuilds_ThrowsAlreadyDone()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);
        var buildNumberByJob = new[] { "OnDemandTest1", "OnDemandTest2" }.ToDictionary(job => job, job => RandomData.NextBuildNumber);
        var update = requestState.TriggerTests(_onDemandRoot, job => Task.CompletedTask)
            .DoneOnDemandTestBuild(_onDemandRoot, new BuildReference("OnDemandTest1", buildNumberByJob["OnDemandTest1"]))
            .DoneOnDemandTestBuild(_onDemandRoot, new BuildReference("OnDemandTest2", buildNumberByJob["OnDemandTest2"]));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => update.TriggerTests(_onDemandRoot, job => throw new InvalidOperationException()),
                Throws.InvalidOperationException.With.Message.EqualTo("Already done"));

            update.ChainDiffs[0].TestBuildDiffs.ToList().ForEach(diff =>
            {
                diff.OnDemandBuild.Match(
                    onPending: _ => Assert.Fail("Expected done build"),
                    onQueued: _ => Assert.Fail("Expected done build"),
                    onDone: buildReference =>
                    {
                        Assert.That(buildReference.JobName.Value.StartsWith("OnDemandTest"));
                        Assert.That(buildReference.BuildNumber, Is.GreaterThan(0));
                    }
                );
            });
        }
    }

    [Test]
    public async Task DoneOnDemandTestBuild_WithMatchingBuild_IsDone()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);
        var buildNumberByJob = new[] { "OnDemandTest1", "OnDemandTest2" }.ToDictionary(jobName => jobName, jobName => RandomData.NextBuildNumber);
        requestState = requestState.TriggerTests(_onDemandRoot, job => Task.CompletedTask);

        var testBuild = new BuildReference("OnDemandTest1", buildNumberByJob["OnDemandTest1"]);
        requestState = requestState.DoneOnDemandTestBuild(_onDemandRoot, testBuild);
        Assert.That(requestState.ChainDiffs[0].TestBuildDiffs.First().OnDemandBuild.IsDone, Is.True);

        testBuild = new BuildReference("OnDemandTest2", buildNumberByJob["OnDemandTest2"]);
        requestState = requestState.DoneOnDemandTestBuild(_onDemandRoot, testBuild);
        Assert.That(requestState.ChainDiffs[0].TestBuildDiffs.Last().OnDemandBuild.IsDone, Is.True);
    }

    [Test]
    public async Task DoneOnDemandTestBuild_WithOtherBuild_NoChange()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
        };
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);
        var buildNumber = RandomData.NextBuildNumber;
        requestState = requestState.TriggerTests(_onDemandRoot, job => Task.CompletedTask);

        var testBuild = new BuildReference("OnDemandTest1", buildNumber);
        requestState = requestState.DoneOnDemandTestBuild(new BuildReference(_onDemandRootJob, RandomData.NextBuildNumber), testBuild);
        requestState.ChainDiffs[0].TestBuildDiffs.First().OnDemandBuild.Match(
            onPending: _ => Assert.Fail("Expected triggered build"),
            onQueued: jobName =>
            {
                Assert.That(jobName.Value, Is.EqualTo("OnDemandTest1"));
            },
            onDone: _ => Assert.Fail("Expected triggered build")
        );
    }

    [Test]
    public async Task DoneOnDemandTestBuild_InvalidQueuedRoot_DoesNothing()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
        };
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);
        var buildNumberByJob = new[] { "OnDemandTest1" }.ToDictionary(jobName => jobName, jobName => RandomData.NextBuildNumber);
        requestState = requestState.TriggerTests(_onDemandRoot, job => Task.CompletedTask);

        // Invalid state for code coverage
        var serializable = requestState.ToSerializable();
        serializable.ChainDiffs[0].OnDemandRoot = RequestRootBuildReference.Queue(_onDemandRootJob, _request.Commit).ToSerializable();
        requestState = serializable.FromSerializable();

        var testBuild = new BuildReference("OnDemandTest1", buildNumberByJob["OnDemandTest1"]);
        requestState = requestState.DoneOnDemandTestBuild(_onDemandRoot, testBuild);
        requestState.ChainDiffs[0].TestBuildDiffs.Single().OnDemandBuild.Match(
            onPending: _ => Assert.Fail("Expected triggered build"),
            onQueued: _ => { },
            onDone: _ => Assert.Fail("Expected triggered build")
        );
    }

    [Test]
    public async Task DoneTestBuild_WhateverTheOrderReferenceOrOnDemand_RequestIsDoneWhenAll()
    {
        using (Assert.EnterMultipleScope())
        {
            var jobNames = new[] { "MainTest1", "MainTest2", "OnDemandTest1", "OnDemandTest2" }.ToList();
            var scenarii = new List<List<string>>
            {
                ([.. jobNames])
            };
            jobNames.Reverse();
            scenarii.Add([.. jobNames]);
            jobNames.Shuffle();
            scenarii.Add([.. jobNames]);
            foreach (var scenario in scenarii)
            {
                var buildNumberByJob = scenario.ToDictionary(jobName => jobName, jobName => RandomData.NextBuildNumber);
                var diffs = new List<RequestBuildDiff>
                {
                    new(new("MainTest1"), new("OnDemandTest1")),
                    new(new("MainTest2"), new("OnDemandTest2")),
                };
                using var mocks = OnDemandStoreMocks(out var onDemandStore);
                var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);
                var update = requestState.TriggerTests(_onDemandRoot, job => Task.CompletedTask);
                for (var i = 0; i < scenario.Count; i++)
                {
                    var jobName = scenario[i];
                    var testBuild = new BuildReference(jobName, buildNumberByJob[jobName]);
                    if (jobName.StartsWith("Main"))
                    {
                        update = update.DoneReferenceTestBuild(_referenceRoot, testBuild);
                    }
                    else
                    {
                        update = update.DoneOnDemandTestBuild(_onDemandRoot, testBuild);
                    }
                    if (i == scenario.Count - 1)
                    {
                        Assert.That(update.IsDone, Is.True);
                    }
                    else
                    {
                        Assert.That(update.IsDone, Is.False);
                    }
                }
            }
        }
    }

    [Test]
    public async Task Abort_WithPendingBuilds_SetsStatusToDone()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);
        var update = requestState.AbortAll();
        Assert.That(update.IsDone, Is.True);
    }

    [Test]
    public async Task Abort_WithTriggeredBuilds_SetsStatusToDone()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);
        var update = requestState.TriggerTests(_onDemandRoot, job => Task.CompletedTask);
        update = update.AbortAll();
        Assert.That(update.IsDone, Is.True);
    }

    [Test]
    public async Task Abort_WithDoneBuilds_SetsStatusToDone()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);
        var buildNumberByJob = new[] { "OnDemandTest1", "OnDemandTest2" }.ToDictionary(jobName => jobName, jobName => RandomData.NextBuildNumber);
        var update = requestState.TriggerTests(_onDemandRoot, job => Task.CompletedTask)
            .DoneOnDemandTestBuild(_onDemandRoot, new BuildReference("OnDemandTest1", buildNumberByJob["OnDemandTest1"]))
            .DoneOnDemandTestBuild(_onDemandRoot, new BuildReference("OnDemandTest2", buildNumberByJob["OnDemandTest2"]));
        update = update.AbortAll();
        Assert.That(update.IsDone, Is.True);
    }

    [Test]
    public async Task TryGetChainReference_WithMatchingReferenceRoot_ReturnsTrue()
    {
        // Arrange
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);

        // Act
        var result = requestState.TryGetChainReference(_referenceRoot, out var chainDiff);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(chainDiff, Is.Not.Null);
        Assert.That(chainDiff!.ReferenceRoot, Is.EqualTo(_referenceRoot));
    }

    [Test]
    public async Task TryGetChainReference_WithNonMatchingReferenceRoot_ReturnsFalse()
    {
        // Arrange
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);
        var otherReferenceRoot = new BuildReference("OtherBuild", RandomData.NextBuildNumber);

        // Act
        var result = requestState.TryGetChainReference(otherReferenceRoot, out var chainDiff);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(chainDiff, Is.Null);
    }

    [Test]
    public async Task TryGetChainReference_WithMultipleChains_ReturnsCorrectChain()
    {
        // Arrange
        var onDemandRootJob1 = new JobName("OnDemandBuild1");
        var onDemandRootJob2 = new JobName("OnDemandBuild2");
        var referenceRoot1 = new BuildReference("MainBuild1", RandomData.NextBuildNumber);
        var referenceRoot2 = new BuildReference("MainBuild2", RandomData.NextBuildNumber);
        var onDemandRoot1 = new BuildReference(onDemandRootJob1, RandomData.NextBuildNumber);
        var onDemandRoot2 = new BuildReference(onDemandRootJob2, RandomData.NextBuildNumber);
        
        var diffs1 = new List<RequestBuildDiff> { new(new("MainTest1"), new("OnDemandTest1")) };
        var diffs2 = new List<RequestBuildDiff> { new(new("MainTest2"), new("OnDemandTest2")) };
        
        var chains = new RequestChain[]
        {
            new(referenceRoot1, RequestRootBuildReference.Queue(onDemandRootJob1, _request.Commit), [.. diffs1]),
            new(referenceRoot2, RequestRootBuildReference.Queue(onDemandRootJob2, _request.Commit), [.. diffs2]),
        };

        using var mocks = StoreMocks.New()
            .WithOnDemandStore([onDemandRootJob1, onDemandRootJob2], out var onDemandStore)
            .WithRootJobs(onDemandRootJob1)
            .WithRootJobs(onDemandRootJob2);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        Func<OnDemandJobKind, JobName, TriggerParameters, Task> triggerBuild = (_, _, _) => Task.CompletedTask;
        var requestState = await RequestState.New(_request, chains, onDemandBuilds, triggerBuild).ConfigureAwait(false);

        // Act
        var result1 = requestState.TryGetChainReference(referenceRoot1, out var foundChain1);
        var result2 = requestState.TryGetChainReference(referenceRoot2, out var foundChain2);

        // Assert
        Assert.That(result1, Is.True);
        Assert.That(foundChain1, Is.Not.Null);
        Assert.That(foundChain1!.ReferenceRoot, Is.EqualTo(referenceRoot1));
        
        Assert.That(result2, Is.True);
        Assert.That(foundChain2, Is.Not.Null);
        Assert.That(foundChain2!.ReferenceRoot, Is.EqualTo(referenceRoot2));
    }

    [Test]
    public async Task TryGetChainOnDemand_WithMatchingTriggeredOnDemandRoot_ReturnsTrue()
    {
        // Arrange
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);

        // Act
        var result = requestState.TryGetChainOnDemand(_onDemandRootJob, _request.Commit, out var chainDiff);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(chainDiff, Is.Not.Null);
        chainDiff!.OnDemandRoot.Match(
            onQueued: (job, commit) =>
            {
                Assert.That(job, Is.EqualTo(_onDemandRootJob));
                Assert.That(commit, Is.EqualTo(_request.Commit));
            },
            onDone: _ => Assert.Fail("Expected triggered on-demand root"));
    }

    [Test]
    public async Task TryGetChainOnDemand_WithMatchingQueuedOnDemandRoot_ReturnsTrue()
    {
        // Arrange
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);

        // Act
        var result = requestState.TryGetChainOnDemand(_onDemandRootJob, _request.Commit, out var chainDiff);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(chainDiff, Is.Not.Null);
        chainDiff!.OnDemandRoot.Match(
            onQueued: (job, commit) =>
            {
                Assert.That(job, Is.EqualTo(_onDemandRootJob));
                Assert.That(commit, Is.EqualTo(_request.Commit));
            },
            onDone: _ => Assert.Fail("Expected queued on-demand root"));
    }

    [Test]
    public async Task TryGetChainOnDemand_WithMatchingDoneOnDemandRoot_ReturnsFalse()
    {
        // Arrange
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);
        requestState = requestState.TriggerTests(_onDemandRoot, job => Task.CompletedTask);

        // Act
        var result = requestState.TryGetChainOnDemand(_onDemandRootJob, _request.Commit, out var chainDiff);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(chainDiff, Is.Null);
    }

    [Test]
    public async Task TryGetChainOnDemand_WithNonMatchingOnDemandRoot_ReturnsFalse()
    {
        // Arrange
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        using var mocks = OnDemandStoreMocks(out var onDemandStore);
        var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);
        var otherOnDemandRoot = new BuildReference("OtherOnDemandBuild", RandomData.NextBuildNumber);

        // Act
        var result = requestState.TryGetChainOnDemand(otherOnDemandRoot.JobName, _request.Commit, out var chainDiff);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(chainDiff, Is.Null);
    }

    [Test]
    public async Task TryGetChainOnDemand_WithMultipleChains_ReturnsCorrectChain()
    {
        // Arrange
        var onDemandRootJob1 = new JobName("OnDemandBuild1");
        var onDemandRootJob2 = new JobName("OnDemandBuild2");
        var referenceRoot1 = new BuildReference("MainBuild1", RandomData.NextBuildNumber);
        var referenceRoot2 = new BuildReference("MainBuild2", RandomData.NextBuildNumber);
        var onDemandRoot1 = new BuildReference(onDemandRootJob1, RandomData.NextBuildNumber);
        var onDemandRoot2 = new BuildReference(onDemandRootJob2, RandomData.NextBuildNumber);
        
        var diffs1 = new List<RequestBuildDiff> { new(new("MainTest1"), new("OnDemandTest1")) };
        var diffs2 = new List<RequestBuildDiff> { new(new("MainTest2"), new("OnDemandTest2")) };

        var chains = new RequestChain[]
        {
            new(referenceRoot1, RequestRootBuildReference.Queue(onDemandRootJob1, _request.Commit), [.. diffs1]),
            new(referenceRoot2, RequestRootBuildReference.Queue(onDemandRootJob2, _request.Commit), [.. diffs2]),
        };

        using var mocks = StoreMocks.New()
            .WithOnDemandStore([onDemandRootJob1, onDemandRootJob2], out var onDemandStore)
            .WithRootJobs(onDemandRootJob1)
            .WithRootJobs(onDemandRootJob2);

        var onDemandBuilds = new OnDemandBuilds(onDemandStore);
        Func<OnDemandJobKind, JobName, TriggerParameters, Task> triggerBuild = (_, _, _) => Task.CompletedTask;
        var requestState = await RequestState.New(_request, chains, onDemandBuilds, triggerBuild).ConfigureAwait(false);

        // Act
        var result1 = requestState.TryGetChainOnDemand(onDemandRoot1.JobName, _request.Commit, out var foundChain1);
        var result2 = requestState.TryGetChainOnDemand(onDemandRoot2.JobName, _request.Commit, out var foundChain2);

        // Assert
        Assert.That(result1, Is.True);
        Assert.That(foundChain1, Is.Not.Null);
        foundChain1!.OnDemandRoot.Match(
            onQueued: (jobName, commit) =>
            {
                Assert.That(jobName, Is.EqualTo(onDemandRoot1.JobName));
                Assert.That(commit, Is.EqualTo(_request.Commit));
            },
            onDone: _ => Assert.Fail("Expected triggered on-demand root"));
        
        Assert.That(result2, Is.True);
        Assert.That(foundChain2, Is.Not.Null);
        foundChain2!.OnDemandRoot.Match(
            onQueued: (jobName, commit) =>
            {
                Assert.That(jobName, Is.EqualTo(onDemandRoot2.JobName));
                Assert.That(commit, Is.EqualTo(_request.Commit));
            },
            onDone: _ => Assert.Fail("Expected triggered on-demand root"));
    }

    [Test]
    public async Task SerializationRoundTrip_Works()
    {
        using (Assert.EnterMultipleScope())
        {
            var diffs = new List<RequestBuildDiff>
            {
                new(new("MainTest1"), new("OnDemandTest1")),
                new(new("MainTest2"), new("OnDemandTest2")),
            };
            using var mocks = OnDemandStoreMocks(out var onDemandStore);
            var requestState = await NewState(diffs, onDemandStore).ConfigureAwait(false);
            var clone = requestState.SerializationRoundTrip<RequestState, RequestState.Serializable>();
            Assert.That(clone.Request, Is.EqualTo(requestState.Request));
            Assert.That(clone.ChainDiffs, Has.Length.EqualTo(requestState.ChainDiffs.Length));
            for (var i = 0; i < clone.ChainDiffs.Length; i++)
            {
                var originalChainDiff = requestState.ChainDiffs[i];
                var clonedChainDiff = clone.ChainDiffs[i];

                Assert.That(clonedChainDiff.Status, Is.EqualTo(originalChainDiff.Status));
                Assert.That(clonedChainDiff.ReferenceRoot, Is.EqualTo(originalChainDiff.ReferenceRoot));
                Assert.That(clonedChainDiff.OnDemandRoot, Is.EqualTo(originalChainDiff.OnDemandRoot));
                Assert.That(clonedChainDiff.TestBuildDiffs.Count, Is.EqualTo(originalChainDiff.TestBuildDiffs.Count()));
            }
        }
    }
}
