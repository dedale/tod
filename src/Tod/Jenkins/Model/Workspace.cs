using Serilog;
using Tod.Core;
using Tod.Git;

namespace Tod.Jenkins;

internal sealed class Workspace(List<BaselineBranch> baselineBranches, OnDemandBuilds onDemandBuilds, OnDemandRequests onDemandRequests, FlakyTests flakyTests)
{
    public IEnumerable<BaselineBranch> BaselineBranches { get; } = baselineBranches;
    public OnDemandBuilds OnDemandBuilds { get; } = onDemandBuilds;
    public OnDemandRequests OnDemandRequests { get; } = onDemandRequests;
    public FlakyTests FlakyTests { get; } = flakyTests;

    public void Add(BaselineBranch baselineBranch)
    {
        // TODO auto save branches when adding new baseline branches
        baselineBranches.Add(baselineBranch);
    }

    // TODO init verb to create workspace directory structure

    public static Workspace New(string dir, JobGroups jobGroups)
    {
        var workspaceStore = new WorkspaceStore(dir);
        var baselineBranches = new List<BaselineBranch>();

        var rootJobNamesByBranch = jobGroups.ByRoot.Values
            .SelectMany(x => x.BaselineJobByBranch)
            .GroupBy(kvp => kvp.Key)
            .ToDictionary(g => g.Key, g => g.Select(kvp => kvp.Value).ToList());
        foreach (var branch in rootJobNamesByBranch.Keys)
        {
            var baselineStore = workspaceStore.GetBaselineStore(branch);
            var baselineBranch = new BaselineBranch(baselineStore);
            foreach (var rootJob in rootJobNamesByBranch[branch])
            {
                baselineBranch.TryAddRoot(rootJob);
            }
            baselineBranches.Add(baselineBranch);
        }

        var testJobNamesByBranch = jobGroups.ByTest.Values
            .SelectMany(x => x.BaselineJobByBranch)
            .GroupBy(kvp => kvp.Key)
            .ToDictionary(g => g.Key, g => g.Select(kvp => kvp.Value).ToList());
        foreach (var baselineBranch in baselineBranches)
        {
            if (testJobNamesByBranch.TryGetValue(baselineBranch.BranchName, out var jobNames))
            {
                foreach (var jobName in jobNames)
                {
                    baselineBranch.TryAddTest(jobName);
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

        var workspace = new Workspace(baselineBranches, onDemandBuilds, onDemandRequests, flakyTests);
        return workspace;
    }

    public static Workspace Load(string dir, IWorkspaceStore workspaceStore)
    {
        MigrateWorkspaceIfNeeded(dir, workspaceStore);

        var baselineBranches = new List<BaselineBranch>();
        foreach (var branch in workspaceStore.Branches)
        {
            var baselineStore = workspaceStore.GetBaselineStore(branch);
            var baselineBranch = new BaselineBranch(baselineStore);
            baselineBranches.Add(baselineBranch);
        }
        var onDemandBuilds = new OnDemandBuilds(workspaceStore.OnDemandStore);
        var onDemandRequests = new OnDemandRequests(Path.Combine(dir, "Requests"));
        var flakyTests = workspaceStore.FlakyStore.Load();
        return new Workspace(baselineBranches, onDemandBuilds, onDemandRequests, flakyTests);
    }

    private static void MigrateWorkspaceIfNeeded(string dir, IWorkspaceStore workspaceStore)
    {
        const int CurrentRequestsFormatVersion = RequestState.CurrentFormatVersion;

        var metadata = workspaceStore.LoadMetadata();

        if (metadata.RequestsFormatVersion < CurrentRequestsFormatVersion)
        {
            Log.Information("Migrating workspace requests from format v{OldVersion} to v{NewVersion}",
                metadata.RequestsFormatVersion, CurrentRequestsFormatVersion);

            MigrateRequests(dir, CurrentRequestsFormatVersion);

            metadata = metadata with { RequestsFormatVersion = CurrentRequestsFormatVersion };
            workspaceStore.SaveMetadata(metadata);

            Log.Information("Workspace migration completed");
        }
    }

    private static void MigrateRequests(string dir, int targetVersion)
    {
        var requestsDir = Path.Combine(dir, "Requests");
        if (!Directory.Exists(requestsDir))
        {
            Log.Debug("No requests directory found, skipping migration");
            return;
        }

        var requestFiles = Directory.GetFiles(requestsDir, "*.json");
        if (requestFiles.Length == 0)
        {
            Log.Debug("No request files found, skipping migration");
            return;
        }

        var options = LockedJsonSerializer<RequestState, RequestState.Serializable>.GetJsonOptions(indented: true);

        Log.Information("Migrating {Count} request files to format v{Version}",
            requestFiles.Length, targetVersion);

        var migrated = 0;
        var skipped = 0;
        foreach (var file in requestFiles)
        {
            var contents = File.ReadAllText(file);

            if (!contents.Contains("\"FormatVersion\""))
            {
                var upgraded = RequestState.UpgradeFormat(contents, options);

                var backup = file + ".bak";
                File.Copy(file, backup, overwrite: true);
                File.WriteAllText(file, upgraded);
                File.Delete(backup);

                migrated++;
            }
            else
            {
                skipped++;
            }
        }

        Log.Information("Migration complete: {Migrated} migrated, {Skipped} already up-to-date",
            migrated, skipped);
    }

    public void UpdateJobs(IWorkspaceStore workspaceStore, JobGroups jobGroups)
    {
        var baselineBranchByBranch = BaselineBranches.ToDictionary(br => br.BranchName, br => br);

        var rootJobNamesByBranch = jobGroups.ByRoot.Values
            .SelectMany(x => x.BaselineJobByBranch)
            .GroupBy(kvp => kvp.Key)
            .ToDictionary(g => g.Key, g => g.Select(kvp => kvp.Value).ToHashSet());
        foreach (var branch in rootJobNamesByBranch.Keys)
        {
            if (baselineBranchByBranch.TryGetValue(branch, out var baselineBranch))
            {
                foreach (var rootJob in rootJobNamesByBranch[branch])
                {
                    if (!baselineBranch.RootBuilds.Contains(rootJob))
                    {
                        Log.Information("Adding root job {@RootJob} to branch {Branch}", rootJob, branch);
                        baselineBranch.TryAddRoot(rootJob);
                    }
                }
                foreach (var rootJob in baselineBranch.RootBuilds.Select(b => b.JobName).ToList())
                {
                    if (!rootJobNamesByBranch[branch].Contains(rootJob))
                    {
                        Log.Information("Removing root job {@RootJob} from branch {Branch}", rootJob, branch);
                        baselineBranch.RemoveRoot(rootJob);
                    }
                }
            }
            else
            {
                var baselineStore = workspaceStore.GetBaselineStore(branch);
                baselineBranch = new BaselineBranch(baselineStore);
                foreach (var rootJob in rootJobNamesByBranch[branch])
                {
                    Log.Information("Adding root job {@RootJob} to new branch {Branch}", rootJob, branch);
                    baselineBranch.TryAddRoot(rootJob);
                }
                Add(baselineBranch);
            }
        }

        var testJobNamesByBranch = jobGroups.ByTest.Values
            .SelectMany(x => x.BaselineJobByBranch)
            .GroupBy(kvp => kvp.Key)
            .ToDictionary(g => g.Key, g => g.Select(kvp => kvp.Value).ToHashSet());
        foreach (var baselineBranch in BaselineBranches)
        {
            var branch = baselineBranch.BranchName;
            if (testJobNamesByBranch.TryGetValue(branch, out var testJobs))
            {
                foreach (var testJob in testJobs)
                {
                    if (!baselineBranch.TestBuilds.Contains(testJob))
                    {
                        Log.Information("Adding test job {@TestJob} to branch {Branch}", testJob, branch);
                        baselineBranch.TryAddTest(testJob);
                    }
                }
                foreach (var testJob in baselineBranch.TestBuilds.Select(b => b.JobName).ToList())
                {
                    if (!testJobNamesByBranch[branch].Contains(testJob))
                    {
                        Log.Information("Removing test job {@TestJob} from branch {Branch}", testJob, branch);
                        baselineBranch.RemoveTest(testJob);
                    }
                }
            }
        }

        var onDemandRootJobs = jobGroups.ByRoot.Values.Select(x => x.OnDemandJob).ToHashSet();
        foreach (var rootJob in onDemandRootJobs)
        {
            if (!OnDemandBuilds.RootBuilds.Contains(rootJob))
            {
                Log.Information("Adding on-demand root job {@RootJob}", rootJob);
                OnDemandBuilds.TryAddRoot(rootJob);
            }
        }
        foreach (var rootJob in OnDemandBuilds.RootBuilds.Select(b => b.JobName).ToList())
        {
            if (!onDemandRootJobs.Contains(rootJob))
            {
                Log.Information("Removing on-demand root job {@RootJob}", rootJob);
                OnDemandBuilds.RemoveRoot(rootJob);
            }
        }
        var onDemandTestJobs = jobGroups.ByTest.Values.Select(x => x.OnDemandJob).ToHashSet();
        foreach (var testJob in onDemandTestJobs)
        {
            if (!OnDemandBuilds.TestBuilds.Contains(testJob))
            {
                Log.Information("Adding on-demand test job {@TestJob}", testJob);
                OnDemandBuilds.TryAddTest(testJob);
            }
        }
        foreach (var testJob in OnDemandBuilds.TestBuilds.Select(b => b.JobName).ToList())
        {
            if (!onDemandTestJobs.Contains(testJob))
            {
                Log.Information("Removing on-demand test job {@TestJob}", testJob);
                OnDemandBuilds.RemoveTest(testJob);
            }
        }
    }

    public int RemoveBuildsOlderThan(DateTime thresholdUtc)
    {
        var removed = 0;
        foreach (var baselineBranch in BaselineBranches)
        {
            removed += baselineBranch.RemoveBuildsOlderThan(thresholdUtc);
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
            var rootJobNames = rootDiffs.Select(d => d.BaselineJob).ToArray();
            if (!workspace.BaselineBranches.TryFindRefCommit(commits, rootJobNames, wantedBranch, out var commit))
            {
                return null;
            }
            return new GitReference(wantedBranch, commit);
        }
        else
        {
            if (!workspace.BaselineBranches.TryGuessBranch(commits, rootFilters, filterManager, out rootDiffs, out var branchName, out var commit))
            {
                return null;
            }
            return new GitReference(branchName, commit);
        }
    }
}
