using System.Diagnostics.CodeAnalysis;

namespace Tod.Jenkins;

internal sealed class JobGroup(Dictionary<BranchName, JobName> referenceJobByBranch, JobName onDemandJob)
{
    public Dictionary<BranchName, JobName> ReferenceJobByBranch { get; } = referenceJobByBranch;
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
        private readonly Dictionary<BranchName, JobName> _refJobByBranch = [];
        private JobName? _ondemandJob;

        public void AddReference(JobName job, BranchName branch)
        {
            if (_refJobByBranch.TryGetValue(branch, out var current))
            {
                throw new ArgumentException($"Job must be unique, cannot add '{job}' job for '{branch}' branch after '{current}'");
            }
            _refJobByBranch.Add(branch, job);
        }

        public void AddOnDemand(JobName job)
        {
            if (_ondemandJob != null)
            {
                throw new ArgumentException($"Job must be unique, cannot add '{job}' job after '{_ondemandJob}'", nameof(job));
            }
            _ondemandJob = job;
        }

        public bool TryBuild([NotNullWhen(true)] out JobGroup? jobGroup, Action<string> addError)
        {
            jobGroup = null;
            // Both _refJobByBranch and _ondemandJob cannot be empty and null by design
            if (_refJobByBranch.Count == 0)
            {
                addError($"No reference job for '{_ondemandJob}' job");
            }
            else if (_ondemandJob == null)
            {
                addError($"No ondemand job for {string.Join(", ", _refJobByBranch.Values.Select(j => $"'{j}'"))} job{(_refJobByBranch.Count > 1 ? "s" : "")}");
            }
            else
            {
                jobGroup = new JobGroup(_refJobByBranch, _ondemandJob);
                return true;
            }
            return false;
        }
    }

    private readonly Dictionary<RootName, JobGroupBuilder> _rootBuilderByName = [];
    private readonly Dictionary<TestName, JobGroupBuilder> _testBuilderByName = [];

    public void AddReferenceRoot(JobName job, BranchName branch, RootName root)
    {
        _rootBuilderByName.GetOrAdd(root, new JobGroupBuilder()).AddReference(job, branch);
    }
    public void AddOnDemandRoot(JobName job, RootName root)
    {
        _rootBuilderByName.GetOrAdd(root, new JobGroupBuilder()).AddOnDemand(job);
    }
    public void AddReferenceTest(JobName job, BranchName branch, TestName test)
    {
        _testBuilderByName.GetOrAdd(test, new JobGroupBuilder()).AddReference(job, branch);
    }
    public void AddOnDemandTest(JobName job, TestName test)
    {
        _testBuilderByName.GetOrAdd(test, new JobGroupBuilder()).AddOnDemand(job);
    }

    public bool TryBuild([NotNullWhen(true)] out JobGroups? jobGroups, Action<string> addError)
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
            addError("No root group");
        }
        if (testGroupByName.Count == 0)
        {
            addError("No test group");
        }
        jobGroups = null;
        return false;
    }
}
