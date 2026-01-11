using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class TestIdTests
{
    [Test]
    public void CompareTo_Works()
    {
        var sorted = new List<TestId?>
        {
            null,
            new("Class11", "Test11"),
            new("Class11", "Test12"),
            new("Class12", "Test21"),
            new("Class12", "Test22"),
        };
        var shuffle = new List<TestId?>(sorted);
        shuffle.Shuffle();
        shuffle.Sort();
        Assert.That(sorted, Is.EqualTo(shuffle));
    }

    [Test]
    public void CompareTo_Null_ReturnsOne()
    {
        var testId = new TestId("Class", "Test");
        Assert.That(testId.CompareTo(null), Is.EqualTo(1));
    }

    [Test]
    public void CompareTo_SameInstance_ReturnsZero()
    {
        var testId = new TestId("Class", "Test");
        Assert.That(testId.CompareTo(testId), Is.Zero);
    }
}
