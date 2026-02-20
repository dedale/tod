using Serilog;
using System.Diagnostics.CodeAnalysis;
using Tod.Git;

namespace Tod.Jenkins;

internal sealed class BaselineBranch : IBuildChains
{
    private readonly ChainReportTrackers _chainReportTrackers;

    public BaselineBranch(IBaselineStore baselineStore)
        : this(baselineStore.Branch, baselineStore.RootStore, baselineStore.TestStore, baselineStore.ChainStore)
    {
    }

    private BaselineBranch(BranchName branchName, IByJobNameStore rootStore, IByJobNameStore testStore, IByChainStore chainStore)
        : this(branchName, new BuildCollections<RootBuild>(rootStore), new BuildCollections<TestBuild>(testStore), new ChainReportTrackers(chainStore))
    {
    }

    private BaselineBranch(BranchName branchName, BuildCollections<RootBuild> rootBuilds, BuildCollections<TestBuild> testBuilds, ChainReportTrackers chainReportTrackers)
    {
        BranchName = branchName;
        RootBuilds = rootBuilds;
        TestBuilds = testBuilds;
        _chainReportTrackers = chainReportTrackers;
    }

    public BranchName BranchName { get; }
    public BuildCollections<RootBuild> RootBuilds { get; }
    public BuildCollections<TestBuild> TestBuilds { get; }

    public ChainReportTracker GetOrCreateChainTracker(string chainName)
    {
        return _chainReportTrackers.GetOrCreate(chainName);
    }

    public ChainReportTracker? GetChainTracker(string chainName)
    {
        return _chainReportTrackers.Get(chainName);
    }

    public bool TryFindRootBuildByCommit(Sha1 commitId, JobName jobName, [NotNullWhen(true)] out RootBuild? rootBuild)
    {
        var rootBuilds = RootBuilds.GetOrAdd(jobName);
        for (var i = 0; i < rootBuilds.Count; i++)
        {
            if (!rootBuilds[i].Commits.Contains(commitId))
            {
                continue;
            }
            for (var j = i; j < rootBuilds.Count; j++)
            {
                if (rootBuilds[j].IsSuccessful)
                {
                    rootBuild = rootBuilds[j];
                    return true;
                }
            }
        }
        rootBuild = null;
        return false;
    }

    public bool TryFindTestBuild(JobName testJobName, BuildReference rootBuild, [NotNullWhen(true)] out TestBuild? testBuild)
    {
        testBuild = null;
        var builds = TestBuilds.FirstOrDefault(x => x.JobName == testJobName);
        if (builds == null)
        {
            return false;
        }
        for (var i = 0; i < builds.Count; i++)
        {
            var candidate = builds[i];
            if (candidate.RootBuilds.Any(r => r.JobName.Equals(rootBuild.JobName) && r.CompareTo(rootBuild) >= 0))
            {
                testBuild = candidate;
                return true;
            }
        }
        return false;
    }

    public int RemoveBuildsOlderThan(DateTime thresholdUtc)
    {
        var removed = 0;
        foreach (var rootBuilds in RootBuilds)
        {
            removed += rootBuilds.RemoveBuildsOlderThan(thresholdUtc);
        }
        foreach (var testBuilds in TestBuilds)
        {
            removed += testBuilds.RemoveBuildsOlderThan(thresholdUtc);
        }
        return removed;
    }
}

internal static class BaselineBranchExtensions
{
    private static bool TryFindBaseCommit(Sha1[] commits, JobName[] jobNames, BaselineBranch baselineBranch, [NotNullWhen(true)] out Sha1? refCommit)
    {
        if (commits.Length > 0)
        {
            var candidates = new List<IEnumerable<Sha1>>();
            foreach (var jobName in jobNames)
            {
                if (baselineBranch.TryFindRootBuildByCommit(commits.First(), jobName, out _))
                {
                    throw new NotSupportedException($"No local commits to test for job {jobName}");
                }
                for (var i = 1; i < commits.Length; i++)
                {
                    if (baselineBranch.TryFindRootBuildByCommit(commits[i], jobName, out _))
                    {
                        if (jobNames.Length > 1)
                        {
                            Log.Information("Found candidate reference commit {Commit} for job {@JobName}", commits[i], jobName);
                        }
                        candidates.Add(commits.Skip(i));
                        break;
                    }
                }
            }
            if (candidates.Count == 0)
            {
                refCommit = null;
                return false;
            }
            var common = candidates.Aggregate((a, b) => a.Intersect(b)).ToHashSet();
            refCommit = commits.FirstOrDefault(common.Contains);
            return refCommit is not null;
        }
        refCommit = null;
        return false;
    }

    public static bool TryFindRefCommit(
        this IEnumerable<BaselineBranch> baselineBranches,
        Sha1[] commits,
        JobName[] jobNames,
        BranchName wantedBranch,
        [NotNullWhen(true)] out Sha1? refCommit)
    {
        refCommit = null;
        var baselineBranch = baselineBranches.FirstOrDefault(br => br.BranchName == wantedBranch);
        if (baselineBranch == null)
        {
            Log.Error("Branch {BranchName} not found in workspace", wantedBranch);
            return false;
        }
        if (!TryFindBaseCommit(commits, jobNames, baselineBranch, out refCommit))
        {
            Log.Error("Cannot find reference commit in {BranchName} branch history", wantedBranch);
            return false;
        }
        Log.Information("Using reference commit {RefCommit}", refCommit);
        return true;
    }

    public static bool TryGuessBranch(
        this IEnumerable<BaselineBranch> baselineBranches,
        Sha1[] commits,
        string[] rootFilters,
        IFilterManager filterManager,
        [NotNullWhen(true)] out JobDiff[] rootDiffs,
        [NotNullWhen(true)] out BranchName? branchName,
        [NotNullWhen(true)] out Sha1? baseCommit)
    {
        Log.Information("No branch specified, guessing...");
        foreach (var baselineBranch in baselineBranches)
        {
            rootDiffs = filterManager.GetRootDiffs(rootFilters, baselineBranch.BranchName);
            var jobNames = rootDiffs.Select(d => d.BaselineJob).ToArray();
            if (TryFindBaseCommit(commits, jobNames, baselineBranch, out baseCommit))
            {
                branchName = baselineBranch.BranchName;
                Log.Information("Using baseline commit {BaseCommit} in {BranchName} branch", baseCommit, baselineBranch.BranchName);
                return true;
            }
        }
        Log.Error("Failed to guess baseline branch");
        rootDiffs = null!;
        branchName = null;
        baseCommit = null;
        return false;
    }
}
