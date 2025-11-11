namespace Tod.Jenkins;

internal sealed class Workspace(List<BranchReference> branchReferences, OnDemandBuilds onDemandBuilds, OnDemandRequests onDemandRequests)
{
    public IEnumerable<BranchReference> BranchReferences { get; } = branchReferences;
    public OnDemandBuilds OnDemandBuilds { get; } = onDemandBuilds;
    public OnDemandRequests OnDemandRequests { get; } = onDemandRequests;

    public void Add(BranchReference branchReference)
    {
        // TODO auto save branches when adding new branch references
        branchReferences.Add(branchReference);
    }

    public static Workspace New(string dir, JobGroups jobGroups)
    {
        var workspaceStore = new WorkspaceStore(dir);
        var branchReferences = new List<BranchReference>();

        var rootJobNamesByBranch = jobGroups.ByRoot.Values
            .SelectMany(x => x.ReferenceJobByBranch)
            .GroupBy(kvp => kvp.Key)
            .ToDictionary(g => g.Key, g => g.Select(kvp => kvp.Value).ToList());
        foreach (var branch in rootJobNamesByBranch.Keys)
        {
            var referenceStore = workspaceStore.GetReferenceStore(branch);
            var branchReference = new BranchReference(referenceStore);
            foreach (var rootJob in rootJobNamesByBranch[branch])
            {
                branchReference.TryAddRoot(rootJob);
            }
            branchReferences.Add(branchReference);
        }

        var testJobNamesByBranch = jobGroups.ByTest.Values
            .SelectMany(x => x.ReferenceJobByBranch)
            .GroupBy(kvp => kvp.Key)
            .ToDictionary(g => g.Key, g => g.Select(kvp => kvp.Value).ToList());
        foreach (var branchReference in branchReferences)
        {
            if (testJobNamesByBranch.TryGetValue(branchReference.BranchName, out var jobNames))
            {
                foreach (var jobName in jobNames)
                {
                    branchReference.TryAddTest(jobName);
                }
            }
        }

        var onDemandRootBuilds = new List<BuildCollection<RootBuild>>();

        var onDemandRootJobs = jobGroups.ByRoot.Values.Select(x => x.OnDemandJob);
        var onDemandStore = workspaceStore.OnDemandStore;
        var onDemandBuilds = new OnDemandBuilds(onDemandRootJobs, onDemandStore);

        var onDemandJobNames = jobGroups.ByTest.Values.Select(x => x.OnDemandJob);
        foreach (var jobName in onDemandJobNames)
        {
            onDemandBuilds.TryAddTest(jobName);
        }

        // TODO Store for requests
        var onDemandRequests = new OnDemandRequests(Path.Combine(dir, "Requests"));

        var workspace = new Workspace(branchReferences, onDemandBuilds, onDemandRequests);
        return workspace;
    }

    public static Workspace Load(string dir, IWorkspaceStore workspaceStore)
    {
        var branchReferences = new List<BranchReference>();
        foreach (var branch in workspaceStore.Branches)
        {
            var referenceStore = workspaceStore.GetReferenceStore(branch);
            var branchReference = new BranchReference(referenceStore);
            branchReferences.Add(branchReference);
        }
        var onDemandBuilds = new OnDemandBuilds(workspaceStore.OnDemandStore);
        var onDemandRequests = new OnDemandRequests(Path.Combine(dir, "Requests"));
        return new Workspace(branchReferences, onDemandBuilds, onDemandRequests);
    }
}
