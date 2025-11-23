using System.Text.Json.Serialization;

namespace Tod.Jenkins;

internal enum ChainStatus
{
    RootTriggered,
    TestsTriggered,
    Done
}

internal sealed class RequestChain(BuildReference referenceRoot, RequestRootBuildReference ondemandRoot, RequestBuildDiff[] testBuildDiffs)
{
    public BuildReference ReferenceRoot { get; } = referenceRoot;
    public RequestRootBuildReference OnDemandRoot { get; } = ondemandRoot;
    public IEnumerable<RequestBuildDiff> TestBuildDiffs { get; } = testBuildDiffs;
}

internal sealed class ChainDiff(ChainStatus status, BuildReference referenceRoot, RequestRootBuildReference onDemandRoot, List<RequestBuildDiff> testBuildDiffs) : IWithCustomSerialization<ChainDiff.Serializable>
{
    public ChainStatus Status { get; } = status;
    public BuildReference ReferenceRoot { get; } = referenceRoot;
    public RequestRootBuildReference OnDemandRoot { get; } = onDemandRoot;
    public IEnumerable<RequestBuildDiff> TestBuildDiffs { get; } = testBuildDiffs;

    public ChainDiff DoneReferenceTestBuild(BuildReference referenceBuild)
    {
        var newTestDiffs = new List<RequestBuildDiff>();
        foreach (var buildDiff in testBuildDiffs)
        {
            if (buildDiff.ReferenceBuild.TryGetPendingReference(out var jobName) && jobName.Equals(referenceBuild.JobName))
            {
                newTestDiffs.Add(buildDiff.DoneReference(referenceBuild.BuildNumber));
            }
            else
            {
                newTestDiffs.Add(buildDiff);
            }
        }
        var newStatus = newTestDiffs.All(d => d.IsDone) ? ChainStatus.Done : Status;
        return new ChainDiff(newStatus, ReferenceRoot, OnDemandRoot, newTestDiffs);
    }

    public Task<ChainDiff> TriggerTests(BuildReference rootReference, Func<JobName, Task> triggerBuild)
    {
        return OnDemandRoot.Match(
            onQueued: async (job, _) =>
            {
                if (rootReference.JobName.Equals(job))
                {
                    var newOnDemandRoot = OnDemandRoot.DoneQueued(rootReference.BuildNumber);
                    var newTestDiffs = new List<RequestBuildDiff>();
                    foreach (var buildDiff in testBuildDiffs)
                    {
                        await buildDiff.OnDemandBuild.Match(
                            onPending: async jobName =>
                            {
                                await triggerBuild(jobName).ConfigureAwait(false);
                                newTestDiffs.Add(buildDiff.QueueOnDemand());
                            },
                            onQueued: _ =>
                            {
                                newTestDiffs.Add(buildDiff);
                                return Task.CompletedTask;
                            },
                            onDone: _ => {
                                newTestDiffs.Add(buildDiff);
                                return Task.CompletedTask;
                            }
                        ).ConfigureAwait(false);
                    }
                    return new ChainDiff(ChainStatus.TestsTriggered, ReferenceRoot, newOnDemandRoot, newTestDiffs);
                }
                return this;
            },
            onDone: reference =>
            {
                if (rootReference.JobName.Equals(reference.JobName))
                {
                    throw new InvalidOperationException("Already done");
                }
                return Task.FromResult(this);
            }
        );
    }

    public ChainDiff DoneOnDemandTestBuild(BuildReference onDemandBuild)
    {
        var newTestDiffs = new List<RequestBuildDiff>();
        foreach (var buildDiff in testBuildDiffs)
        {
            if (buildDiff.OnDemandBuild.TryGetQueued(out var jobName) && jobName.Equals(onDemandBuild.JobName))
            {
                newTestDiffs.Add(buildDiff.DoneOnDemand(onDemandBuild.BuildNumber));
            }
            else
            {
                newTestDiffs.Add(buildDiff);
            }
        }
        var newStatus = newTestDiffs.All(d => d.IsDone) ? ChainStatus.Done : Status;
        return new ChainDiff(newStatus, ReferenceRoot, OnDemandRoot, newTestDiffs);
    }

    public ChainDiff Abort()
    {
        return new ChainDiff(ChainStatus.Done, ReferenceRoot, OnDemandRoot, testBuildDiffs);
    }

    internal sealed class Serializable : ICustomSerializable<ChainDiff>
    {
        [JsonConstructor]
        private Serializable(ChainStatus status, BuildReference referenceRoot, RequestRootBuildReference.Serializable onDemandRoot, List<RequestBuildDiff.Serializable> testBuildDiffs)
        {
            Status = status;
            ReferenceRoot = referenceRoot;
            OnDemandRoot = onDemandRoot;
            TestBuildDiffs = testBuildDiffs;
        }
        public Serializable(ChainDiff chainDiff)
            : this(
                chainDiff.Status,
                chainDiff.ReferenceRoot,
                new RequestRootBuildReference.Serializable(chainDiff.OnDemandRoot),
                [.. chainDiff.TestBuildDiffs.Select(d => new RequestBuildDiff.Serializable(d))]
            )
        {
        }
        public ChainStatus Status { get; set; }
        public BuildReference ReferenceRoot { get; set; }
        public RequestRootBuildReference.Serializable OnDemandRoot { get; set; }
        public List<RequestBuildDiff.Serializable> TestBuildDiffs { get; set; }

        public ChainDiff FromSerializable()
        {
            var onDemandRoot = OnDemandRoot.FromSerializable();
            var testDiffs = TestBuildDiffs.Select(d => d.FromSerializable()).ToList();
            return new ChainDiff(Status, ReferenceRoot, onDemandRoot, testDiffs);
        }
    }

    public Serializable ToSerializable()
    {
        return new Serializable(this);
    }
}
