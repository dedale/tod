using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class BuildBranchTests
{
    [Test]
    public void Match_WithValidBranchName_ReturnsBranchName()
    {
        var featureBranch = new BranchName("feature");
        var buildBranch = BuildBranch.Create(featureBranch);

        var result = buildBranch.Match(
            onBranch: branch => branch == featureBranch,
            onOnDemand: () => false);
        Assert.That(result, Is.True);

        buildBranch.Match(
            branch => Assert.That(branch, Is.EqualTo(featureBranch)),
            () => Assert.Fail("Expected branch name, but got OnDemand."));
    }

    [Test]
    public void Match_OnDemand()
    {
        var result = BuildBranch.OnDemand.Match(
            onBranch: _ => false,
            onOnDemand: () => true);
        Assert.That(result, Is.True);

        BuildBranch.OnDemand.Match(
            branch => Assert.Fail("Expected OnDemand, but got Branch."),
            () => { });
    }
}
