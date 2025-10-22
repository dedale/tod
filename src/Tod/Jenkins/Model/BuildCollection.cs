using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Tod.Jenkins;

internal sealed class BuildCollection<T> : IEnumerable<T> where T : BaseBuild
{
    [method: JsonConstructor]
    internal sealed class Serializable(JobName jobName, List<T> builds)
    {
        public Serializable(BuildCollection<T> buildCollection)
            : this(buildCollection.JobName, [.. buildCollection])
        {
        }

        public JobName JobName { get; set; } = jobName;
        public List<T> Builds { get; set; } = builds;
        public BuildCollection<T> ToBuildCollection()
        {
            return new BuildCollection<T>(JobName, Builds);
        }
    }

    private readonly List<T> _builds;
    private readonly HashSet<int> _buildNumbers;
    private readonly JobName jobName;
    private readonly Dictionary<int, T> _byNumber;

    private BuildCollection(JobName jobName, List<T> builds)
    {
        this.jobName = jobName;
        _builds = builds;
        _buildNumbers = [.. builds.Select(b => b.BuildNumber)];
        _byNumber = builds.DistinctBy(b => b.BuildNumber).ToDictionary(b => b.BuildNumber);
    }

    public BuildCollection(JobName jobName)
        : this(jobName, [])
    {
    }

    public BuildCollection(JobName jobName, IEnumerable<T> builds)
        : this(jobName, [.. builds])
    {
        if (_builds.Count != _buildNumbers.Count)
        {
            throw new ArgumentException("Duplicate builds in the initial collection.", nameof(builds));
        }
    }

    public bool Contains(int buildNumber)
    {
        return _buildNumbers.Contains(buildNumber);
    }

    public bool TryAdd(T build)
    {
        if (build.JobName != jobName)
        {
            throw new ArgumentException($"Build job name '{build.JobName}' does not match collection job name '{jobName}'.", nameof(build));
        }
        if (_buildNumbers.Add(build.BuildNumber))
        {
            if (_builds.Count > 0 && _builds[^1].BuildNumber > build.BuildNumber)
            {
                throw new InvalidOperationException("Builds must be added in ascending order by build number.");
            }
            _builds.Add(build);
            _byNumber.Add(build.BuildNumber, build);
            return true;
        }
        return false;
    }

    public IEnumerator<T> GetEnumerator()
    {
        return _builds.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public JobName JobName => jobName;

    public int Count => _builds.Count;

    public T this[int index] => _builds[index];

    public bool TryGetBuild(BuildReference reference, [NotNullWhen(true)] out T? build)
    {
        return _byNumber.TryGetValue(reference.BuildNumber, out build);
    }

    public T this[BuildReference buildReference]
    {
        get
        {
            if (buildReference.JobName != jobName)
            {
                throw new ArgumentException($"Build job name '{buildReference.JobName}' does not match collection job name '{jobName}'.", nameof(buildReference));
            }
            return _byNumber[buildReference.BuildNumber];
        }
    }
}

internal sealed class BuildCollections<T> : IEnumerable<BuildCollection<T>> where T : BaseBuild
{
    private readonly List<BuildCollection<T>> _collections;
    private readonly Dictionary<JobName, BuildCollection<T>> _byJobName;

    private BuildCollections(List<BuildCollection<T>> collections)
        : this(collections, collections.ToDictionary(c => c.JobName))
    {
    }

    private BuildCollections(List<BuildCollection<T>> collections, Dictionary<JobName, BuildCollection<T>> byJobName)
    {
        _collections = collections;
        _byJobName = byJobName;
    }

    public BuildCollections(IEnumerable<BuildCollection<T>> collections)
        : this([.. collections])
    {
    }

    public BuildCollections()
        : this([], [])
    {
    }

    public IEnumerator<BuildCollection<T>> GetEnumerator()
    {
        return _collections.GetEnumerator();
    }

    [ExcludeFromCodeCoverage]
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public BuildCollection<T> GetOrAdd(JobName jobName)
    {
        if (!_byJobName.TryGetValue(jobName, out var collection))
        {
            collection = new BuildCollection<T>(jobName);
            _collections.Add(collection);
            _byJobName.Add(jobName, collection);
        }
        return collection;
    }

    public BuildCollection<T> this[int i] => _collections[i];

    public int Count => _collections.Count;

    public BuildCollection<T> this[JobName jobName] => GetOrAdd(jobName);
}