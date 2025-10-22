using NUnit.Framework;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class RandomTests
{
    [Test]
    public void NextRootBuild_WithNCommits()
    {
        var build = RandomData.NextRootBuild(commits: 3);
        Assert.That(build.Commits, Has.Length.EqualTo(3));
    }
}
