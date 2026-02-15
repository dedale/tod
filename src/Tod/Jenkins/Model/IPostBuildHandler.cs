using Tod.Git;

namespace Tod.Jenkins;

internal interface IPostBuildHandler
{
    Task PostReferenceRootBuild(RootBuild rootBuild, JobName[] scheduled);
    Task PostReferenceTestBuild(BuildReference rootBuild, BuildReference testBuild);
    Task PostOnDemandRootBuild(BuildReference rootBuild, Sha1 commit, bool success);
    Task PostOnDemandTestBuild(BuildReference rootBuild, BuildReference testBuild);
}
