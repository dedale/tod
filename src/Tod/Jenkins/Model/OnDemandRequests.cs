using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Tod.Core;
using Tod.Git;

namespace Tod.Jenkins;

internal sealed class CachedRequest(string path)
{
    private readonly Cached<RequestState, RequestState.Serializable> _cached = new(path);

    public static CachedRequest New(RequestState requestState, string path)
    {
        Cached<RequestState, RequestState.Serializable>.New(requestState, path);
        return new CachedRequest(path);
    }

    public RequestState Value => _cached.Value;

    public ILockedJson<RequestState> Lock(string reason)
    {
        return _cached.Lock(reason);
    }
}

// TODO Purge old requests

internal sealed class OnDemandRequests
{
    private readonly string _requestPath;
    private readonly Dictionary<Guid, CachedRequest> _requestById;
    private readonly BuildReferenceComparer _buildReferenceComparer;

    public string RequestPath => _requestPath;

    [JsonConstructor]
    public OnDemandRequests(string requestPath)
        : this(requestPath, null)
    {
    }

    public OnDemandRequests(string requestPath, BuildReferenceComparer? buildReferenceComparer)
    {
        _requestPath = requestPath;
        Directory.CreateDirectory(_requestPath);
        _requestById = Directory.GetFiles(_requestPath).Select(f => new CachedRequest(f)).ToDictionary(r => r.Value.Request.Id);
        _buildReferenceComparer = buildReferenceComparer ?? BuildReferenceComparer.Default;
    }

    [JsonIgnore]
    public List<CachedRequest> ActiveRequests => [.. _requestById.Values.Where(r => r.Value.ChainDiffs.Any(cd => cd.Status != ChainStatus.Done))];

    [JsonIgnore]
    public List<CachedRequest> AllRequests => [.. _requestById.Values];

    public CachedRequest Add(RequestState requestState)
    {
        var cached = CachedRequest.New(requestState, Path.Combine(_requestPath, $"{requestState.Request.Id}.json"));
        _requestById.Add(requestState.Request.Id, cached);
        return cached;
    }

    public LockedJsons<RequestState> GetPendingReferenceTest(BuildReference rootBuild, JobName testJob)
    {
        var requests = new LockedJsons<RequestState>();
        foreach (var cached in _requestById.Values)
        {
            var request = cached.Value;
            if (!request.TryGetChainReference(rootBuild, _buildReferenceComparer, out var chainDiff))
            {
                continue;
            }
            foreach (var buildDiff in chainDiff.TestBuildDiffs)
            {
                if (buildDiff.TryGetPendingReference(out var jobName) && jobName.Equals(testJob))
                {
                    requests.Add(cached.Lock(nameof(GetPendingReferenceTest)));
                    break;
                }
            }
        }
        return requests;
    }

    public bool TryGetRootQueued(JobName onDemandRootJob, Sha1 commit, [NotNullWhen(true)] out ILockedJson<RequestState>? lockedRequest)
    {
        foreach (var cached in _requestById.Values)
        {
            var request = cached.Value;
            if (request.TryGetChainOnDemand(onDemandRootJob, commit, out var chainDiff) && chainDiff.Status == ChainStatus.RootTriggered)
            {
                lockedRequest = cached.Lock(nameof(TryGetRootQueued));
                return true;
            }
        }
        lockedRequest = null;
        return false;
    }

    public bool TryGetTestQueued(BuildReference rootBuild, JobName testJob, [NotNullWhen(true)] out ILockedJson<RequestState>? lockedRequest)
    {
        foreach (var cached in _requestById.Values)
        {
            var request = cached.Value;
            foreach (var chainDiff in request.ChainDiffs)
            {
                var sameRoot = chainDiff.OnDemandRoot.Match(
                    onQueued: (_, _) => false,
                    onDone: buildRef => _buildReferenceComparer.Equals(buildRef, rootBuild)
                );
                if (!sameRoot)
                {
                    continue;
                }
                foreach (var buildDiff in chainDiff.TestBuildDiffs)
                {
                    if (buildDiff.OnDemandBuild.TryGetQueued(out var triggeredBuild) && triggeredBuild.Equals(testJob))
                    {
                        lockedRequest = cached.Lock(nameof(TryGetTestQueued));
                        return true;
                    }
                }
            }
        }
        lockedRequest = null;
        return false;
    }
}
