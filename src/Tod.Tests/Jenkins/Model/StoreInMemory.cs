using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

internal sealed class InMemoryByJobNameStore(BuildBranch buildBranch) : IByJobNameStore
{
    private readonly HashSet<JobName> _jobs = [];
    private readonly Dictionary<JobName, object> _store = [];

    public BuildBranch BuildBranch => buildBranch;

    public IEnumerable<JobName> JobNames => _store.Keys;

    public void Add(JobName jobName)
    {
        _jobs.Add(jobName);
    }

    public void Remove(JobName jobName)
    {
        _jobs.Remove(jobName);
        _store.Remove(jobName);
    }

    public T Load<T>(JobName jobName, Func<T> create)
    {
        if (_store.TryGetValue(jobName, out var item))
        {
            return (T)item;
        }
        return create();
    }

    public void Save<T>(JobName jobName, T item)
    {
        _store[jobName] = item!;
    }
}

internal sealed class InMemoryByJobNameStoreFactory(BuildBranch buildBranch) : IByJobNameStoreFactory
{
    private readonly InMemoryByJobNameStore _store = new(buildBranch);

    public IByJobNameStore New() => _store;
}

internal sealed class InMemoryByChainStore : IByChainStore
{
    private readonly HashSet<string> _chainNames = [];
    private readonly Dictionary<string, object> _store = [];

    public IEnumerable<string> ChainNames => _chainNames;

    public void Add(string chainName)
    {
        _chainNames.Add(chainName);
    }

    public void Save<T>(string chainName, T item)
    {
        _store[chainName] = item!;
    }

    public T Load<T>(string chainName, Func<T> create)
    {
        if (_store.TryGetValue(chainName, out var item))
        {
            return (T)item;
        }
        return create();
    }
}

internal sealed class InMemoryReferenceStore : IReferenceStore
{
    private readonly BranchName _branch;
    private readonly InMemoryByJobNameStore _rootStore;
    private readonly InMemoryByJobNameStore _testStore;
    private readonly InMemoryByChainStore _chainStore;

    public InMemoryReferenceStore(BranchName branch)
    {
        _branch = branch;
        var buildBranch = BuildBranch.Create(branch);
        _rootStore = new InMemoryByJobNameStore(buildBranch);
        _testStore = new InMemoryByJobNameStore(buildBranch);
        _chainStore = new InMemoryByChainStore();
    }

    public BranchName Branch => _branch;
    public IByJobNameStore RootStore => _rootStore;
    public IByJobNameStore TestStore => _testStore;
    public IByChainStore ChainStore => _chainStore;
}

internal sealed class InMemoryOnDemandStore : IOnDemandStore
{
    private readonly InMemoryByJobNameStore _rootStore;
    private readonly InMemoryByJobNameStore _testStore;

    public InMemoryOnDemandStore()
    {
        _rootStore = new InMemoryByJobNameStore(BuildBranch.OnDemand);
        _testStore = new InMemoryByJobNameStore(BuildBranch.OnDemand);
    }

    public IByJobNameStore RootStore => _rootStore;
    public IByJobNameStore TestStore => _testStore;
}

internal sealed class InMemoryFlakyStore : IFlakyStore
{
    public static InMemoryFlakyStore Default = new();

    private InMemoryFlakyStore()
    {
    }

    private FlakyTests _flakyTests = new(Default);

    public FlakyTests Load()
    {
        return _flakyTests;
    }

    public void Save(FlakyTests flakyTests)
    {
        _flakyTests = flakyTests;
    }
}

internal sealed class InMemoryWorkspaceStore : IWorkspaceStore
{
    private readonly Dictionary<BranchName, InMemoryReferenceStore> _referenceByBranch = [];
    private readonly InMemoryOnDemandStore _onDemandStore = new();

    public IEnumerable<BranchName> Branches => _referenceByBranch.Keys;

    public IReferenceStore GetReferenceStore(BranchName branch)
    {
        if (_referenceByBranch.TryGetValue(branch, out var referenceStore))
        {
            return referenceStore;
        }
        referenceStore = new InMemoryReferenceStore(branch);
        _referenceByBranch.Add(branch, referenceStore);
        return referenceStore;
    }

    public IOnDemandStore OnDemandStore => _onDemandStore;
    public IFlakyStore FlakyStore => InMemoryFlakyStore.Default;
}
