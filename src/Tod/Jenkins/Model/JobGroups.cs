using System.Diagnostics.CodeAnalysis;

namespace Tod.Jenkins;

internal sealed class JobGroup(Dictionary<BranchName, JobName> referenceJobByBranch, JobName onDemandJob)
{
    public Dictionary<BranchName, JobName> BaselineJobByBranch { get; } = referenceJobByBranch;
    public JobName OnDemandJob { get; } = onDemandJob;
}

internal sealed class JobGroups(Dictionary<RootName, JobGroup> byRoot, Dictionary<TestName, JobGroup> byTest)
{
    public Dictionary<RootName, JobGroup> ByRoot { get; } = byRoot;
    public Dictionary<TestName, JobGroup> ByTest { get; } = byTest;
}

internal sealed class JobGroupsBuilder
{
    private sealed class JobGroupBuilder
    {
        private readonly Dictionary<BranchName, JobName> _baseJobByBranch = [];
        private JobName? _ondemandJob;

        public void AddBaseline(JobName job, BranchName branch)
        {
            if (_baseJobByBranch.TryGetValue(branch, out var current))
            {
                throw new ArgumentException($"Job must be unique, cannot add '{job}' job for '{branch}' branch after '{current}'");
            }
            _baseJobByBranch.Add(branch, job);
        }

        public void AddOnDemand(JobName job)
        {
            if (_ondemandJob != null)
            {
                throw new ArgumentException($"Job must be unique, cannot add '{job}' job after '{_ondemandJob}'", nameof(job));
            }
            _ondemandJob = job;
        }

        public bool TryBuild([NotNullWhen(true)] out JobGroup? jobGroup, Action<string, object?[]?> addError)
        {
            jobGroup = null;
            // Both _refJobByBranch and _ondemandJob cannot be empty and null by design
            if (_baseJobByBranch.Count == 0)
            {
                addError("No reference job for '{@Job}' job", [_ondemandJob]);
            }
            else if (_ondemandJob == null)
            {
                addError($"No ondemand job for {string.Join(", ", _baseJobByBranch.Values.Select(j => "'{@Job}'"))} job{(_baseJobByBranch.Count > 1 ? "s" : "")}", [.. _baseJobByBranch.Values]);
            }
            else
            {
                jobGroup = new JobGroup(_baseJobByBranch, _ondemandJob);
                return true;
            }
            return false;
        }
    }

    private readonly Dictionary<RootName, JobGroupBuilder> _rootBuilderByName = [];
    private readonly Dictionary<TestName, JobGroupBuilder> _testBuilderByName = [];

    public void AddBaselineRoot(JobName job, BranchName branch, RootName root)
    {
        _rootBuilderByName.GetOrAdd(root, new JobGroupBuilder()).AddBaseline(job, branch);
    }
    public void AddOnDemandRoot(JobName job, RootName root)
    {
        _rootBuilderByName.GetOrAdd(root, new JobGroupBuilder()).AddOnDemand(job);
    }
    public void AddBaselineTest(JobName job, BranchName branch, TestName test)
    {
        _testBuilderByName.GetOrAdd(test, new JobGroupBuilder()).AddBaseline(job, branch);
    }
    public void AddOnDemandTest(JobName job, TestName test)
    {
        _testBuilderByName.GetOrAdd(test, new JobGroupBuilder()).AddOnDemand(job);
    }

    public bool TryBuild([NotNullWhen(true)] out JobGroups? jobGroups, Action<string, object?[]?> addError)
    {
        var rootGroupByName = new Dictionary<RootName, JobGroup>();
        foreach (var (root, builder) in _rootBuilderByName)
        {
            if (builder.TryBuild(out var testGroup, addError))
            {
                rootGroupByName.Add(root, testGroup);
            }
        }
        var testGroupByName = new Dictionary<TestName, JobGroup>();
        foreach (var (test, builder) in _testBuilderByName)
        {
            if (builder.TryBuild(out var testGroup, addError))
            {
                testGroupByName.Add(test, testGroup);
            }
        }
        if (rootGroupByName.Count > 0 && testGroupByName.Count > 0)
        {
            jobGroups = new JobGroups(rootGroupByName, testGroupByName);
            return true;
        }
        if (rootGroupByName.Count == 0)
        {
            addError("No root group", []);
        }
        if (testGroupByName.Count == 0)
        {
            addError("No test group", []);
        }
        jobGroups = null;
        return false;
    }
}
