using CommandLine;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Tod.Core;
using Tod.Gerrit;
using Tod.Git;
using Tod.Jenkins;
using Tod.Net;

namespace Tod;

[ExcludeFromCodeCoverage]
internal static class Program
{
    private static class ExitCodes
    {
        public const int Success = 0;
        public const int BadRequest = 1;
        public const int InternalError = 2;
    }

    private static async Task<int> SyncJobs(SyncOptions options)
    {
        var config = JenkinsConfig.Load(options.ConfigPath);
        JobName.Init(config.JobMappings);
        using var jenkinsClient = new JenkinsClient(config, Environment.UserName, options.JenkinsToken);
        var jobManager = new JobManager(config, jenkinsClient);
        var jobGroups = await jobManager.TryLoad(jobNames => config.SaveJobs(options.ConfigPath, jobNames)).ConfigureAwait(false);
        var workspaceStore = new WorkspaceStore(options.WorkspaceDir);
        var workSpace = Workspace.Load(options.WorkspaceDir, workspaceStore);
        workSpace.UpdateJobs(workspaceStore, jobGroups!);
        return ExitCodes.Success;
    }

    private static async Task<int> Sync(SyncOptions options)
    {
        if (options.Jobs)
        {
            return await SyncJobs(options).ConfigureAwait(false);
        }

        var stopwatch = Stopwatch.StartNew();
        var config = JenkinsConfig.Load(options.ConfigPath);
        Debug.Assert(config is not null);
        JobName.Init(config.JobMappings);

        using var jenkinsClient = new JenkinsClient(config, Environment.UserName, options.JenkinsToken);
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
        var reportSender = new RequestReportSender(new JenkinsJobLinker(config), mailSender);
        var requestManager = new RequestManager(workSpace, jenkinsClient, reportSender);

        var postBuildHandlers = new List<IPostBuildHandler> { requestManager };

        if (config.BaselineReportConfig?.Enabled == true)
        {
            foreach (var baselineBranch in workSpace.BaselineBranches)
            {
                var baselineReportHandler = new BaselineReportHandler(baselineBranch, config, workSpace.FlakyTests);
                postBuildHandlers.Add(baselineReportHandler);
            }
        }

        var jenkinsSynchronizer = new JenkinsSynchronizer(jenkinsClient, postBuildHandlers);
        await jenkinsSynchronizer.Update(workSpace).ConfigureAwait(false);

        if (config.KeptDays != null && config.KeptDays > 0)
        {
            Log.Debug("Removing builds older than {KeptDays} days", config.KeptDays);
            var removed = workSpace.RemoveBuildsOlderThan(DateTime.UtcNow.AddDays((double)-config.KeptDays));
            if (removed > 0)
            {
                Log.Information($"Removed {{Removed}} old {(removed > 1 ? "builds" : "build")}", removed);
            }
        }
        Log.Information("Sync completed in {Duration}", stopwatch.Elapsed);

        return ExitCodes.Success;
    }

    private static async Task<int> New(NewOptions options)
    {
        using var gitRepo = new GitRepo(Environment.CurrentDirectory);
        var commits = gitRepo.GetLastCommits(50);
        var config = JenkinsConfig.Load(options.ConfigPath);
        JobName.Init(config.JobMappings);
        var workspace = Workspace.Load(options.WorkspaceDir, new WorkspaceStore(options.WorkspaceDir));
        var wantedBranch = options.BranchName is not null ? new BranchName(options.BranchName) : null;
        var rootFilters = options.RootFilters.ToArray();

        var userName = options.User ?? Environment.UserName;
        var userDomain = options.UserDomain ?? Environment.UserDomainName;

        var jenkinsClient = new JenkinsClient(config, userName, options.JenkinsToken);
        var jobManager = new JobManager(config, jenkinsClient);
        var jobGroups = await jobManager.TryLoad().ConfigureAwait(false);
        Debug.Assert(jobGroups is not null);
        var filterManager = new FilterManager(config, jobGroups);

        var gitReference = workspace.GetGitReference(filterManager, wantedBranch, rootFilters, commits, out var rootDiffs);
        if (gitReference == null)
        {
            return ExitCodes.BadRequest;
        }

        var request = Request.Create(commits.First(), gitReference, [.. options.TestFilters], userName, UserServices.GetUserEmail(userName, userDomain));

        Log.Information("Registering new request {RequestId} for commit {Commit} on branch {Branch}",
            request.Id, request.Commit, request.GitReference.Branch);

        if (!string.IsNullOrEmpty(config.GerritReviewServer))
        {
            using var gerritClient = new GerritClient(config.GerritReviewServer, userName, options.GerritToken);
            if (!await gerritClient.IsKnown(request.Commit).ConfigureAwait(false))
            {
                Log.Error("Commit {Commit} is not known in Gerrit. Jenkins will not be able to checkout the code. " +
                    "Make sure the commit has been pushed to Gerrit as a patchset.", request.Commit);
                return ExitCodes.BadRequest;
            }
            Log.Debug("Commit {Commit} found in Gerrit", request.Commit);
        }

        var chainBuilder = new RequestChainBuilder(workspace, filterManager);
        var chains = chainBuilder.Get(request.Commit, request.GitReference, rootDiffs, request.GetTestFilters());

        var userActiveRequestsCount = workspace.OnDemandRequests.ActiveRequests
            .Count(r => r.Value.Request.UserName == userName);
        var requestValidator = new RequestValidator(config, jenkinsClient);
        if (!await requestValidator.Validate(chains, userName, userActiveRequestsCount).ConfigureAwait(false))
        {
            Log.Error("Request validation failed.");
            return ExitCodes.BadRequest;
        }

        var mailSender = new MailSender(config.MailConfig);
        var reportSender = new RequestReportSender(new JenkinsJobLinker(config), mailSender);
        var requestManager = new RequestManager(workspace, jenkinsClient, reportSender);
        await requestManager.Register(request, chains).ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private static Task<int> Jobs(JobsOptions options)
    {
        var config = JenkinsConfig.Load(options.ConfigPath);
        JobName.Init(config.JobMappings);
        var workspace = Workspace.Load(options.WorkspaceDir, new WorkspaceStore(options.WorkspaceDir));
        var wantedBranch = options.BranchName is not null ? new BranchName(options.BranchName) : null;
        var rootFilters = options.RootFilters.ToArray();

        Log.Debug("Using cached jobs in Jenkins config");
        var jobGroups = JobManager.TryLoad(config, config.JobNames);
        Debug.Assert(jobGroups is not null);
        var filterManager = new FilterManager(config, jobGroups);

        var commits = options.Commits.Select(x => new Sha1(x)).ToArray();
        if (commits.Length == 0)
        {
            using (var gitRepo = new GitRepo(Environment.CurrentDirectory))
            {
                commits = gitRepo.GetLastCommits(50);
            }
        }

        var gitReference = workspace.GetGitReference(filterManager, wantedBranch, rootFilters, commits, out var rootDiffs);
        if (gitReference == null)
        {
            return Task.FromResult(ExitCodes.BadRequest);
        }

        var chainBuilder = new RequestChainBuilder(workspace, filterManager);
        var chains = chainBuilder.Get(commits.First(), gitReference, rootDiffs, [.. options.TestFilters]);
        foreach (var chain in chains)
        {
            Log.Information("Root Job: {@RootJob} ({Duration})", chain.OnDemandRoot.JobName, chain.RootDuration);
            foreach (var testBuildDiff in chain.TestBuildDiffs)
            {
                Log.Information("  Test Job: {@TestJob} ({Duration})", testBuildDiff.OnDemandBuild.JobName, testBuildDiff.TestDuration);
            }
        }
        Log.Information("Total: {Duration}", chains.TotalDuration());
        return Task.FromResult(ExitCodes.Success);
    }

    private static Task<int> List(ListOptions options)
    {
        // No need for job mappings when listing requests
        var workspace = Workspace.Load(options.WorkspaceDir, new WorkspaceStore(options.WorkspaceDir));

        var requests = options.All
            ? workspace.OnDemandRequests.AllRequests
            : workspace.OnDemandRequests.ActiveRequests;

        var userName = options.User ?? Environment.UserName;
        var userRequests = requests
            .Where(r => r.Value.Request.UserName == userName)
            .OrderBy(r => r.Value.Request.CreatedUtc)
            .ToList();

        if (userRequests.Count == 0)
        {
            Log.Information("No {RequestType} requests found for user {User}",
                options.All ? "requests" : "active requests", userName);
            return Task.FromResult(ExitCodes.Success);
        }

        Log.Information($"Found {{Count}} {(options.All ? "request" + (userRequests.Count > 1 ? "s" : "") : "active request" + (userRequests.Count > 1 ? "s" : ""))} for user {{User}}:",
            userRequests.Count, userName);

        foreach (var cached in userRequests)
        {
            var request = cached.Value;
            Log.Information("");
            Log.Information("Request ID: {RequestId}", request.Request.Id);
            Log.Information("  Created: {CreatedUtc:yyyy-MM-dd HH:mm:ss}", request.Request.CreatedUtc);
            Log.Information("  Branch: {Branch}", request.Request.GitReference.Branch);
            Log.Information("  Commit: {Commit}", request.Request.Commit);
            Log.Information("  Filters: {Filters}", request.Request.TestFilters);
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
                Log.Information("    Chain {@JobName}: {ChainStatus}", chain.OnDemandRoot.JobName, chainStatus);
            }
        }

        return Task.FromResult(ExitCodes.Success);
    }

    private static async Task<int> Report(ReportOptions options)
    {
        if (!Guid.TryParse(options.RequestId, out var requestId))
        {
            Log.Error("Invalid request ID format: '{RequestId}'", options.RequestId);
            return ExitCodes.BadRequest;
        }
        var config = JenkinsConfig.Load(options.ConfigPath);
        JobName.Init(config.JobMappings);
        var reportSender = new RequestReportSender(
            new JenkinsJobLinker(config),
            new MailSender(config.MailConfig)
        );
        var workspace = Workspace.Load(options.WorkspaceDir, new WorkspaceStore(options.WorkspaceDir));
        var cachedRequest = workspace.OnDemandRequests.AllRequests.FirstOrDefault(r => r.Value.Request.Id == requestId);
        if (cachedRequest == null)
        {
            Log.Error("Request with ID '{RequestId}' not found in workspace", requestId);
            return ExitCodes.BadRequest;
        }
        Log.Information("Generating report for request {RequestId}", requestId);
        var report = RequestReportBuilder.Instance.Build(cachedRequest.Value, workspace);
        await reportSender.Send(cachedRequest.Value, report).ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private static async Task<int> Abort(AbortOptions options)
    {
        if (!Guid.TryParse(options.RequestId, out var requestId))
        {
            Log.Error("Invalid request ID format: '{RequestId}'", options.RequestId);
            return ExitCodes.BadRequest;
        }

        // No need for job mappings when aborting requests
        var workspace = Workspace.Load(options.WorkspaceDir, new WorkspaceStore(options.WorkspaceDir));
        var cachedRequest = workspace.OnDemandRequests.AllRequests.FirstOrDefault(r => r.Value.Request.Id == requestId);
        if (cachedRequest == null)
        {
            Log.Error("Request with ID '{RequestId}' not found in workspace", requestId);
            return ExitCodes.BadRequest;
        }

        var userName = options.User ?? Environment.UserName;
        if (cachedRequest.Value.Request.UserName != userName)
        {
            Log.Error("Cannot abort request '{RequestId}'. Request belongs to user '{Owner}' but you are '{CurrentUser}'",
                requestId, cachedRequest.Value.Request.UserName, userName);
            return ExitCodes.BadRequest;
        }

        using var lockedRequest = cachedRequest.Lock(nameof(Abort));
        await lockedRequest.Update(r => Task.FromResult(r.AbortAll())).ConfigureAwait(false);

        Log.Information("Request {RequestId} has been aborted", requestId);
        return ExitCodes.Success;
    }

    private static Task<int> Filters(FiltersOptions options)
    {
        var config = JenkinsConfig.Load(options.ConfigPath);
        var jobGroups = JobManager.TryLoad(config, config.JobNames);
        if (jobGroups == null)
        {
            Log.Error("Failed to load job groups. Please run 'sync --jobs' first.");
            return Task.FromResult(ExitCodes.InternalError);
        }

        // Implicit constraint: all jobs exist for (at least) one reference branch
        var testGroups = jobGroups.ByTest.Select(g => g.Value).ToList();
        var refBranch = testGroups
            .SelectMany(g => g.BaselineJobByBranch)
            .GroupBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Value).ToArray())
            .MaxBy(kvp => kvp.Value.Length)
            .Key;

        // No need for job mappings when analyzing filters
        var workspace = Workspace.Load(options.WorkspaceDir, new WorkspaceStore(options.WorkspaceDir));
        var baselineBranch = workspace.BaselineBranches.Single(b => b.BranchName == refBranch);
        var durationByRefJob = baselineBranch.TestBuilds.ToDictionary(x => x.JobName, x => x.AverageDuration);
        var durationByOnDemandJob = testGroups.ToDictionary(g => g.OnDemandJob, g => durationByRefJob[g.BaselineJobByBranch[refBranch]]);

        FilterAnalyzer.LogFilters(config, jobGroups, durationByOnDemandJob);

        return Task.FromResult(ExitCodes.Success);
    }

    private static async Task<int> Main(string[] args)
    {
        var loggingLevel = new LoggingLevelSwitch(args.Any(a => a == "-d" || a == "--debug") ? LogEventLevel.Debug : LogEventLevel.Information);

        Log.Logger = new LoggerConfiguration()
            .Destructure.With<JobNameDestructuringPolicy>()
            .Destructure.With<BuildReferenceDestructuringPolicy>()
            .Destructure.With<BuildResultInfoDestructuringPolicy>()
            .Enrich.With<TimeSpanEnricher>()
            .MinimumLevel.ControlledBy(loggingLevel)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}", theme: AnsiConsoleTheme.Literate)
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
                errors => Task.FromResult(ExitCodes.BadRequest)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            return ExitCodes.InternalError;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
