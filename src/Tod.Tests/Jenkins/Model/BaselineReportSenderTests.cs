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
    private BaselineReportSender _sender;
    private JenkinsConfig _config;
    private IFlakyTests _flakyTests;

    [SetUp]
    public void SetUp()
    {
        _mockMailSender = new Mock<IMailSender>(MockBehavior.Strict);
        _sender = new BaselineReportSender(_mockMailSender.Object);
        _config = JenkinsConfig.New("https://jenkins.example.com");
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
        var report = new BaselineChainReport(_branch, "TestChain", [rootBuild], new Dictionary<JobName, FailedTestDiff> { [_testJob1] = diff });

        await _sender.SendReport(report);
    }

    [Test]
    public async Task SendReport_WithNewFailures_SendsToAllAuthors()
    {
        var rootBuild = RandomData.NextRootBuild(commits: 2);
        var authors = rootBuild.CommitAuthors;

        var failedTest = new FailedTest("TestClass", "TestMethod", "Error");
        var diff = FailedTestDiffer.Diff(_testJob1, [], [failedTest], _flakyTests);

        var report = new BaselineChainReport(_branch, "TestChain", [rootBuild], new Dictionary<JobName, FailedTestDiff> { [_testJob1] = diff });

        _mockMailSender.Setup(m => m.Send(
            authors[0].Email!,
            "master Build Report: TestChain",
            It.IsAny<string>(),
            It.IsAny<string>()
        )).Returns(Task.CompletedTask);

        _mockMailSender.Setup(m => m.Send(
            authors[1].Email!,
            "master Build Report: TestChain",
            It.IsAny<string>(),
            It.IsAny<string>()
        )).Returns(Task.CompletedTask);

        await _sender.SendReport(report);
    }

    [Test]
    public async Task SendReport_WithSingleAuthor_SendsOnce()
    {
        var rootBuild = RandomData.NextRootBuild(commits: 1);
        var author = rootBuild.CommitAuthors[0];

        var failedTest = new FailedTest("TestClass", "TestMethod", "Error");
        var diff = FailedTestDiffer.Diff(_testJob1, [], [failedTest], _flakyTests);

        var report = new BaselineChainReport(_branch, "TestChain", [rootBuild], new Dictionary<JobName, FailedTestDiff> { [_testJob1] = diff });

        _mockMailSender.Setup(m => m.Send(
            author.Email!,
            "master Build Report: TestChain",
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

        var uniqueAuthors = new[] { rootBuild1.CommitAuthors[0].Email, rootBuild2.CommitAuthors[0].Email }
            .Distinct()
            .ToArray();

        var failedTest = new FailedTest("TestClass", "TestMethod", "Error");
        var diff = FailedTestDiffer.Diff(_testJob1, [], [failedTest], _flakyTests);

        var report = new BaselineChainReport(_branch, "TestChain", [rootBuild1, rootBuild2], new Dictionary<JobName, FailedTestDiff> { [_testJob1] = diff });

        foreach (var email in uniqueAuthors)
        {
            _mockMailSender.Setup(m => m.Send(
                email!,
                "master Build Report: TestChain",
                It.IsAny<string>(),
                It.IsAny<string>()
            )).Returns(Task.CompletedTask);
        }

        await _sender.SendReport(report);
    }

    [Test]
    public async Task SendReport_WithEmailSendFailure_LogsErrorAndContinues()
    {
        var rootBuild = RandomData.NextRootBuild(commits: 2);
        var authors = rootBuild.CommitAuthors;

        var failedTest = new FailedTest("TestClass", "TestMethod", "Error");
        var diff = FailedTestDiffer.Diff(_testJob1, [], [failedTest], _flakyTests);

        var report = new BaselineChainReport(_branch, "TestChain", [rootBuild], new Dictionary<JobName, FailedTestDiff> { [_testJob1] = diff });

        _mockMailSender.Setup(m => m.Send(
            authors[0].Email!,
            "master Build Report: TestChain",
            It.IsAny<string>(),
            It.IsAny<string>()
        )).ThrowsAsync(new Exception("SMTP error"));

        _mockMailSender.Setup(m => m.Send(
            authors[1].Email!,
            "master Build Report: TestChain",
            It.IsAny<string>(),
            It.IsAny<string>()
        )).Returns(Task.CompletedTask);

        await _sender.SendReport(report);
    }

    [Test]
    public async Task SendReport_WithEmptyAuthorEmail_SkipsAuthor()
    {
        var commits = new[] { RandomData.NextSha1() };
        var authors = new[] { new CommitAuthor("Author Name", string.Empty) };
        var rootBuild = new RootBuild(
            new JobName("TestJob"),
            "id",
            RandomData.NextBuildNumber,
            DateTime.UtcNow.AddMinutes(-10),
            DateTime.UtcNow,
            true,
            commits,
            [],
            authors
        );

        var failedTest = new FailedTest("TestClass", "TestMethod", "Error");
        var diff = FailedTestDiffer.Diff(_testJob1, [], [failedTest], _flakyTests);

        var report = new BaselineChainReport(_branch, "TestChain", [rootBuild], new Dictionary<JobName, FailedTestDiff> { [_testJob1] = diff });

        await _sender.SendReport(report);
    }

    [Test]
    public async Task SendReport_WithMultipleTestJobs_IncludesAllInReport()
    {
        var rootBuild = RandomData.NextRootBuild(commits: 1);
        var author = rootBuild.CommitAuthors[0];

        var failedTest1 = new FailedTest("TestClass1", "TestMethod1", "Error1");
        var failedTest2 = new FailedTest("TestClass2", "TestMethod2", "Error2");
        var diff1 = FailedTestDiffer.Diff(_testJob1, [], [failedTest1], _flakyTests);
        var diff2 = FailedTestDiffer.Diff(_testJob2, [], [failedTest2], _flakyTests);

        var report = new BaselineChainReport(
            _branch,
            "TestChain",
            [rootBuild],
            new Dictionary<JobName, FailedTestDiff>
            {
                [_testJob1] = diff1,
                [_testJob2] = diff2
            }
        );

        _mockMailSender.Setup(m => m.Send(
            author.Email!,
            "master Build Report: TestChain",
            It.IsAny<string>(),
            It.Is<string>(body => body.Contains("TestClass1") && body.Contains("TestClass2"))
        )).Returns(Task.CompletedTask);

        await _sender.SendReport(report);
    }

    [Test]
    public async Task SendReport_IncludesChainNameInSubject()
    {
        var rootBuild = RandomData.NextRootBuild(commits: 1);
        var author = rootBuild.CommitAuthors[0];

        var failedTest = new FailedTest("TestClass", "TestMethod", "Error");
        var diff = FailedTestDiffer.Diff(_testJob1, [], [failedTest], _flakyTests);

        var report = new BaselineChainReport(_branch, "MyCustomChain", [rootBuild], new Dictionary<JobName, FailedTestDiff> { [_testJob1] = diff });

        _mockMailSender.Setup(m => m.Send(
            author.Email!,
            "master Build Report: MyCustomChain",
            It.IsAny<string>(),
            It.IsAny<string>()
        )).Returns(Task.CompletedTask);

        await _sender.SendReport(report);
    }
}
