using System.Text.Json;

namespace Tod.Jenkins;

internal interface IByJobNameStore
{
    BuildBranch BuildBranch { get; }
    IEnumerable<JobName> JobNames { get; }
    void Add(JobName jobName);
    void Save<T>(JobName jobName, T item);
    T Load<T>(JobName jobName, Func<JobName, T> create);
}

internal interface IByJobNameStoreFactory
{
    IByJobNameStore New();
}

internal interface IReferenceStore
{
    BranchName Branch { get; }
    IByJobNameStore RootStore { get; }
    IByJobNameStore TestStore { get; }
}

internal interface IOnDemandStore
{
    IByJobNameStore RootStore { get; }
    IByJobNameStore TestStore { get; }
}

internal interface IWorkspaceStore
{
    IEnumerable<BranchName> Branches { get; }
    IReferenceStore GetReferenceStore(BranchName branch);
    IOnDemandStore OnDemandStore { get; }
}

internal abstract class BuildBranch
{
    public abstract void Match(Action<BranchName> onBranch, Action onOnDemand);
    public abstract T Match<T>(Func<BranchName, T> onBranch, Func<T> onOnDemand);

    private sealed class Branch(BranchName branch) : BuildBranch
    {
        public override void Match(Action<BranchName> onBranch, Action onOnDemand) => onBranch(branch);
        public override T Match<T>(Func<BranchName, T> onBranch, Func<T> onOnDemand) => onBranch(branch);
    }

    private sealed class Custom : BuildBranch
    {
        public override void Match(Action<BranchName> onBranch, Action onOnDemand) => onOnDemand();
        public override T Match<T>(Func<BranchName, T> onBranch, Func<T> onOnDemand) => onOnDemand();
    }

    public static BuildBranch Create(BranchName branch) => new Branch(branch);

    public static readonly BuildBranch OnDemand = new Custom();

    public KeyValuePair<string, object?> Tag => Match(
        onBranch: branch => KeyValuePair.Create("Branch", (object?)branch),
        onOnDemand: () => KeyValuePair.Create("Branch", (object?)"OnDemand"));
}

internal sealed class ByJobNameStore : IByJobNameStore
{
    private readonly BuildBranch _buildBranch;
    private readonly HashSet<JobName> _jobNames;
    private readonly string _jsonPath;
    private readonly string _jsonDir;

    public ByJobNameStore(BuildBranch buildBranch, IEnumerable<JobName> jobNames, string jsonPath)
    {
        _buildBranch = buildBranch;
        _jobNames = [.. jobNames];
        _jsonPath = jsonPath;
        _jsonDir = Path.GetDirectoryName(_jsonPath)!;
        if (File.Exists(_jsonPath))
        {
            _jobNames = [.. JsonSerializer.Deserialize<List<JobName>>(File.ReadAllText(_jsonPath))!];
        }
    }

    public BuildBranch BuildBranch => _buildBranch;

    public IEnumerable<JobName> JobNames => _jobNames;

    public void Add(JobName jobName)
    {
        if (_jobNames.Add(jobName))
        {
            Directory.CreateDirectory(_jsonDir);
            var json = JsonSerializer.Serialize(_jobNames);
            File.WriteAllText(_jsonPath, json);
        }
    }

    public void Save<T>(JobName jobName, T item)
    {
        var json = JsonSerializer.Serialize(item);
        var jsonPath = Path.Combine(_jsonDir, $"{jobName.Value.Replace('/', Path.DirectorySeparatorChar)}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        File.WriteAllText(jsonPath, json);
    }

    public T Load<T>(JobName jobName, Func<JobName, T> create)
    {
        var jsonPath = Path.Combine(_jsonDir, $"{jobName.Value.Replace('/', Path.DirectorySeparatorChar)}.json");
        if (File.Exists(jsonPath))
        {
            var json = File.ReadAllText(jsonPath);
            return JsonSerializer.Deserialize<T>(json)!;
        }
        return create(jobName);
    }
}

internal sealed class ByJobNameStoreFactory(BuildBranch buildBranch, string jsonPath) : IByJobNameStoreFactory
{
    public IByJobNameStore New()
    {
        if (File.Exists(jsonPath))
        {
            var jobNames = JsonSerializer.Deserialize<List<JobName>>(File.ReadAllText(jsonPath))!;
            return new ByJobNameStore(buildBranch, jobNames, jsonPath);
        }
        return new ByJobNameStore(buildBranch, [], jsonPath);
    }
}

internal sealed class ReferenceStore : IReferenceStore
{
    public ReferenceStore(BranchName branch, string dir)
    {
        var buildBranch = BuildBranch.Create(branch);
        Branch = branch;
        RootStore = new ByJobNameStoreFactory(buildBranch, Path.Combine(dir, "RootBuilds.json")).New();
        TestStore = new ByJobNameStoreFactory(buildBranch, Path.Combine(dir, "TestBuilds.json")).New();
    }

    public BranchName Branch { get; }
    public IByJobNameStore RootStore { get; }
    public IByJobNameStore TestStore { get; }
}

internal sealed class OnDemandStore(string dir) : IOnDemandStore
{
    public IByJobNameStore RootStore { get; } = new ByJobNameStoreFactory(BuildBranch.OnDemand, Path.Combine(dir, "RootBuilds.json")).New();
    public IByJobNameStore TestStore { get; } = new ByJobNameStoreFactory(BuildBranch.OnDemand, Path.Combine(dir, "TestBuilds.json")).New();
}

internal sealed class WorkspaceStore : IWorkspaceStore
{
    private readonly string _dir;
    private readonly string _branchJson;
    private readonly HashSet<BranchName> _branches;
    private readonly Dictionary<BranchName, IReferenceStore> _referenceByBranch = new();

    public WorkspaceStore(string dir)
    {
        if (File.Exists(dir))
        {
            throw new ArgumentException($"The path '{dir}' is a file, but a directory is expected.");
        }
        _dir = dir;
        _branchJson = Path.Combine(dir, "Branches.json");
        _branches = File.Exists(_branchJson) ? JsonSerializer.Deserialize<List<BranchName>>(File.ReadAllText(_branchJson))!.ToHashSet() : [];
        OnDemandStore = new OnDemandStore(Path.Combine(_dir, "OnDemand"));
    }

    private void Save()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_branchJson, JsonSerializer.Serialize(_branches));
    }

    public IEnumerable<BranchName> Branches => _branches;

    public IReferenceStore GetReferenceStore(BranchName branch)
    {
        if (!_branches.Contains(branch))
        {
            _branches.Add(branch);
            Save();
        }
        if (_referenceByBranch.TryGetValue(branch, out var referenceStore))
        {
            return referenceStore;
        }
        referenceStore = new ReferenceStore(branch, Path.Combine(_dir, branch.Value));
        _referenceByBranch.Add(branch, referenceStore);
        return referenceStore;
    }

    public IOnDemandStore OnDemandStore { get; }
}
