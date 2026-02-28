using Moq;
using NUnit.Framework;
using Tod.Jenkins;
using Tod.Net;

namespace Tod.Tests.Jenkins.Model;

[TestFixture]
internal sealed class BaselineReportSenderTests
{
    private readonly BranchName _branch = new("master");
    private readonly JobName _testJob1 = new("TestJob1");
    private readonly JobName _testJob2 = new("TestJob2");

    private Mock<IMailSender> _mockMailSender;
    private Mock<IJobLinker> _mockJobLinker;
    private BaselineReportSender _sender;
    private IFlakyTests _flakyTests;

    [SetUp]
    public void SetUp()
    {
        _mockMailSender = new Mock<IMailSender>(MockBehavior.Strict);
        _mockJobLinker = new Mock<IJobLinker>(MockBehavior.Loose);
        _mockJobLinker.Setup(l => l.GetUrl(It.IsAny<JobName>(), It.IsAny<int>())).Returns("http://jenkins/job/1");
        _mockJobLinker.Setup(l => l.GetUrl(It.IsAny<JobName>())).Returns("http://jenkins/job");
        _sender = new BaselineReportSender(_mockJobLinker.Object, _mockMailSender.Object, hideFlakies: false);
        var flakyStore = InMemoryFlakyStore.Default;
        _flakyTests = new FlakyTests(flakyStore);
    }

    [TearDown]
    public void TearDown()
    {
        _mockMailSender.VerifyAll();
    }

    [Test]
    public async Task SendReport_WithNoAuthors_LogsWarningAndDoesNotSend()
    {
        var rootBuild = RandomData.NextRootBuild(commits: 0);
        var report = new BaselineChainReport(_branch, "TestChain", [rootBuild], []);

        await _sender.SendReport(report);
    }

    [Test]
    public async Task SendReport_WithNoNewFailures_DoesNotSend()
    {
        var rootBuild = RandomData.NextRootBuild(commits: 1);
        var diff = FailedTestDiffer.Diff(_testJob1, [], [], _flakyTests);
        var report = new BaselineChainReport(_branch, "TestChain", [rootBuild], new Dictionary<JobName, BuildDiffResult> { [_testJob1] = NextBuildDiffResult(_testJob1, diff) });

        await _sender.SendReport(report);
    }

    [Test]
    public async Task SendReport_WithNewFailures_SendsToAllAuthors()
    {
        var rootBuild = RandomData.NextRootBuild(commits: 2);
        var authors = rootBuild.Commits.Select(c => c.Author);
        var recipients = string.Join(", ", authors.Select(a => a?.Email).Distinct());

        var failedTest = new FailedTest("TestClass", "TestMethod", "Error");
        var diff = FailedTestDiffer.Diff(_testJob1, [], [failedTest], _flakyTests);

        var report = new BaselineChainReport(_branch, "TestChain", [rootBuild], new Dictionary<JobName, BuildDiffResult> { [_testJob1] = NextBuildDiffResult(_testJob1, diff) });

        _mockMailSender.Setup(m => m.Send(
            recipients,
            It.Is<string>(s => s.StartsWith("master Build Report")),
            It.IsAny<string>(),
            It.IsAny<string>()
        )).Returns(Task.CompletedTask);

        await _sender.SendReport(report);
    }

    [Test]
    public async Task SendReport_WithSingleAuthor_SendsOnce()
    {
        var rootBuild = RandomData.NextRootBuild(commits: 1);
        var author = rootBuild.Commits[0].Author;

        var failedTest = new FailedTest("TestClass", "TestMethod", "Error");
        var diff = FailedTestDiffer.Diff(_testJob1, [], [failedTest], _flakyTests);

        var report = new BaselineChainReport(_branch, "TestChain", [rootBuild], new Dictionary<JobName, BuildDiffResult> { [_testJob1] = NextBuildDiffResult(_testJob1, diff) });

        _mockMailSender.Setup(m => m.Send(
            author!.Email!,
            It.Is<string>(s => s.StartsWith("master Build Report")),
            It.IsAny<string>(),
            It.IsAny<string>()
        )).Returns(Task.CompletedTask);

        await _sender.SendReport(report);
    }

    [Test]
    public async Task SendReport_WithMultipleRootBuilds_CombinesAuthors()
    {
        var rootBuild1 = RandomData.NextRootBuild(buildNumber: 100, commits: 1);
        var rootBuild2 = RandomData.NextRootBuild(buildNumber: 101, commits: 1);

        var uniqueAuthors = new[] { rootBuild1.Commits[0].Author?.Email, rootBuild2.Commits[0].Author?.Email }
            .Distinct()
            .ToArray();
        var recipients = string.Join(", ", uniqueAuthors);

        var failedTest = new FailedTest("TestClass", "TestMethod", "Error");
        var diff = FailedTestDiffer.Diff(_testJob1, [], [failedTest], _flakyTests);

        var report = new BaselineChainReport(_branch, "TestChain", [rootBuild1, rootBuild2], new Dictionary<JobName, BuildDiffResult> { [_testJob1] = NextBuildDiffResult(_testJob1, diff) });

        _mockMailSender.Setup(m => m.Send(
            recipients,
            It.Is<string>(s => s.StartsWith("master Build Report")),
            It.IsAny<string>(),
            It.IsAny<string>()
        )).Returns(Task.CompletedTask);

        await _sender.SendReport(report);
    }

    [Test]
    public async Task SendReport_WithEmailSendFailure_LogsError()
    {
        var rootBuild = RandomData.NextRootBuild(commits: 2);
        var authors = rootBuild.Commits.Select(c => c.Author);
        var recipients = string.Join(", ", authors.Select(a => a?.Email).Distinct());

        var failedTest = new FailedTest("TestClass", "TestMethod", "Error");
        var diff = FailedTestDiffer.Diff(_testJob1, [], [failedTest], _flakyTests);

        var report = new BaselineChainReport(_branch, "TestChain", [rootBuild], new Dictionary<JobName, BuildDiffResult> { [_testJob1] = NextBuildDiffResult(_testJob1, diff) });

        _mockMailSender.Setup(m => m.Send(
            recipients,
            It.Is<string>(s => s.StartsWith("master Build Report")),
            It.IsAny<string>(),
            It.IsAny<string>()
        )).ThrowsAsync(new Exception("SMTP error"));

        await _sender.SendReport(report);
    }

    [Test]
    public async Task SendReport_WithEmptyAuthorEmail_SkipsAuthor()
    {
        var commits = new[] { new Commit(RandomData.NextSha1()) };
        var authors = new[] { new CommitAuthor("Author Name", string.Empty) };
        var rootBuild = new RootBuild(
            new JobName("TestJob"),
            "id",
            RandomData.NextBuildNumber,
            DateTime.UtcNow.AddMinutes(-10),
            DateTime.UtcNow,
            true,
            commits,
            []
        );

        var failedTest = new FailedTest("TestClass", "TestMethod", "Error");
        var diff = FailedTestDiffer.Diff(_testJob1, [], [failedTest], _flakyTests);

        var report = new BaselineChainReport(_branch, "TestChain", [rootBuild], new Dictionary<JobName, BuildDiffResult> { [_testJob1] = NextBuildDiffResult(_testJob1, diff) });

        await _sender.SendReport(report);
    }

    [Test]
    public async Task SendReport_WithMultipleTestJobs_IncludesAllInReport()
    {
        var rootBuild = RandomData.NextRootBuild(commits: 1);
        var author = rootBuild.Commits[0].Author;

        var failedTest1 = new FailedTest("TestClass1", "TestMethod1", "Error1");
        var failedTest2 = new FailedTest("TestClass2", "TestMethod2", "Error2");
        var diff1 = FailedTestDiffer.Diff(_testJob1, [], [failedTest1], _flakyTests);
        var diff2 = FailedTestDiffer.Diff(_testJob2, [], [failedTest2], _flakyTests);

        var report = new BaselineChainReport(
            _branch,
            "TestChain",
            [rootBuild],
            new Dictionary<JobName, BuildDiffResult>
            {
                [_testJob1] = NextBuildDiffResult(_testJob1, diff1),
                [_testJob2] = NextBuildDiffResult(_testJob2, diff2)
            }
        );

        _mockMailSender.Setup(m => m.Send(
            author!.Email!,
            It.Is<string>(s => s.StartsWith("master Build Report")),
            It.IsAny<string>(),
            It.Is<string>(body => body.Contains("TestClass1") && body.Contains("TestClass2"))
        )).Returns(Task.CompletedTask);

        await _sender.SendReport(report);
    }

    [Test]
    public async Task SendReport_IncludesChainNameInSubject()
    {
        var rootBuild = RandomData.NextRootBuild(commits: 1);
        var author = rootBuild.Commits[0].Author;

        var failedTest = new FailedTest("TestClass", "TestMethod", "Error");
        var diff = FailedTestDiffer.Diff(_testJob1, [], [failedTest], _flakyTests);

        var report = new BaselineChainReport(_branch, "MyCustomChain", [rootBuild], new Dictionary<JobName, BuildDiffResult> { [_testJob1] = NextBuildDiffResult(_testJob1, diff) });

        _mockMailSender.Setup(m => m.Send(
            author!.Email!,
            It.Is<string>(s => s.Contains("Build Report") && s.Contains(rootBuild.Reference.ToString())),
            It.IsAny<string>(),
            It.IsAny<string>()
        )).Returns(Task.CompletedTask);

        await _sender.SendReport(report);
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task SendReport_WithFlakyTests_IncludedBasedOnHideFlakies(bool hideFlakyTests, bool isIncluded)
    {
        var testJob3 = new JobName("TestJob3");
        var sender = new BaselineReportSender(_mockJobLinker.Object, _mockMailSender.Object, hideFlakyTests);
        var rootBuild = RandomData.NextRootBuild(commits: 1);

        const string sharedError = "SharedError";
        var uniqueTest = new FailedTest("UniqueTestClass", "UniqueTestMethod", "UniqueError");
        var flakyTest = new FailedTest("FlakyTestClass", "FlakyTestMethod", sharedError);
        var sharedErrorTest = new FailedTest("SharedErrorTestClass", "SharedErrorTestMethod", sharedError);

        var mockFlakyTests = new Mock<IFlakyTests>(MockBehavior.Strict);
        mockFlakyTests.Setup(f => f.IsFlaky(_testJob1, It.IsAny<TestId>())).Returns(false);
        mockFlakyTests.Setup(f => f.IsFlaky(_testJob2, It.IsAny<TestId>())).Returns(true);
        mockFlakyTests.Setup(f => f.IsFlaky(testJob3, It.IsAny<TestId>())).Returns(false);

        var diff1 = FailedTestDiffer.Diff(_testJob1, [], [uniqueTest], mockFlakyTests.Object);
        var diff2 = FailedTestDiffer.Diff(_testJob2, [], [flakyTest], mockFlakyTests.Object);
        var diff3 = FailedTestDiffer.Diff(testJob3, [], [sharedErrorTest], mockFlakyTests.Object);

        var report = new BaselineChainReport(_branch, "TestChain", [rootBuild], new Dictionary<JobName, BuildDiffResult>
        {
            [_testJob1] = NextBuildDiffResult(_testJob1, diff1),
            [_testJob2] = NextBuildDiffResult(_testJob2, diff2),
            [testJob3] = NextBuildDiffResult(testJob3, diff3)
        });

        string capturedAttachment = null!;
        _mockMailSender
            .Setup(m => m.Send(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string>((_, _, _, attachment) => capturedAttachment = attachment)
            .Returns(Task.CompletedTask);

        await sender.SendReport(report);

        Assert.That(capturedAttachment, Does.Contain("UniqueTestClass.UniqueTestMethod"));
        Assert.That(capturedAttachment, isIncluded
            ? Does.Contain("FlakyTestClass.FlakyTestMethod")
            : Does.Not.Contain("FlakyTestClass.FlakyTestMethod"));
        Assert.That(capturedAttachment, isIncluded
            ? Does.Contain("SharedErrorTestClass.SharedErrorTestMethod")
            : Does.Not.Contain("SharedErrorTestClass.SharedErrorTestMethod"));

        mockFlakyTests.VerifyAll();
    }

    private static BuildDiffResult NextBuildDiffResult(JobName testJob, FailedTestDiff diff)
    {
        var baselineRef = new BuildReferenceResult(testJob, RandomData.NextBuildNumber, BuildStatus.Success);
        var currentRef = new BuildReferenceResult(testJob, RandomData.NextBuildNumber, BuildStatus.Success);
        return new BuildDiffResult(baselineRef, currentRef, BuildDiff.Diff(diff));
    }
}
