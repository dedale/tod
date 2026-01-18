namespace Tod.Jenkins;

internal sealed class FilterJobs(TestFilter filter, List<JobName> jobs)
{
    public TestFilter Filter { get; } = filter;
    public List<JobName> Jobs { get; } = jobs;
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

internal sealed class FilterAnalyzer(JenkinsConfig config, JobGroups jobGroups)
{
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
                            filterJobs.Jobs.Add(group.OnDemandJob);
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
                        filterJobs.Jobs.Add(group.OnDemandJob);
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
}
