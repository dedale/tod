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

    public void LogChainStatuses()
    {
        foreach (var chain in ChainDiffs)
        {
            Log.Information("   {JobName} Chain Status: {Status}", chain.OnDemandRoot.JobName, chain.Status);
        }
    }

    public bool TryGetChainReference(BuildReference referenceRoot, [NotNullWhen(true)] out ChainDiff? chainDiff)
    {
        chainDiff = ChainDiffs.FirstOrDefault(c => c.ReferenceRoot.Equals(referenceRoot));
        return chainDiff != null;
    }

    public bool TryGetChainOnDemand(BuildReference onDemandRoot, [NotNullWhen(true)] out ChainDiff? chainDiff)
    {
        chainDiff = ChainDiffs.FirstOrDefault(c => c.OnDemandRoot.Match(
            onPending: _ => false,
            onTriggered: buildRef => buildRef.Equals(onDemandRoot),
            onDone: buildRef => buildRef.Equals(onDemandRoot)
        ));
        return chainDiff != null;
    }

    public static RequestState New(Request request, BuildReference referenceRoot, BuildReference onDemandRoot, List<RequestBuildDiff> buildDiffs)
    {
        // fix status when reusing ondemand builds
        var chainDiff = new ChainDiff(ChainStatus.RootTriggered, referenceRoot, RequestBuildReference.Create(onDemandRoot.JobName).Trigger(onDemandRoot.BuildNumber), buildDiffs);
        return new RequestState(request, [chainDiff]);
    }

    public static async Task<RequestState> New(Request request, RequestChain[] requestChains, OnDemandBuilds onDemandBuilds, Func<JobName, Sha1, Task<int>> triggerBuild)
    {
        var chainDiffs = new List<ChainDiff>();
        foreach (var requestChain in requestChains)
        {
            ChainStatus status;
            RequestBuildReference onDemandRoot;
            var rootJobName = requestChain.OnDemandRoot.JobName;
            var buildDiffs = requestChain.TestBuildDiffs.ToList();
            var onDemandRootBuilds = onDemandBuilds.RootBuilds[rootJobName];
            var onDemandRootBuild = onDemandRootBuilds.FirstOrDefault(r => r.IsSuccessful && r.Commits.Contains(request.Commit));
            if (onDemandRootBuild == null)
            {
                int rootBuildNumber = await triggerBuild(rootJobName, request.Commit).ConfigureAwait(false);
                onDemandRoot = requestChain.OnDemandRoot.Trigger(rootBuildNumber);
                status = ChainStatus.RootTriggered;
            }
            else
            {
                int rootBuildNumber = onDemandRootBuild.BuildNumber;
                onDemandRoot = requestChain.OnDemandRoot.Trigger(rootBuildNumber).DoneTriggered();
                var testBuildsByJobName = onDemandBuilds.TestBuilds.ToDictionary(x => x.JobName);
                for (var i = 0; i < buildDiffs.Count; i++)
                {
                    var diff = buildDiffs[i];
                    var testBuilds = testBuildsByJobName[diff.OnDemandBuild.JobName];
                    var testBuild = testBuilds.FirstOrDefault(b => b.RootBuild == onDemandRootBuild.Reference);
                    if (testBuild != null)
                    {
                        Log.Information("Reusing on-demand build {TestBuild}", testBuild);
                        buildDiffs[i] = buildDiffs[i].TriggerOnDemand(testBuild.BuildNumber).DoneOnDemand();
                    }
                    else
                    {
                        var testBuildNumber = await triggerBuild(diff.OnDemandBuild.JobName, request.Commit).ConfigureAwait(false);
                        buildDiffs[i] = buildDiffs[i].TriggerOnDemand(testBuildNumber);
                    }
                }
                status = buildDiffs.All(d => d.IsDone) ? ChainStatus.Done : ChainStatus.TestsTriggered;
            }
            chainDiffs.Add(new ChainDiff(status, requestChain.ReferenceRoot, onDemandRoot, buildDiffs));
        }
        return new RequestState(request, [.. chainDiffs]);
    }

    public RequestState DoneReferenceTestBuild(BuildReference rootBuild, BuildReference testBuild)
    {
        var newChains = new List<ChainDiff>();
        foreach (var chainDiff in ChainDiffs)
        {
            if (chainDiff.ReferenceRoot.Equals(rootBuild))
            {
                newChains.Add(chainDiff.DoneReferenceTestBuild(testBuild));
            }
            else
            {
                newChains.Add(chainDiff);
            }
        }
        return new RequestState(Request, [.. newChains]);
    }

    public RequestState TriggerTests(Func<JobName, Sha1, Task<int>> triggerBuild)
    {
        var newChains = ChainDiffs.Select(chainDiff => chainDiff.TriggerTests(Request.Commit, triggerBuild));
        return new RequestState(Request, [.. newChains]);
    }

    public RequestState DoneOnDemandTestBuild(BuildReference rootBuild, BuildReference testBuild)
    {
        var newChains = new List<ChainDiff>();
        foreach (var chainDiff in ChainDiffs)
        {
            chainDiff.OnDemandRoot.Match(
                onPending: _ => newChains.Add(chainDiff),
                onTriggered: _ => newChains.Add(chainDiff),
                onDone: buildRef => newChains.Add(buildRef.Equals(rootBuild) ? chainDiff.DoneOnDemandTestBuild(testBuild) : chainDiff)
            );
        }
        return new RequestState(Request, [.. newChains]);
    }

    public RequestState Abort()
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
