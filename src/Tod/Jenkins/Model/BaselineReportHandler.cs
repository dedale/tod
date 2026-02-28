using System.Diagnostics.CodeAnalysis;
using Tod.Git;
using Tod.Net;

namespace Tod.Jenkins;

internal sealed class BaselineReportHandler(BaselineBranch baselineBranch, JenkinsConfig config, IFlakyTests flakyTests) : IPostBuildHandler
{
    private readonly JobMatchCollection<BaselineJobMatch, BaselineJobPattern> _baselineJobMatches = new(config.BaselineJobs.Select(j => new BaselineJobPattern(j)));
    private readonly Dictionary<JobName, string> _chainByJob = [];
    private readonly bool _hideFlakies = config.BaselineReportConfig?.HideFlakyTests == true;

    private bool TryGetChain(JobName job, [NotNullWhen(true)] out string? chain)
    {
        if (_chainByJob.TryGetValue(job, out chain))
        {
            return true;
        }
        if (_baselineJobMatches.FindFirst(job, out var baselineJobMatch))
        {
            chain = baselineJobMatch.Match(
                (branch, root) =>
                {
                    foreach (var rootFilter in config.RootFilters)
                    {
                        if (rootFilter.Matches(root, out var chainName))
                        {
                            return chainName;
                        }
                    }
                    return null;
                },
                (branch, test) =>
                {
                    foreach (var testFilter in config.TestFilters)
                    {
                        if (testFilter.Matches(test, out var chainName))
                        {
                            return chainName;
                        }
                    }
                    return null;
                });
            if (chain == null)
            {
                return false;
            }
            _chainByJob.Add(job, chain);
            return true;
        }
        chain = null;
        return false;
    }

    public Task PostBaselineRootBuild(RootBuild rootBuild, JobName[] scheduled)
    {
        if (config.BaselineReportConfig?.Enabled == true && rootBuild.Commits.Length > 0)
        {
            if (TryGetChain(rootBuild.JobName, out var chain))
            {
                var tracker = baselineBranch.GetOrCreateChainTracker(chain);
                tracker.AddRootBuild(rootBuild, scheduled);
            }
        }
        return Task.CompletedTask;
    }

    public async Task PostBaselineTestBuild(BuildReference rootBuild, BuildReference testBuild)
    {
        if (config.BaselineReportConfig?.Enabled != true)
        {
            return;
        }
        if (TryGetChain(testBuild.JobName, out var chain))
        {
            var tracker = baselineBranch.GetOrCreateChainTracker(chain);
            await tracker.MarkTestDone(rootBuild.BuildNumber, testBuild.JobName, testBuild, () => SendReferenceReportsIfReady(chain, tracker)).ConfigureAwait(false);
        }
    }

    public Task PostOnDemandRootBuild(BuildReference rootBuild, Sha1 commit, bool success)
    {
        return Task.CompletedTask;
    }

    public Task PostOnDemandTestBuild(BuildReference rootBuild, BuildReference testBuild)
    {
        return Task.CompletedTask;
    }

    private async Task SendReferenceReportsIfReady(string chainName, ChainReportTracker tracker)
    {
        var readyChains = tracker.GetReadyForReport();
        if (readyChains.Length > 0)
        {
            var report = BaselineReportBuilder.Build(readyChains, chainName, baselineBranch, flakyTests);
            if (report != null)
            {
                var sender = new BaselineReportSender(new JenkinsJobLinker(config), new MailSender(config.MailConfig), _hideFlakies);
                await sender.SendReport(report).ConfigureAwait(false);
            }
        }
    }
}
