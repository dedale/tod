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

        var chainBuilder = new RequestChainBuilder(workspace, filterManager);
        var chains = chainBuilder.Get(request.Commit, request.GitReference, rootDiffs, request.GetFilters());

        var requestState = await RequestState.New(request, chains, workspace.OnDemandBuilds, jenkinsClient.TriggerBuild).ConfigureAwait(false);
        workspace.OnDemandRequests.Add(requestState);

        Log.Information("Request {RequestId} registered", request.Id);
        requestState.LogChainStatuses();

        // Can be done when reusing all builds
        if (requestState.IsDone)
        {
            await reportSender.Send(requestState, workspace).ConfigureAwait(false);
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
                        await reportSender.Send(update, workspace).ConfigureAwait(false);
                    }
                }

                Log.Information("Request {RequestId} updated", update.Request.Id);
                update.LogChainStatus(onDemandRoot.JobName);
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
                await reportSender.Send(update, workspace).ConfigureAwait(false);
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
                    await reportSender.Send(update, workspace).ConfigureAwait(false);
                }

                Log.Information("Request {RequestId} updated", update.Request.Id);
                update.LogChainStatus(rootBuild.JobName);
            }
            finally
            {
                lockedRequest.Dispose();
            }
        }
    }
}

