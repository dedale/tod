using Serilog;
using Tod.Git;

namespace Tod.Jenkins;

internal interface IPostBuildHandler
{
    void PostOnDemandRootBuild(BuildReference rootBuild, bool success);
    void PostOnDemandTestBuild(BuildReference rootBuild, BuildReference testBuild);
    void PostReferenceTestBuild(BuildReference rootBuild, BuildReference testBuild);
}

internal sealed class RequestManager(Workspace workspace, IFilterManager filterManager, IJenkinsClient jenkinsClient, IReportSender reportSender) : IPostBuildHandler
{
    public async Task Register(Request request, RootDiff[] rootDiffs)
    {
        Log.Information("Registering new request {RequestId} for commit {Commit} on branch {Branch}",
            request.Id, request.Commit, request.ReferenceBranchName);

        var branchReference = workspace.BranchReferences.FirstOrDefault(r => r.BranchName == request.ReferenceBranchName);
        if (branchReference == null)
        {
            Log.Error("Cannot use branch {Branch} for reference - branch not found", request.ReferenceBranchName);
            throw new InvalidOperationException($"Cannot use '{request.ReferenceBranchName}' branch for reference");
        }

        var roots = new List<(JobName ReferenceJob, BuildReference RootBuild, JobName OnDemandJob)>();
        foreach (var rootDiff in rootDiffs)
        {
            if (branchReference.TryFindRootBuildByCommit(request.ParentCommit, rootDiff.ReferenceJob, out var rootBuild))
            {
                roots.Add((rootDiff.ReferenceJob, rootBuild.Reference, rootDiff.OnDemandJob));
                Log.Debug("Found reference root build {RootBuild} for parent commit {Commit}", rootBuild, request.ParentCommit);
            }
            else
            {
                Log.Error("Unknown parent commit {Commit} in branch {Branch} for job {JobName}",
                    request.ParentCommit, request.ReferenceBranchName, rootDiff.ReferenceJob);
                throw new InvalidOperationException($"Unknown parent commit '{request.ParentCommit}' for job '{rootDiff.ReferenceJob}'");
            }
        }

        var requestChains = new List<RequestChain>();
        foreach (var (refRootJob, refRootBuild, onDemandJob) in roots)
        {
            var testBuildDiffs = filterManager.GetTestBuildDiffs(request.GetFilters(), request.ReferenceBranchName);
            for (var i = 0; i < testBuildDiffs.Length; i++)
            {
                if (branchReference.TryFindTestBuild(testBuildDiffs[i].ReferenceBuild.JobName, refRootBuild, out var refTestBuild))
                {
                    Log.Debug("Reusing reference test build {TestBuild}", refTestBuild);
                    testBuildDiffs[i] = testBuildDiffs[i].DoneReference(refTestBuild.BuildNumber);
                }
            }
            requestChains.Add(new RequestChain(refRootBuild, RequestBuildReference.Create(onDemandJob), testBuildDiffs));
        }

        Func<JobName, Sha1, Task<int>> triggerBuild = (jobName, refSpec) => jenkinsClient.TriggerBuild(jobName, refSpec);
        var requestState = await RequestState.New(request, [.. requestChains], workspace.OnDemandBuilds, triggerBuild).ConfigureAwait(false);
        workspace.OnDemandRequests.Add(requestState);

        Log.Information("Request {RequestId} registered", request.Id);
        requestState.LogChainStatuses();

        // Can be done when reusing all builds
        if (requestState.IsDone)
        {
            reportSender.Send(requestState, workspace);
        }
    }

    public void PostOnDemandRootBuild(BuildReference onDemandRoot, bool success)
    {
        // Protection to handle custom builds triggered manually outside requests
        if (workspace.OnDemandRequests.TryGetRootTriggered(onDemandRoot, out var lockedRequest))
        {
            try
            {
                Log.Information("On-demand root build {OnDemandBuild} completed for request {RequestId}",
                    onDemandRoot, lockedRequest.Value.Request.Id);

                RequestState update;
                if (success)
                {
                    Log.Information("{OnDemandBuild} succeeded; Triggering test builds", onDemandRoot);
                    update = lockedRequest.Update(r => r.TriggerTests((jobName, commit) => jenkinsClient.TriggerBuild(jobName, commit)));
                }
                else
                {
                    Log.Information("{OnDemandBuild} failed; Aborting request", onDemandRoot);
                    update = lockedRequest.Update(r => r.Abort());
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

    public void PostReferenceTestBuild(BuildReference rootBuild, BuildReference testBuild)
    {
        using var lockedRequests = workspace.OnDemandRequests.GetPendingReferenceTest(rootBuild, testBuild.JobName);

        if (lockedRequests.Count > 0)
        {
            Log.Information("Reference test build {TestBuild} completed - updating {RequestCount} request(s)", testBuild, lockedRequests.Count);
        }

        foreach (var lockedRequest in lockedRequests)
        {
            var update = lockedRequest.Update(r => r.DoneReferenceTestBuild(rootBuild, testBuild));

            Log.Debug("Updated request {RequestId} with reference test build {TestBuild}", update.Request.Id, testBuild);

            if (update.IsDone)
            {
                reportSender.Send(update, workspace);
            }
        }
    }

    public void PostOnDemandTestBuild(BuildReference rootBuild, BuildReference testBuild)
    {
        if (workspace.OnDemandRequests.TryGetTestTriggered(rootBuild, testBuild, out var lockedRequest))
        {
            try
            {
                Log.Information("On-demand test build {TestBuild} completed for request {RequestId}", testBuild, lockedRequest.Value.Request.Id);

                var update = lockedRequest.Update(r => r.DoneOnDemandTestBuild(rootBuild, testBuild));

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

