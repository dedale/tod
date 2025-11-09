using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Tod.Jenkins;

/// For test jobs, Pattern must contain a group named 'test'
internal sealed record ReferenceJobConfig(string Pattern, BranchName BranchName, bool IsRoot);

/// For test jobs, Pattern must contain a group named 'test'
internal sealed record OnDemandJobConfig(string Pattern, bool IsRoot);

internal sealed class TestFilter(string name, string pattern, string group)
{
    private readonly Regex _regex = new(pattern);

    public string Name { get; } = name;
    public string Pattern { get; } = pattern;
    public string Group { get; } = group;

    public override bool Equals(object? obj)
    {
        return obj is TestFilter filter &&
               Name == filter.Name &&
               Pattern == filter.Pattern &&
               Group == filter.Group;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, Pattern, Group);
    }

    public bool Matches(TestName testName)
    {
        return _regex.IsMatch(testName.Value);
    }
}

internal sealed class JenkinsConfig
{
    private readonly Dictionary<string, TestFilter> _filtersByName;

    public JenkinsConfig(string url)
        : this(url, [], [], [], [], [])
    {
    }

    [JsonConstructor]
    public JenkinsConfig(string url, string[] multiBranchFolders, JobName[] jobNames, ReferenceJobConfig[] referenceJobs, OnDemandJobConfig[] onDemandJobs, TestFilter[] filters)
    {
        Url = url;
        MultiBranchFolders = multiBranchFolders;
        JobNames = jobNames;
        ReferenceJobs = referenceJobs;
        OnDemandJobs = onDemandJobs;
        Filters = filters;
        _filtersByName = filters.ToDictionary(f => f.Name);
    }

    public static JenkinsConfig New(
        string url,
        string[]? multiBranchFolders = null,
        JobName[]? jobNames = null,
        ReferenceJobConfig[]? referenceJobs = null,
        OnDemandJobConfig[]? onDemandJobs = null,
        TestFilter[]? filters = null
    )
    {
        return new JenkinsConfig(
            url,
            multiBranchFolders ?? [],
            jobNames ?? [],
            referenceJobs ?? [],
            onDemandJobs ?? [],
            filters ?? []
        );
    }

    public string Url { get; }
    public string[] MultiBranchFolders { get; }
    public JobName[] JobNames { get; }
    public ReferenceJobConfig[] ReferenceJobs { get; }
    public OnDemandJobConfig[] OnDemandJobs { get; }
    public TestFilter[] Filters { get; }

    public bool TryGetFilter(string name, [NotNullWhen(true)] out TestFilter? filter)
    {
        return _filtersByName.TryGetValue(name, out filter);
    }

    private static readonly JsonSerializerOptions s_jsonOptions = GetJsonOptions();

    private static JsonSerializerOptions GetJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new SingleStringValueConverterFactory());
        return options;
    }

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, s_jsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    public static JenkinsConfig Load(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        var config = JsonSerializer.Deserialize<JenkinsConfig>(json, s_jsonOptions);
        if (config == null)
        {
            throw new InvalidOperationException($"Cannot deserialize config from '{path}'");
        }
        return config;
    }
}
