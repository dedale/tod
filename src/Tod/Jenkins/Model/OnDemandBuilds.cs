using System.Diagnostics.CodeAnalysis;

namespace Tod.Jenkins;

internal sealed class OnDemandBuilds(BuildCollections<RootBuild> rootBuilds, BuildCollections<TestBuild> testBuilds) : IBuildChains
{
    public OnDemandBuilds(IOnDemandStore onDemandStore)
        : this(new BuildCollections<RootBuild>(onDemandStore.RootStore), new BuildCollections<TestBuild>(onDemandStore.TestStore))
    {
    }

    public OnDemandBuilds(IEnumerable<JobName> rootJobs, IOnDemandStore onDemandStore)
        : this(new BuildCollections<RootBuild>(rootJobs, onDemandStore.RootStore), new BuildCollections<TestBuild>(onDemandStore.TestStore))
    {
        foreach (var rootJob in rootJobs)
        {
            onDemandStore.RootStore.Add(rootJob);
        }
    }

    public BuildCollections<RootBuild> RootBuilds { get; } = rootBuilds;
    public BuildCollections<TestBuild> TestBuilds { get; } = testBuilds;

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
            if (candidate.RootBuilds.Any(r => r.JobName.Equals(rootBuild.JobName) && r.Equals(rootBuild)))
            {
                testBuild = candidate;
                return true;
            }
        }
        return false;
    }

    public bool TryGetRootBuild(BuildReference buildReference, [NotNullWhen(true)] out RootBuild? rootBuild)
    {
        return RootBuilds[buildReference.JobName].TryGetBuild(buildReference, out rootBuild);
    }
}
