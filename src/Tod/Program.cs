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
    private static async Task<int> SyncJobs(SyncOptions options)
    {
        var config = JenkinsConfig.Load(options.ConfigPath);
        using var jenkinsClient = new JenkinsClient(config, options.UserToken);
        var jobManager = new JobManager(config, jenkinsClient);
        var jobGroups = await jobManager.TryLoad(jobNames => config.SaveJobs(options.ConfigPath, jobNames)).ConfigureAwait(false);
        var workspaceStore = new WorkspaceStore(options.WorkspaceDir);
        var workSpace = Workspace.Load(options.WorkspaceDir, workspaceStore);
        workSpace.UpdateJobs(workspaceStore, jobGroups!);
        return 0;
    }

    private static async Task<int> Sync(SyncOptions options)
    {
        if (options.Jobs)
        {
            return await SyncJobs(options).ConfigureAwait(false);
        }

        var config = JenkinsConfig.Load(options.ConfigPath);
        Debug.Assert(config is not null);

        using var jenkinsClient = new JenkinsClient(config, options.UserToken);
        JobGroups? jobGroups;
        if (config.JobNames.Length == 0)
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
        var requestManager = new RequestManager(workSpace, jenkinsClient, reportSender);
        var jenkinsSynchronizer = new JenkinsSynchronizer(jenkinsClient, requestManager);
        await jenkinsSynchronizer.Update(workSpace).ConfigureAwait(false);

        if (config.KeptDays != null && config.KeptDays > 0)
        {
            Log.Debug("Removing builds older than {KeptDays} days", config.KeptDays);
            var removed = workSpace.RemoveBuildsOlderThan(DateTime.UtcNow.AddDays((double)-config.KeptDays));
            if (removed > 0)
            {
                Log.Information("Removed {Removed} old {Builds}", removed, removed > 1 ? "builds" : "build");
            }
        }

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

        var request = Request.Create(commits.First(), gitReference, [.. options.TestFilters], UserServices.CurrentUserEmail);

        Log.Information("Registering new request {RequestId} for commit {Commit} on branch {Branch}",
            request.Id, request.Commit, request.GitReference.Branch);

        var chainBuilder = new RequestChainBuilder(workspace, filterManager);
        var chains = chainBuilder.Get(request.Commit, request.GitReference, rootDiffs, request.GetFilters());

        var requestValidator = new RequestValidator(config, jenkinsClient);
        if (!await requestValidator.Validate(chains).ConfigureAwait(false))
        {
            Log.Error("Request validation failed.");
            return 1;
        }

        var mailSender = new MailSender(config.MailConfig);
        var reportSender = new ReportSender(new JenkinsJobLinker(config), mailSender);
        var requestManager = new RequestManager(workspace, jenkinsClient, reportSender);
        await requestManager.Register(request, chains).ConfigureAwait(false);
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
            Log.Information("Root Job: {RootJob} ({Duration})", chain.OnDemandRoot.JobName, chain.RootDuration);
            foreach (var testBuildDiff in chain.TestBuildDiffs)
            {
                Log.Information("  Test Job: {TestJob} ({Duration})", testBuildDiff.OnDemandBuild.JobName, testBuildDiff.TestDuration);
            }
        }
        Log.Information("Total: {Duration}", chains.TotalDuration());
        return Task.FromResult(0);
    }

    private static Task<int> List(ListOptions options)
    {
        var workspace = Workspace.Load(options.WorkspaceDir, new WorkspaceStore(options.WorkspaceDir));

        var requests = options.All
            ? workspace.OnDemandRequests.AllRequests
            : workspace.OnDemandRequests.ActiveRequests;

        var userRequests = requests
            .Where(r => r.Value.Request.UserName == Environment.UserName)
            .OrderByDescending(r => r.Value.Request.CreatedUtc)
            .ToList();

        if (userRequests.Count == 0)
        {
            Log.Information("No {RequestType} requests found for user {User}",
                options.All ? "requests" : "active requests",
                Environment.UserName);
            return Task.FromResult(0);
        }

        Log.Information("Found {Count} {RequestType} for user {User}:",
            userRequests.Count,
            options.All ? "request" + (userRequests.Count > 1 ? "s" : "") : "active request" + (userRequests.Count > 1 ? "s" : ""),
            Environment.UserName);

        foreach (var cached in userRequests.OrderBy(r => r.Value.Request.CreatedUtc))
        {
            var request = cached.Value;
            Log.Information("");
            Log.Information("Request ID: {RequestId}", request.Request.Id);
            Log.Information("  Created: {CreatedUtc}", request.Request.CreatedUtc);
            Log.Information("  Branch: {Branch}", request.Request.GitReference.Branch);
            Log.Information("  Commit: {Commit}", request.Request.Commit);
            Log.Information("  Filters: {Filters}", request.Request.Filters);
            Log.Information("  Status: {Status}", request.IsDone ? "Done" : "Active");

            foreach (var chain in request.ChainDiffs)
            {
                var chainStatus = chain.Status switch
                {
                    ChainStatus.RootTriggered => "Root Triggered",
                    ChainStatus.TestsTriggered => "Tests Triggered",
                    ChainStatus.Done => "Done",
                    _ => chain.Status.ToString()
                };
                Log.Information("    Chain {JobName}: {ChainStatus}", chain.OnDemandRoot.JobName, chainStatus);
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

    private static async Task<int> Abort(AbortOptions options)
    {
        if (!Guid.TryParse(options.RequestId, out var requestId))
        {
            Log.Error("Invalid request ID format: '{RequestId}'", options.RequestId);
            return 1;
        }

        var workspace = Workspace.Load(options.WorkspaceDir, new WorkspaceStore(options.WorkspaceDir));
        var cachedRequest = workspace.OnDemandRequests.AllRequests.FirstOrDefault(r => r.Value.Request.Id == requestId);
        if (cachedRequest == null)
        {
            Log.Error("Request with ID '{RequestId}' not found in workspace", requestId);
            return 1;
        }

        if (cachedRequest.Value.Request.UserName != Environment.UserName)
        {
            Log.Error("Cannot abort request '{RequestId}'. Request belongs to user '{Owner}' but you are '{CurrentUser}'",
                requestId,
                cachedRequest.Value.Request.UserName,
                Environment.UserName);
            return 1;
        }

        using var lockedRequest = cachedRequest.Lock(nameof(Abort));
        await lockedRequest.Update(r => Task.FromResult(r.AbortAll())).ConfigureAwait(false);

        Log.Information("Request {RequestId} has been aborted", requestId);
        return 0;
    }

    private static Task<int> Filters(FiltersOptions options)
    {
        var config = JenkinsConfig.Load(options.ConfigPath);
        var jobGroups = JobManager.TryLoad(config, config.JobNames);
        if (jobGroups == null)
        {
            Log.Error("Failed to load job groups. Please run 'sync --jobs' first.");
            return Task.FromResult(1);
        }

        var analyzer = new FilterAnalyzer(config, jobGroups);
        var result = analyzer.Run();

        foreach (var chain in result.Chains.OrderBy(x => x))
        {
            Log.Information("Chain: {Chain}", chain);
            var filters = result.GetChainFilters(chain);
            Log.Information("  Root: {RootFilter}: {RootJob}", filters.RootFilter.Name, filters.RootJob);
            foreach (var testName in filters.TestsByFilter.Keys.OrderBy(x => x))
            {
                Log.Information("    {TestFilter}", testName);
                foreach (var job in filters.TestsByFilter[testName].Jobs.OrderBy(x => x))
                {
                    Log.Information("      {TestJob}", job);
                }
            }
        }
        foreach (var group in result.TestGroups)
        {
            Log.Information("Test Group: {Group}", group);
            var testsByFilter = result.GetTestsByFilterForGroup(group);
            foreach (var filter in testsByFilter.Keys.OrderBy(f => f))
            {
                var tests = testsByFilter[filter];
                Log.Information("  {Filter}", filter);
                foreach (var job in tests.Jobs.OrderBy(j => j))
                {
                    Log.Information("    {Job}", job);
                }
            }
        }
        if (result.Errors.Length > 0)
        {
            Log.Error($"Error{(result.Errors.Length > 1 ? "s" : "")}:");
            foreach (var error in result.Errors)
            {
                Log.Error("  {Error}", error);
            }
        }

        return Task.FromResult(0);
    }

    private static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        try
        {
            Log.Debug(Environment.CommandLine);

            return await Parser.Default.ParseArguments<SyncOptions, NewOptions, JobsOptions, ListOptions, ReportOptions, AbortOptions, FiltersOptions>(args).MapResult(
                (SyncOptions options) => Sync(options),
                (NewOptions options) => New(options),
                (JobsOptions options) => Jobs(options),
                (ListOptions options) => List(options),
                (ReportOptions options) => Report(options),
                (AbortOptions options) => Abort(options),
                (FiltersOptions options) => Filters(options),
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
