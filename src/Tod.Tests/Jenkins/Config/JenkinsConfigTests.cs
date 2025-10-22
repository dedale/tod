using NUnit.Framework;
using System.Text;
using System.Text.Json;
using Tod.Jenkins;
using Tod.Tests.IO;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class JenkinsConfigTests
{
    [Test]
    public void SaveLoad_Works()
    {
        using var tempDir = new TempDirectory();
        var jobs = new JobName[]
        {
            new("MAIN-build"),
            new("MAIN-tests"),
            new("MAIN-integration-tests"),
            new("CUSTOM-build"),
            new("CUSTOM-tests"),
            new("CUSTOM-integration-tests"),
        };
        var refJobConfigs = new[]
        {
            new ReferenceJobConfig("^MAIN-(?<root>build)", new("main"), true),
            new ReferenceJobConfig("^MAIN-(?<test>.*)", new("main"), false),
        };
        var onDemandJobConfigs = new[]
        {
            new OnDemandJobConfig("CUSTOM-(?<root>build)", true),
            new OnDemandJobConfig("CUSTOM-(?<test>.*)", false),
        };
        var filters = new[]
        {
            new TestFilter("tests", "^tests$", "tests"),
            new TestFilter("integration", "^integration-tests$", "tests"),
        };
        var config = new JenkinsConfig("http://localhost:8080", jobs, refJobConfigs, onDemandJobConfigs, filters);
        var path = Path.Combine(tempDir.Directory.Path, "jenkins_config.json");
        try
        {
            config.Save(path);
            var reloaded = JenkinsConfig.Load(path);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(reloaded.Url, Is.EqualTo(config.Url));
                Assert.That(reloaded.JobNames, Is.EquivalentTo(config.JobNames));
                Assert.That(reloaded.ReferenceJobs, Is.EquivalentTo(config.ReferenceJobs));
                Assert.That(reloaded.OnDemandJobs, Is.EquivalentTo(config.OnDemandJobs));
                Assert.That(reloaded.Filters, Is.EquivalentTo(config.Filters));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Load_NullConfig_ThrowsInvalidOperationException()
    {
        using var tempDir = new TempDirectory();
        var path = Path.Combine(tempDir.Directory.Path, "jenkins_config.json");
        File.WriteAllText(path, JsonSerializer.Serialize((JenkinsConfig)null!), Encoding.UTF8);
        Assert.That(() => JenkinsConfig.Load(path), Throws.InvalidOperationException.And.Message.StartsWith("Cannot deserialize config from "));
    }
}
