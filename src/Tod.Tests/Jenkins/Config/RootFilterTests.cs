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
}
