using Tod.Git;

namespace Tod.Jenkins;

internal interface IPostBuildHandler
{
    Task PostBaselineRootBuild(RootBuild rootBuild, JobName[] scheduled);
    Task PostBaselineTestBuild(BuildReference rootBuild, BuildReference testBuild);
    Task PostOnDemandRootBuild(BuildReference rootBuild, Sha1 commit, bool success);
    Task PostOnDemandTestBuild(BuildReference rootBuild, BuildReference testBuild);
}
