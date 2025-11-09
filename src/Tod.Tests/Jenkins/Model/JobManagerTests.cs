using Moq;
using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class JobManagerTests
{
    private const string Url = "http://localhost:8080";

    [TestCase("MAIN-orphan-tests")]
    [TestCase("MAIN-orphan-tests;CUSTOM-another-orphan-tests")]
    public async Task TryLoad_WithMatchingJobs_ReturnsValidJobGroups(string extraJobs)
    {
        var mainBranch = new BranchName("main");
        var referenceJobs = new[]
        {
            new ReferenceJobConfig("MAIN-(?<root>build)", mainBranch, true),
            new ReferenceJobConfig("MAIN-(?<test>.*tests)", mainBranch, false)
        };
        var onDemandJobs = new[]
        {
            new OnDemandJobConfig("CUSTOM-(?<root>build)", true),
            new OnDemandJobConfig("CUSTOM-(?<test>.*tests)", false)
        };
        var config = JenkinsConfig.New(Url, referenceJobs: referenceJobs, onDemandJobs: onDemandJobs);

        var jobNames = new List<JobName>
        {
            new("MAIN-build"),
            new("MAIN-integration-tests"),
            new("CUSTOM-build"),
            new("CUSTOM-integration-tests"),
        };
        extraJobs.Split(';').ToList().ForEach(j => jobNames.Add(new JobName(j)));

        var client = new Mock<IJenkinsClient>(MockBehavior.Strict);
        client.Setup(x => x.GetJobNames(config.MultiBranchFolders)).ReturnsAsync([.. jobNames]);

        var manager = new JobManager(config, client.Object);

        var result = await manager.TryLoad().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);

            // Root group validation
            Assert.That(result.ByRoot, Has.Count.EqualTo(1));
            var rootGroup = result.ByRoot[new RootName("build")];
            Assert.That(rootGroup.ReferenceJobByBranch, Has.Count.EqualTo(1));
            Assert.That(rootGroup.ReferenceJobByBranch[mainBranch].Value, Is.EqualTo("MAIN-build"));
            Assert.That(rootGroup.OnDemandJob.Value, Is.EqualTo("CUSTOM-build"));

            // Test groups validation
            Assert.That(result.ByTest, Has.Count.EqualTo(1));
            var testGroup = result.ByTest[new TestName("integration-tests")];
            Assert.That(testGroup.ReferenceJobByBranch[mainBranch].Value, Is.EqualTo("MAIN-integration-tests"));
            Assert.That(testGroup.OnDemandJob.Value, Is.EqualTo("CUSTOM-integration-tests"));
        }

        client.VerifyAll();
    }

    [Test]
    public async Task TryLoad_WithNoMatchingJobs_ReturnsNull()
    {
        var referenceJobs = new[]
        {
            new ReferenceJobConfig("MAIN-(?<root>build)", new("main"), true),
            new ReferenceJobConfig("MAIN-(?<test>.*)", new("main"), false)
        };
        var onDemandJobs = new[]
        {
            new OnDemandJobConfig("CUSTOM-(?<root>build)", true),
            new OnDemandJobConfig("CUSTOM-(?<test>.*)", false)
        };
        var config = JenkinsConfig.New(Url, referenceJobs: referenceJobs, onDemandJobs: onDemandJobs);

        var jobNames = new[]
        {
            new JobName("OTHER-build"), // No matching jobs
            new JobName("OTHER-test"),
        };

        var client = new Mock<IJenkinsClient>(MockBehavior.Strict);
        client.Setup(x => x.GetJobNames(config.MultiBranchFolders)).ReturnsAsync(jobNames);

        var manager = new JobManager(config, client.Object);
        var result = await manager.TryLoad().ConfigureAwait(false);
        Assert.That(result, Is.Null);
        client.VerifyAll();
    }

    [Test]
    public async Task TryLoad_WithIncompleteJobSet_ReturnsNull()
    {
        var mainBranch = new BranchName("main");
        var referenceJobs = new[]
        {
            new ReferenceJobConfig("MAIN-(?<root>build)", mainBranch, true),
            new ReferenceJobConfig("MAIN-(?<test>.*)", mainBranch, false)
        };
        var onDemandJobs = new[]
        {
            new OnDemandJobConfig("CUSTOM-(?<root>build)", true),
            new OnDemandJobConfig("CUSTOM-(?<test>.*)", false)
        };
        var config = JenkinsConfig.New(Url, referenceJobs: referenceJobs, onDemandJobs: onDemandJobs);

        var jobNames = new[]
        {
            new JobName("MAIN-build"),
            new JobName("MAIN-integration-tests"),
            // Missing CUSTOM-build and CUSTOM-integration-tests
        };

        var client = new Mock<IJenkinsClient>(MockBehavior.Strict);
        client.Setup(x => x.GetJobNames(config.MultiBranchFolders)).ReturnsAsync(jobNames);

        var manager = new JobManager(config, client.Object);
        var result = await manager.TryLoad().ConfigureAwait(false);
        Assert.That(result, Is.Null);
        client.VerifyAll();
    }

    [Test]
    public async Task TryLoad_WithMultipleBranches_ReturnsValidJobGroups()
    {
        var mainBranch = new BranchName("main");
        var prodBranch = new BranchName("prod");
        var referenceJobs = new[]
        {
            new ReferenceJobConfig("MAIN-(?<root>build)", mainBranch, true),
            new ReferenceJobConfig("MAIN-(?<test>.*tests)", mainBranch, false),
            new ReferenceJobConfig("PROD-(?<root>build)", prodBranch, true),
            new ReferenceJobConfig("PROD-(?<test>.*tests)", prodBranch, false)
        };
        var onDemandJobs = new[]
        {
            new OnDemandJobConfig("CUSTOM-(?<root>build)", true),
            new OnDemandJobConfig("CUSTOM-(?<test>.*tests)", false)
        };
        var config = JenkinsConfig.New(Url, referenceJobs: referenceJobs, onDemandJobs: onDemandJobs);

        var jobNames = new[]
        {
            new JobName("MAIN-build"),
            new JobName("MAIN-integration-tests"),
            new JobName("MAIN-unit-tests"),
            new JobName("PROD-build"),
            new JobName("PROD-integration-tests"),
            new JobName("PROD-unit-tests"),
            new JobName("CUSTOM-build"),
            new JobName("CUSTOM-integration-tests"),
            new JobName("CUSTOM-unit-tests")
        };

        var client = new Mock<IJenkinsClient>(MockBehavior.Strict);
        client.Setup(x => x.GetJobNames(config.MultiBranchFolders)).ReturnsAsync(jobNames);

        var manager = new JobManager(config, client.Object);
        var result = await manager.TryLoad().ConfigureAwait(false);
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            // Root group validation
            Assert.That(result.ByRoot, Has.Count.EqualTo(1));
            var rootGroup = result.ByRoot[new RootName("build")];
            Assert.That(rootGroup.ReferenceJobByBranch, Has.Count.EqualTo(2));
            Assert.That(rootGroup.ReferenceJobByBranch[mainBranch].Value, Is.EqualTo("MAIN-build"));
            Assert.That(rootGroup.ReferenceJobByBranch[prodBranch].Value, Is.EqualTo("PROD-build"));
            Assert.That(rootGroup.OnDemandJob.Value, Is.EqualTo("CUSTOM-build"));

            // Test groups validation
            Assert.That(result.ByTest, Has.Count.EqualTo(2));

            var integrationGroup = result.ByTest[new TestName("integration-tests")];
            Assert.That(integrationGroup.ReferenceJobByBranch[mainBranch].Value, Is.EqualTo("MAIN-integration-tests"));
            Assert.That(integrationGroup.ReferenceJobByBranch[prodBranch].Value, Is.EqualTo("PROD-integration-tests"));
            Assert.That(integrationGroup.OnDemandJob.Value, Is.EqualTo("CUSTOM-integration-tests"));

            var unitGroup = result.ByTest[new TestName("unit-tests")];
            Assert.That(unitGroup.ReferenceJobByBranch[mainBranch].Value, Is.EqualTo("MAIN-unit-tests"));
            Assert.That(unitGroup.ReferenceJobByBranch[prodBranch].Value, Is.EqualTo("PROD-unit-tests"));
            Assert.That(unitGroup.OnDemandJob.Value, Is.EqualTo("CUSTOM-unit-tests"));
        }

        client.VerifyAll();
    }
}
