using CommandLine;
using Serilog;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
        var reportSender = new ReportSender(new RequestReportBuilder(), mailSender);
        var requestManager = new RequestManager(workSpace, filterManager, jenkinsClient, reportSender);
        var jenkinsSynchronizer = new JenkinsSynchronizer(jenkinsClient, requestManager);
        await jenkinsSynchronizer.Update(workSpace).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> New(NewOptions options)
    {
        using var gitRepo = new GitRepo();
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

        JobDiff[] rootDiffs;
        BranchName refBranch;
        Sha1 refCommit;
        if (wantedBranch != null)
        {
            rootDiffs = filterManager.GetRootDiffs(rootFilters, wantedBranch);
            var rootJobNames = rootDiffs.Select(d => d.ReferenceJob).ToArray();
            if (!workspace.BranchReferences.TryFindRefCommit(commits, rootJobNames, wantedBranch, out var commit))
            {
                return 1;
            }
            refBranch = wantedBranch;
            refCommit = commit!;
        }
        else
        {
            if (!workspace.BranchReferences.TryGuessBranch(commits, rootFilters, filterManager, out rootDiffs, out var branchName, out var commit))
            {
                return 1;
            }
            refBranch = branchName;
            refCommit = commit;
        }

        var mailSender = new MailSender(config.MailConfig);
        var reportSender = new ReportSender(new RequestReportBuilder(), mailSender);
        var requestManager = new RequestManager(workspace, filterManager, jenkinsClient, reportSender);
        var request = Request.Create(commits.First(), refCommit, refBranch, [.. options.TestFilters]);
        await requestManager.Register(request, rootDiffs).ConfigureAwait(false);
        return 0;
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

            return await Parser.Default.ParseArguments<SyncOptions, NewOptions>(args).MapResult(
                (SyncOptions options) => Sync(options),
                (NewOptions options) => New(options),
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
