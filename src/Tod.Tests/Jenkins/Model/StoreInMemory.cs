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

    public T Load<T>(JobName jobName, Func<JobName, T> create)
    {
        if (_store.TryGetValue(jobName, out var item))
        {
            return (T)item;
        }
        return create(jobName);
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

internal sealed class InMemoryReferenceStore : IReferenceStore
{
    private readonly BranchName _branch;
    private readonly InMemoryByJobNameStore _rootStore;
    private readonly InMemoryByJobNameStore _testStore;

    public InMemoryReferenceStore(BranchName branch)
    {
        _branch = branch;
        var buildBranch = BuildBranch.Create(branch);
        _rootStore = new InMemoryByJobNameStore(buildBranch);
        _testStore = new InMemoryByJobNameStore(buildBranch);
    }

    public BranchName Branch => _branch;
    public IByJobNameStore RootStore => _rootStore;
    public IByJobNameStore TestStore => _testStore;
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
}
