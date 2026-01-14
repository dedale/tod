namespace Tod.Jenkins;

internal interface IBuildChains
{
    BuildCollections<RootBuild> RootBuilds { get; }
    BuildCollections<TestBuild> TestBuilds { get; }
}

internal static class IBuildChainsExtensions
{
    public static void TryAddRoot(this IBuildChains chains, JobName rootJobName)
    {
        chains.RootBuilds.GetOrAdd(rootJobName);
    }

    public static void RemoveRoot(this IBuildChains chains, JobName rootJobName)
    {
        chains.RootBuilds.Remove(rootJobName);
    }

    public static bool TryAdd(this IBuildChains chains, RootBuild rootBuild)
    {
        if (chains.RootBuilds[rootBuild.JobName].TryAdd(rootBuild))
        {
            foreach (var scheduled in rootBuild.Scheduled)
            {
                chains.TestBuilds.GetOrAdd(scheduled);
            }
            return true;
        }
        return false;
    }

    public static void TryAddTest(this IBuildChains chains, JobName testJobName)
    {
        chains.TestBuilds.GetOrAdd(testJobName);
    }

    public static void RemoveTest(this IBuildChains chains, JobName testJobName)
    {
        chains.TestBuilds.Remove(testJobName);
    }

    public static bool TryAdd(this IBuildChains chains, TestBuild testBuild)
    {
        return chains.TestBuilds.GetOrAdd(testBuild.JobName).TryAdd(testBuild);
    }

    public static TestBuild GetTestBuild(this IBuildChains chains, BuildReference buildReference)
    {
        return chains.TestBuilds.GetOrAdd(buildReference.JobName)[buildReference];
    }

    public static int RemoveBuildsOlderThan(this IBuildChains chains, DateTime thresholdUtc)
    {
        var removed = 0;
        foreach (var rootBuilds in chains.RootBuilds)
        {
            removed += rootBuilds.RemoveBuildsOlderThan(thresholdUtc);
        }
        foreach (var testBuilds in chains.TestBuilds)
        {
            removed += testBuilds.RemoveBuildsOlderThan(thresholdUtc);
        }
        return removed;
    }
}
