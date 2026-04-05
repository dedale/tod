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
        using var temp = new TempDirectory();
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
            new BaselineJobConfig("^MAIN-(?<root>build)", new("main"), true),
            new BaselineJobConfig("^MAIN-(?<test>.*)", new("main"), false),
        };
        var onDemandJobConfigs = new[]
        {
            new OnDemandJobConfig("CUSTOM-(?<root>build)", true),
            new OnDemandJobConfig("CUSTOM-(?<test>.*)", false),
        };
        var testFilters = new[]
        {
            new TestFilter("tests", "^tests$", "tests"),
            new TestFilter("integration", "^integration-tests$", "tests"),
        };
        var config = JenkinsConfig.New("http://localhost:8080", jobNames: jobs, baselineJobs: refJobConfigs, onDemandJobs: onDemandJobConfigs, testFilters: testFilters);
        var path = Path.Combine(temp.Path, "jenkins_config.json");
        try
        {
            config.Save(path);
            var reloaded = JenkinsConfig.Load(path);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(reloaded.Url, Is.EqualTo(config.Url));
                Assert.That(reloaded.JobNames, Is.EquivalentTo(config.JobNames));
                Assert.That(reloaded.BaselineJobs, Is.EquivalentTo(config.BaselineJobs));
                Assert.That(reloaded.OnDemandJobs, Is.EquivalentTo(config.OnDemandJobs));
                Assert.That(reloaded.RootFilters, Is.EquivalentTo(config.RootFilters));
                Assert.That(reloaded.TestFilters, Is.EquivalentTo(config.TestFilters));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Load_Succeeds_WhenJsonContainsComments()
    {
        using var temp = new TempDirectory();
        var config = JenkinsConfig.New("http://localhost:8080", jobNames: [new("MAIN-build")]);
        var path = Path.Combine(temp.Path, "jenkins_config.json");
        config.Save(path);
        var json = File.ReadAllText(path, Encoding.UTF8);
        File.WriteAllText(path, "// config file\n" + json.Replace("\"url\"", "/* server */ \"url\""), Encoding.UTF8);

        var reloaded = JenkinsConfig.Load(path);

        Assert.That(reloaded.Url, Is.EqualTo(config.Url));
    }

    [Test]
    public void Load_NullConfig_ThrowsInvalidOperationException()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "jenkins_config.json");
        File.WriteAllText(path, JsonSerializer.Serialize((JenkinsConfig)null!), Encoding.UTF8);
        Assert.That(() => JenkinsConfig.Load(path), Throws.InvalidOperationException.And.Message.StartsWith("Cannot deserialize config from "));
    }

    [Test]
    public void SaveJobs_UpdatesJobNamesAndPreservesOtherProperties()
    {
        using var temp = new TempDirectory();
        var originalJobs = new JobName[]
        {
            new("MAIN-build"),
            new("MAIN-tests"),
        };
        var refJobConfigs = new[]
        {
            new BaselineJobConfig("^MAIN-(?<root>build)", new("main"), true),
        };
        var onDemandJobConfigs = new[]
        {
            new OnDemandJobConfig("CUSTOM-(?<root>build)", true),
        };
        var rootFilters = new[]
        {
            new RootFilter("build", "build"),
        };
        var testFilters = new[]
        {
            new TestFilter("tests", "^tests$", "tests"),
        };
        var originalConfig = JenkinsConfig.New(
            "http://localhost:8080",
            multiBranchFolders: ["folder1", "folder2"],
            jobNames: originalJobs,
            baselineJobs: refJobConfigs,
            onDemandJobs: onDemandJobConfigs,
            rootFilters: rootFilters,
            chainTestGroup: "chains",
            testFilters: testFilters,
            keptDays: 30
        );

        var path = Path.Combine(temp.Path, "jenkins_config.json");
        originalConfig.Save(path);

        var updatedJobs = new JobName[]
        {
            new("MAIN-build"),
            new("MAIN-tests"),
            new("MAIN-integration"),
            new("CUSTOM-build"),
        };

        originalConfig.SaveJobs(path, updatedJobs);

        var reloaded = JenkinsConfig.Load(path);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloaded.JobNames, Is.EquivalentTo(updatedJobs));
            Assert.That(reloaded.Url, Is.EqualTo(originalConfig.Url));
            Assert.That(reloaded.MultiBranchFolders, Is.EquivalentTo(originalConfig.MultiBranchFolders));
            Assert.That(reloaded.BaselineJobs, Is.EquivalentTo(originalConfig.BaselineJobs));
            Assert.That(reloaded.OnDemandJobs, Is.EquivalentTo(originalConfig.OnDemandJobs));
            Assert.That(reloaded.RootFilters, Is.EquivalentTo(originalConfig.RootFilters));
            Assert.That(reloaded.ChainTestGroup, Is.EqualTo(originalConfig.ChainTestGroup));
            Assert.That(reloaded.TestFilters, Is.EquivalentTo(originalConfig.TestFilters));
            Assert.That(reloaded.KeptDays, Is.EqualTo(originalConfig.KeptDays));
        }
    }
}
