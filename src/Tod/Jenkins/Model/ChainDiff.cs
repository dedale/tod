using System.Text.Json.Serialization;
using Tod.Git;

namespace Tod.Jenkins;

internal enum ChainStatus
{
    RootTriggered,
    TestsTriggered,
    Done
}

internal sealed class RequestChain(BuildReference referenceRoot, RequestBuildReference ondemandRoot, RequestBuildDiff[] testBuildDiffs)
{
    public BuildReference ReferenceRoot { get; } = referenceRoot;
    public RequestBuildReference OnDemandRoot { get; } = ondemandRoot;
    public IEnumerable<RequestBuildDiff> TestBuildDiffs { get; } = testBuildDiffs;
}

internal sealed class ChainDiff(ChainStatus status, BuildReference referenceRoot, RequestBuildReference onDemandRoot, List<RequestBuildDiff> testBuildDiffs) : IWithCustomSerialization<ChainDiff.Serializable>
{
    public ChainStatus Status { get; } = status;
    public BuildReference ReferenceRoot { get; } = referenceRoot;
    public RequestBuildReference OnDemandRoot { get; } = onDemandRoot;
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

    public ChainDiff TriggerTests(Sha1 commit, Func<JobName, Sha1, Task<int>> triggerBuild)
    {
        var newOnDemandRoot = OnDemandRoot.DoneTriggered();
        var newTestDiffs = new List<RequestBuildDiff>();
        foreach (var buildDiff in testBuildDiffs)
        {
            buildDiff.OnDemandBuild.Match(
                onPending: async jobName =>
                {
                    var buildNumber = await triggerBuild(jobName, commit).ConfigureAwait(false);
                    newTestDiffs.Add(buildDiff.TriggerOnDemand(buildNumber));
                },
                onTriggered: _ => newTestDiffs.Add(buildDiff),
                onDone: _ => newTestDiffs.Add(buildDiff)
            );
        }
        return new ChainDiff(ChainStatus.TestsTriggered, ReferenceRoot, newOnDemandRoot, newTestDiffs);
    }

    public ChainDiff DoneOnDemandTestBuild(BuildReference onDemandBuild)
    {
        var newTestDiffs = new List<RequestBuildDiff>();
        foreach (var buildDiff in testBuildDiffs)
        {
            if (buildDiff.OnDemandBuild.TryGetTriggered(out var triggeredBuild) && triggeredBuild.Equals(onDemandBuild))
            {
                newTestDiffs.Add(buildDiff.DoneOnDemand());
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
        private Serializable(ChainStatus status, BuildReference referenceRoot, RequestBuildReference.Serializable onDemandRoot, List<RequestBuildDiff.Serializable> testBuildDiffs)
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
                new RequestBuildReference.Serializable(chainDiff.OnDemandRoot),
                [.. chainDiff.TestBuildDiffs.Select(d => new RequestBuildDiff.Serializable(d))]
            )
        {
        }
        public ChainStatus Status { get; set; }
        public BuildReference ReferenceRoot { get; set; }
        public RequestBuildReference.Serializable OnDemandRoot { get; set; }
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
