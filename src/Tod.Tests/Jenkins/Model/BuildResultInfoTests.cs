using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class BuildResultInfoTests
{
    [Test]
    public void Success_CreatesSuccessResult()
    {
        var result = BuildResultInfo.Success("SUCCESS");

        Assert.That(result.Value, Is.EqualTo("SUCCESS"));
        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void Failure_CreatesFailureResult()
    {
        var result = BuildResultInfo.Failure("FAILURE");

        Assert.That(result.Value, Is.EqualTo("FAILURE"));
        Assert.That(result.IsSuccess, Is.False);
    }
}
