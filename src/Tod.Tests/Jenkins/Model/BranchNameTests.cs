using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class BranchNameTests
{
    [Test]
    public void Constructor_ValidName_SetsValue()
    {
        const string name = "main";
        var branchName = new BranchName(name);
        Assert.That(branchName.Value, Is.EqualTo(name));
    }

    [TestCase("main", "develop", 9)]
    [TestCase("develop", "main", -9)]
    [TestCase("main", "main", 0)]
    public void CompareTo_DifferentValues_ReturnsExpectedOrder(string value1, string value2, int expectedResult)
    {
        var branch1 = new BranchName(value1);
        var branch2 = new BranchName(value2);

        Assert.That(branch1.CompareTo(branch2), Is.EqualTo(expectedResult));
    }

    [Test]
    public void CompareTo_WithNull_ReturnsOne()
    {
        var branchName = new BranchName("main");
        Assert.That(branchName.CompareTo(null), Is.EqualTo(1));
    }
}
