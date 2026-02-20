using Serilog;

namespace Tod.Jenkins;

internal sealed class JobManager(JenkinsConfig config, IJenkinsClient client)
{
    private readonly JenkinsConfig _config = config;
    private readonly IJenkinsClient _client = client;

    public async Task<JobGroups?> TryLoad(Action<JobName[]>? saveJobs = null)
    {
        var jobNames = await _client.GetJobNames(_config.MultiBranchFolders).ConfigureAwait(false);
        if (jobNames.Length > 0)
        {
            saveJobs?.Invoke(jobNames);
        }
        return TryLoad(_config, jobNames);
    }

    public static JobGroups? TryLoad(JenkinsConfig config, JobName[] jobNames)
    {
        var baselineJobMatches = new JobMatchCollection<BaselineJobMatch, BaselineJobPattern>(config.BaselineJobs.Select(j => new BaselineJobPattern(j)));
        var ondemandJobMatches = new JobMatchCollection<OnDemandJobMatch, OnDemandJobPattern>(config.OnDemandJobs.Select(j => new OnDemandJobPattern(j)));
        var jobGroupsBuilder = new JobGroupsBuilder();
        foreach (var jobName in jobNames)
        {
            if (baselineJobMatches.FindFirst(jobName, out var baselineJobMatch))
            {
                baselineJobMatch.Match(
                    (branch, root) => jobGroupsBuilder.AddBaselineRoot(jobName, branch, root),
                    (branch, test) => jobGroupsBuilder.AddBaselineTest(jobName, branch, test)
                );
            }
            else if (ondemandJobMatches.FindFirst(jobName, out var onDemandJobMatch))
            {
                onDemandJobMatch.Match(
                    root => jobGroupsBuilder.AddOnDemandRoot(jobName, root),
                    test => jobGroupsBuilder.AddOnDemandTest(jobName, test)
                );
            }
        }
        var errors = new List<(string Message, object?[]? Args)>();
        if (jobGroupsBuilder.TryBuild(out var jobGroups, (m, xs) => errors.Add((m, xs))))
        {
            if (errors.Count > 0)
            {
                Log.Warning($"JobGroups loaded with {errors.Count} warning{(errors.Count > 1 ? "s" : "")}:");
                errors.ForEach(x => Log.Warning(x.Message, x.Args));
            }
            return jobGroups;
        }
        return null;
    }
}

