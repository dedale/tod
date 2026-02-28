using Moq;
using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins.Model;

[TestFixture]
internal sealed class BaselineReportHandlerTests
{
    private static readonly BranchName s_mainBranch = new("main");
    private static readonly JobName s_rootJob = new("MAIN-build");
    private static readonly JobName s_testJob1 = new("MAIN-test1");
    private static readonly JobName s_testJob2 = new("MAIN-test2");

    private BaselineBranch _baselineBranch;
    private JenkinsConfig _config = null!;
    private Mock<IFlakyTests> _mockFlakyTests;

    [SetUp]
    public void SetUp()
    {
        var store = new InMemoryBaselineStore(s_mainBranch);
        _baselineBranch = new BaselineBranch(store);
        _mockFlakyTests = new Mock<IFlakyTests>(MockBehavior.Strict);
    }

    [TearDown]
    public void TearDown()
    {
        _mockFlakyTests.VerifyAll();
    }

    private static JenkinsConfig CreateConfigWithReports(bool enabled)
    {
        if (enabled)
        {
            return JenkinsConfig.New(
                "https://jenkins.test",
                rootFilters:
                [
                    new RootFilter("build", "build")
                ],
                testFilters:
                [
                    new TestFilter("test", "test.*", "tests")
                ],
                baselineJobs:
                [
                    new BaselineJobConfig("MAIN-(?<root>build)", s_mainBranch, true),
                    new BaselineJobConfig("MAIN-(?<test>test.*)", s_mainBranch, false),
                ],
                baselineReportConfig: new BaselineReportConfig(true)
            );
        }
        return JenkinsConfig.New("https://jenkins.test");
    }

    [Test]
    public void PostReferenceRootBuild_WithReportsDisabled_DoesNotAddToTracker()
    {
        _config = CreateConfigWithReports(false);
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var rootBuild = RandomData.NextRootBuild(jobName: s_rootJob.Value, commits: 2, testJobNames: [s_testJob1.Value]);

        handler.PostBaselineRootBuild(rootBuild, [s_testJob1]);

        var tracker = _baselineBranch.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Null);
    }

    [Test]
    public void PostReferenceRootBuild_WithNoCommitAuthors_DoesNotAddToTracker()
    {
        _config = CreateConfigWithReports(true);
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var rootBuild = RandomData.NextRootBuild(jobName: s_rootJob.Value, commits: 0, testJobNames: [s_testJob1.Value]);

        handler.PostBaselineRootBuild(rootBuild, [s_testJob1]);

        var tracker = _baselineBranch.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Null);
    }

    [Test]
    public void PostReferenceRootBuild_WithNoMatchingChainName_DoesNotAddToTracker()
    {
        _config = CreateConfigWithReports(true);
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var otherRootJob = new JobName("OTHER-build");
        var rootBuild = RandomData.NextRootBuild(jobName: otherRootJob.Value, commits: 2, testJobNames: [s_testJob1.Value]);

        handler.PostBaselineRootBuild(rootBuild, [s_testJob1]);

        var tracker = _baselineBranch.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Null);
    }

    [Test]
    public async Task PostReferenceRootBuild_WithValidBuild_AddsToTracker()
    {
        _config = CreateConfigWithReports(true);
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var rootBuild = RandomData.NextRootBuild(jobName: s_rootJob.Value, commits: 2, testJobNames: [s_testJob1.Value]);

        await handler.PostBaselineRootBuild(rootBuild, [s_testJob1]).ConfigureAwait(false);

        var tracker = _baselineBranch.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Not.Null);
        var serializable = tracker!.ToSerializable();
        Assert.That(serializable.BaselineChains.Count, Is.EqualTo(1));
        Assert.That(serializable.ContainsBuild(rootBuild), Is.True);
    }

    [Test]
    public async Task PostReferenceTestBuild_WithReportsDisabled_DoesNotMarkTestDone()
    {
        _config = CreateConfigWithReports(false);
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var rootBuildRef = new BuildReference(s_rootJob, RandomData.NextBuildNumber);
        var testBuildRef = new BuildReference(s_testJob1, RandomData.NextBuildNumber);

        await handler.PostBaselineTestBuild(rootBuildRef, testBuildRef).ConfigureAwait(false);

        var tracker = _baselineBranch.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Null);
    }

    [Test]
    public async Task PostReferenceTestBuild_WithNoMatchingChainName_DoesNotMarkTestDone()
    {
        _config = CreateConfigWithReports(true);
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var otherRootJob = new JobName("OTHER-build");
        var otherTestJob = new JobName("OTHER-test");
        var rootBuildRef = new BuildReference(otherRootJob, RandomData.NextBuildNumber);
        var testBuildRef = new BuildReference(otherTestJob, RandomData.NextBuildNumber);

        await handler.PostBaselineTestBuild(rootBuildRef, testBuildRef).ConfigureAwait(false);

        var tracker = _baselineBranch.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Null);
    }

    [Test]
    public async Task PostReferenceTestBuild_WithValidBuild_MarksTestDone()
    {
        _config = CreateConfigWithReports(true);
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var rootBuild = RandomData.NextRootBuild(jobName: s_rootJob.Value, commits: 2, testJobNames: [s_testJob1.Value, s_testJob2.Value]);

        await handler.PostBaselineRootBuild(rootBuild, [s_testJob1, s_testJob2]).ConfigureAwait(false);

        var testBuildRef = new BuildReference(s_testJob1, RandomData.NextBuildNumber);
        await handler.PostBaselineTestBuild(rootBuild.Reference, testBuildRef).ConfigureAwait(false);

        var tracker = _baselineBranch.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Not.Null);
        var readyChains = tracker!.GetReadyForReport();
        Assert.That(readyChains, Is.Empty);
    }

    [Test]
    public async Task PostReferenceTestBuild_WhenAllTestsComplete_MarksReportAsSent()
    {
        _config = CreateConfigWithReports(true);
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var rootBuild = RandomData.NextRootBuild(jobName: s_rootJob.Value, commits: 2, testJobNames: [s_testJob1.Value]);

        _baselineBranch.TryAddRoot(s_rootJob);
        _baselineBranch.TryAdd(rootBuild);

        await handler.PostBaselineRootBuild(rootBuild, [s_testJob1]).ConfigureAwait(false);

        var testBuild = new TestBuild(
            s_testJob1,
            "test-id",
            RandomData.NextBuildNumber,
            DateTime.UtcNow.AddMinutes(-10),
            DateTime.UtcNow,
            true,
            [rootBuild.Reference],
            []
        );
        _baselineBranch.TryAdd(testBuild);

        await handler.PostBaselineTestBuild(rootBuild.Reference, testBuild.Reference).ConfigureAwait(false);

        var tracker = _baselineBranch.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Not.Null);
        var readyChains = tracker!.GetReadyForReport();
        Assert.That(readyChains, Is.Empty);
    }

    [Test]
    public void PostOnDemandRootBuild_IsNoOp()
    {
        _config = CreateConfigWithReports(true);
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var rootBuildRef = new BuildReference(s_rootJob, RandomData.NextBuildNumber);
        var commit = RandomData.NextSha1();

        var task = handler.PostOnDemandRootBuild(rootBuildRef, commit, true);

        Assert.That(task.IsCompleted, Is.True);
    }

    [Test]
    public void PostOnDemandTestBuild_IsNoOp()
    {
        _config = CreateConfigWithReports(true);
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var rootBuildRef = new BuildReference(s_rootJob, RandomData.NextBuildNumber);
        var testBuildRef = new BuildReference(s_testJob1, RandomData.NextBuildNumber);

        var task = handler.PostOnDemandTestBuild(rootBuildRef, testBuildRef);

        Assert.That(task.IsCompleted, Is.True);
    }

    [Test]
    public async Task PostReferenceTestBuild_WithMultipleRootFilters_TracksEachChain()
    {
        var frontendRootJob = new JobName("MAIN-frontend-build");
        var frontendTestJob = new JobName("MAIN-frontend-test1");
        var backendRootJob = new JobName("MAIN-backend-build");
        var backendTestJob = new JobName("MAIN-backend-test1");

        _config = JenkinsConfig.New(
            "https://jenkins.test",
            rootFilters:
            [
                new RootFilter("frontend", "(?<chain>frontend)-build$"),
                new RootFilter("backend", "(?<chain>backend)-build$")
            ],
            testFilters:
            [
                new TestFilter("test", "test.*", "tests")
            ],
            baselineJobs:
            [
                new BaselineJobConfig("MAIN-(?<root>.*build)", s_mainBranch, true),
                new BaselineJobConfig("MAIN-(?<test>.*test.*)", s_mainBranch, false),
            ],
            baselineReportConfig: new BaselineReportConfig(true)
        );

        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);

        var frontendRootBuild = RandomData.NextRootBuild(jobName: frontendRootJob.Value, commits: 2, testJobNames: [frontendTestJob.Value]);
        var backendRootBuild = RandomData.NextRootBuild(jobName: backendRootJob.Value, commits: 2, testJobNames: [backendTestJob.Value]);

        _baselineBranch.TryAdd(frontendRootBuild);
        _baselineBranch.TryAdd(backendRootBuild);

        await handler.PostBaselineRootBuild(frontendRootBuild, [frontendTestJob]).ConfigureAwait(false);
        await handler.PostBaselineRootBuild(backendRootBuild, [backendTestJob]).ConfigureAwait(false);

        var frontendTestBuild = new TestBuild(
            frontendTestJob,
            "test-id-1",
            RandomData.NextBuildNumber,
            DateTime.UtcNow.AddMinutes(-10),
            DateTime.UtcNow,
            true,
            [frontendRootBuild.Reference],
            []
        );
        var backendTestBuild = new TestBuild(
            backendTestJob,
            "test-id-2",
            RandomData.NextBuildNumber,
            DateTime.UtcNow.AddMinutes(-10),
            DateTime.UtcNow,
            true,
            [backendRootBuild.Reference],
            []
        );

        _baselineBranch.TryAdd(frontendTestBuild);
        _baselineBranch.TryAdd(backendTestBuild);

        await handler.PostBaselineTestBuild(frontendRootBuild.Reference, frontendTestBuild.Reference).ConfigureAwait(false);
        await handler.PostBaselineTestBuild(backendRootBuild.Reference, backendTestBuild.Reference).ConfigureAwait(false);

        var frontendTracker = _baselineBranch.GetChainTracker("frontend");
        var backendTracker = _baselineBranch.GetChainTracker("backend");

        Assert.That(frontendTracker, Is.Not.Null);
        Assert.That(backendTracker, Is.Not.Null);
        Assert.That(frontendTracker!.GetReadyForReport(), Is.Empty);
        Assert.That(backendTracker!.GetReadyForReport(), Is.Empty);
    }

    [Test]
    public async Task PostReferenceTestBuild_WithFailedTests_IncludesInReport()
    {
        _config = CreateConfigWithReports(true);
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var rootBuild = RandomData.NextRootBuild(jobName: s_rootJob.Value, commits: 2, testJobNames: [s_testJob1.Value]);

        _baselineBranch.TryAddRoot(s_rootJob);
        _baselineBranch.TryAdd(rootBuild);

        await handler.PostBaselineRootBuild(rootBuild, [s_testJob1]).ConfigureAwait(false);

        var failedTests = new[]
        {
            new FailedTest("TestClass", "TestMethod", "Error details")
        };

        var testBuild = new TestBuild(
            s_testJob1,
            "test-id",
            RandomData.NextBuildNumber,
            DateTime.UtcNow.AddMinutes(-10),
            DateTime.UtcNow,
            false,
            [rootBuild.Reference],
            failedTests
        );
        _baselineBranch.TryAdd(testBuild);

        _mockFlakyTests.Setup(f => f.IsFlaky(s_testJob1, It.IsAny<TestId>())).Returns(false);

        await handler.PostBaselineTestBuild(rootBuild.Reference, testBuild.Reference).ConfigureAwait(false);

        var tracker = _baselineBranch.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Not.Null);
        Assert.That(tracker!.GetReadyForReport(), Is.Empty);
    }

    [Test]
    public async Task TryGetChain_RootJobWithDefaultChain_ReturnsDefaultChain()
    {
        _config = JenkinsConfig.New(
            "https://jenkins.test",
            rootFilters:
            [
                new RootFilter("build", "build")
            ],
            baselineJobs:
            [
                new BaselineJobConfig("MAIN-(?<root>build)", s_mainBranch, true)
            ],
            baselineReportConfig: new BaselineReportConfig(true)
        );
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var rootBuild = RandomData.NextRootBuild(jobName: s_rootJob.Value, commits: 1, testJobNames: []);

        await handler.PostBaselineRootBuild(rootBuild, []).ConfigureAwait(false);

        var tracker = _baselineBranch.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Not.Null);
    }

    [Test]
    public async Task TryGetChain_RootJobWithNamedChain_ReturnsNamedChain()
    {
        _config = JenkinsConfig.New(
            "https://jenkins.test",
            rootFilters:
            [
                new RootFilter("frontend-build", "^(?<chain>frontend)-build$")
            ],
            baselineJobs:
            [
                new BaselineJobConfig("MAIN-(?<root>frontend-build)", s_mainBranch, true)
            ],
            baselineReportConfig: new BaselineReportConfig(true)
        );
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var frontendJob = new JobName("MAIN-frontend-build");
        var rootBuild = RandomData.NextRootBuild(jobName: frontendJob.Value, commits: 1, testJobNames: []);

        await handler.PostBaselineRootBuild(rootBuild, []).ConfigureAwait(false);

        var tracker = _baselineBranch.GetChainTracker("frontend");
        Assert.That(tracker, Is.Not.Null);
    }

    [Test]
    public async Task TryGetChain_TestJobWithDefaultChain_ReturnsDefaultChain()
    {
        _config = JenkinsConfig.New(
            "https://jenkins.test",
            rootFilters:
            [
                new RootFilter("build", "build")
            ],
            testFilters:
            [
                new TestFilter("unit", "unit", "tests")
            ],
            baselineJobs:
            [
                new BaselineJobConfig("MAIN-(?<root>build)", s_mainBranch, true),
                new BaselineJobConfig("MAIN-(?<test>unit)", s_mainBranch, false)
            ],
            baselineReportConfig: new BaselineReportConfig(true)
        );
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var testJob = new JobName("MAIN-unit");
        var rootBuild = RandomData.NextRootBuild(jobName: s_rootJob.Value, commits: 1, testJobNames: [testJob.Value]);

        _baselineBranch.TryAddRoot(s_rootJob);
        _baselineBranch.TryAddTest(testJob);
        _baselineBranch.TryAdd(rootBuild);
        await handler.PostBaselineRootBuild(rootBuild, [testJob]).ConfigureAwait(false);

        var testBuild = new TestBuild(
            testJob,
            "test-id",
            RandomData.NextBuildNumber,
            DateTime.UtcNow.AddMinutes(-10),
            DateTime.UtcNow,
            true,
            [rootBuild.Reference],
            []
        );
        _baselineBranch.TryAdd(testBuild);

        await handler.PostBaselineTestBuild(rootBuild.Reference, testBuild.Reference).ConfigureAwait(false);

        var tracker = _baselineBranch.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Not.Null);
    }

    [Test]
    public async Task TryGetChain_TestJobWithNamedChain_ReturnsNamedChain()
    {
        _config = JenkinsConfig.New(
            "https://jenkins.test",
            rootFilters:
            [
                new RootFilter("frontend-build", "^(?<chain>frontend)-build$")
            ],
            testFilters:
            [
                new TestFilter("frontend-unit", "^(?<chain>frontend)-unit$", "tests")
            ],
            baselineJobs:
            [
                new BaselineJobConfig("MAIN-(?<root>frontend-build)", s_mainBranch, true),
                new BaselineJobConfig("MAIN-(?<test>frontend-unit)", s_mainBranch, false)
            ],
            baselineReportConfig: new BaselineReportConfig(true)
        );
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var frontendRootJob = new JobName("MAIN-frontend-build");
        var frontendTestJob = new JobName("MAIN-frontend-unit");
        var rootBuild = RandomData.NextRootBuild(jobName: frontendRootJob.Value, commits: 1, testJobNames: [frontendTestJob.Value]);

        _baselineBranch.TryAddRoot(frontendRootJob);
        _baselineBranch.TryAddTest(frontendTestJob);
        _baselineBranch.TryAdd(rootBuild);
        await handler.PostBaselineRootBuild(rootBuild, [frontendTestJob]).ConfigureAwait(false);

        var testBuild = new TestBuild(
            frontendTestJob,
            "test-id",
            RandomData.NextBuildNumber,
            DateTime.UtcNow.AddMinutes(-10),
            DateTime.UtcNow,
            true,
            [rootBuild.Reference],
            []
        );
        _baselineBranch.TryAdd(testBuild);

        await handler.PostBaselineTestBuild(rootBuild.Reference, testBuild.Reference).ConfigureAwait(false);

        var tracker = _baselineBranch.GetChainTracker("frontend");
        Assert.That(tracker, Is.Not.Null);
    }

    [Test]
    public async Task TryGetChain_JobNotMatchingBaselinePattern_ReturnsFalse()
    {
        _config = JenkinsConfig.New(
            "https://jenkins.test",
            rootFilters:
            [
                new RootFilter("build", "build")
            ],
            baselineJobs:
            [
                new BaselineJobConfig("MAIN-(?<root>build)", s_mainBranch, true)
            ],
            baselineReportConfig: new BaselineReportConfig(true)
        );
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var otherJob = new JobName("OTHER-build");
        var rootBuild = RandomData.NextRootBuild(jobName: otherJob.Value, commits: 1, testJobNames: []);

        await handler.PostBaselineRootBuild(rootBuild, []).ConfigureAwait(false);

        var tracker = _baselineBranch.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Null);
    }

    [Test]
    public async Task TryGetChain_RootJobNotMatchingAnyFilter_ReturnsFalse()
    {
        _config = JenkinsConfig.New(
            "https://jenkins.test",
            rootFilters:
            [
                new RootFilter("deploy", "deploy")
            ],
            baselineJobs:
            [
                new BaselineJobConfig("MAIN-(?<root>build)", s_mainBranch, true)
            ],
            baselineReportConfig: new BaselineReportConfig(true)
        );
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var rootBuild = RandomData.NextRootBuild(jobName: s_rootJob.Value, commits: 1, testJobNames: []);

        await handler.PostBaselineRootBuild(rootBuild, []).ConfigureAwait(false);

        var tracker = _baselineBranch.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Null);
    }

    [Test]
    public async Task TryGetChain_TestJobNotMatchingAnyFilter_ReturnsFalse()
    {
        _config = JenkinsConfig.New(
            "https://jenkins.test",
            rootFilters:
            [
                new RootFilter("build", "build")
            ],
            testFilters:
            [
                new TestFilter("integration", "integration", "tests")
            ],
            baselineJobs:
            [
                new BaselineJobConfig("MAIN-(?<root>build)", s_mainBranch, true),
                new BaselineJobConfig("MAIN-(?<test>unit)", s_mainBranch, false)
            ],
            baselineReportConfig: new BaselineReportConfig(true)
        );
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var testJob = new JobName("MAIN-unit");
        var rootBuild = RandomData.NextRootBuild(jobName: s_rootJob.Value, commits: 1, testJobNames: [testJob.Value]);

        _baselineBranch.TryAddRoot(s_rootJob);
        _baselineBranch.TryAddTest(testJob);
        _baselineBranch.TryAdd(rootBuild);
        await handler.PostBaselineRootBuild(rootBuild, [testJob]).ConfigureAwait(false);

        var testBuild = new TestBuild(
            testJob,
            "test-id",
            RandomData.NextBuildNumber,
            DateTime.UtcNow.AddMinutes(-10),
            DateTime.UtcNow,
            true,
            [rootBuild.Reference],
            []
        );

        await handler.PostBaselineTestBuild(rootBuild.Reference, testBuild.Reference).ConfigureAwait(false);

        var tracker = _baselineBranch.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Not.Null);
        var serializable = tracker!.ToSerializable();
        Assert.That(serializable.BaselineChains[0].TestBuilds.Count, Is.EqualTo(1));
        Assert.That(serializable.BaselineChains[0].TestBuilds[0].Pending, Is.Not.Null);
        Assert.That(serializable.BaselineChains[0].TestBuilds[0].Pending, Is.EqualTo(testJob));
    }

    [Test]
    public async Task TryGetChain_CalledTwiceForSameJob_UsesCachedValue()
    {
        _config = JenkinsConfig.New(
            "https://jenkins.test",
            rootFilters:
            [
                new RootFilter("build", "build")
            ],
            baselineJobs:
            [
                new BaselineJobConfig("MAIN-(?<root>build)", s_mainBranch, true)
            ],
            baselineReportConfig: new BaselineReportConfig(true)
        );
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var rootBuild1 = RandomData.NextRootBuild(jobName: s_rootJob.Value, buildNumber: 100, commits: 1, testJobNames: []);
        var rootBuild2 = RandomData.NextRootBuild(jobName: s_rootJob.Value, buildNumber: 101, commits: 1, testJobNames: []);

        await handler.PostBaselineRootBuild(rootBuild1, []).ConfigureAwait(false);
        await handler.PostBaselineRootBuild(rootBuild2, []).ConfigureAwait(false);

        var tracker = _baselineBranch.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Not.Null);
        var serializable = tracker!.ToSerializable();
        Assert.That(serializable.BaselineChains.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task TryGetChain_DifferentJobsSameChain_UseSameTracker()
    {
        _config = JenkinsConfig.New(
            "https://jenkins.test",
            rootFilters:
            [
                new RootFilter("build1", "build1"),
                new RootFilter("build2", "build2")
            ],
            baselineJobs:
            [
                new BaselineJobConfig("MAIN-(?<root>build1)", s_mainBranch, true),
                new BaselineJobConfig("MAIN-(?<root>build2)", s_mainBranch, true)
            ],
            baselineReportConfig: new BaselineReportConfig(true)
        );
        var handler = new BaselineReportHandler(_baselineBranch, _config, _mockFlakyTests.Object);
        var job1 = new JobName("MAIN-build1");
        var job2 = new JobName("MAIN-build2");
        var rootBuild1 = RandomData.NextRootBuild(jobName: job1.Value, commits: 1, testJobNames: []);
        var rootBuild2 = RandomData.NextRootBuild(jobName: job2.Value, commits: 1, testJobNames: []);

        await handler.PostBaselineRootBuild(rootBuild1, []).ConfigureAwait(false);
        await handler.PostBaselineRootBuild(rootBuild2, []).ConfigureAwait(false);

        var tracker = _baselineBranch.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Not.Null);
        var serializable = tracker!.ToSerializable();
        Assert.That(serializable.BaselineChains.Count, Is.EqualTo(2));
    }
}

