using Moq;
using NUnit.Framework;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

internal sealed class BuildList
{
    public Build[] Builds { get; set; } = [];
}

internal sealed class TestCase(string className, string testName, string status, string errorDetails)
{
    private static readonly Random _rand = new();

    public string ClassName { get; } = className;
    [JsonPropertyName("name")]
    public string TestName { get; } = testName;
    public string Status { get; } = status;
    public string ErrorDetails { get; } = errorDetails;

    public bool IsFailed => Status == "FAILED";

    public static TestCase Random()
    {
        var className = $"MyTests.Class{(char)('A' + _rand.Next(0, 26))}";
        var testName = $"Test{_rand.Next(1, 100)}";
        if (_rand.Next(0, 2) == 0)
        {
            var errorDetails = $"Error details for {className}.{testName}";
            return new TestCase(className, testName, "FAILED", errorDetails);
        }
        else
        {
            return new TestCase(className, testName, "PASSED", string.Empty);
        }
    }
}

internal sealed class TestSuite(TestCase[] cases)
{
    public TestCase[] Cases { get; } = cases;
}

internal sealed class TestReport(TestSuite[] suites)
{
    public TestSuite[] Suites { get; } = suites;
}

[TestFixture]
internal sealed class JenkinsClientTests
{
    private static readonly string s_url = "http://localhost:8080";
    private static readonly JenkinsConfig s_config = new(s_url);

    [Test]
    public void TestConstructor()
    {
        using var client = new JenkinsClient(s_config, "user:token");
        Assert.That(client, Is.Not.Null);
    }

    [Test]
    public async Task TestGetLastBuilds()
    {
        var jobName = new JobName("MyJob");
        var count = 100;
        var builds = RandomBuilds.Generate(5).ToArray();
        var apiClient = new Mock<IApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(c => c.GetAsync($"{s_url}/{jobName.UrlPath}/api/json?tree=builds[id,number,result,timestamp,duration,building,changeSets[items[commitId]]]{{0,{count}}}"))
            .ReturnsAsync(new BuildList { Builds = builds }.Serialize());
        apiClient.Setup(c => c.Dispose());
        using (var client = new JenkinsClient(s_config, "user:token", apiClient.Object))
        {
            var lastBuilds = await client.GetLastBuilds(jobName, count).ConfigureAwait(false);
            Assert.That(lastBuilds, Has.Length.EqualTo(builds.Length));
            for (var i = 0; i < builds.Length; i++)
            {
                BuildAssertions.AssertEqual(builds[i], lastBuilds[i]);
            }
        }
        apiClient.VerifyAll();
    }

    [Test]
    public async Task TestGetLastBuilds_IgnoredBuilding()
    {
        var jobName = new JobName("MyJob");
        var count = 100;
        var builds = RandomBuilds.Generate(5, [], buildings: [false, true, false, true, false]).ToArray();
        var apiClient = new Mock<IApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(c => c.GetAsync($"{s_url}/{jobName.UrlPath}/api/json?tree=builds[id,number,result,timestamp,duration,building,changeSets[items[commitId]]]{{0,{count}}}"))
            .ReturnsAsync(new BuildList { Builds = builds }.Serialize());
        apiClient.Setup(c => c.Dispose());
        using (var client = new JenkinsClient(s_config, "user:token", apiClient.Object))
        {
            var lastBuilds = await client.GetLastBuilds(jobName, count).ConfigureAwait(false);
            var expectedBuilds = builds.Where(b => !b.Building).ToArray();
            Assert.That(lastBuilds, Has.Length.EqualTo(expectedBuilds.Length));
            for (var i = 0; i < expectedBuilds.Length; i++)
            {
                BuildAssertions.AssertEqual(expectedBuilds[i], lastBuilds[i]);
            }
        }
        apiClient.VerifyAll();
    }

    [Test]
    public async Task GetScheduledJobs_WithJobs_Parsed()
    {
        var jobName = new JobName("MyBuild");
        var buildNumber = 42;
        var scheduledJobs = new JobName[] {
            new("JobA/JobA1"),
            new("JobB"),
        };
        var apiClient = new Mock<IApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(c => c.GetStringAsync($"{s_url}/{jobName.UrlPath}/{buildNumber}/consoleText"))
            .ReturnsAsync(
                string.Join(Environment.NewLine,
                [
                    "Some log output...",
                    "... time stamp ... Scheduling project: JobA » JobA1",
                    "[Pipeline] build",
                    "... time stamp ... Scheduling project: JobB",
                    "[Pipeline] build",
                ])
            );
        apiClient.Setup(c => c.Dispose());
        using (var client = new JenkinsClient(s_config, "user:token", apiClient.Object))
        {
            var jobs = await client.GetScheduledJobs(new(jobName, buildNumber)).ConfigureAwait(false);
            Assert.That(jobs, Is.EquivalentTo(scheduledJobs));
        }
        apiClient.VerifyAll();
    }

    [Test]
    public async Task GetTriggeredBuilds_WithBuilds_Parsed()
    {
        var jobName = new JobName("MyBuild");
        var buildNumber = 42;
        var triggeredBuilds = new BuildReference[] {
            new("MyDevTests", 54),
            new("MyIntegrationTests", 23),
        };
        var apiClient = new Mock<IApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(c => c.GetStringAsync($"{s_url}/{jobName.UrlPath}/{buildNumber}/consoleText"))
            .ReturnsAsync(
                string.Join(Environment.NewLine,
                [
                    "Some log output...",
                    "Triggering a new build of MyDevTests #54",
                    "Some more log output...",
                    "Triggering a new build of MyIntegrationTests #23",
                    "End of log."
                ])
            );
        apiClient.Setup(c => c.Dispose());
        using (var client = new JenkinsClient(s_config, "user:token", apiClient.Object))
        {
            var builds = await client.GetTriggeredBuilds(new(jobName, buildNumber)).ConfigureAwait(false);
            Assert.That(builds, Is.EquivalentTo(triggeredBuilds));
        }
        apiClient.VerifyAll();
    }

    [Test]
    public async Task GetFailCount_Defined()
    {
        var jobName = new JobName("MyTests");
        var buildNumber = 17;
        var apiClient = new Mock<IApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(c => c.GetAsync($"{s_url}/{jobName.UrlPath}/{buildNumber}/api/json?tree=actions[failCount]"))
            .ReturnsAsync(new { actions = new[] { new { failCount = 3 } } }.Serialize());
        apiClient.Setup(c => c.Dispose());
        using (var client = new JenkinsClient(s_config, "user:token", apiClient.Object))
        {
            var failCount = await client.GetFailCount(new(jobName, buildNumber)).ConfigureAwait(false);
            Assert.That(failCount, Is.EqualTo(3));
        }
        apiClient.VerifyAll();
    }

    [Test]
    public async Task GetFailCount_Undefined_ReturnsZero()
    {
        var jobName = new JobName("MyTests");
        var buildNumber = 17;
        var apiClient = new Mock<IApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(c => c.GetAsync($"{s_url}/{jobName.UrlPath}/{buildNumber}/api/json?tree=actions[failCount]"))
            .ReturnsAsync(new { actions = new[] {
                new { foo = "bar" }
            } }.Serialize());
        apiClient.Setup(c => c.Dispose());
        using (var client = new JenkinsClient(s_config, "user:token", apiClient.Object))
        {
            var failCount = await client.GetFailCount(new(jobName, buildNumber)).ConfigureAwait(false);
            Assert.That(failCount, Is.EqualTo(0));
        }
        apiClient.VerifyAll();
    }

    [Test]
    public async Task GetTestData_Defined_ReturnsExpectedValues()
    {
        var jobName = new JobName("MyTests");
        var buildNumber = 17;
        var timestamp = DateTimeOffset.UtcNow.AddHours(-5);
        var apiClient = new Mock<IApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(c => c.GetAsync($"{s_url}/{jobName.UrlPath}/{buildNumber}/api/json?tree=actions[failCount,causes[upstreamBuild,upstreamProject]]"))
            .ReturnsAsync(new { actions = new[] {
                new {
                    failCount = 3,
                    causes = new[] {
                        new {
                            upstreamBuild = 42,
                            upstreamProject = "MyUpstreamProject" } } } } }.Serialize());
        apiClient.Setup(c => c.Dispose());
        using (var client = new JenkinsClient(s_config, "user:token", apiClient.Object))
        {
            var buildData = await client.GetTestData(new(jobName, buildNumber)).ConfigureAwait(false);
            Assert.That(buildData.FailCount, Is.EqualTo(3));
            Assert.That(buildData.UpstreamBuilds, Has.Length.EqualTo(1));
            Assert.That(buildData.UpstreamBuilds[0], Is.EqualTo(new BuildReference("MyUpstreamProject", 42)));
        }
        apiClient.VerifyAll();
    }

    [Test]
    public async Task GetTestData_NoUpstream_ReturnsOnlyFailCount()
    {
        var jobName = new JobName("MyTests");
        var buildNumber = 17;
        var timestamp = DateTimeOffset.UtcNow.AddHours(-5);
        var apiClient = new Mock<IApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(c => c.GetAsync($"{s_url}/{jobName.UrlPath}/{buildNumber}/api/json?tree=actions[failCount,causes[upstreamBuild,upstreamProject]]"))
            .ReturnsAsync(new {
                actions = new[] {
                new {
                    failCount = 7,
                    causes = new[] {
                        new {
                            foo = "bar" } } } }
            }.Serialize());
        apiClient.Setup(c => c.Dispose());
        using (var client = new JenkinsClient(s_config, "user:token", apiClient.Object))
        {
            var buildData = await client.GetTestData(new(jobName, buildNumber)).ConfigureAwait(false);
            Assert.That(buildData.FailCount, Is.EqualTo(7));
            Assert.That(buildData.UpstreamBuilds, Has.Length.EqualTo(0));
        }
        apiClient.VerifyAll();
    }

    [Test]
    public async Task GetTestData_NoFailCount_ReturnsExpectedValues()
    {
        var jobName = new JobName("MyTests");
        var buildNumber = 17;
        var timestamp = DateTimeOffset.UtcNow.AddHours(-5);
        var apiClient = new Mock<IApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(c => c.GetAsync($"{s_url}/{jobName.UrlPath}/{buildNumber}/api/json?tree=actions[failCount,causes[upstreamBuild,upstreamProject]]"))
            .ReturnsAsync(new
            {
                actions = new[] {
                new {
                    causes = new[] {
                        new {
                            upstreamBuild = 42,
                            upstreamProject = "MyUpstreamProject" } } } }
            }.Serialize());
        apiClient.Setup(c => c.Dispose());
        using (var client = new JenkinsClient(s_config, "user:token", apiClient.Object))
        {
            var buildData = await client.GetTestData(new(jobName, buildNumber)).ConfigureAwait(false);
            Assert.That(buildData.FailCount, Is.EqualTo(0));
            Assert.That(buildData.UpstreamBuilds, Has.Length.EqualTo(1));
            Assert.That(buildData.UpstreamBuilds[0], Is.EqualTo(new BuildReference("MyUpstreamProject", 42)));
        }
        apiClient.VerifyAll();
    }

    [Test]
    public async Task TestGetFailedTests()
    {
        var jobName = new JobName("MyTests");
        var buildNumber = 17;
        var tests = Enumerable.Range(0, 10).Select(_ => TestCase.Random()).ToArray();
        var testReport = new TestReport(
        [
            new TestSuite(tests)
        ]);
        var apiClient = new Mock<IApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(c => c.GetAsync($"{s_url}/{jobName.UrlPath}/{buildNumber}/testReport/api/json"))
            .ReturnsAsync(testReport.Serialize());
        apiClient.Setup(c => c.Dispose());
        using (var client = new JenkinsClient(s_config, "user:token", apiClient.Object))
        {
            var failedTests = await client.GetFailedTests(new(jobName, buildNumber)).ConfigureAwait(false);
            var expectedFailedTests = tests.Where(t => t.IsFailed).Select(t => new FailedTest(t.ClassName, t.TestName, t.ErrorDetails)).ToArray();
            Assert.That(failedTests, Is.EquivalentTo(expectedFailedTests));
        }
        apiClient.VerifyAll();
    }

    [Test]
    public async Task TestTriggerBuild()
    {
        var jobName = new JobName("MyBuild");
        var commit = RandomData.NextSha1();
        var location = $"{s_url}/queue/item/123/";
        var apiClient = new Mock<IApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(c => c.PostAsync($"{s_url}/crumbIssuer/api/json", $"{s_url}/{jobName.UrlPath}/buildWithParameters?BUILD_REF_SPEC={Uri.EscapeDataString(commit.Value)}"))
            .ReturnsAsync(location);
        var queueUrl = location + "api/json";
        var expectedBuildNumber = 77;
        apiClient
            .Setup(c => c.GetAsync(queueUrl))
            .ReturnsAsync(new { executable = new { number = expectedBuildNumber } }.Serialize());
        apiClient.Setup(c => c.Dispose());
        using (var client = new JenkinsClient(s_config, "user:token", apiClient.Object))
        {
            var buildNumber = await client.TriggerBuild(jobName, commit, 1).ConfigureAwait(false);
            Assert.That(buildNumber, Is.EqualTo(expectedBuildNumber));
        }
        apiClient.VerifyAll();
    }

    [Test]
    public void TestTriggerBuild_NotInQueue()
    {
        var jobName = new JobName("MyBuild");
        var commit = RandomData.NextSha1();
        var apiClient = new Mock<IApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(c => c.PostAsync($"{s_url}/crumbIssuer/api/json", $"{s_url}/{jobName.UrlPath}/buildWithParameters?BUILD_REF_SPEC={Uri.EscapeDataString(commit.Value)}"))
            .ReturnsAsync("");
        apiClient.Setup(c => c.Dispose());
        using (var client = new JenkinsClient(s_config, "user:token", apiClient.Object))
        {
            Assert.That(async () => await client.TriggerBuild(jobName, commit, 1).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.EqualTo("Missing queue local header in response"));
        }
        apiClient.VerifyAll();
    }

    [Test]
    public void TestTriggerBuild_Timeout()
    {
        var jobName = new JobName("MyBuild");
        var commit = RandomData.NextSha1();
        var location = $"{s_url}/queue/item/123/";
        var apiClient = new Mock<IApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(c => c.PostAsync($"{s_url}/crumbIssuer/api/json", $"{s_url}/{jobName.UrlPath}/buildWithParameters?BUILD_REF_SPEC={Uri.EscapeDataString(commit.Value)}"))
            .ReturnsAsync(location);
        var queueUrl = location + "api/json";
        var emptyDoc = JsonDocument.Parse("{}");
        apiClient
            .Setup(c => c.GetAsync(queueUrl))
            .ReturnsAsync(emptyDoc);
        apiClient.Setup(c => c.Dispose());
        using (var client = new JenkinsClient(s_config, "user:token", apiClient.Object))
        {
            Assert.That(async () => await client.TriggerBuild(jobName, commit, 1).ConfigureAwait(false),
                Throws.InstanceOf<TimeoutException>().With.Message.EqualTo("Timed out waiting for build to be scheduled."));
        }
        apiClient.VerifyAll();
    }

    [Test]
    public async Task TestGetRootBuild()
    {
        var jobName = new JobName("MyTest");
        var buildNumber = 42;
        var apiClient = new Mock<IApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(c => c.GetStringAsync($"{s_url}/{jobName.UrlPath}/{buildNumber}/consoleText"))
            .ReturnsAsync(
                string.Join(Environment.NewLine,
                [
                    "Some log output...",
                    "13:06:24  Copied 397 artifacts from \"MyBuild\" build number 9\r\n",
                    "End of log."
                ])
            );
        apiClient.Setup(c => c.Dispose());
        using (var client = new JenkinsClient(s_config, "user:token", apiClient.Object))
        {
            var rootBuild = await client.TryGetRootBuild(new(jobName, buildNumber)).ConfigureAwait(false);
            Assert.That(rootBuild, Is.EqualTo(new BuildReference("MyBuild", 9)));
        }
        apiClient.VerifyAll();
    }

    [Test]
    public async Task TestGetRootBuildMissing()
    {
        var jobName = new JobName("MyTest");
        var buildNumber = 42;
        var apiClient = new Mock<IApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(c => c.GetStringAsync($"{s_url}/{jobName.UrlPath}/{buildNumber}/consoleText"))
            .ReturnsAsync(
                string.Join(Environment.NewLine,
                [
                    "Some log output...",
                    "End of log."
                ])
            );
        apiClient.Setup(c => c.Dispose());
        using (var client = new JenkinsClient(s_config, "user:token", apiClient.Object))
        {
            var rootBuild = await client.TryGetRootBuild(new(jobName, buildNumber)).ConfigureAwait(false);
            Assert.That(rootBuild, Is.Null);
        }
        apiClient.VerifyAll();
    }

    [Test]
    public async Task GetJobNames_WithoutMultiBranchFolders()
    {
        var apiClient = new Mock<IApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(c => c.GetAsync($"{s_url}/api/json?tree=jobs[name]"))
            .ReturnsAsync(new { jobs = new[] { new { name = "JobA" }, new { name = "JobB" } } }.Serialize());
        apiClient.Setup(c => c.Dispose());
        using (var client = new JenkinsClient(s_config, "user:token", apiClient.Object))
        {
            var jobs = await client.GetJobNames([]).ConfigureAwait(false);
            Assert.That(jobs, Is.EqualTo([new JobName("JobA"), new JobName("JobB")]));
        }
        apiClient.VerifyAll();
    }

    [Test]
    public async Task GetJobNames_WithMultiBranchFolders()
    {
        var apiClient = new Mock<IApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(c => c.GetAsync($"{s_url}/api/json?tree=jobs[name]"))
            .ReturnsAsync(new { jobs = new[] {
                new { name = "AJob1" },
                new { name = "Folder1" },
                new { name = "Folder2" },
                new { name = "ZJob2" },
            } }.Serialize());

        var multiBranchFolders = new[]
        {
            "Folder1/SubFolder1",
            "Folder2/SubFolder2",
        };

        apiClient
            .Setup(c => c.GetAsync($"{s_url}/job/Folder1/job/SubFolder1/api/json?tree=jobs[name]"))
            .ReturnsAsync(new { jobs = new[] {
                new { name = "Job111" },
                new { name = "Job112" },
            } }.Serialize());

        apiClient
            .Setup(c => c.GetAsync($"{s_url}/job/Folder2/job/SubFolder2/api/json?tree=jobs[name]"))
            .ReturnsAsync(new { jobs = new[] {
                new { name = "Job221" },
                new { name = "Job222" },
            } }.Serialize());

        apiClient.Setup(c => c.Dispose());
        using (var client = new JenkinsClient(s_config, "user:token", apiClient.Object))
        {
            var jobs = await client.GetJobNames(multiBranchFolders).ConfigureAwait(false);
            Assert.That(jobs, Is.EqualTo([
                new JobName("AJob1"),
                new JobName("Folder1"),
                new JobName("Folder1/SubFolder1/Job111"),
                new JobName("Folder1/SubFolder1/Job112"),
                new JobName("Folder2"),
                new JobName("Folder2/SubFolder2/Job221"),
                new JobName("Folder2/SubFolder2/Job222"),
                new JobName("ZJob2"),
            ]));
        }
        apiClient.VerifyAll();
    }
}
