namespace Tod.Jenkins;

internal interface IFilterManager
{
    RootDiff[] GetRootDiffs(RootName[] rootNames, BranchName referenceBranch);
    RequestBuildDiff[] GetTestBuildDiffs(string[] requestFilters, BranchName referenceBranch);
}

internal sealed class FilterManager(JenkinsConfig config, JobGroups jobGroups) : IFilterManager
{
    public RootDiff[] GetRootDiffs(RootName[] rootNames, BranchName referenceBranch)
    {
        var rootDiffs = new List<RootDiff>();
        foreach (var (name, group) in jobGroups.ByRoot)
        {
            if (!rootNames.Contains(name))
            {
                continue;
            }
            if (!group.ReferenceJobByBranch.TryGetValue(referenceBranch, out var referenceJob))
            {
                throw new InvalidOperationException($"No reference job for '{referenceBranch}' branch in test group");
            }
            rootDiffs.Add(new RootDiff(referenceJob, group.OnDemandJob));
        }
        return [.. rootDiffs];
    }

    public RequestBuildDiff[] GetTestBuildDiffs(string[] requestFilters, BranchName referenceBranch)
    {
        var filters = new List<TestFilter>();
        var unknownFilters = new List<string>();
        foreach (var filter in requestFilters)
        {
            if (config.TryGetFilter(filter, out var testFilter))
            {
                filters.Add(testFilter);
            }
            else
            {
                unknownFilters.Add(filter);
            }
        }
        if (unknownFilters.Count > 0)
        {
            throw new InvalidOperationException($"Unknown test filter{(unknownFilters.Count > 1 ? "s" : "")}: {string.Join(", ", unknownFilters.Select(f => $"'{f}'"))}");
        }

        // Filter test groups based on the provided filters:
        // - Filters are grouped by their Group property
        // - Within each group, a test must match at least one filter (OR)
        // - A test must match at least one filter from EVERY group (AND)
        // Example: For filter groups [(A,B), (C)], tests must match: (A OR B) AND (C)
        var testGroups = filters
            .GroupBy(f => f.Group).Select(g => g.ToList())
            .Aggregate(
                jobGroups.ByTest.Select(x => new { TestName = x.Key, JobGroup = x.Value }),
                (groups, filterGroup) => groups.Where(g => filterGroup.Any(f => f.Matches(g.TestName)))
            )
            .Select(x => x.JobGroup)
            .ToArray();
        if (testGroups.Length == 0)
        {
            throw new InvalidOperationException($"No test groups for the request filter{(filters.Count > 1 ? "s" : "")}: {string.Join(", ", filters.Select(f => $"'{f.Name}'"))}");
        }
        var testBuildDiffs = new List<RequestBuildDiff>();
        foreach (var group in testGroups)
        {
            if (!group.ReferenceJobByBranch.TryGetValue(referenceBranch, out var referenceJob))
            {
                throw new InvalidOperationException($"No reference job for '{referenceBranch}' branch in test group");
            }
            var testBuildDiff = new RequestBuildDiff(referenceJob, group.OnDemandJob);
            testBuildDiffs.Add(testBuildDiff);
        }
        return [.. testBuildDiffs];
    }
}
