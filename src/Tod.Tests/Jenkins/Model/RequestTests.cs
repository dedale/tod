using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class RequestTests
{
    private static readonly string s_userEmail = $"user@example.org";

    [Test]
    public void AssertSerializable_Works()
    {
        var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), new("main"), ["tests"], s_userEmail);
        request.AssertSerializable();
    }
}
