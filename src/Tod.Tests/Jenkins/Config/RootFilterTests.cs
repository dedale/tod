using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class RootFilterTests
{
    [Test]
    public void GetHashCode_ConsistentWithEquals_EqualObjectsHaveSameHashCode()
    {
        var filter1 = new RootFilter("MyFilter", "build");
        var filter2 = new RootFilter("MyFilter", "build");

        Assert.That(filter1.Equals(filter2), Is.True);
        Assert.That(filter1.GetHashCode(), Is.EqualTo(filter2.GetHashCode()));
    }

    [Test]
    public void Equals_DifferentObjects_ReturnsFalse()
    {
        var filter1 = new RootFilter("FilterA", "patternA");
        var filter2 = new RootFilter("FilterB", "patternB");
        Assert.That(filter1.Equals(filter2), Is.False);

        var filter11 = new RootFilter("FilterA", "patternAA");
        Assert.That(filter1.Equals(filter11), Is.False);

        var filter3 = new TestFilter("FilterC", "patternC", "group");
        Assert.That(filter1.Equals(filter3), Is.False);
    }

    [Test]
    public void Matches_WithChainGroup_ReturnsTrueAndExtractsChain()
    {
        var filter = new RootFilter("TestFilter", @"^build/(?<chain>.+)$");
        var rootName = new RootName("build/feature-branch");
        var result = filter.Matches(rootName, out var chain);
        Assert.That(result, Is.True);
        Assert.That(chain, Is.EqualTo("feature-branch"));
    }

    [Test]
    public void Matches_WithoutChainGroup_ReturnsTrueAndDefaultChain()
    {
        var filter = new RootFilter("TestFilter", @"^build-.+$");
        var rootName = new RootName("build-123");
        var result = filter.Matches(rootName, out var chain);
        Assert.That(result, Is.True);
        Assert.That(chain, Is.EqualTo(RootFilter.DefaultChain));
    }
}
