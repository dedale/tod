using Serilog;
using System.Text.Json.Serialization;
using Tod.Git;

namespace Tod.Jenkins;

internal enum ChainStatus
{
    RootTriggered,
    TestsTriggered,
    Done
}

internal sealed class RequestChain(BuildReference referenceRoot, RequestRootBuildReference ondemandRoot, TimeSpan rootDuration, RequestBuildDiff[] testBuildDiffs)
{
    public RequestChain(BuildReference referenceRoot, RequestRootBuildReference ondemandRoot, RequestBuildDiff[] testBuildDiffs)
        : this(referenceRoot, ondemandRoot, TimeSpan.Zero, testBuildDiffs)
    {
    }

    public BuildReference ReferenceRoot { get; } = referenceRoot;
    public RequestRootBuildReference OnDemandRoot { get; } = ondemandRoot;
    public TimeSpan RootDuration { get; } = rootDuration;
    public IEnumerable<RequestBuildDiff> TestBuildDiffs { get; } = testBuildDiffs;
}

internal static class RequestChainExtensions
{
    public static TimeSpan TotalDuration(this RequestChain[] chains)
    {
        var totalDuration = TimeSpan.Zero;
        foreach (var chain in chains)
        {
            totalDuration += chain.RootDuration;
            foreach (var testBuildDiff in chain.TestBuildDiffs)
            {
                totalDuration += testBuildDiff.TestDuration;
            }
        }
        return totalDuration;
    }
}

internal sealed class RequestChainBuilder(Workspace workspace, IFilterManager filterManager)
{
    public RequestChain[] Get(Sha1 commit, GitReference gitReference, JobDiff[] rootDiffs, string[] testFilters)
    {
        var branchReference = workspace.BranchReferences.FirstOrDefault(r => r.BranchName == gitReference.Branch);
        if (branchReference == null)
        {
            Log.Error("Cannot use branch {Branch} for reference - branch not found", gitReference.Branch);
            throw new InvalidOperationException($"Cannot use '{gitReference.Branch}' branch for reference");
        }

        var roots = new List<(string rootChain, BuildReference rootBuild, JobName onDemandJob)>();
        foreach (var rootDiff in rootDiffs)
        {
            if (branchReference.TryFindRootBuildByCommit(gitReference.Commit, rootDiff.ReferenceJob, out var rootBuild))
            {
                roots.Add((rootDiff.Chain, rootBuild.Reference, rootDiff.OnDemandJob));
                Log.Debug("Found reference root build {RootBuild} for parent commit {Commit}", rootBuild, gitReference.Commit);
            }
            else
            {
                Log.Error("Unknown parent commit {Commit} in branch {Branch} for job {JobName}",
                    gitReference.Commit, gitReference.Branch, rootDiff.ReferenceJob);
                throw new InvalidOperationException($"Unknown parent commit '{gitReference.Commit}' for job '{rootDiff.ReferenceJob}'");
            }
        }

        var requestChains = new List<RequestChain>();
        foreach (var (rootChain, refRootBuild, onDemandJob) in roots)
        {
            var testJobDiffs = filterManager.GetTestBuildDiffs(rootChain, testFilters, gitReference.Branch);
            var testBuildDiffs = new List<RequestBuildDiff>(testJobDiffs.Length);
            for (var i = 0; i < testJobDiffs.Length; i++)
            {
                var testDuration = branchReference.TestBuilds[testJobDiffs[i].ReferenceJob].AverageDuration;
                var buildDiff = new RequestBuildDiff(testJobDiffs[i].ReferenceJob, testJobDiffs[i].OnDemandJob, testDuration);
                if (branchReference.TryFindTestBuild(testJobDiffs[i].ReferenceJob, refRootBuild, out var refTestBuild))
                {
                    Log.Debug("Reusing reference test build {TestBuild}", refTestBuild);
                    buildDiff = buildDiff.DoneReference(refTestBuild.BuildNumber);
                }
                testBuildDiffs.Add(buildDiff);
            }
            var rootDuration = branchReference.RootBuilds[refRootBuild.JobName].AverageDuration;
            requestChains.Add(new RequestChain(refRootBuild, RequestRootBuildReference.Queue(onDemandJob, commit), rootDuration, [.. testBuildDiffs]));
        }
        return [.. requestChains];
    }
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
