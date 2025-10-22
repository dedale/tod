using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class BuildResultExtensionsTests
{
    [TestCase("SUCCESS", BuildResult.Success)]
    [TestCase("FAILURE", BuildResult.Failure)]
    [TestCase("ABORTED", BuildResult.Aborted)]
    [TestCase("UNSTABLE", BuildResult.Unstable)]
    [TestCase("NOT_BUILT", BuildResult.NotBuilt)]
    public void ToBuildResult_ValidInput_ReturnsExpectedResult(string input, BuildResult expected)
    {
        var result = input.ToBuildResult();
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void ToBuildResult_InvalidInput_ThrowsArgumentException()
    {
        var invalidInput = "INVALID_STATUS";
        Assert.That(invalidInput.ToBuildResult,
            Throws.TypeOf<ArgumentException>().And.Message.Contain($"Unknown build result: '{invalidInput}'"));
    }

    [TestCase(BuildResult.Success, "SUCCESS")]
    [TestCase(BuildResult.Failure, "FAILURE")]
    [TestCase(BuildResult.Aborted, "ABORTED")]
    [TestCase(BuildResult.Unstable, "UNSTABLE")]
    [TestCase(BuildResult.NotBuilt, "NOT_BUILT")]
    public void ToJenkinsString_ValidInput_ReturnsExpectedString(BuildResult input, string expected)
    {
        var result = input.ToJenkinsString();
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void ToJenkinsString_InvalidInput_ThrowsArgumentOutOfRangeException()
    {
        var invalidInput = (BuildResult)999;
        Assert.That(() => invalidInput.ToJenkinsString(),
            Throws.TypeOf<ArgumentOutOfRangeException>().And.Message.Contain($"Unknown build result: '{invalidInput}'"));
    }
}
