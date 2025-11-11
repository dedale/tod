using Serilog;
using System.Diagnostics.CodeAnalysis;
using Tod.Git;

namespace Tod.Jenkins;

internal sealed class BranchReference
{
    public BranchReference(IReferenceStore referenceStore)
        : this(referenceStore.Branch, referenceStore.RootStore, referenceStore.TestStore)
    {
    }

    private BranchReference(BranchName branchName, IByJobNameStore rootStore, IByJobNameStore testStore)
        : this(branchName, new BuildCollections<RootBuild>(rootStore), new BuildCollections<TestBuild>(testStore))
    {
    }

    private BranchReference(BranchName branchName, BuildCollections<RootBuild> rootBuilds, BuildCollections<TestBuild> testBuilds)
    {
        BranchName = branchName;
        RootBuilds = rootBuilds;
        TestBuilds = testBuilds;
    }

    public BranchName BranchName { get; }
    public BuildCollections<RootBuild> RootBuilds { get; }
    public BuildCollections<TestBuild> TestBuilds { get; }

    public void TryAddRoot(JobName rootJobName)
    {
        RootBuilds.GetOrAdd(rootJobName);
    }

    public bool TryAdd(RootBuild rootBuild)
    {
        if (RootBuilds.GetOrAdd(rootBuild.JobName).TryAdd(rootBuild))
        {
            foreach (var triggered in rootBuild.Triggered)
            {
                TestBuilds.GetOrAdd(triggered.JobName);
            }
            return true;
        }
        return false;
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

    public void TryAddTest(JobName testJobName)
    {
        TestBuilds.GetOrAdd(testJobName);
    }

    public bool TryAdd(TestBuild testBuild)
    {
        return TestBuilds.GetOrAdd(testBuild.JobName).TryAdd(testBuild);
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
            if (candidate.RootBuild.JobName.Equals(rootBuild.JobName) && candidate.RootBuild.CompareTo(rootBuild) >= 0)
            {
                testBuild = candidate;
                return true;
            }
        }
        return false;
    }

    public TestBuild GetTestBuild(BuildReference buildReference)
    {
        return TestBuilds.GetOrAdd(buildReference.JobName)[buildReference];
    }
}

internal static class BranchReferenceExtensions
{
    private static bool TryFindRefCommit(Sha1[] commits, JobName[] jobNames, BranchReference branchReference, [NotNullWhen(true)] out Sha1? refCommit)
    {
        if (commits.Length > 0)
        {
            var candidates = new List<IEnumerable<Sha1>>();
            foreach (var jobName in jobNames)
            {
                if (branchReference.TryFindRootBuildByCommit(commits.First(), jobName, out _))
                {
                    throw new NotSupportedException($"No local commits to test for job {jobName}");
                }
                for (var i = 1; i < commits.Length; i++)
                {
                    if (branchReference.TryFindRootBuildByCommit(commits[i], jobName, out _))
                    {
                        if (jobNames.Length > 1)
                        {
                            Log.Information("Found candidate reference commit {Commit} for job {JobName}", commits[i], jobName);
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
        this IEnumerable<BranchReference> branchReferences,
        Sha1[] commits,
        JobName[] jobNames,
        BranchName wantedBranch,
        [NotNullWhen(true)] out Sha1? refCommit)
    {
        refCommit = null;
        var branchReference = branchReferences.FirstOrDefault(br => br.BranchName == wantedBranch);
        if (branchReference == null)
        {
            Log.Error("Branch {BranchName} not found in workspace", wantedBranch);
            return false;
        }
        if (!TryFindRefCommit(commits, jobNames, branchReference, out refCommit))
        {
            Log.Error("Cannot find reference commit in {BranchName} branch history", wantedBranch);
            return false;
        }
        Log.Information("Using reference commit {RefCommit}", refCommit);
        return true;
    }

    public static bool TryGuessBranch(
        this IEnumerable<BranchReference> branchReferences,
        Sha1[] commits,
        RootName[] rootNames,
        IFilterManager filterManager,
        [NotNullWhen(true)] out RootDiff[] rootDiffs,
        [NotNullWhen(true)] out BranchName? branchName,
        [NotNullWhen(true)] out Sha1? refCommit)
    {
        Log.Information("No branch specified, guessing...");
        foreach (var branchReference in branchReferences)
        {
            rootDiffs = filterManager.GetRootDiffs(rootNames, branchReference.BranchName);
            var jobNames = rootDiffs.Select(d => d.ReferenceJob).ToArray();
            if (TryFindRefCommit(commits, jobNames, branchReference, out refCommit))
            {
                branchName = branchReference.BranchName;
                Log.Information("Using reference commit {RefCommit} in {BranchName} branch", refCommit, branchReference.BranchName);
                return true;
            }
        }
        Log.Error("Failed to guess reference branch");
        rootDiffs = null!;
        branchName = null;
        refCommit = null;
        return false;
    }
}
