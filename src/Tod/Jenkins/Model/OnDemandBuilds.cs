using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Tod.Jenkins;

internal sealed class OnDemandBuilds(BuildCollections<RootBuild> rootBuilds, BuildCollections<TestBuild> testBuilds)
{
    private OnDemandBuilds(BuildCollection<RootBuild> rootBuilds, BuildCollections<TestBuild> testBuilds)
        : this(new BuildCollections<RootBuild>([rootBuilds]), testBuilds)
    {
    }

    public OnDemandBuilds(List<BuildCollection<RootBuild>> rootBuilds, List<BuildCollection<TestBuild>> testBuilds)
        : this(new BuildCollections<RootBuild>(rootBuilds), new BuildCollections<TestBuild>(testBuilds))
    {
    }

    public OnDemandBuilds(BuildCollection<RootBuild> rootBuilds, List<BuildCollection<TestBuild>> testBuilds)
        : this(rootBuilds, new BuildCollections<TestBuild>(testBuilds))
    {
    }

    public OnDemandBuilds(JobName rootJobName)
        : this(new BuildCollection<RootBuild>(rootJobName), new BuildCollections<TestBuild>())
    {
    }

    public BuildCollections<RootBuild> RootBuilds { get; } = rootBuilds;
    public BuildCollections<TestBuild> TestBuilds { get; } = testBuilds;

    public bool TryAdd(RootBuild rootBuild)
    {
        if (RootBuilds[rootBuild.JobName].TryAdd(rootBuild))
        {
            foreach (var triggered in rootBuild.Triggered)
            {
                TestBuilds.GetOrAdd(triggered.JobName);
            }
            return true;
        }
        return false;
    }

    public void TryAdd(JobName testJobName)
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
            if (candidate.RootBuild.JobName.Equals(rootBuild.JobName) && candidate.RootBuild.Equals(rootBuild))
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

    public TestBuild GetTestBuild(BuildReference buildReference)
    {
        return TestBuilds[buildReference.JobName][buildReference];
    }

    [method: JsonConstructor]
    internal sealed class Serializable(List<BuildCollection<RootBuild>.Serializable> rootBuilds, List<BuildCollection<TestBuild>.Serializable> testBuilds)
    {
        public Serializable(OnDemandBuilds onDemandBuilds)
            : this(
                [.. onDemandBuilds.RootBuilds.Select(x => new BuildCollection<RootBuild>.Serializable(x))],
                [.. onDemandBuilds.TestBuilds.Select(x => new BuildCollection<TestBuild>.Serializable(x))])
        {
        }

        public List<BuildCollection<RootBuild>.Serializable> RootBuilds { get; set; } = rootBuilds;
        public List<BuildCollection<TestBuild>.Serializable> TestBuilds { get; set; } = testBuilds;

        public OnDemandBuilds FromSerializable()
        {
            var rootBuildCollection = RootBuilds.Select(x => x.ToBuildCollection()).ToList();
            var testBuildCollection = TestBuilds.Select(x => x.ToBuildCollection()).ToList();
            return new OnDemandBuilds(rootBuildCollection, testBuildCollection);
        }
    }

    public Serializable ToSerializable()
    {
        return new Serializable(this);
    }
}
