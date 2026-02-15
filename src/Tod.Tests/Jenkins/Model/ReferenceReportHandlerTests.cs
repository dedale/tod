using Moq;
using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins.Model;

[TestFixture]
internal sealed class ReferenceReportHandlerTests
{
    private static readonly BranchName s_mainBranch = new("main");
    private static readonly BranchName s_prodBranch = new("PROD");
    private static readonly JobName s_rootJob = new("MAIN-build");
    private static readonly JobName s_testJob1 = new("MAIN-test1");
    private static readonly JobName s_testJob2 = new("MAIN-test2");

    private BranchReference _branchReference;
    private JenkinsConfig _config = null!;
    private Mock<IFlakyTests> _mockFlakyTests;

    [SetUp]
    public void SetUp()
    {
        var store = new InMemoryReferenceStore(s_mainBranch);
        _branchReference = new BranchReference(store);
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
                referenceJobs:
                [
                    new ReferenceJobConfig("MAIN-(?<root>build)", s_mainBranch, true),
                    new ReferenceJobConfig("MAIN-(?<test>test.*)", s_mainBranch, false),
                ],
                referenceReportConfig: new ReferenceReportConfig(true)
            );
        }
        return JenkinsConfig.New("https://jenkins.test");
    }

    [Test]
    public void PostReferenceRootBuild_WithReportsDisabled_DoesNotAddToTracker()
    {
        _config = CreateConfigWithReports(false);
        var handler = new ReferenceReportHandler(_branchReference, _config, _mockFlakyTests.Object);
        var rootBuild = RandomData.NextRootBuild(jobName: s_rootJob.Value, commits: 2, testJobNames: [s_testJob1.Value]);

        handler.PostReferenceRootBuild(rootBuild, [s_testJob1]);

        var tracker = _branchReference.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Null);
    }

    [Test]
    public void PostReferenceRootBuild_WithNoCommitAuthors_DoesNotAddToTracker()
    {
        _config = CreateConfigWithReports(true);
        var handler = new ReferenceReportHandler(_branchReference, _config, _mockFlakyTests.Object);
        var rootBuild = RandomData.NextRootBuild(jobName: s_rootJob.Value, commits: 0, testJobNames: [s_testJob1.Value]);

        handler.PostReferenceRootBuild(rootBuild, [s_testJob1]);

        var tracker = _branchReference.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Null);
    }

    [Test]
    public void PostReferenceRootBuild_WithNoMatchingChainName_DoesNotAddToTracker()
    {
        _config = CreateConfigWithReports(true);
        var handler = new ReferenceReportHandler(_branchReference, _config, _mockFlakyTests.Object);
        var otherRootJob = new JobName("OTHER-build");
        var rootBuild = RandomData.NextRootBuild(jobName: otherRootJob.Value, commits: 2, testJobNames: [s_testJob1.Value]);

        handler.PostReferenceRootBuild(rootBuild, [s_testJob1]);

        var tracker = _branchReference.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Null);
    }

    [Test]
    public async Task PostReferenceRootBuild_WithValidBuild_AddsToTracker()
    {
        _config = CreateConfigWithReports(true);
        var handler = new ReferenceReportHandler(_branchReference, _config, _mockFlakyTests.Object);
        var rootBuild = RandomData.NextRootBuild(jobName: s_rootJob.Value, commits: 2, testJobNames: [s_testJob1.Value]);

        await handler.PostReferenceRootBuild(rootBuild, [s_testJob1]).ConfigureAwait(false);

        var tracker = _branchReference.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Not.Null);
        var serializable = tracker!.ToSerializable();
        Assert.That(serializable.ReferenceChains.Count, Is.EqualTo(1));
        Assert.That(serializable.ContainsBuild(rootBuild), Is.True);
    }

    [Test]
    public async Task PostReferenceTestBuild_WithReportsDisabled_DoesNotMarkTestDone()
    {
        _config = CreateConfigWithReports(false);
        var handler = new ReferenceReportHandler(_branchReference, _config, _mockFlakyTests.Object);
        var rootBuildRef = new BuildReference(s_rootJob, RandomData.NextBuildNumber);
        var testBuildRef = new BuildReference(s_testJob1, RandomData.NextBuildNumber);

        await handler.PostReferenceTestBuild(rootBuildRef, testBuildRef).ConfigureAwait(false);

        var tracker = _branchReference.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Null);
    }

    [Test]
    public async Task PostReferenceTestBuild_WithNoMatchingChainName_DoesNotMarkTestDone()
    {
        _config = CreateConfigWithReports(true);
        var handler = new ReferenceReportHandler(_branchReference, _config, _mockFlakyTests.Object);
        var otherRootJob = new JobName("OTHER-build");
        var rootBuildRef = new BuildReference(otherRootJob, RandomData.NextBuildNumber);
        var testBuildRef = new BuildReference(s_testJob1, RandomData.NextBuildNumber);

        await handler.PostReferenceTestBuild(rootBuildRef, testBuildRef).ConfigureAwait(false);

        var tracker = _branchReference.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Null);
    }

    [Test]
    public async Task PostReferenceTestBuild_WithValidBuild_MarksTestDone()
    {
        _config = CreateConfigWithReports(true);
        var handler = new ReferenceReportHandler(_branchReference, _config, _mockFlakyTests.Object);
        var rootBuild = RandomData.NextRootBuild(jobName: s_rootJob.Value, commits: 2, testJobNames: [s_testJob1.Value, s_testJob2.Value]);

        await handler.PostReferenceRootBuild(rootBuild, [s_testJob1, s_testJob2]).ConfigureAwait(false);

        var testBuildRef = new BuildReference(s_testJob1, RandomData.NextBuildNumber);
        await handler.PostReferenceTestBuild(rootBuild.Reference, testBuildRef).ConfigureAwait(false);

        var tracker = _branchReference.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Not.Null);
        var readyBuilds = tracker!.GetReadyForReport();
        Assert.That(readyBuilds, Is.Empty);
    }

    [Test]
    public async Task PostReferenceTestBuild_WhenAllTestsComplete_MarksReportAsSent()
    {
        _config = CreateConfigWithReports(true);
        var handler = new ReferenceReportHandler(_branchReference, _config, _mockFlakyTests.Object);
        var rootBuild = RandomData.NextRootBuild(jobName: s_rootJob.Value, commits: 2, testJobNames: [s_testJob1.Value]);

        _branchReference.TryAddRoot(s_rootJob);
        _branchReference.TryAdd(rootBuild);

        await handler.PostReferenceRootBuild(rootBuild, [s_testJob1]).ConfigureAwait(false);

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
        _branchReference.TryAdd(testBuild);

        await handler.PostReferenceTestBuild(rootBuild.Reference, testBuild.Reference).ConfigureAwait(false);

        var tracker = _branchReference.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Not.Null);
        var readyBuilds = tracker!.GetReadyForReport();
        Assert.That(readyBuilds, Is.Empty);
    }

    [Test]
    public void PostOnDemandRootBuild_IsNoOp()
    {
        _config = CreateConfigWithReports(true);
        var handler = new ReferenceReportHandler(_branchReference, _config, _mockFlakyTests.Object);
        var rootBuildRef = new BuildReference(s_rootJob, RandomData.NextBuildNumber);
        var commit = RandomData.NextSha1();

        var task = handler.PostOnDemandRootBuild(rootBuildRef, commit, true);

        Assert.That(task.IsCompleted, Is.True);
    }

    [Test]
    public void PostOnDemandTestBuild_IsNoOp()
    {
        _config = CreateConfigWithReports(true);
        var handler = new ReferenceReportHandler(_branchReference, _config, _mockFlakyTests.Object);
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
            referenceJobs:
            [
                new ReferenceJobConfig("MAIN-(?<root>.*build)", s_mainBranch, true),
                new ReferenceJobConfig("MAIN-(?<test>.*test.*)", s_mainBranch, false),
            ],
            referenceReportConfig: new ReferenceReportConfig(true)
        );

        var handler = new ReferenceReportHandler(_branchReference, _config, _mockFlakyTests.Object);

        var frontendRootBuild = RandomData.NextRootBuild(jobName: frontendRootJob.Value, commits: 2, testJobNames: [frontendTestJob.Value]);
        var backendRootBuild = RandomData.NextRootBuild(jobName: backendRootJob.Value, commits: 2, testJobNames: [backendTestJob.Value]);

        //_branchReference.TryAddRoot(frontendRootJob);
        //_branchReference.TryAddRoot(backendRootJob);
        _branchReference.TryAdd(frontendRootBuild);
        _branchReference.TryAdd(backendRootBuild);

        await handler.PostReferenceRootBuild(frontendRootBuild, [frontendTestJob]).ConfigureAwait(false);
        await handler.PostReferenceRootBuild(backendRootBuild, [backendTestJob]).ConfigureAwait(false);

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

        _branchReference.TryAdd(frontendTestBuild);
        _branchReference.TryAdd(backendTestBuild);

        await handler.PostReferenceTestBuild(frontendRootBuild.Reference, frontendTestBuild.Reference).ConfigureAwait(false);
        await handler.PostReferenceTestBuild(backendRootBuild.Reference, backendTestBuild.Reference).ConfigureAwait(false);

        var frontendTracker = _branchReference.GetChainTracker("frontend");
        var backendTracker = _branchReference.GetChainTracker("backend");

        Assert.That(frontendTracker, Is.Not.Null);
        Assert.That(backendTracker, Is.Not.Null);
        Assert.That(frontendTracker!.GetReadyForReport(), Is.Empty);
        Assert.That(backendTracker!.GetReadyForReport(), Is.Empty);
    }

    [Test]
    public async Task PostReferenceTestBuild_WithFailedTests_IncludesInReport()
    {
        _config = CreateConfigWithReports(true);
        var handler = new ReferenceReportHandler(_branchReference, _config, _mockFlakyTests.Object);
        var rootBuild = RandomData.NextRootBuild(jobName: s_rootJob.Value, commits: 2, testJobNames: [s_testJob1.Value]);

        _branchReference.TryAddRoot(s_rootJob);
        _branchReference.TryAdd(rootBuild);

        await handler.PostReferenceRootBuild(rootBuild, [s_testJob1]).ConfigureAwait(false);

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
        _branchReference.TryAdd(testBuild);

        _mockFlakyTests.Setup(f => f.IsFlaky(s_testJob1, It.IsAny<TestId>())).Returns(false);

        await handler.PostReferenceTestBuild(rootBuild.Reference, testBuild.Reference).ConfigureAwait(false);

        var tracker = _branchReference.GetChainTracker(RootFilter.DefaultChain);
        Assert.That(tracker, Is.Not.Null);
        Assert.That(tracker!.GetReadyForReport(), Is.Empty);
    }
}
