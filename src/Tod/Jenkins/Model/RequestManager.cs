using Serilog;
using Tod.Git;

namespace Tod.Jenkins;

internal sealed class RequestManager(Workspace workspace, IJenkinsClient jenkinsClient, IRequestReportSender reportSender) : IPostBuildHandler
{
    public async Task Register(Request request, RequestChain[] chains)
    {
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

    public Task PostBaselineRootBuild(RootBuild rootBuild, JobName[] scheduled)
    {
        return Task.CompletedTask;
    }

    public async Task PostOnDemandRootBuild(BuildReference onDemandRoot, Sha1 commit, bool success)
    {
        // Protection to handle custom builds triggered manually outside requests
        if (workspace.OnDemandRequests.TryGetRootQueued(onDemandRoot.JobName, commit, out var lockedRequest))
        {
            try
            {
                Log.Information("On-demand root build {@OnDemandBuild} completed for request {RequestId}",
                    onDemandRoot, lockedRequest.Value.Request.Id);

                RequestState update;
                if (success)
                {
                    Log.Information("{@OnDemandBuild} succeeded; Triggering test builds", onDemandRoot);
                    var triggerParameters = new TriggerParameters(commit, onDemandRoot.BuildNumber);
                    Func<JobName, Task> triggerBuild = jobName => jenkinsClient.TriggerBuild(OnDemandJobKind.Test, jobName, triggerParameters);
                    update = await lockedRequest.Update(async request => await request.TriggerTests(onDemandRoot, triggerBuild).ConfigureAwait(false));
                }
                else
                {
                    Log.Information("{@OnDemandBuild} failed; Aborting request", onDemandRoot);
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

    public async Task PostBaselineTestBuild(BuildReference rootBuild, BuildReference testBuild)
    {
        using var lockedRequests = workspace.OnDemandRequests.GetPendingBaselineTest(rootBuild, testBuild.JobName);

        if (lockedRequests.Count > 0)
        {
            Log.Information("Reference test build {@TestBuild} completed - updating {RequestCount} {$Requests}",
                testBuild, lockedRequests.Count, lockedRequests.Count > 1 ? "requests" : "request");
        }

        foreach (var lockedRequest in lockedRequests)
        {
            var update = await lockedRequest.Update(r => Task.FromResult(r.DoneBaselineTestBuild(rootBuild, testBuild))).ConfigureAwait(false);

            Log.Debug("Updated request {RequestId} with reference test build {@TestBuild}", update.Request.Id, testBuild);

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
                Log.Information("On-demand test build {@TestBuild} completed for request {RequestId}", testBuild, lockedRequest.Value.Request.Id);

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

