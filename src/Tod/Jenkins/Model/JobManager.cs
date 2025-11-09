using Serilog;

namespace Tod.Jenkins;

internal sealed class JobManager(JenkinsConfig config, IJenkinsClient client)
{
    private readonly JenkinsConfig _config = config;
    private readonly IJenkinsClient _client = client;

    public async Task<JobGroups?> TryLoad()
    {
        var jobNames = await _client.GetJobNames(_config.MultiBranchFolders).ConfigureAwait(false);
        return TryLoad(_config, jobNames);
    }

    public static JobGroups? TryLoad(JenkinsConfig config, JobName[] jobNames)
    {
        var refJobMatches = new JobMatchCollection<ReferenceJobMatch, ReferenceJobPattern>(config.ReferenceJobs.Select(j => new ReferenceJobPattern(j)));
        var ondemandJobMatches = new JobMatchCollection<OnDemandJobMatch, OnDemandJobPattern>(config.OnDemandJobs.Select(j => new OnDemandJobPattern(j)));
        var jobGroupsBuilder = new JobGroupsBuilder();
        foreach (var jobName in jobNames)
        {
            if (refJobMatches.FindFirst(jobName, out var refJobMatch))
            {
                refJobMatch.Match(
                    (branch, root) => jobGroupsBuilder.AddReferenceRoot(jobName, branch, root),
                    (branch, test) => jobGroupsBuilder.AddReferenceTest(jobName, branch, test)
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
        var errors = new List<string>();
        if (jobGroupsBuilder.TryBuild(out var jobGroups, errors.Add))
        {
            if (errors.Count > 0)
            {
                Log.Warning($"JobGroups loaded with {errors.Count} warning{(errors.Count > 1 ? "s" : "")}:");
                errors.ForEach(Log.Warning);
            }
            return jobGroups;
        }
        return null;
    }
}

