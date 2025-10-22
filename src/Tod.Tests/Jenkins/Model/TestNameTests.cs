using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class TestNameTests
{
    [Test]
    public void Constructor_ValidName_SetsValue()
    {
        const string name = "integration-tests";
        var testName = new TestName(name);
        Assert.That(testName.Value, Is.EqualTo(name));
    }

    [TestCase("test-a", "test-b", -1)]
    [TestCase("test-b", "test-a", 1)]
    [TestCase("test-a", "test-a", 0)]
    public void CompareTo_DifferentValues_ReturnsExpectedOrder(string value1, string value2, int expectedResult)
    {
        var test1 = new TestName(value1);
        var test2 = new TestName(value2);

        Assert.That(test1.CompareTo(test2), Is.EqualTo(expectedResult));
    }

    [Test]
    public void CompareTo_WithNull_ReturnsOne()
    {
        var testName = new TestName("integration-tests");
        Assert.That(testName.CompareTo(null), Is.EqualTo(1));
    }
}
