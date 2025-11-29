using CommandLine;
using Serilog;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Tod.Core;
using Tod.Git;
using Tod.Jenkins;
using Tod.Net;

namespace Tod;

[ExcludeFromCodeCoverage]
internal static class Program
{
    private static async Task<int> Sync(SyncOptions options)
    {
        var config = JenkinsConfig.Load(options.ConfigPath);
        Debug.Assert(config is not null);

        using var jenkinsClient = new JenkinsClient(config, options.UserToken);
        JobGroups? jobGroups;
        if (options.NoCache || config.JobNames.Length == 0)
        {
            var jobManager = new JobManager(config, jenkinsClient);
            jobGroups = await jobManager.TryLoad().ConfigureAwait(false);
        }
        else
        {
            jobGroups = JobManager.TryLoad(config, config.JobNames);
        }
        Debug.Assert(jobGroups is not null);

        var workSpace = Workspace.Load(options.WorkspaceDir, new WorkspaceStore(options.WorkspaceDir));
        var filterManager = new FilterManager(config, jobGroups);
        var mailSender = new MailSender(config.MailConfig);
        var reportSender = new ReportSender(new JenkinsJobLinker(config), mailSender);
        var requestManager = new RequestManager(workSpace, filterManager, jenkinsClient, reportSender);
        var jenkinsSynchronizer = new JenkinsSynchronizer(jenkinsClient, requestManager);
        await jenkinsSynchronizer.Update(workSpace).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> New(NewOptions options)
    {
        using var gitRepo = new GitRepo(Environment.CurrentDirectory);
        var commits = gitRepo.GetLastCommits(50);
        var workspace = Workspace.Load(options.WorkspaceDir, new WorkspaceStore(options.WorkspaceDir));
        var wantedBranch = options.BranchName is not null ? new BranchName(options.BranchName) : null;
        var rootFilters = options.RootFilters.ToArray();

        var config = JenkinsConfig.Load(options.ConfigPath);
        var jenkinsClient = new JenkinsClient(config, options.UserToken);
        var jobManager = new JobManager(config, jenkinsClient);
        var jobGroups = await jobManager.TryLoad().ConfigureAwait(false);
        Debug.Assert(jobGroups is not null);
        var filterManager = new FilterManager(config, jobGroups);

        var gitReference = workspace.GetGitReference(filterManager, wantedBranch, rootFilters, commits, out var rootDiffs);
        if (gitReference == null)
        {
            return 1;
        }

        var mailSender = new MailSender(config.MailConfig);
        var reportSender = new ReportSender(new JenkinsJobLinker(config), mailSender);
        var requestManager = new RequestManager(workspace, filterManager, jenkinsClient, reportSender);
        var request = Request.Create(commits.First(), gitReference, [.. options.TestFilters], UserDirectory.CurrentUserEmail);
        await requestManager.Register(request, rootDiffs).ConfigureAwait(false);
        return 0;
    }

    private static Task<int> Jobs(JobsOptions options)
    {
        using var gitRepo = new GitRepo(Environment.CurrentDirectory);
        var commits = gitRepo.GetLastCommits(50);
        var workspace = Workspace.Load(options.WorkspaceDir, new WorkspaceStore(options.WorkspaceDir));
        var wantedBranch = options.BranchName is not null ? new BranchName(options.BranchName) : null;
        var rootFilters = options.RootFilters.ToArray();

        var config = JenkinsConfig.Load(options.ConfigPath);
        Log.Debug("Using cached jobs in Jenkins config");
        var jobGroups = JobManager.TryLoad(config, config.JobNames);
        Debug.Assert(jobGroups is not null);
        var filterManager = new FilterManager(config, jobGroups);

        var gitReference = workspace.GetGitReference(filterManager, wantedBranch, rootFilters, commits, out var rootDiffs);
        if (gitReference == null)
        {
            return Task.FromResult(1);
        }

        var chainBuilder = new RequestChainBuilder(workspace, filterManager);
        var chains = chainBuilder.Get(commits.First(), gitReference, rootDiffs, [.. options.TestFilters]);
        foreach (var chain in chains)
        {
            Log.Information("Root Job; {RootJob}", chain.OnDemandRoot.JobName);
            foreach (var testBuildDiff in chain.TestBuildDiffs)
            {
                Log.Information("  Test Job: {TestJob}", testBuildDiff.OnDemandBuild.JobName);
            }
        }
        return Task.FromResult(0);
    }

    private static async Task<int> Report(ReportOptions options)
    {
        if (!Guid.TryParse(options.RequestId, out var requestId))
        {
            Log.Error("Invalid request ID format: '{RequestId}'", options.RequestId);
            return 1;
        }
        var config = JenkinsConfig.Load(options.ConfigPath);
        var reportSender = new ReportSender(
            new JenkinsJobLinker(config),
            new MailSender(config.MailConfig)
        );
        var workspace = Workspace.Load(options.WorkspaceDir, new WorkspaceStore(options.WorkspaceDir));
        var cachedRequest = workspace.OnDemandRequests.AllRequests.FirstOrDefault(r => r.Value.Request.Id == requestId);
        if (cachedRequest == null)
        {
            Log.Error("Request with ID '{RequestId}' not found in workspace", requestId);
            return 1;
        }
        var report = RequestReportBuilder.Instance.Build(cachedRequest.Value, workspace);
        await reportSender.Send(cachedRequest.Value, report).ConfigureAwait(false);
        return 0;
    }

    private static Task<int> RemoveJob(RemoveJobOptions options)
    {
        var config = JenkinsConfig.Load(options.ConfigPath);
        var jobGroups = JobManager.TryLoad(config, config.JobNames);
        Debug.Assert(jobGroups != null);
        if (!jobGroups.ByTest.ContainsKey(new TestName(options.GroupName)))
        {
            Log.Error("No job group found for name '{Group}'", options.GroupName);
            return Task.FromResult(1);
        }
        JobGroup jobGroup = jobGroups.ByTest.Single(kvp => kvp.Key.Value == options.GroupName).Value;
        var keptJobs = jobGroup.ReferenceJobByBranch.Select(kvp => kvp.Value).ToList();
        keptJobs.Add(jobGroup.OnDemandJob);
        var newConfig = JenkinsConfig.New(
            url: config.Url,
            multiBranchFolders: config.MultiBranchFolders,
            jobNames: [.. config.JobNames.Where(job => !keptJobs.Contains(job))],
            referenceJobs: config.ReferenceJobs,
            onDemandJobs: config.OnDemandJobs,
            triggerConfigs: config.TriggerConfigs,
            rootFilters: config.RootFilters,
            chainTestGroup: config.ChainTestGroup,
            testFilters: config.TestFilters,
            mailConfig: config.MailConfig
        );
        newConfig.Save(options.ConfigPath);
        var workspace = Workspace.Load(options.WorkspaceDir, new WorkspaceStore(options.WorkspaceDir));
        foreach (var branchReference in workspace.BranchReferences)
        {
            if (jobGroup.ReferenceJobByBranch.TryGetValue(branchReference.BranchName, out var testJob))
            {
                branchReference.RemoveTest(testJob);
            }
        }
        workspace.OnDemandBuilds.RemoveTest(jobGroup.OnDemandJob);
        return Task.FromResult(0);
    }

    // TODO list
    // TODO report
    // TODO abort/cancel

    private static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        try
        {
            Log.Debug(Environment.CommandLine);

            return await Parser.Default.ParseArguments<SyncOptions, NewOptions, JobsOptions, RemoveJobOptions, ReportOptions>(args).MapResult(
                (SyncOptions options) => Sync(options),
                (NewOptions options) => New(options),
                (JobsOptions options) => Jobs(options),
                (RemoveJobOptions options) => RemoveJob(options),
                (ReportOptions options) => Report(options),
                errors => Task.FromResult(1)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
