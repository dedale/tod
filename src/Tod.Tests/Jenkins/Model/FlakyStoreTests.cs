using NUnit.Framework;
using Tod.Jenkins;
using Tod.Tests.IO;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class FlakyStoreTests
{
    [Test]
    public void Constructor_NonExistingFile_ReturnsEmptyFlakyTests()
    {
        using var temp = new TempDirectory();
        var jsonPath = Path.Combine(temp.Path, "non_existing_file.json");
        var flakyStore = new FlakyStore(jsonPath);
        var flakyTests = flakyStore.Load();
        Assert.That(flakyTests, Is.Not.Null);
    }
}
