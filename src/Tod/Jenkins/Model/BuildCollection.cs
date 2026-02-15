using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Tod.Core;

namespace Tod.Jenkins;

internal sealed class BuildCollection<T>(JobName jobName, IByJobNameStore byJobNameStore) : IEnumerable<T> where T : BaseBuild
{
    private readonly Lazy<InnerCollection> _innerCollection = new(() => InnerCollection.Load(jobName, byJobNameStore));

    public bool Contains(int buildNumber) => _innerCollection.Value.Contains(buildNumber);

    public bool TryAdd(T build, bool sorted = true) => _innerCollection.Value.TryAdd(build, sorted);

    public int RemoveBuildsOlderThan(DateTime thresholdUtc) => _innerCollection.Value.RemoveBuildsOlderThan(thresholdUtc);

    public IEnumerator<T> GetEnumerator()
    {
        return _innerCollection.Value.GetEnumerator();
    }

    [ExcludeFromCodeCoverage]
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public JobName JobName { get; } = jobName;

    public int Count => _innerCollection.Value.Count;

    public T this[int index] => _innerCollection.Value[index];

    private TimeSpan? _averageDuration = null;
    public TimeSpan AverageDuration
    {
        get
        {
            if (!_averageDuration.HasValue)
            {
                if (_innerCollection.Value.Count == 0)
                {
                    _averageDuration = TimeSpan.Zero;
                }
                else
                {
                    var totalTicks = _innerCollection.Value.Sum(b => (b.EndTimeUtc - b.StartTimeUtc).Ticks);
                    _averageDuration = TimeSpan.FromTicks(totalTicks / _innerCollection.Value.Count);
                }
            }
            return _averageDuration.Value;
        }
    }

    public bool TryGetBuild(BuildReference reference, [NotNullWhen(true)] out T? build) => _innerCollection.Value.TryGetBuild(reference, out build);

    public T this[BuildReference buildReference] => _innerCollection.Value[buildReference];

    // Cannot be private for Mock setup
    internal sealed class InnerCollection : IEnumerable<T>
    {
        [method: JsonConstructor]
        internal sealed class Serializable(JobName jobName, List<T> builds)
        {
            public Serializable(InnerCollection buildCollection)
                : this(buildCollection.JobName, [.. buildCollection])
            {
            }

            public JobName JobName { get; set; } = jobName;
            public List<T> Builds { get; set; } = builds;

            public InnerCollection ToInnerCollection(IByJobNameStore byJobNameStore)
            {
                return new InnerCollection(JobName, Builds, Save);

                void Save(InnerCollection items)
                {
                    Telemetry.BuildsSaved.Add(items.Count, byJobNameStore.BuildBranch.Tag, JobName.Tag);

                    byJobNameStore.Save(JobName, new Serializable(items));
                }
            }
        }

        public static InnerCollection Load(JobName jobName, IByJobNameStore byJobNameStore)
        {
            var serializable = byJobNameStore.Load(jobName, () => new Serializable(jobName, []));

            Telemetry.BuildsLoaded.Add(serializable.Builds.Count, byJobNameStore.BuildBranch.Tag, jobName.Tag);

            return serializable.ToInnerCollection(byJobNameStore);
        }

        private readonly JobName _jobName;
        private readonly List<T> _builds;
        private readonly HashSet<int> _buildNumbers;
        private readonly Dictionary<int, T> _byNumber;
        private readonly Action _save;

        private InnerCollection(JobName jobName, List<T> builds, Action<InnerCollection> save)
        {
            _jobName = jobName;
            _builds = builds;
            _buildNumbers = [.. builds.Select(b => b.BuildNumber)];
            _byNumber = builds.DistinctBy(b => b.BuildNumber).ToDictionary(b => b.BuildNumber);
            _save = () => save(this);
        }

        public bool Contains(int buildNumber)
        {
            return _buildNumbers.Contains(buildNumber);
        }

        public bool TryAdd(T build, bool sorted = true)
        {
            if (build.JobName != _jobName)
            {
                throw new ArgumentException($"Build job name '{build.JobName}' does not match collection job name '{_jobName}'.", nameof(build));
            }
            if (_buildNumbers.Add(build.BuildNumber))
            {
                if (sorted && _builds.Count > 0 && _builds[^1].BuildNumber > build.BuildNumber)
                {
                    throw new InvalidOperationException("Builds must be added in ascending order by build number.");
                }
                _builds.Add(build);
                _byNumber.Add(build.BuildNumber, build);
                _save();
                return true;
            }
            return false;
        }

        public int RemoveBuildsOlderThan(DateTime thresholdUtc)
        {
            var removed = 0;
            for (var i = 0; i < _builds.Count;)
            {
                if (_builds[i].EndTimeUtc < thresholdUtc)
                {
                    removed++;
                    var buildNumber = _builds[i].BuildNumber;
                    _builds.RemoveAt(i);
                    _buildNumbers.Remove(buildNumber);
                    _byNumber.Remove(buildNumber);
                }
                else
                {
                    // Builds are in ascending order by end time, so we can stop here
                    break;
                }
            }
            if (removed > 0)
            {
                _save();
            }
            return removed;
        }

        public JobName JobName => _jobName;

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
                if (buildReference.JobName != _jobName)
                {
                    throw new ArgumentException($"Build job name '{buildReference.JobName}' does not match collection job name '{_jobName}'.", nameof(buildReference));
                }
                return _byNumber[buildReference.BuildNumber];
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _builds.GetEnumerator();
        }

        [ExcludeFromCodeCoverage]
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}

internal sealed class BuildCollections<T> : IEnumerable<BuildCollection<T>> where T : BaseBuild
{
    private readonly List<BuildCollection<T>> _collections;
    private readonly Dictionary<JobName, BuildCollection<T>> _byJobName;
    private readonly IByJobNameStore _byJobNameStore;

    private static BuildCollection<T> NewCollection(JobName jobName, IByJobNameStore byJobNameStore)
    {
        return new BuildCollection<T>(jobName, byJobNameStore);
    }

    public BuildCollections(IByJobNameStore byJobNameStore)
        : this(byJobNameStore.JobNames, byJobNameStore)
    {
    }

    public BuildCollections(IEnumerable<JobName> jobNames, IByJobNameStore byJobNameStore)
    {
        _collections = [.. jobNames.Select(j => NewCollection(j, byJobNameStore))];
        _byJobName = _collections.ToDictionary(c => c.JobName);
        _byJobNameStore = byJobNameStore;
    }

    public bool Contains(JobName jobName) => _byJobName.ContainsKey(jobName);

    public BuildCollection<T> GetOrAdd(JobName jobName)
    {
        if (!_byJobName.TryGetValue(jobName, out var collection))
        {
            collection = NewCollection(jobName, _byJobNameStore);
            _collections.Add(collection);
            _byJobName.Add(jobName, collection);
            _byJobNameStore.Add(jobName);
        }
        return collection;
    }

    public void Remove(JobName jobName)
    {
        if (_byJobName.ContainsKey(jobName))
        {
            var i = Enumerable.Range(0, _collections.Count).First(index => _collections[index].JobName == jobName);
            _collections.RemoveAt(i);
            _byJobName.Remove(jobName);
            _byJobNameStore.Remove(jobName);
        }
    }

    public BuildCollection<T> this[int i] => _collections[i];

    // for testing
    public int Count => _collections.Count;

    public BuildCollection<T> this[JobName jobName] => GetOrAdd(jobName);

    public IEnumerator<BuildCollection<T>> GetEnumerator()
    {
        return _collections.GetEnumerator();
    }

    [ExcludeFromCodeCoverage]
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
