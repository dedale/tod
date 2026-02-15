using Serilog;
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

internal enum OnDemandJobKind
{
    Root,
    Test
}

internal enum TriggerParameter
{
    GitRef,
    BuildSelector // Only build number is supported for now
}

internal sealed record TriggerConfig(OnDemandJobKind JobKind, TriggerParameter Parameter, string Name)
{
    public KeyValuePair<string, string> GetParameter(TriggerParameters triggerParameters)
    {
        switch (Parameter)
        {
            case TriggerParameter.GitRef:
                return new KeyValuePair<string, string>(Name, triggerParameters.Commit.Value);
            case TriggerParameter.BuildSelector:
                if (triggerParameters.UpstreamBuildNumber == null)
                {
                    throw new InvalidOperationException("Upstream build number is required for BuildSelector trigger parameter");
                }
                return new KeyValuePair<string, string>(Name, $"<SpecificBuildSelector><buildNumber>{triggerParameters.UpstreamBuildNumber}</buildNumber></SpecificBuildSelector>");
            default:
                throw new NotSupportedException($"Unsupported trigger parameter: {Parameter}");
        }
    }
}

internal sealed class RootFilter(string name, string pattern)
{
    public static readonly string DefaultChain = string.Empty;

    private readonly Regex _regex = new(pattern, RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));

    public string Name { get; } = name;
    public string Pattern { get; } = pattern;

    public override bool Equals(object? obj)
    {
        return obj is RootFilter filter &&
               Name == filter.Name &&
               Pattern == filter.Pattern;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, Pattern);
    }

    public bool Matches(RootName rootName, [NotNullWhen(true)] out string? chain)
    {
        var match = _regex.Match(rootName.Value);
        if (match.Success)
        {
            chain = match.Groups.Keys.Contains("chain") ? match.Groups["chain"].Value : DefaultChain;
        }
        else
        {
            chain = null;
        }
        return match.Success;
    }
}

internal sealed class TestFilter(string name, string pattern, string group)
{
    private readonly Regex _regex = new(pattern, RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));

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

    public bool Matches(TestName testName, [NotNullWhen(true)] out string? chain)
    {
        var match = _regex.Match(testName.Value);
        if (match.Success)
        {
            chain = match.Groups.Keys.Contains("chain") ? match.Groups["chain"].Value : RootFilter.DefaultChain;
        }
        else
        {
            chain = null;
        }
        return match.Success;
    }
}

internal sealed class MailConfig(string from, string smtpHost)
{
    public string From { get; } = from;
    public string SmtpHost { get; } = smtpHost;
}

internal sealed record LoadThreshold(int QueueSize, TimeSpan MaxRequestDuration);

internal sealed record JobMapping(string OldName, string NewName);

internal sealed record ReferenceReportConfig(
    bool Enabled
);

internal sealed class JenkinsConfig
{
    private static readonly MailConfig s_emptyMailConfig = new(string.Empty, string.Empty);

    private readonly Dictionary<string, RootFilter> _rootFilterByName;
    private readonly Dictionary<string, TestFilter> _testFilterByName;

    public JenkinsConfig(string url)
        : this(url, [], [], [], [], [], [], string.Empty, [], s_emptyMailConfig, null, [], [], null, null, null)
    {
    }

    [JsonConstructor]
    public JenkinsConfig(
        string url,
        string[] multiBranchFolders,
        JobName[] jobNames,
        ReferenceJobConfig[] referenceJobs,
        OnDemandJobConfig[] onDemandJobs,
        TriggerConfig[] triggerConfigs,
        RootFilter[] rootFilters,
        string chainTestGroup,
        TestFilter[] testFilters,
        MailConfig mailConfig,
        int? keptDays,
        LoadThreshold[] loadThresholds,
        JobMapping[] jobMappings,
        int? maxUserActiveRequests,
        string? gerritReviewServer,
        ReferenceReportConfig? referenceReportConfig)
    {
        Url = url;
        MultiBranchFolders = multiBranchFolders;
        JobNames = jobNames;
        ReferenceJobs = referenceJobs;
        OnDemandJobs = onDemandJobs;
        TriggerConfigs = triggerConfigs;
        RootFilters = rootFilters;
        _rootFilterByName = RootFilters.ToDictionary(f => f.Name);
        ChainTestGroup = chainTestGroup;
        TestFilters = testFilters;
        _testFilterByName = testFilters.ToDictionary(f => f.Name);
        MailConfig = mailConfig;
        KeptDays = keptDays;
        LoadThresholds = loadThresholds;
        JobMappings = jobMappings;
        MaxUserActiveRequests = maxUserActiveRequests;
        GerritReviewServer = gerritReviewServer;
        ReferenceReportConfig = referenceReportConfig;
    }

    public static JenkinsConfig New(
        string url,
        string[]? multiBranchFolders = null,
        JobName[]? jobNames = null,
        ReferenceJobConfig[]? referenceJobs = null,
        OnDemandJobConfig[]? onDemandJobs = null,
        TriggerConfig[]? triggerConfigs = null,
        RootFilter[]? rootFilters = null,
        string? chainTestGroup = null,
        TestFilter[]? testFilters = null,
        MailConfig? mailConfig = null,
        int? keptDays = null,
        LoadThreshold[]? loadThresholds = null,
        JobMapping[]? jobMappings = null,
        int? maxUserActiveRequests = null,
        string? gerritReviewServer = null,
        ReferenceReportConfig? referenceReportConfig = null
    )
    {
        return new JenkinsConfig(
            url,
            multiBranchFolders ?? [],
            jobNames ?? [],
            referenceJobs ?? [],
            onDemandJobs ?? [],
            triggerConfigs ?? [],
            rootFilters ?? [],
            chainTestGroup ?? string.Empty,
            testFilters ?? [],
            mailConfig ?? s_emptyMailConfig,
            keptDays ?? null,
            loadThresholds ?? [],
            jobMappings ?? [],
            maxUserActiveRequests ?? null,
            gerritReviewServer ?? null,
            referenceReportConfig ?? null
        );
    }

    public string Url { get; }
    public string[] MultiBranchFolders { get; }
    public JobName[] JobNames { get; }
    public ReferenceJobConfig[] ReferenceJobs { get; }
    public OnDemandJobConfig[] OnDemandJobs { get; }
    public TriggerConfig[] TriggerConfigs { get; }
    public RootFilter[] RootFilters { get; }
    public string ChainTestGroup { get; }
    public TestFilter[] TestFilters { get; }
    public MailConfig MailConfig { get; }
    public int? KeptDays { get; }
    public LoadThreshold[] LoadThresholds { get; }
    public JobMapping[] JobMappings { get; }
    public int? MaxUserActiveRequests { get; }
    public string? GerritReviewServer { get; }
    public ReferenceReportConfig? ReferenceReportConfig { get; }

    public bool TryGetRootFilter(string name, [NotNullWhen(true)] out RootFilter? filter)
    {
        return _rootFilterByName.TryGetValue(name, out filter);
    }

    public bool TryGetTestFilter(string name, [NotNullWhen(true)] out TestFilter? filter)
    {
        return _testFilterByName.TryGetValue(name, out filter);
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

    public void SaveJobs(string configPath, JobName[] jobNames)
    {
        Log.Debug("Saving {JobCount} jobs to config", jobNames.Length);
        var newConfig = new JenkinsConfig(
            url: Url,
            multiBranchFolders: MultiBranchFolders,
            jobNames: jobNames,
            referenceJobs: ReferenceJobs,
            onDemandJobs: OnDemandJobs,
            triggerConfigs: TriggerConfigs,
            rootFilters: RootFilters,
            chainTestGroup: ChainTestGroup,
            testFilters: TestFilters,
            mailConfig: MailConfig,
            keptDays: KeptDays,
            loadThresholds: LoadThresholds,
            jobMappings: JobMappings,
            maxUserActiveRequests: MaxUserActiveRequests,
            gerritReviewServer: GerritReviewServer,
            referenceReportConfig: ReferenceReportConfig
        );
        newConfig.Save(configPath);
    }
}
