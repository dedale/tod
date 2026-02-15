using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Tod.Git;

namespace Tod.Jenkins;

internal static class DictionaryExtensions
{
    public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, Func<TKey, TValue> valueFactory)
    {
        if (dict.TryGetValue(key, out var existing))
        {
            return existing;
        }
        var value = valueFactory(key);
        dict.Add(key, value);
        return value;
    }

    public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue value) => dict.GetOrAdd(key, _ => value);
}

[DebuggerStepThrough]
[JsonConverter(typeof(SingleStringValueConverterFactory))]
internal sealed record JobName(string Value) : IComparable<JobName>, IEquatable<JobName>
{
    private static JobMapping[] s_jobMappings = [];

    private readonly string _urlPath = $"job/{string.Join("/job/", Value.Split('/'))}";

    public string UrlPath => _urlPath;

    public int CompareTo(JobName? other)
    {
        return string.Compare(Value, other?.Value, StringComparison.Ordinal);
    }

    [ExcludeFromCodeCoverage]
    public override string ToString()
    {
        return Value;
    }

    public KeyValuePair<string, object?> Tag => KeyValuePair.Create<string, object?>(nameof(JobName), this);

    private static string FixJobName(string name, JobMapping[] jobMappings)
    {
        return jobMappings
            .Aggregate(name, (current, mapping) => current.Replace(mapping.OldName, mapping.NewName, StringComparison.Ordinal));
    }

    internal bool Equals(JobName? other, JobMapping[]? jobMappings)
    {
        if (other is null)
        {
            return false;
        }
        jobMappings ??= s_jobMappings;
        return Value.Equals(other.Value, StringComparison.Ordinal)
            || FixJobName(Value, jobMappings).Equals(FixJobName(other.Value, jobMappings), StringComparison.Ordinal);
    }

    public bool Equals(JobName? other)
    {
        return Equals(other, null);
    }

    internal int GetHashCode(JobMapping[]? jobMappings)
    {
        return FixJobName(Value, jobMappings ?? s_jobMappings).GetHashCode(StringComparison.Ordinal);
    }

    public override int GetHashCode()
    {
        return GetHashCode(null);
    }

    [ExcludeFromCodeCoverage]
    public static void Init(JobMapping[]? jobMappings)
    {
        s_jobMappings = jobMappings ?? [];
    }
}

[DebuggerStepThrough]
[JsonConverter(typeof(SingleStringValueConverterFactory))]
internal sealed record BranchName(string Value) : IComparable<BranchName>
{
    public int CompareTo(BranchName? other)
    {
        return string.Compare(Value, other?.Value, StringComparison.Ordinal);
    }

    public override string ToString()
    {
        return Value;
    }
}

[DebuggerStepThrough]
[JsonConverter(typeof(SingleStringValueConverterFactory))]
internal sealed record RootName(string Value)
{
    [ExcludeFromCodeCoverage]
    public override string ToString()
    {
        return Value;
    }
}

[DebuggerStepThrough]
[JsonConverter(typeof(SingleStringValueConverterFactory))]
internal sealed record TestName(string Value) : IComparable<TestName>
{
    public int CompareTo(TestName? other)
    {
        return string.Compare(Value, other?.Value, StringComparison.Ordinal);
    }

    [ExcludeFromCodeCoverage]
    public override string ToString()
    {
        return Value;
    }
}

[method: JsonConstructor]
internal sealed record BuildReference(JobName JobName, int BuildNumber) : IComparable<BuildReference>, IEquatable<BuildReference>
{
    public BuildReference(string jobName, int buildBumber)
        : this(new JobName(jobName), buildBumber)
    {
    }

    public int CompareTo(BuildReference? other)
    {
        if (other is null)
        {
            return 1;
        }
        var c = JobName.CompareTo(other.JobName);
        if (c != 0)
        {
            return c;
        }
        return BuildNumber.CompareTo(other.BuildNumber);
    }

    public BuildReference Next() => new(JobName, BuildNumber + 1);

    internal bool Equals(BuildReference? other, JobMapping[]? jobMappings)
    {
        return ReferenceEquals(this, other)
            || (other is not null && JobName.Equals(other.JobName, jobMappings) && BuildNumber == other.BuildNumber);
    }

    public bool Equals(BuildReference? other)
    {
        return Equals(other, null);
    }

    internal int GetHashCode(JobMapping[]? jobMappings)
    {
        return HashCode.Combine(JobName.GetHashCode(jobMappings), BuildNumber);
    }

    public override int GetHashCode()
    {
        return GetHashCode(null);
    }

    [ExcludeFromCodeCoverage]
    public override string ToString()
    {
        return $"{JobName} #{BuildNumber}";
    }
}

internal sealed class BuildResultInfo
{
    public string Value { get; }
    public bool IsSuccess { get; }

    private BuildResultInfo(string value, bool isSuccess)
    {
        Value = value;
        IsSuccess = isSuccess;
    }

    public static BuildResultInfo Success(string value)
    {
        return new BuildResultInfo(value, true);
    }

    public static BuildResultInfo Failure(string value)
    {
        return new BuildResultInfo(value, false);
    }

    [ExcludeFromCodeCoverage]
    public override string ToString()
    {
        return Value;
    }
}

internal abstract class BaseBuild(JobName jobName, string id, int buildNumber, DateTime startTimeUtc, DateTime endTimeUtc, bool isSuccessful)
{
    private readonly BuildReference _reference = new(jobName, buildNumber);

    public JobName JobName { get; } = jobName;
    public string Id { get; } = id;
    public int BuildNumber { get; } = buildNumber;
    public DateTime StartTimeUtc { get; } = startTimeUtc;
    public DateTime EndTimeUtc { get; } = endTimeUtc;
    public bool IsSuccessful { get; } = isSuccessful;

    [JsonIgnore]
    public BuildReference Reference => _reference;
}

internal sealed class RootBuild(JobName jobName, string id, int buildNumber, DateTime startTimeUtc, DateTime endTimeUtc, bool isSuccessful, Sha1[] commits, JobName[] scheduled, CommitAuthor[]? commitAuthors = null)
    : BaseBuild(jobName, id, buildNumber, startTimeUtc, endTimeUtc, isSuccessful)
{
    public Sha1[] Commits { get; } = commits;
    public JobName[] Scheduled { get; } = scheduled;
    public CommitAuthor[] CommitAuthors { get; } = commitAuthors ?? [];

    [ExcludeFromCodeCoverage]
    public override string ToString()
    {
        return Reference.ToString();
    }
}

[method: JsonConstructor]
internal sealed class TestBuild(JobName jobName, string id, int buildNumber, DateTime startTimeUtc, DateTime endTimeUtc, bool isSuccessful, BuildReference[] rootBuilds, FailedTest[] failedTests)
    : BaseBuild(jobName, id, buildNumber, startTimeUtc, endTimeUtc, isSuccessful)
{
    public TestBuild(JobName jobName, string id, int buildNumber, DateTime startTimeUtc, DateTime endTimeUtc, bool isSuccessful, BuildReference rootBuild, FailedTest[] failedTests)
        : this(jobName, id, buildNumber, startTimeUtc, endTimeUtc, isSuccessful, [rootBuild], failedTests)
    {
    }

    public BuildReference[] RootBuilds { get; } = rootBuilds;
    public FailedTest[] FailedTests { get; } = failedTests;

    [ExcludeFromCodeCoverage]
    public override string ToString()
    {
        return Reference.ToString();
    }
}

internal sealed class TestBuildData(int failCount, BuildReference[] upstreamBuilds)
{
    public int FailCount { get; } = failCount;
    public BuildReference[] UpstreamBuilds { get; } = upstreamBuilds;
}

internal interface IWithCustomSerialization<TSerializable>
{
    TSerializable ToSerializable();
}

internal interface ICustomSerializable<TCustom>
{
    TCustom FromSerializable();
}
