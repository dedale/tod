using Serilog;

namespace Tod.Jenkins;

internal sealed class FilterJobs(TestFilter filter, Dictionary<JobName, TimeSpan> durationByJob)
{
    public TestFilter Filter { get; } = filter;
    public IEnumerable<JobName> Jobs => durationByJob.Keys;
    public TimeSpan TotalDuration => durationByJob.Values.Aggregate(TimeSpan.Zero, (a, b) => a + b);

    public void Add(JobName job, TimeSpan duration)
    {
        durationByJob.Add(job, duration);
    }
}

internal sealed class ChainFilters(string name, RootFilter rootFilter, JobName rootJob, Dictionary<string, FilterJobs> testsByFilter)
{
    public string Name { get; } = name;
    public RootFilter RootFilter { get; } = rootFilter;
    public JobName RootJob { get; } = rootJob;
    public Dictionary<string, FilterJobs> TestsByFilter { get; } = testsByFilter;
}

internal sealed class JobFilters
{
    private readonly Dictionary<string, ChainFilters> _chainFiltersByChain;
    private readonly Dictionary<string, Dictionary<string, FilterJobs>> _testsByFilterByGroup;
    private readonly List<string> _errors;

    public JobFilters(Dictionary<string, ChainFilters> chainFiltersByChain, Dictionary<string, Dictionary<string, FilterJobs>> testsByFilterByGroup, List<string> errors)
    {
        _chainFiltersByChain = chainFiltersByChain;
        _testsByFilterByGroup = testsByFilterByGroup;
        _errors = errors;
    }

    public string[] Chains => [.. _chainFiltersByChain.Keys];
    public ChainFilters GetChainFilters(string chain) => _chainFiltersByChain[chain];

    public string[] TestGroups => [.. _testsByFilterByGroup.Keys];
    public Dictionary<string, FilterJobs> GetTestsByFilterForGroup(string group) => _testsByFilterByGroup[group];

    public string[] Errors => [.. _errors];
}

internal sealed class FilterAnalyzer(JenkinsConfig config, JobGroups jobGroups, Dictionary<JobName, TimeSpan> durationByOnDemandJob)
{
    public FilterAnalyzer(JenkinsConfig config, JobGroups jobGroups)
        : this(config, jobGroups, [])
    {
    }

    public JobFilters Run()
    {
        var chainFiltersByChain = new Dictionary<string, ChainFilters>();
        var testsByFilterByGroup = new Dictionary<string, Dictionary<string, FilterJobs>>();
        var errors = new List<string>();

        foreach (var rootFilter in config.RootFilters)
        {
            var matched = false;
            foreach (var (name, group) in jobGroups.ByRoot)
            {
                if (rootFilter.Matches(name, out var chain))
                {
                    matched = true;
                    if (!chainFiltersByChain.TryGetValue(chain, out var chainFilters))
                    {
                        chainFiltersByChain.Add(chain, new ChainFilters(chain, rootFilter, group.OnDemandJob, []));
                    }
                    break;
                }
            }
            if (!matched)
            {
                errors.Add($"{nameof(RootFilter)} '{rootFilter.Name}' does not match any root job");
            }
        }
        foreach (var testFilter in config.TestFilters)
        {
            if (testFilter.Group == config.ChainTestGroup)
            {
                var matched = false;
                foreach (var (name, group) in jobGroups.ByTest)
                {
                    if (testFilter.Matches(name, out var chain))
                    {
                        matched = true;
                        if (chainFiltersByChain.TryGetValue(chain, out var chainFilters))
                        {
                            var filterJobs = chainFilters.TestsByFilter.GetOrAdd(testFilter.Name, _ => new(testFilter, []));
                            filterJobs.Add(group.OnDemandJob, durationByOnDemandJob[group.OnDemandJob]);
                        }
                        else
                        {
                            errors.Add($"{nameof(TestFilter)} '{testFilter.Name}' matches chain '{chain}' which has no matching {nameof(RootFilter)}");
                        }
                    }
                }
                if (!matched)
                {
                    errors.Add($"{nameof(TestFilter)} '{testFilter.Name}' does not match any test job");
                }
            }
            else
            {
                var matched = false;
                foreach (var (name, group) in jobGroups.ByTest)
                {
                    if (testFilter.Matches(name, out var _))
                    {
                        matched = true;
                        var testsByFilter = testsByFilterByGroup.GetOrAdd(testFilter.Group, _ => []);
                        if (!testsByFilter.TryGetValue(testFilter.Name, out var filterJobs))
                        {
                            filterJobs = new FilterJobs(testFilter, []);
                            testsByFilter.Add(testFilter.Name, filterJobs);
                        }
                        filterJobs.Add(group.OnDemandJob, durationByOnDemandJob[group.OnDemandJob]);
                    }
                }
                if (!matched)
                {
                    errors.Add($"{nameof(TestFilter)} '{testFilter.Name}' does not match any test job");
                }
            }
        }
        return new JobFilters(chainFiltersByChain, testsByFilterByGroup, errors);
    }

    public static void LogFilters(JenkinsConfig config, JobGroups jobGroups, Dictionary<JobName, TimeSpan> durationByOnDemandJob, ILogger? logger = null)
    {
        logger ??= Log.Logger;

        var analyzer = new FilterAnalyzer(config, jobGroups, durationByOnDemandJob);
        var result = analyzer.Run();

        foreach (var chain in result.Chains.OrderBy(x => x))
        {
            logger.Information("Chain: {Chain}", chain);
            var filters = result.GetChainFilters(chain);
            logger.Information("  Root: '{RootFilter}': {@RootJob}", filters.RootFilter.Name, filters.RootJob);
            foreach (var testName in filters.TestsByFilter.Keys.OrderBy(x => x))
            {
                logger.Information("    '{TestFilter}' ({Duration})", testName, filters.TestsByFilter[testName].TotalDuration);
                foreach (var job in filters.TestsByFilter[testName].Jobs.OrderBy(x => x))
                {
                    logger.Information("      {@TestJob}", job);
                }
            }
        }
        foreach (var group in result.TestGroups)
        {
            logger.Information("Test Group: {Group}", group);
            var testsByFilter = result.GetTestsByFilterForGroup(group);
            foreach (var filter in testsByFilter.Keys.OrderBy(f => f))
            {
                var tests = testsByFilter[filter];
                logger.Information("  '{Filter}' ({Duration})", filter, tests.TotalDuration);
                foreach (var job in tests.Jobs.OrderBy(j => j))
                {
                    logger.Information("    {@Job}", job);
                }
            }
        }
        if (result.Errors.Length > 0)
        {
            logger.Error($"Error{(result.Errors.Length > 1 ? "s" : "")}:");
            foreach (var error in result.Errors)
            {
                logger.Error("  {Error}", error);
            }
        }
    }
}
