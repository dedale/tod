using Serilog;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Tod.Git;

namespace Tod.Jenkins;

internal sealed class RequestState : IWithCustomSerialization<RequestState.Serializable>
{
    private RequestState(Request request, ChainDiff[] chainDiffs)
    {
        Request = request;
        ChainDiffs = chainDiffs;
    }

    public Request Request { get; }
    public ChainDiff[] ChainDiffs { get; }

    public bool IsDone => ChainDiffs.All(c => c.Status == ChainStatus.Done);

    public void LogChainStatus(JobName onDemandRootJob)
    {
        foreach (var chain in ChainDiffs.Where(chain => chain.OnDemandRoot.JobName == onDemandRootJob))
        {
            var pending = chain.TestBuildDiffs.Where(diff => !diff.IsDone).Count();
            Log.Information($"   {{@JobName}} Chain Status: {{Status}} ({{Count}} build{(pending > 1 ? "s" : "")} pending)", chain.OnDemandRoot.JobName, chain.Status, pending);
        }
    }

    public void LogChainStatuses()
    {
        foreach (var chain in ChainDiffs)
        {
            Log.Information("   {@JobName} Chain Status: {Status}", chain.OnDemandRoot.JobName, chain.Status);
        }
    }

    public bool TryGetBaselineChain(BuildReference baselineRoot, [NotNullWhen(true)] out ChainDiff? chainDiff)
    {
        chainDiff = ChainDiffs.FirstOrDefault(c => c.BaselineRoot.Equals(baselineRoot));
        return chainDiff != null;
    }

    public bool TryGetOnDemandChain(JobName onDemandRootJob, Sha1 commit, [NotNullWhen(true)] out ChainDiff? chainDiff)
    {
        chainDiff = ChainDiffs.FirstOrDefault(c => c.OnDemandRoot.Match(
            onQueued: (j, c) => j.Equals(onDemandRootJob) && c.Equals(commit),
            onDone: _ => false
        ));
        return chainDiff != null;
    }

    public static async Task<RequestState> New(
        Request request,
        RequestChain[] requestChains,
        OnDemandBuilds onDemandBuilds,
        Func<OnDemandJobKind, JobName, TriggerParameters, Task> triggerBuild
    )
    {
        var chainDiffs = new List<ChainDiff>();
        foreach (var requestChain in requestChains)
        {
            ChainStatus status;
            RequestRootBuildReference onDemandRoot;
            var rootJobName = requestChain.OnDemandRoot.JobName;
            var buildDiffs = requestChain.TestBuildDiffs.ToList();
            var onDemandRootBuilds = onDemandBuilds.RootBuilds[rootJobName];
            var onDemandRootBuild = onDemandRootBuilds.FirstOrDefault(r => r.IsSuccessful && r.Commits.Contains(request.Commit));
            if (onDemandRootBuild == null)
            {
                var parameters = new TriggerParameters(request.Commit, null);
                await triggerBuild(OnDemandJobKind.Root, rootJobName, parameters).ConfigureAwait(false);
                onDemandRoot = requestChain.OnDemandRoot;
                status = ChainStatus.RootTriggered;
                Log.Information("Triggered on-demand root build {@OnDemandRootJob}", rootJobName);
            }
            else
            {
                Log.Information("Reusing on-demand root build {@OnDemandRootBuild}", onDemandRootBuild.Reference);
                int rootBuildNumber = onDemandRootBuild.BuildNumber;
                var parameters = new TriggerParameters(request.Commit, rootBuildNumber);
                onDemandRoot = requestChain.OnDemandRoot.DoneQueued(rootBuildNumber);
                var testBuildsByJobName = onDemandBuilds.TestBuilds.ToDictionary(x => x.JobName);
                for (var i = 0; i < buildDiffs.Count; i++)
                {
                    var diff = buildDiffs[i];
                    var testBuilds = testBuildsByJobName[diff.OnDemandBuild.JobName];
                    var testBuild = testBuilds.FirstOrDefault(b => b.RootBuilds.Contains(onDemandRootBuild.Reference));
                    if (testBuild != null)
                    {
                        Log.Information("Reusing on-demand test build {@TestBuild}", testBuild.Reference);
                        buildDiffs[i] = buildDiffs[i].RecycleOnDemand(testBuild.BuildNumber);
                    }
                    else
                    {
                        await triggerBuild(OnDemandJobKind.Test, diff.OnDemandBuild.JobName, parameters).ConfigureAwait(false);
                        buildDiffs[i] = buildDiffs[i].QueueOnDemand();
                    }
                }
                status = buildDiffs.All(d => d.IsDone) ? ChainStatus.Done : ChainStatus.TestsTriggered;
            }
            chainDiffs.Add(new ChainDiff(status, requestChain.BaselineRoot, onDemandRoot, buildDiffs));
        }
        return new RequestState(request, [.. chainDiffs]);
    }

    public RequestState DoneBaselineTestBuild(BuildReference rootBuild, BuildReference testBuild)
    {
        var newChains = new List<ChainDiff>();
        foreach (var chainDiff in ChainDiffs)
        {
            if (chainDiff.BaselineRoot.Equals(rootBuild))
            {
                newChains.Add(chainDiff.DoneBaselineTestBuild(testBuild));
            }
            else
            {
                newChains.Add(chainDiff);
            }
        }
        return new RequestState(Request, [.. newChains]);
    }

    public async Task<RequestState> TriggerTests(BuildReference rootReference, Func<JobName, Task> triggerBuild)
    {
        var newChains = ChainDiffs.Select(async chainDiff => await chainDiff.TriggerTests(rootReference, triggerBuild).ConfigureAwait(false)).ToArray();
        await Task.WhenAll(newChains).ConfigureAwait(false);
        return new RequestState(Request, [.. newChains.Select(t => t.Result)]);
    }

    public RequestState DoneOnDemandTestBuild(BuildReference rootBuild, BuildReference testBuild)
    {
        var newChains = new List<ChainDiff>();
        foreach (var chainDiff in ChainDiffs)
        {
            chainDiff.OnDemandRoot.Match(
                onQueued: (_, _) => newChains.Add(chainDiff),
                onDone: buildRef => newChains.Add(buildRef.Equals(rootBuild) ? chainDiff.DoneOnDemandTestBuild(testBuild) : chainDiff)
            );
        }
        return new RequestState(Request, [.. newChains]);
    }

    public RequestState AbortChain(JobName rootJob)
    {
        var newChains = ChainDiffs.Select(chainDiff => chainDiff.OnDemandRoot.JobName.Equals(rootJob) ? chainDiff.Abort() : chainDiff);
        return new RequestState(Request, [.. newChains]);
    }

    public RequestState AbortAll()
    {
        return new RequestState(Request, [.. ChainDiffs.Select(chainDiff => chainDiff.Abort())]);
    }

    internal sealed class Serializable : ICustomSerializable<RequestState>
    {
        [JsonConstructor]
        private Serializable(Request request, List<ChainDiff.Serializable> chainDiffs)
        {
            Request = request;
            ChainDiffs = chainDiffs;
        }

        public Serializable(RequestState state)
            : this(state.Request, [.. state.ChainDiffs.Select(d => new ChainDiff.Serializable(d))])
        {
        }

        public Request Request { get; set; }
        public List<ChainDiff.Serializable> ChainDiffs { get; set; }

        public RequestState FromSerializable()
        {
            var chainDiffs = ChainDiffs.Select(d => d.FromSerializable());
            return new RequestState(Request, [.. chainDiffs]);
        }
    }

    public Serializable ToSerializable()
    {
        return new Serializable(this);
    }
}
