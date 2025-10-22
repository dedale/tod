using NUnit.Framework;
using Tod.Tests.Jenkins;

namespace Tod.Tests;

[TestFixture]
internal sealed class FailedTestTests
{
    [Test]
    public void AssertSerializable_Works()
    {
        new FailedTest("TheClass", "TheTest", "Error details").AssertSerializable();
    }
}
