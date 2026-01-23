using Serilog;
using Tod.Git;

namespace Tod.Jenkins;

internal sealed class Workspace(List<BranchReference> branchReferences, OnDemandBuilds onDemandBuilds, OnDemandRequests onDemandRequests, FlakyTests flakyTests)
{
    public IEnumerable<BranchReference> BranchReferences { get; } = branchReferences;
    public OnDemandBuilds OnDemandBuilds { get; } = onDemandBuilds;
    public OnDemandRequests OnDemandRequests { get; } = onDemandRequests;
    public FlakyTests FlakyTests { get; } = flakyTests;

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

        var onDemandTestJobs = jobGroups.ByTest.Values.Select(x => x.OnDemandJob);
        foreach (var testJob in onDemandTestJobs)
        {
            onDemandBuilds.TryAddTest(testJob);
        }

        // TODO Store for requests

        // No need to manage job mappings when creating a new workspace
        var onDemandRequests = new OnDemandRequests(Path.Combine(dir, "Requests"));

        var flakyStore = workspaceStore.FlakyStore;
        var flakyTests = new FlakyTests(flakyStore);
        flakyStore.Save(flakyTests);

        var workspace = new Workspace(branchReferences, onDemandBuilds, onDemandRequests, flakyTests);
        return workspace;
    }

    public static Workspace Load(string dir, IWorkspaceStore workspaceStore, JobMapping[]? jobMappings = null)
    {
        var branchReferences = new List<BranchReference>();
        foreach (var branch in workspaceStore.Branches)
        {
            var referenceStore = workspaceStore.GetReferenceStore(branch);
            var branchReference = new BranchReference(referenceStore);
            branchReferences.Add(branchReference);
        }
        var onDemandBuilds = new OnDemandBuilds(workspaceStore.OnDemandStore);
        var buildReferenceComparer = new BuildReferenceComparer(jobMappings ?? []);
        var onDemandRequests = new OnDemandRequests(Path.Combine(dir, "Requests"), buildReferenceComparer);
        var flakyTests = workspaceStore.FlakyStore.Load();
        return new Workspace(branchReferences, onDemandBuilds, onDemandRequests, flakyTests);
    }

    public void UpdateJobs(IWorkspaceStore workspaceStore, JobGroups jobGroups)
    {
        var branchReferenceByBranch = BranchReferences.ToDictionary(br => br.BranchName, br => br);

        var rootJobNamesByBranch = jobGroups.ByRoot.Values
            .SelectMany(x => x.ReferenceJobByBranch)
            .GroupBy(kvp => kvp.Key)
            .ToDictionary(g => g.Key, g => g.Select(kvp => kvp.Value).ToHashSet());
        foreach (var branch in rootJobNamesByBranch.Keys)
        {
            if (branchReferenceByBranch.TryGetValue(branch, out var branchReference))
            {
                foreach (var rootJob in rootJobNamesByBranch[branch])
                {
                    if (!branchReference.RootBuilds.Contains(rootJob))
                    {
                        Log.Information("Adding root job {RootJob} to branch {Branch}", rootJob, branch);
                        branchReference.TryAddRoot(rootJob);
                    }
                }
                foreach (var rootJob in branchReference.RootBuilds.Select(b => b.JobName).ToList())
                {
                    if (!rootJobNamesByBranch[branch].Contains(rootJob))
                    {
                        Log.Information("Removing root job {RootJob} from branch {Branch}", rootJob, branch);
                        branchReference.RemoveRoot(rootJob);
                    }
                }
            }
            else
            {
                var referenceStore = workspaceStore.GetReferenceStore(branch);
                branchReference = new BranchReference(referenceStore);
                foreach (var rootJob in rootJobNamesByBranch[branch])
                {
                    Log.Information("Adding root job {RootJob} to new branch {Branch}", rootJob, branch);
                    branchReference.TryAddRoot(rootJob);
                }
                branchReferences.Add(branchReference);
            }
        }

        var testJobNamesByBranch = jobGroups.ByTest.Values
            .SelectMany(x => x.ReferenceJobByBranch)
            .GroupBy(kvp => kvp.Key)
            .ToDictionary(g => g.Key, g => g.Select(kvp => kvp.Value).ToHashSet());
        foreach (var branchReference in branchReferences)
        {
            var branch = branchReference.BranchName;
            if (testJobNamesByBranch.TryGetValue(branch, out var testJobs))
            {
                foreach (var testJob in testJobs)
                {
                    if (!branchReference.TestBuilds.Contains(testJob))
                    {
                        Log.Information("Adding test job {TestJob} to branch {Branch}", testJob, branch);
                        branchReference.TryAddTest(testJob);
                    }
                }
                foreach (var testJob in branchReference.TestBuilds.Select(b => b.JobName).ToList())
                {
                    if (!testJobNamesByBranch[branch].Contains(testJob))
                    {
                        Log.Information("Removing test job {TestJob} from branch {Branch}", testJob, branch);
                        branchReference.RemoveTest(testJob);
                    }
                }
            }
        }

        var onDemandRootJobs = jobGroups.ByRoot.Values.Select(x => x.OnDemandJob).ToHashSet();
        foreach (var rootJob in onDemandRootJobs)
        {
            if (!OnDemandBuilds.RootBuilds.Contains(rootJob))
            {
                Log.Information("Adding on-demand root job {RootJob}", rootJob);
                OnDemandBuilds.TryAddRoot(rootJob);
            }
        }
        foreach (var rootJob in OnDemandBuilds.RootBuilds.Select(b => b.JobName).ToList())
        {
            if (!onDemandRootJobs.Contains(rootJob))
            {
                Log.Information("Removing on-demand root job {RootJob}", rootJob);
                OnDemandBuilds.RemoveRoot(rootJob);
            }
        }
        var onDemandTestJobs = jobGroups.ByTest.Values.Select(x => x.OnDemandJob).ToHashSet();
        foreach (var testJob in onDemandTestJobs)
        {
            if (!OnDemandBuilds.TestBuilds.Contains(testJob))
            {
                Log.Information("Adding on-demand test job {TestJob}", testJob);
                OnDemandBuilds.TryAddTest(testJob);
            }
        }
        foreach (var testJob in OnDemandBuilds.TestBuilds.Select(b => b.JobName).ToList())
        {
            if (!onDemandTestJobs.Contains(testJob))
            {
                Log.Information("Removing on-demand test job {TestJob}", testJob);
                OnDemandBuilds.RemoveTest(testJob);
            }
        }
    }

    public int RemoveBuildsOlderThan(DateTime thresholdUtc)
    {
        var removed = 0;
        foreach (var branchReference in BranchReferences)
        {
            removed += branchReference.RemoveBuildsOlderThan(thresholdUtc);
        }
        removed += OnDemandBuilds.RemoveBuildsOlderThan(thresholdUtc);
        return removed;
    }
}

internal static class WorkspaceExtensions
{
    public static GitReference? GetGitReference(this Workspace workspace, FilterManager filterManager, BranchName? wantedBranch, string[] rootFilters, Sha1[] commits, out JobDiff[] rootDiffs)
    {
        if (wantedBranch != null)
        {
            rootDiffs = filterManager.GetRootDiffs(rootFilters, wantedBranch);
            var rootJobNames = rootDiffs.Select(d => d.ReferenceJob).ToArray();
            if (!workspace.BranchReferences.TryFindRefCommit(commits, rootJobNames, wantedBranch, out var commit))
            {
                return null;
            }
            return new GitReference(wantedBranch, commit);
        }
        else
        {
            if (!workspace.BranchReferences.TryGuessBranch(commits, rootFilters, filterManager, out rootDiffs, out var branchName, out var commit))
            {
                return null;
            }
            return new GitReference(branchName, commit);
        }
    }
}
