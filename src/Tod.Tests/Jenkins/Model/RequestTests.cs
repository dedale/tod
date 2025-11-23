using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class RequestTests
{
    private static string GetUserEmail(string userName) => $"{userName}@example.org";

    [Test]
    public void AssertSerializable_Works()
    {
        var request = Request.Create(RandomData.NextSha1(), RandomData.NextSha1(), new("main"), ["tests"], GetUserEmail);
        request.AssertSerializable();
    }
}
