using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Tod.Git;

namespace Tod.Jenkins;

/*Jenkins job graph:
 * - api/json: jobs: list of jobs{name}
 * - pipeline (can be in another repo): copyArtifacts = dependency
 * - easy workaround: name of root job and patterns for test jobs in config file
 */
/* Recycle root builds for requests only if same commit and successful
 */
/* Use case: run a new request with some filters
 * - Check if there is a successful root build for the same commit in the reference branch
 * - If yes, reuse it; if not, trigger a new root build and wait for it to complete
 * - For each test job pattern, check if there is a successful test build for the same root build
 * - If yes, reuse it; if not, trigger a new test build and wait for it to complete
 */
/* Keep running client or rely on a scheduled agent
 */

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
internal sealed record JobName(string Value) : IComparable<JobName>
{
    private readonly string _urlPath = $"job/{string.Join("/job/", Value.Split('/'))}";

    public string UrlPath => _urlPath;

    public int CompareTo(JobName? other)
    {
        return string.Compare(Value, other?.Value, StringComparison.Ordinal);
    }

    public override string ToString()
    {
        return Value;
    }

    public KeyValuePair<string, object?> Tag => KeyValuePair.Create<string, object?>(nameof(JobName), this);
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
internal sealed record BuildReference(JobName JobName, int BuildNumber) : IComparable<BuildReference>
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

    public override string ToString()
    {
        return $"{JobName} #{BuildNumber}";
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

    public BuildReference Reference => _reference;
}

internal sealed class RootBuild : BaseBuild
{
    public RootBuild(JobName jobName, string id, int buildNumber, DateTime startTimeUtc, DateTime endTimeUtc, bool isSuccessful, Sha1[] commits, BuildReference[] triggered)
        : base(jobName, id, buildNumber, startTimeUtc, endTimeUtc, isSuccessful)
    {
        Commits = commits;
        Triggered = triggered;
    }

    public Sha1[] Commits { get; }
    public BuildReference[] Triggered { get; }

    [ExcludeFromCodeCoverage]
    public override string ToString()
    {
        return Reference.ToString();
    }
}

internal sealed class TestBuild : BaseBuild
{
    public TestBuild(JobName jobName, string id, int buildNumber, DateTime startTimeUtc, DateTime endTimeUtc, bool isSuccessful, BuildReference rootBuild, FailedTest[] failedTests)
        : base(jobName, id, buildNumber, startTimeUtc, endTimeUtc, isSuccessful)
    {
        RootBuild = rootBuild;
        FailedTests = failedTests;
    }

    public BuildReference RootBuild { get; }
    public FailedTest[] FailedTests { get; }

    [ExcludeFromCodeCoverage]
    public override string ToString()
    {
        return Reference.ToString();
    }
}

internal interface IWithCustomSerialization<TSerializable>
{
    TSerializable ToSerializable();
}

internal interface ICustomSerializable<TCustom>
{
    TCustom FromSerializable();
}
