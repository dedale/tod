using NUnit.Framework;
using Tod.Git;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class RequestStateTests
{
    private readonly Request _request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), new("main"), ["tests"]);
    private readonly BuildReference _referenceRoot = new("MainBuild", RandomData.NextBuildNumber);
    private readonly BuildReference _onDemandRoot = new("OnDemandBuild", RandomData.NextBuildNumber);

    [Test]
    public void DoneReferenceTestBuild_WithMatchingBuild_IsDone()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);
        Assert.That(requestState.ChainDiffs[0].TestBuildDiffs.First().ReferenceBuild.IsDone, Is.False);
        var testBuild = new BuildReference("MainTest1", RandomData.NextBuildNumber);
        var update = requestState.DoneReferenceTestBuild(_referenceRoot, testBuild);
        Assert.That(update.ChainDiffs[0].TestBuildDiffs.First().ReferenceBuild.IsDone, Is.True);
    }

    [Test]
    public void DoneReferenceTestBuild_WithNoMatchingBuildJob_IsNotDone()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);
        var testBuild = new BuildReference("OtherTest", RandomData.NextBuildNumber);
        var update = requestState.DoneReferenceTestBuild(_referenceRoot, testBuild);
        Assert.That(update.ChainDiffs[0].TestBuildDiffs.First().ReferenceBuild.IsDone, Is.False);
    }

    [Test]
    public void DoneReferenceTestBuild_WithNoMatchingBuildNumber_IsNotDone()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);
        var testBuild = new BuildReference("MainTest1", RandomData.NextBuildNumber);
        var update = requestState.DoneReferenceTestBuild(new BuildReference(_referenceRoot.JobName, RandomData.NextBuildNumber), testBuild);
        Assert.That(update.ChainDiffs[0].TestBuildDiffs.First().ReferenceBuild.IsDone, Is.False);
    }

    [Test]
    public void TriggerTests_WithPendingBuilds_UpdatesBuilds()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);
        var update = requestState.TriggerTests((job, refSpec) => Task.FromResult(RandomData.NextBuildNumber));
        using (Assert.EnterMultipleScope())
        {
            update.ChainDiffs[0].TestBuildDiffs.ToList().ForEach(diff =>
            {
                diff.OnDemandBuild.Match(
                    onPending: jobName => Assert.Fail("Expected triggered build"),
                    onTriggered: buildReference =>
                    {
                        Assert.That(buildReference.JobName.Value.StartsWith("OnDemandTest"));
                        Assert.That(buildReference.BuildNumber, Is.GreaterThan(0));
                    },
                    onDone: _ => Assert.Fail("Expected triggered build")
                );
            });
        }
    }

    [Test]
    public void TriggerTests_WithTriggeredBuilds_ThrowsAlreadyDone()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);
        var update = requestState.TriggerTests((job, refSpec) => Task.FromResult(RandomData.NextBuildNumber));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => update.TriggerTests((job, refSpec) => throw new InvalidOperationException()),
                Throws.InvalidOperationException.With.Message.EqualTo("Already done"));

            update.ChainDiffs[0].TestBuildDiffs.ToList().ForEach(diff =>
            {
                diff.OnDemandBuild.Match(
                    onPending: jobName => Assert.Fail("Expected triggered build"),
                    onTriggered: buildReference =>
                    {
                        Assert.That(buildReference.JobName.Value.StartsWith("OnDemandTest"));
                        Assert.That(buildReference.BuildNumber, Is.GreaterThan(0));
                    },
                    onDone: _ => Assert.Fail("Expected triggered build")
                );
            });
        }
    }

    [Test]
    public void TriggerTests_WithDoneBuilds_ThrowsAlreadyDone()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);
        var buildNumberByJob = new[] { "OnDemandTest1", "OnDemandTest2" }.ToDictionary(job => job, job => RandomData.NextBuildNumber);
        var update = requestState.TriggerTests((job, refSpec) => Task.FromResult(buildNumberByJob[job.Value]))
            .DoneOnDemandTestBuild(_onDemandRoot, new BuildReference("OnDemandTest1", buildNumberByJob["OnDemandTest1"]))
            .DoneOnDemandTestBuild(_onDemandRoot, new BuildReference("OnDemandTest2", buildNumberByJob["OnDemandTest2"]));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => update.TriggerTests((job, refSpec) => throw new InvalidOperationException()),
                Throws.InvalidOperationException.With.Message.EqualTo("Already done"));

            update.ChainDiffs[0].TestBuildDiffs.ToList().ForEach(diff =>
            {
                diff.OnDemandBuild.Match(
                    onPending: _ => Assert.Fail("Expected done build"),
                    onTriggered: _ => Assert.Fail("Expected done build"),
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
    public void DoneOnDemandTestBuild_WithMatchingBuild_IsDone()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);
        var buildNumberByJob = new[] { "OnDemandTest1", "OnDemandTest2" }.ToDictionary(jobName => jobName, jobName => RandomData.NextBuildNumber);
        requestState = requestState.TriggerTests((job, refSpec) => Task.FromResult(buildNumberByJob[job.Value]));

        var testBuild = new BuildReference("OnDemandTest1", buildNumberByJob["OnDemandTest1"]);
        requestState = requestState.DoneOnDemandTestBuild(_onDemandRoot, testBuild);
        Assert.That(requestState.ChainDiffs[0].TestBuildDiffs.First().OnDemandBuild.IsDone, Is.True);

        testBuild = new BuildReference("OnDemandTest2", buildNumberByJob["OnDemandTest2"]);
        requestState = requestState.DoneOnDemandTestBuild(_onDemandRoot, testBuild);
        Assert.That(requestState.ChainDiffs[0].TestBuildDiffs.Last().OnDemandBuild.IsDone, Is.True);
    }

    [Test]
    public void DoneOnDemandTestBuild_WithOtherBuild_NoChange()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
        };
        var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);
        var buildNumber = RandomData.NextBuildNumber;
        requestState = requestState.TriggerTests((job, refSpec) => Task.FromResult(buildNumber));

        var testBuild = new BuildReference("OnDemandTest1", buildNumber);
        requestState = requestState.DoneOnDemandTestBuild(new BuildReference(_onDemandRoot.JobName, RandomData.NextBuildNumber), testBuild);
        requestState.ChainDiffs[0].TestBuildDiffs.First().OnDemandBuild.Match(
            onPending: _ => Assert.Fail("Expected triggered build"),
            onTriggered: buildRef =>
            {
                Assert.That(buildRef.JobName.Value, Is.EqualTo("OnDemandTest1"));
                Assert.That(buildRef.BuildNumber, Is.EqualTo(buildNumber));
            },
            onDone: _ => Assert.Fail("Expected triggered build")
        );
    }

    [Test]
    public void DoneOnDemandTestBuild_InvalidPendingRoot_DoesNothing()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
        };
        var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);
        var buildNumberByJob = new[] { "OnDemandTest1" }.ToDictionary(jobName => jobName, jobName => RandomData.NextBuildNumber);
        requestState = requestState.TriggerTests((job, refSpec) => Task.FromResult(buildNumberByJob[job.Value]));

        // Invalid state for code coverage
        var serializable = requestState.ToSerializable();
        serializable.ChainDiffs[0].OnDemandRoot = RequestBuildReference.Create(_onDemandRoot.JobName).ToSerializable();
        requestState = serializable.FromSerializable();

        var testBuild = new BuildReference("OnDemandTest1", buildNumberByJob["OnDemandTest1"]);
        requestState = requestState.DoneOnDemandTestBuild(_onDemandRoot, testBuild);
        requestState.ChainDiffs[0].TestBuildDiffs.Single().OnDemandBuild.Match(
            onPending: _ => Assert.Fail("Expected triggered build"),
            onTriggered: _ => { },
            onDone: _ => Assert.Fail("Expected triggered build")
        );
    }

    [Test]
    public void DoneOnDemandTestBuild_InvalidTriggeredRoot_DoesNothing()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
        };
        var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);
        var buildNumberByJob = new[] { "OnDemandTest1" }.ToDictionary(jobName => jobName, jobName => RandomData.NextBuildNumber);
        requestState = requestState.TriggerTests((job, refSpec) => Task.FromResult(buildNumberByJob[job.Value]));

        // Invalid state for code coverage
        var serializable = requestState.ToSerializable();
        serializable.ChainDiffs[0].OnDemandRoot = RequestBuildReference.Create(_onDemandRoot.JobName).Trigger(_onDemandRoot.BuildNumber).ToSerializable();
        requestState = serializable.FromSerializable();

        var testBuild = new BuildReference("OnDemandTest1", buildNumberByJob["OnDemandTest1"]);
        requestState = requestState.DoneOnDemandTestBuild(_onDemandRoot, testBuild);
        requestState.ChainDiffs[0].TestBuildDiffs.Single().OnDemandBuild.Match(
            onPending: _ => Assert.Fail("Expected triggered build"),
            onTriggered: _ => { },
            onDone: _ => Assert.Fail("Expected triggered build")
        );
    }

    [Test]
    public void DoneTestBuild_WhateverTheOrderReferenceOrOnDemand_RequestIsDoneWhenAll()
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
                var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);
                var update = requestState.TriggerTests((job, refSpec) => Task.FromResult(buildNumberByJob[job.Value]));
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
    public void Abort_WithPendingBuilds_SetsStatusToDone()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);
        var update = requestState.Abort();
        Assert.That(update.IsDone, Is.True);
    }

    [Test]
    public void Abort_WithTriggeredBuilds_SetsStatusToDone()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);
        var update = requestState.TriggerTests((job, refSpec) => Task.FromResult(RandomData.NextBuildNumber));
        update = update.Abort();
        Assert.That(update.IsDone, Is.True);
    }

    [Test]
    public void Abort_WithDoneBuilds_SetsStatusToDone()
    {
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);
        var buildNumberByJob = new[] { "OnDemandTest1", "OnDemandTest2" }.ToDictionary(jobName => jobName, jobName => RandomData.NextBuildNumber);
        var update = requestState.TriggerTests((job, refSpec) => Task.FromResult(buildNumberByJob[job.Value]))
            .DoneOnDemandTestBuild(_onDemandRoot, new BuildReference("OnDemandTest1", buildNumberByJob["OnDemandTest1"]))
            .DoneOnDemandTestBuild(_onDemandRoot, new BuildReference("OnDemandTest2", buildNumberByJob["OnDemandTest2"]));
        update = update.Abort();
        Assert.That(update.IsDone, Is.True);
    }

    [Test]
    public void TryGetChainReference_WithMatchingReferenceRoot_ReturnsTrue()
    {
        // Arrange
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);

        // Act
        var result = requestState.TryGetChainReference(_referenceRoot, out var chainDiff);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(chainDiff, Is.Not.Null);
        Assert.That(chainDiff!.ReferenceRoot, Is.EqualTo(_referenceRoot));
    }

    [Test]
    public void TryGetChainReference_WithNonMatchingReferenceRoot_ReturnsFalse()
    {
        // Arrange
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);
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
        var referenceRoot1 = new BuildReference("MainBuild1", RandomData.NextBuildNumber);
        var referenceRoot2 = new BuildReference("MainBuild2", RandomData.NextBuildNumber);
        var onDemandRoot1 = new BuildReference("OnDemandBuild1", RandomData.NextBuildNumber);
        var onDemandRoot2 = new BuildReference("OnDemandBuild2", RandomData.NextBuildNumber);
        
        var diffs1 = new List<RequestBuildDiff> { new(new("MainTest1"), new("OnDemandTest1")) };
        var diffs2 = new List<RequestBuildDiff> { new(new("MainTest2"), new("OnDemandTest2")) };
        
        var chains = new RequestChain[]
        {
            new(referenceRoot1, RequestBuildReference.Create(new("OnDemandBuild1")), [.. diffs1]),
            new(referenceRoot2, RequestBuildReference.Create(new("OnDemandBuild2")), [.. diffs2]),
        };
        var onDemandBuilds = new OnDemandBuilds(new BuildCollections<RootBuild>(), new());
        Func<JobName, Sha1, Task<int>> triggerBuild = (job, sha1) => job.Value switch
        {
            "OnDemandBuild1" => Task.FromResult(onDemandRoot1.BuildNumber),
            "OnDemandBuild2" => Task.FromResult(onDemandRoot2.BuildNumber),
            _ => Task.FromResult(RandomData.NextBuildNumber),
        };
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
    public void TryGetChainOnDemand_WithMatchingTriggeredOnDemandRoot_ReturnsTrue()
    {
        // Arrange
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);

        // Act
        var result = requestState.TryGetChainOnDemand(_onDemandRoot, out var chainDiff);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(chainDiff, Is.Not.Null);
        chainDiff!.OnDemandRoot.Match(
            onPending: _ => Assert.Fail("Expected triggered on-demand root"),
            onTriggered: buildRef => Assert.That(buildRef, Is.EqualTo(_onDemandRoot)),
            onDone: _ => Assert.Fail("Expected triggered on-demand root"));
    }

    [Test]
    public void TryGetChainOnDemand_WithInvalidPendingRoot_ReturnsFalse()
    {
        // Arrange
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);

        var serializable = requestState.ToSerializable();
        serializable.ChainDiffs[0].OnDemandRoot = RequestBuildReference.Create(_onDemandRoot.JobName).ToSerializable();
        requestState = serializable.FromSerializable();

        // Act
        var result = requestState.TryGetChainOnDemand(_onDemandRoot, out var chainDiff);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(chainDiff, Is.Null);
    }

    [Test]
    public void TryGetChainOnDemand_WithMatchingDoneOnDemandRoot_ReturnsTrue()
    {
        // Arrange
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs)
            .TriggerTests((job, refSpec) => Task.FromResult(RandomData.NextBuildNumber));

        // Act
        var result = requestState.TryGetChainOnDemand(_onDemandRoot, out var chainDiff);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(chainDiff, Is.Not.Null);
        chainDiff!.OnDemandRoot.Match(
            onPending: _ => Assert.Fail("Expected done on-demand root"),
            onTriggered: _ => Assert.Fail("Expected done on-demand root"),
            onDone: buildRef => Assert.That(buildRef, Is.EqualTo(_onDemandRoot)));
    }

    [Test]
    public void TryGetChainOnDemand_WithNonMatchingOnDemandRoot_ReturnsFalse()
    {
        // Arrange
        var diffs = new List<RequestBuildDiff>
        {
            new(new("MainTest1"), new("OnDemandTest1")),
            new(new("MainTest2"), new("OnDemandTest2")),
        };
        var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);
        var otherOnDemandRoot = new BuildReference("OtherOnDemandBuild", RandomData.NextBuildNumber);

        // Act
        var result = requestState.TryGetChainOnDemand(otherOnDemandRoot, out var chainDiff);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(chainDiff, Is.Null);
    }

    [Test]
    public async Task TryGetChainOnDemand_WithPendingOnDemandRoot_ReturnsFalse()
    {
        // Arrange
        var referenceRoot = new BuildReference("MainBuild1", RandomData.NextBuildNumber);
        var pendingOnDemandRoot = RequestBuildReference.Create(new JobName("PendingBuild"));

        var chains = new RequestChain[]
        {
            new(referenceRoot, RequestBuildReference.Create(new("PendingBuild")), [new RequestBuildDiff(new("MainTest1"), new("OnDemandTest1"))]),
        };
        var onDemandBuilds = new OnDemandBuilds(new BuildCollections<RootBuild>(), new());
        Func<JobName, Sha1, Task<int>> triggerBuild = (job, sha1) => Task.FromResult(RandomData.NextBuildNumber);
        var requestState = await RequestState.New(_request, chains, onDemandBuilds, triggerBuild).ConfigureAwait(false);

        var searchRoot = new BuildReference("PendingBuild", 123);

        // Act
        var result = requestState.TryGetChainOnDemand(searchRoot, out var foundChain);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(foundChain, Is.Null);
    }

    [Test]
    public async Task TryGetChainOnDemand_WithMultipleChains_ReturnsCorrectChain()
    {
        // Arrange
        var referenceRoot1 = new BuildReference("MainBuild1", RandomData.NextBuildNumber);
        var referenceRoot2 = new BuildReference("MainBuild2", RandomData.NextBuildNumber);
        var onDemandRoot1 = new BuildReference("OnDemandBuild1", RandomData.NextBuildNumber);
        var onDemandRoot2 = new BuildReference("OnDemandBuild2", RandomData.NextBuildNumber);
        
        var diffs1 = new List<RequestBuildDiff> { new(new("MainTest1"), new("OnDemandTest1")) };
        var diffs2 = new List<RequestBuildDiff> { new(new("MainTest2"), new("OnDemandTest2")) };
        
        var chains = new RequestChain[]
        {
            new(referenceRoot1, RequestBuildReference.Create(new("OnDemandBuild1")), [.. diffs1]),
            new(referenceRoot2, RequestBuildReference.Create(new("OnDemandBuild2")), [.. diffs2]),
        };
        var onDemandBuilds = new OnDemandBuilds(new BuildCollections<RootBuild>(), new());
        Func<JobName, Sha1, Task<int>> triggerBuild = (job, sha1) => job.Value switch
        {
            "OnDemandBuild1" => Task.FromResult(onDemandRoot1.BuildNumber),
            "OnDemandBuild2" => Task.FromResult(onDemandRoot2.BuildNumber),
            _ => Task.FromResult(RandomData.NextBuildNumber),
        };
        var requestState = await RequestState.New(_request, chains, onDemandBuilds, triggerBuild).ConfigureAwait(false);

        // Act
        var result1 = requestState.TryGetChainOnDemand(onDemandRoot1, out var foundChain1);
        var result2 = requestState.TryGetChainOnDemand(onDemandRoot2, out var foundChain2);

        // Assert
        Assert.That(result1, Is.True);
        Assert.That(foundChain1, Is.Not.Null);
        foundChain1!.OnDemandRoot.Match(
            onPending: _ => Assert.Fail("Expected triggered on-demand root"),
            onTriggered: buildRef => Assert.That(buildRef, Is.EqualTo(onDemandRoot1)),
            onDone: _ => Assert.Fail("Expected triggered on-demand root"));
        
        Assert.That(result2, Is.True);
        Assert.That(foundChain2, Is.Not.Null);
        foundChain2!.OnDemandRoot.Match(
            onPending: _ => Assert.Fail("Expected triggered on-demand root"),
            onTriggered: buildRef => Assert.That(buildRef, Is.EqualTo(onDemandRoot2)),
            onDone: _ => Assert.Fail("Expected triggered on-demand root"));
    }

    [Test]
    public void SerializationRoundTrip_Works()
    {
        using (Assert.EnterMultipleScope())
        {
            var diffs = new List<RequestBuildDiff>
            {
                new(new("MainTest1"), new("OnDemandTest1")),
                new(new("MainTest2"), new("OnDemandTest2")),
            };
            var requestState = RequestState.New(_request, _referenceRoot, _onDemandRoot, diffs);
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
