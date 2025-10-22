using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Tod.Core;

namespace Tod.Jenkins;

internal sealed class CachedRequest
{
    private readonly Cached<RequestState, RequestState.Serializable> _cached;

    public CachedRequest(string path)
    {
        _cached = new Cached<RequestState, RequestState.Serializable>(path);
    }

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

internal sealed class OnDemandRequests
{
    private readonly string _requestPath;
    private readonly Dictionary<Guid, CachedRequest> _requestById;

    public string RequestPath => _requestPath;

    public OnDemandRequests(string requestPath)
    {
        _requestPath = requestPath;
        Directory.CreateDirectory(_requestPath);
        _requestById = Directory.GetFiles(_requestPath).Select(f => new CachedRequest(f)).ToDictionary(r => r.Value.Request.Id);
    }

    [JsonIgnore]
    public List<CachedRequest> ActiveRequests => [.. _requestById.Values.Where(r => r.Value.ChainDiffs.Any(cd => cd.Status != ChainStatus.Done))];

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
            if (!request.TryGetChainReference(rootBuild, out var chainDiff))
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

    public bool TryGetRootTriggered(BuildReference onDemandRoot, [NotNullWhen(true)] out ILockedJson<RequestState>? lockedRequest)
    {
        foreach (var cached in _requestById.Values)
        {
            var request = cached.Value;
            if (request.TryGetChainOnDemand(onDemandRoot, out var chainDiff) && chainDiff.Status == ChainStatus.RootTriggered)
            {
                lockedRequest = cached.Lock(nameof(TryGetRootTriggered));
                return true;
            }
        }
        lockedRequest = null;
        return false;
    }

    public bool TryGetTestTriggered(BuildReference rootBuild, BuildReference testBuild, [NotNullWhen(true)] out ILockedJson<RequestState>? lockedRequest)
    {
        foreach (var cached in _requestById.Values)
        {
            var request = cached.Value;
            foreach (var chainDiff in request.ChainDiffs)
            {
                var sameRoot = chainDiff.OnDemandRoot.Match(
                    onPending: _ => false,
                    onTriggered: _ => false,
                    onDone: buildRef => buildRef.Equals(rootBuild)
                );
                if (!sameRoot)
                {
                    continue;
                }
                foreach (var buildDiff in chainDiff.TestBuildDiffs)
                {
                    if (buildDiff.OnDemandBuild.TryGetTriggered(out var triggeredBuild) && triggeredBuild.Equals(testBuild))
                    {
                        lockedRequest = cached.Lock(nameof(TryGetTestTriggered));
                        return true;
                    }
                }
            }
        }
        lockedRequest = null;
        return false;
    }
}
