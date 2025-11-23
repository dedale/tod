using Serilog;
using Tod.Git;

namespace Tod.Jenkins;

internal interface IPostBuildHandler
{
    Task PostOnDemandRootBuild(BuildReference rootBuild, Sha1 commit, bool success);
    Task PostOnDemandTestBuild(BuildReference rootBuild, BuildReference testBuild);
    Task PostReferenceTestBuild(BuildReference rootBuild, BuildReference testBuild);
}

internal sealed class RequestManager(Workspace workspace, IFilterManager filterManager, IJenkinsClient jenkinsClient, IReportSender reportSender) : IPostBuildHandler
{
    public async Task Register(Request request, JobDiff[] rootDiffs)
    {
        Log.Information("Registering new request {RequestId} for commit {Commit} on branch {Branch}",
            request.Id, request.Commit, request.GitReference.Branch);

        var branchReference = workspace.BranchReferences.FirstOrDefault(r => r.BranchName == request.GitReference.Branch);
        if (branchReference == null)
        {
            Log.Error("Cannot use branch {Branch} for reference - branch not found", request.GitReference.Branch);
            throw new InvalidOperationException($"Cannot use '{request.GitReference.Branch}' branch for reference");
        }

        var roots = new List<(JobName ReferenceJob, BuildReference RootBuild, JobName OnDemandJob)>();
        foreach (var rootDiff in rootDiffs)
        {
            if (branchReference.TryFindRootBuildByCommit(request.GitReference.Commit, rootDiff.ReferenceJob, out var rootBuild))
            {
                roots.Add((rootDiff.ReferenceJob, rootBuild.Reference, rootDiff.OnDemandJob));
                Log.Debug("Found reference root build {RootBuild} for parent commit {Commit}", rootBuild, request.GitReference.Commit);
            }
            else
            {
                Log.Error("Unknown parent commit {Commit} in branch {Branch} for job {JobName}",
                    request.GitReference.Commit, request.GitReference.Branch, rootDiff.ReferenceJob);
                throw new InvalidOperationException($"Unknown parent commit '{request.GitReference.Commit}' for job '{rootDiff.ReferenceJob}'");
            }
        }

        var requestChains = new List<RequestChain>();
        foreach (var (refRootJob, refRootBuild, onDemandJob) in roots)
        {
            var testJobDiffs = filterManager.GetTestBuildDiffs(request.GetFilters(), request.GitReference.Branch);
            var testBuildDiffs = new List<RequestBuildDiff>(testJobDiffs.Length);
            for (var i = 0; i < testJobDiffs.Length; i++)
            {
                var buildDiff = new RequestBuildDiff(testJobDiffs[i].ReferenceJob, testJobDiffs[i].OnDemandJob);
                if (branchReference.TryFindTestBuild(testJobDiffs[i].ReferenceJob, refRootBuild, out var refTestBuild))
                {
                    Log.Debug("Reusing reference test build {TestBuild}", refTestBuild);
                    buildDiff = buildDiff.DoneReference(refTestBuild.BuildNumber);
                }
                testBuildDiffs.Add(buildDiff);
            }
            requestChains.Add(new RequestChain(refRootBuild, RequestRootBuildReference.Queue(onDemandJob, request.Commit), [.. testBuildDiffs]));
        }

        var requestState = await RequestState.New(request, [.. requestChains], workspace.OnDemandBuilds, jenkinsClient.TriggerBuild).ConfigureAwait(false);
        workspace.OnDemandRequests.Add(requestState);

        Log.Information("Request {RequestId} registered", request.Id);
        requestState.LogChainStatuses();

        // Can be done when reusing all builds
        if (requestState.IsDone)
        {
            reportSender.Send(requestState, workspace);
        }
    }

    public async Task PostOnDemandRootBuild(BuildReference onDemandRoot, Sha1 commit, bool success)
    {
        // Protection to handle custom builds triggered manually outside requests
        if (workspace.OnDemandRequests.TryGetRootQueued(onDemandRoot.JobName, commit, out var lockedRequest))
        {
            try
            {
                Log.Information("On-demand root build {OnDemandBuild} completed for request {RequestId}",
                    onDemandRoot, lockedRequest.Value.Request.Id);

                RequestState update;
                if (success)
                {
                    Log.Information("{OnDemandBuild} succeeded; Triggering test builds", onDemandRoot);
                    var triggerParameters = new TriggerParameters(commit, onDemandRoot.BuildNumber);
                    Func<JobName, Task> triggerBuild = jobName => jenkinsClient.TriggerBuild(OnDemandJobKind.Test, jobName, triggerParameters);
                    update = await lockedRequest.Update(async request => await request.TriggerTests(onDemandRoot, triggerBuild).ConfigureAwait(false));
                }
                else
                {
                    Log.Information("{OnDemandBuild} failed; Aborting request", onDemandRoot);
                    update = await lockedRequest.Update(r => Task.FromResult(r.AbortChain(onDemandRoot.JobName))).ConfigureAwait(false);

                    if (update.IsDone)
                    {
                        reportSender.Send(update, workspace);
                    }
                }

                Log.Information("Request {RequestId} updated", update.Request.Id);
                update.LogChainStatuses();
            }
            finally
            {
                lockedRequest.Dispose();
            }
        }
    }

    public async Task PostReferenceTestBuild(BuildReference rootBuild, BuildReference testBuild)
    {
        using var lockedRequests = workspace.OnDemandRequests.GetPendingReferenceTest(rootBuild, testBuild.JobName);

        if (lockedRequests.Count > 0)
        {
            Log.Information("Reference test build {TestBuild} completed - updating {RequestCount} request(s)", testBuild, lockedRequests.Count);
        }

        foreach (var lockedRequest in lockedRequests)
        {
            var update = await lockedRequest.Update(r => Task.FromResult(r.DoneReferenceTestBuild(rootBuild, testBuild))).ConfigureAwait(false);

            Log.Debug("Updated request {RequestId} with reference test build {TestBuild}", update.Request.Id, testBuild);

            if (update.IsDone)
            {
                reportSender.Send(update, workspace);
            }
        }
    }

    public async Task PostOnDemandTestBuild(BuildReference rootBuild, BuildReference testBuild)
    {
        if (workspace.OnDemandRequests.TryGetTestQueued(rootBuild, testBuild.JobName, out var lockedRequest))
        {
            try
            {
                Log.Information("On-demand test build {TestBuild} completed for request {RequestId}", testBuild, lockedRequest.Value.Request.Id);

                var update = await lockedRequest.Update(r => Task.FromResult(r.DoneOnDemandTestBuild(rootBuild, testBuild))).ConfigureAwait(false);

                if (update.IsDone)
                {
                    reportSender.Send(update, workspace);
                }

                Log.Information("Request {RequestId} updated", update.Request.Id);
                update.LogChainStatuses();
            }
            finally
            {
                lockedRequest.Dispose();
            }
        }
    }
}

