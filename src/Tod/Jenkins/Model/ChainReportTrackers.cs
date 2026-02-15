namespace Tod.Jenkins;

// Manages ChainReportTrackers for multiple chains, loading them from the store on initialization and saving them back when needed.
internal sealed class ChainReportTrackers
{
    private readonly Dictionary<string, ChainReportTracker> _trackers;
    private readonly IByChainStore _store;

    public ChainReportTrackers(IByChainStore store)
    {
        _store = store;
        _trackers = [];
        foreach (var chainName in _store.ChainNames)
        {
            var loaded = _store.Load(chainName, () => new ChainReportTracker.Serializable(chainName, []));
            _trackers[chainName] = loaded.FromSerializable(_store);
        }
    }

    public ChainReportTracker GetOrCreate(string chainName)
    {
        if (!_trackers.TryGetValue(chainName, out var tracker))
        {
            tracker = new ChainReportTracker(chainName, _store);
            _trackers[chainName] = tracker;
            _store.Add(chainName);
        }
        return tracker;
    }

    public ChainReportTracker? Get(string chainName)
    {
        return _trackers.GetValueOrDefault(chainName);
    }

    public void Save(string chainName)
    {
        if (_trackers.TryGetValue(chainName, out var tracker))
        {
            _store.Save(chainName, tracker.ToSerializable());
        }
    }

    public void SaveAll()
    {
        foreach (var (chainName, tracker) in _trackers)
        {
            _store.Save(chainName, tracker.ToSerializable());
        }
    }
}
